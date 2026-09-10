/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Replay;

/// <summary>
///     In-memory Hardware Dev Center used by handler tests and <c>--replay</c>. One store is shared
///     for the process so create/upload/commit/wait see the same submissions.
/// </summary>
public sealed class ReplayStore : IDevCenterHandlerFactory, IBlobTransfer, IErrorReportFetcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errorReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pollIndex = new(StringComparer.Ordinal);
    private long _nextId = 1;

    public List<ReplayProduct> Products { get; } = [];
    public List<ReplaySubmission> Submissions { get; } = [];
    public List<ReplayShippingLabel> ShippingLabels { get; } = [];
    public List<ReplayPreprodPackage> Preprod { get; } = [];

    public static ReplayStore Empty() => new();

    public static ReplayStore Load(string path)
    {
        string json = File.ReadAllText(path);
        ReplayCatalog catalog = JsonSerializer.Deserialize<ReplayCatalog>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Replay fixture '{path}' deserialized to nothing.");
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return FromCatalog(catalog, directory);
    }

    public static ReplayStore FromCatalog(ReplayCatalog catalog, string? blobDirectory = null)
    {
        ReplayStore store = new();
        store.Products.AddRange(catalog.Products);
        store.Submissions.AddRange(catalog.Submissions);
        store.ShippingLabels.AddRange(catalog.ShippingLabels);
        store.Preprod.AddRange(catalog.Preprod);
        foreach (KeyValuePair<string, string> report in catalog.ErrorReports)
        {
            store._errorReports[report.Key] = report.Value;
        }

        foreach (KeyValuePair<string, ReplayBlob> blob in catalog.Blobs)
        {
            if (!string.IsNullOrEmpty(blob.Value.Text))
            {
                store._blobs[blob.Key] = Encoding.UTF8.GetBytes(blob.Value.Text);
            }
            else if (!string.IsNullOrEmpty(blob.Value.File))
            {
                string file = Path.IsPathRooted(blob.Value.File) || blobDirectory == null
                    ? blob.Value.File
                    : Path.Combine(blobDirectory, blob.Value.File);
                store._blobs[blob.Key] = File.ReadAllBytes(file);
            }
        }

        return store;
    }

    public string NextId()
    {
        lock (_gate)
        {
            return Interlocked.Increment(ref _nextId).ToString();
        }
    }

    public void SeedBlob(string url, byte[] content)
    {
        lock (_gate)
        {
            _blobs[url] = content;
        }
    }

    public void SeedErrorReport(string url, string content)
    {
        lock (_gate)
        {
            _errorReports[url] = content;
        }
    }

    public ReplaySubmission AddSubmission(ReplaySubmission submission)
    {
        lock (_gate)
        {
            Submissions.Add(submission);
            return submission;
        }
    }

    public Task<IDevCenterHandler> CreateAsync(
        string profileName, AuthMode authMode, AadPromptMode promptMode, uint httpTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IDevCenterHandler>(new ReplayDevCenterHandler(this));
    }

    public Task<IDevCenterPreprodHandler> CreatePreprodAsync(
        string profileName, AuthMode authMode, AadPromptMode promptMode, uint httpTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IDevCenterPreprodHandler>(new ReplayDevCenterPreprodHandler(this));
    }

    public Task UploadAsync(Uri url, string localPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _blobs[url.ToString()] = File.ReadAllBytes(localPath);
        }

        return Task.CompletedTask;
    }

    public Task DownloadAsync(Uri url, string localPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        lock (_gate)
        {
            if (!_blobs.TryGetValue(url.ToString(), out bytes!))
            {
                bytes = Encoding.UTF8.GetBytes($"replay-blob:{url}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath))!);
        File.WriteAllBytes(localPath, bytes);
        return Task.CompletedTask;
    }

    public Task<string> DownloadToStringAsync(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_errorReports.TryGetValue(url.ToString(), out string? report))
            {
                return Task.FromResult(report);
            }

            if (_blobs.TryGetValue(url.ToString(), out byte[]? bytes))
            {
                return Task.FromResult(Encoding.UTF8.GetString(bytes));
            }
        }

        return Task.FromResult($"replay-blob:{url}");
    }

    public Task<string?> FetchAsync(string? errorReport, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(errorReport))
        {
            return Task.FromResult<string?>(null);
        }

        lock (_gate)
        {
            if (_errorReports.TryGetValue(errorReport, out string? content))
            {
                return Task.FromResult<string?>(content);
            }
        }

        if (Uri.TryCreate(errorReport, UriKind.Absolute, out Uri? url))
        {
            return DownloadToStringAsync(url, cancellationToken).ContinueWith(
                t => (string?)t.Result, cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        return Task.FromResult<string?>(null);
    }

    internal ReplaySubmission? FindSubmission(string productId, string submissionId)
    {
        lock (_gate)
        {
            return Submissions.FirstOrDefault(s =>
                IdsEqual(s.ProductId, productId) && IdsEqual(s.Id, submissionId));
        }
    }

    internal ReplaySubmission SnapshotForGet(string productId, string submissionId)
    {
        lock (_gate)
        {
            ReplaySubmission? live = Submissions.FirstOrDefault(s =>
                IdsEqual(s.ProductId, productId) && IdsEqual(s.Id, submissionId));
            if (live == null)
            {
                throw new KeyNotFoundException($"submission {productId}/{submissionId}");
            }

            if (live.Polls is { Count: > 0 })
            {
                string key = $"{productId}/{submissionId}";
                int index = _pollIndex.GetValueOrDefault(key);
                ReplaySubmissionPoll poll = live.Polls[Math.Min(index, live.Polls.Count - 1)];
                if (index < live.Polls.Count - 1)
                {
                    _pollIndex[key] = index + 1;
                }

                return ApplyPoll(Clone(live), poll);
            }

            return Clone(live);
        }
    }

    internal List<ReplaySubmission> ListSubmissions(string productId)
    {
        lock (_gate)
        {
            return Submissions.Where(s => IdsEqual(s.ProductId, productId)).Select(Clone).ToList();
        }
    }

    internal void MarkCommitted(string productId, string submissionId)
    {
        lock (_gate)
        {
            ReplaySubmission? live = Submissions.FirstOrDefault(s =>
                IdsEqual(s.ProductId, productId) && IdsEqual(s.Id, submissionId));
            if (live != null)
            {
                live.CommitStatus = "commitComplete";
            }
        }
    }

    internal static bool IdsEqual(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static DevCenterResponse<T> Ok<T>(params T[] values) => new() { ReturnValue = [.. values] };

    internal static DevCenterResponse<T> NotFound<T>(string message) => new()
    {
        Error = new DevCenterErrorDetails
        {
            Code = "entityNotFound",
            Message = message,
            HttpErrorCode = (int)HttpStatusCode.NotFound
        },
        ReturnValue = []
    };

    internal static DevCenterResponse<T> InvalidState<T>(string message) => new()
    {
        Error = new DevCenterErrorDetails
        {
            Code = "requestInvalidForCurrentState",
            Message = message,
            HttpErrorCode = (int)HttpStatusCode.BadRequest
        },
        ReturnValue = []
    };

    internal static WorkflowStatus? ToWorkflow(ReplayWorkflowStatus? status)
    {
        if (status == null)
        {
            return null;
        }

        return new WorkflowStatus
        {
            CurrentStep = status.CurrentStep,
            State = status.State,
            Messages = status.Messages,
            ErrorReport = status.ErrorReport
        };
    }

    internal static Download? ToDownloads(IEnumerable<ReplayDownload>? items)
    {
        List<ReplayDownload> list = items?.ToList() ?? [];
        if (list.Count == 0)
        {
            return null;
        }

        return new Download
        {
            Items = list.Select(i => new Download.Item
            {
                Type = i.Type,
                Url = string.IsNullOrEmpty(i.Url) ? null : new Uri(i.Url, UriKind.RelativeOrAbsolute)
            }).ToList()
        };
    }

    internal static Submission ToSubmission(ReplaySubmission source)
    {
        return new Submission
        {
            Id = source.Id,
            ProductId = source.ProductId,
            Name = source.Name,
            Type = source.Type,
            CommitStatus = source.CommitStatus,
            WorkflowStatus = ToWorkflow(source.WorkflowStatus),
            Downloads = ToDownloads(source.Downloads)
        };
    }

    internal static Product ToProduct(ReplayProduct source)
    {
        return new Product
        {
            Id = source.Id,
            ProductName = source.ProductName,
            TestHarness = source.TestHarness
        };
    }

    internal static ShippingLabel ToShippingLabel(ReplayShippingLabel source)
    {
        return new ShippingLabel
        {
            Id = source.Id,
            ProductId = source.ProductId,
            SubmissionId = source.SubmissionId,
            Name = source.Name,
            WorkflowStatus = ToWorkflow(source.WorkflowStatus)
        };
    }

    private static ReplaySubmission Clone(ReplaySubmission source)
    {
        return new ReplaySubmission
        {
            Id = source.Id,
            ProductId = source.ProductId,
            Name = source.Name,
            Type = source.Type,
            CommitStatus = source.CommitStatus,
            WorkflowStatus = CloneWorkflow(source.WorkflowStatus),
            Downloads = source.Downloads.Select(d => new ReplayDownload { Type = d.Type, Url = d.Url }).ToList(),
            Polls = source.Polls
        };
    }

    private static ReplayWorkflowStatus? CloneWorkflow(ReplayWorkflowStatus? status)
    {
        if (status == null)
        {
            return null;
        }

        return new ReplayWorkflowStatus
        {
            CurrentStep = status.CurrentStep,
            State = status.State,
            Messages = status.Messages?.ToList(),
            ErrorReport = status.ErrorReport
        };
    }

    private static ReplaySubmission ApplyPoll(ReplaySubmission live, ReplaySubmissionPoll poll)
    {
        if (poll.CommitStatus != null)
        {
            live.CommitStatus = poll.CommitStatus;
        }

        if (poll.WorkflowStatus != null)
        {
            live.WorkflowStatus = CloneWorkflow(poll.WorkflowStatus);
        }

        if (poll.Downloads != null)
        {
            live.Downloads = poll.Downloads.Select(d => new ReplayDownload { Type = d.Type, Url = d.Url }).ToList();
        }

        return live;
    }
}
