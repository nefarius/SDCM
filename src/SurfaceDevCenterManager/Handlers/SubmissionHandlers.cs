/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Cli;
using SurfaceDevCenterManager.Json;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Handlers;

public sealed record SubmissionCreateInput(string ProductId, string InputPath, GlobalInvocationOptions Global);

public sealed class SubmissionCreateHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(SubmissionCreateInput input, CancellationToken cancellationToken)
    {
        NewSubmission newSubmission;
        try
        {
            newSubmission = InputFileReader.Read<NewSubmission>(input.InputPath);
        }
        catch (InputFileException ex)
        {
            output.Error(ex.Message);
            return ExitCode.InvalidArguments;
        }

        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .NewSubmission(input.ProductId, newSubmission).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                output.Result(submission, s => s.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission create");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionListInput(string ProductId, string? SubmissionId, GlobalInvocationOptions Global);

public sealed class SubmissionListHandler(IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(SubmissionListInput input, CancellationToken cancellationToken)
    {
        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                if (!string.IsNullOrEmpty(input.SubmissionId))
                {
                    output.Error("submission list --submission-id is deprecated; use 'submission get'.");
                }

                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                output.Results(response.ReturnValue ?? [], s => s.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission list");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionGetInput(string ProductId, string SubmissionId, GlobalInvocationOptions Global);

public sealed class SubmissionGetHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IErrorReportFetcher errorReports)
{
    public async Task<ExitCode> RunAsync(SubmissionGetInput input, CancellationToken cancellationToken)
    {
        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                object model = output.Format == OutputFormat.Json
                    ? await WorkflowJson.SubmissionWithErrorReportAsync(submission, errorReports, cancellationToken)
                        .ConfigureAwait(false)
                    : submission;
                output.Result(model, _ => submission.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission get");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionStatusInput(string ProductId, string SubmissionId, GlobalInvocationOptions Global);

public sealed class SubmissionStatusHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IErrorReportFetcher errorReports)
{
    public async Task<ExitCode> RunAsync(SubmissionStatusInput input, CancellationToken cancellationToken)
    {
        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                SubmissionStatusDocument status = await WorkflowJson
                    .StatusAsync(submission, errorReports, cancellationToken).ConfigureAwait(false);
                output.Result(status, WorkflowJson.DumpStatus);
                return SubmissionReadiness.IsFailed(submission) ? ExitCode.WorkflowFailed : ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission status");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionCommitInput(string ProductId, string SubmissionId, GlobalInvocationOptions Global);

public sealed class SubmissionCommitHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(SubmissionCommitInput input, CancellationToken cancellationToken)
    {
        return await factory.UseAsync(input.Global, output, async api =>
        {
            output.Progress($"Committing submission {input.SubmissionId}...");
            try
            {
                if (await TryAlreadyCommittedAsync(api, input, alreadyCommitted: true).ConfigureAwait(false) is { } before)
                {
                    return before;
                }

                DevCenterResponse<bool> response = await api
                    .CommitSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    if (await TryAlreadyCommittedAsync(api, input, alreadyCommitted: true).ConfigureAwait(false) is { } after)
                    {
                        return after;
                    }

                    return errors.Report(response.Error);
                }

                return EmitCommit(input, commitStatus: CommitStatuses.Complete, alreadyCommitted: false);
            }
            catch (Exception ex)
            {
                if (await TryAlreadyCommittedAsync(api, input, alreadyCommitted: true).ConfigureAwait(false) is { } afterEx)
                {
                    return afterEx;
                }

                return errors.ReportException(ex, "submission commit");
            }
        }, cancellationToken);
    }

    private async Task<ExitCode?> TryAlreadyCommittedAsync(
        IDevCenterHandler api, SubmissionCommitInput input, bool alreadyCommitted)
    {
        try
        {
            DevCenterResponse<Submission> status = await api
                .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
            if (status.Error == null && status.ReturnValue is { Count: > 0 } &&
                CommitStatuses.IsComplete(status.ReturnValue[0].CommitStatus))
            {
                return EmitCommit(input, status.ReturnValue[0].CommitStatus, alreadyCommitted);
            }
        }
        catch (Exception)
        {
            // Fall through to the original commit error path.
        }

        return null;
    }

    private ExitCode EmitCommit(SubmissionCommitInput input, string? commitStatus, bool alreadyCommitted)
    {
        CommitResult result = new()
        {
            ProductId = input.ProductId,
            SubmissionId = input.SubmissionId,
            CommitStatus = commitStatus,
            AlreadyCommitted = alreadyCommitted
        };
        output.Progress(alreadyCommitted ? "Submission was already committed." : "Commit accepted.");
        output.Result(result, r =>
        {
            Console.WriteLine($"productId: {r.ProductId}");
            Console.WriteLine($"submissionId: {r.SubmissionId}");
            Console.WriteLine($"commitStatus: {r.CommitStatus}");
            Console.WriteLine($"alreadyCommitted: {r.AlreadyCommitted}");
        });
        return ExitCode.Success;
    }
}

public sealed record SubmissionUploadInput(
    string ProductId, string SubmissionId, string PackagePath, GlobalInvocationOptions Global);

public sealed class SubmissionUploadHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IBlobTransfer blobs)
{
    public async Task<ExitCode> RunAsync(SubmissionUploadInput input, CancellationToken cancellationToken)
    {
        if (!File.Exists(input.PackagePath))
        {
            output.Error($"Package file not found: {input.PackagePath}");
            return ExitCode.IoError;
        }

        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                if (!CommitStatuses.IsPending(submission.CommitStatus))
                {
                    output.Error(
                        $"Cannot upload: commitStatus is '{submission.CommitStatus ?? "(none)"}' " +
                        $"(expected {CommitStatuses.Pending}).");
                    return ExitCode.InvalidState;
                }

                Download.Item? uploadTarget = submission.Downloads?.Items?
                    .FirstOrDefault(i => string.Equals(i.Type, SubmissionReadiness.InitialPackage,
                        StringComparison.OrdinalIgnoreCase));

                if (uploadTarget?.Url is null)
                {
                    output.Error("This submission has no 'initialPackage' upload URL.");
                    return ExitCode.InvalidState;
                }

                output.Progress($"Uploading '{input.PackagePath}'...");
                await blobs.UploadAsync(uploadTarget.Url, input.PackagePath, cancellationToken).ConfigureAwait(false);
                UploadResult result = new()
                {
                    ProductId = input.ProductId,
                    SubmissionId = input.SubmissionId,
                    PackagePath = input.PackagePath
                };
                output.Progress("Upload complete.");
                output.Result(result, r =>
                {
                    Console.WriteLine($"productId: {r.ProductId}");
                    Console.WriteLine($"submissionId: {r.SubmissionId}");
                    Console.WriteLine($"packagePath: {r.PackagePath}");
                });
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission upload");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionDownloadInput(
    string ProductId, string SubmissionId, string OutputFile, bool Overwrite, GlobalInvocationOptions Global);

public sealed class SubmissionDownloadHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IBlobTransfer blobs)
{
    public async Task<ExitCode> RunAsync(SubmissionDownloadInput input, CancellationToken cancellationToken)
    {
        ExitCode prepared = DownloadPath.Prepare(input.OutputFile, input.Overwrite, output);
        if (prepared != ExitCode.Success)
        {
            return prepared;
        }

        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                Download.Item? downloadTarget = submission.Downloads?.Items?
                    .FirstOrDefault(i => string.Equals(i.Type, SubmissionReadiness.SignedPackage,
                        StringComparison.OrdinalIgnoreCase));

                if (downloadTarget?.Url is null)
                {
                    output.Error("This submission has no 'signedPackage' download available yet.");
                    return ExitCode.InvalidState;
                }

                output.Progress($"Downloading to '{input.OutputFile}'...");
                await blobs.DownloadAsync(downloadTarget.Url, input.OutputFile, cancellationToken)
                    .ConfigureAwait(false);
                DownloadResult result = new()
                {
                    ProductId = input.ProductId,
                    SubmissionId = input.SubmissionId,
                    OutputFile = input.OutputFile,
                    Type = SubmissionReadiness.SignedPackage
                };
                output.Progress("Download complete.");
                output.Result(result, r =>
                {
                    Console.WriteLine($"outputFile: {r.OutputFile}");
                    Console.WriteLine($"type: {r.Type}");
                });
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission download");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionMetadataDownloadInput(
    string ProductId, string SubmissionId, string OutputFile, bool Overwrite, GlobalInvocationOptions Global);

public sealed class SubmissionMetadataDownloadHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IBlobTransfer blobs)
{
    public async Task<ExitCode> RunAsync(SubmissionMetadataDownloadInput input, CancellationToken cancellationToken)
    {
        ExitCode prepared = DownloadPath.Prepare(input.OutputFile, input.Overwrite, output);
        if (prepared != ExitCode.Success)
        {
            return prepared;
        }

        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<Submission> response = await api
                    .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out Submission submission))
                {
                    return ExitCode.InvalidState;
                }

                Download.Item? metadataTarget = submission.Downloads?.Items?
                    .FirstOrDefault(i => string.Equals(i.Type, SubmissionReadiness.DriverMetadata,
                        StringComparison.OrdinalIgnoreCase));

                if (metadataTarget?.Url is null)
                {
                    output.Error("This submission has no publisher metadata yet - run 'submission metadata create' first.");
                    return ExitCode.InvalidState;
                }

                output.Progress($"Downloading publisher metadata to '{input.OutputFile}'...");
                await blobs.DownloadAsync(metadataTarget.Url, input.OutputFile, cancellationToken)
                    .ConfigureAwait(false);
                DownloadResult result = new()
                {
                    ProductId = input.ProductId,
                    SubmissionId = input.SubmissionId,
                    OutputFile = input.OutputFile,
                    Type = SubmissionReadiness.DriverMetadata
                };
                output.Progress("Download complete.");
                output.Result(result, r =>
                {
                    Console.WriteLine($"outputFile: {r.OutputFile}");
                    Console.WriteLine($"type: {r.Type}");
                });
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission metadata download");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionMetadataCreateInput(string ProductId, string SubmissionId, GlobalInvocationOptions Global);

public sealed class SubmissionMetadataCreateHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(SubmissionMetadataCreateInput input, CancellationToken cancellationToken)
    {
        return await factory.UseAsync(input.Global, output, async api =>
        {
            output.Progress("Requesting publisher metadata generation...");
            try
            {
                DevCenterResponse<bool> response = await api
                    .CreateMetaData(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                MetadataCreateResult result = new()
                {
                    ProductId = input.ProductId,
                    SubmissionId = input.SubmissionId
                };
                output.Progress("Metadata generation requested; poll 'submission metadata download' once ready.");
                output.Result(result, r =>
                {
                    Console.WriteLine($"productId: {r.ProductId}");
                    Console.WriteLine($"submissionId: {r.SubmissionId}");
                    Console.WriteLine("requested: True");
                });
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission metadata create");
            }
        }, cancellationToken);
    }
}

public sealed record SubmissionWaitInput(
    string ProductId,
    string SubmissionId,
    bool WaitMetadata,
    uint PollIntervalSeconds,
    uint? WaitTimeoutSeconds,
    GlobalInvocationOptions Global);

public sealed class SubmissionWaitHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors, IErrorReportFetcher errorReports)
{
    public async Task<ExitCode> RunAsync(SubmissionWaitInput input, CancellationToken cancellationToken)
    {
        using CancellationTokenSource? timeoutCts = input.WaitTimeoutSeconds is { } seconds
            ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds))
            : null;
        using CancellationTokenSource linkedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return await factory.UseAsync(input.Global, output, async api =>
        {
            try
            {
                while (true)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    DevCenterResponse<Submission> response = await api
                        .GetSubmission(input.ProductId, input.SubmissionId).ConfigureAwait(false);
                    if (response.Error != null)
                    {
                        return errors.Report(response.Error);
                    }

                    if (!response.TryGetSingle(output, out Submission submission))
                    {
                        return ExitCode.InvalidState;
                    }

                    WorkflowStatus? status = submission.WorkflowStatus;

                    if (output.Format == OutputFormat.Text && status != null)
                    {
                        await status.Dump().ConfigureAwait(false);
                    }

                    if (SubmissionReadiness.IsWaitDone(submission, input.WaitMetadata))
                    {
                        bool failed = SubmissionReadiness.IsFailed(submission);
                        object model = output.Format == OutputFormat.Json
                            ? await WorkflowJson.SubmissionWithErrorReportAsync(
                                submission, errorReports, linkedCts.Token).ConfigureAwait(false)
                            : submission;
                        output.Result(model, _ => submission.Dump());
                        return failed ? ExitCode.WorkflowFailed : ExitCode.Success;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(PollingDefaults.ClampPollInterval(input.PollIntervalSeconds)),
                            linkedCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
            {
                output.Error($"Timed out after {input.WaitTimeoutSeconds}s waiting for the submission to become ready (signedPackage or finalizeIngestion completed).");
                return ExitCode.Canceled;
            }
            catch (OperationCanceledException)
            {
                return ExitCode.Canceled;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "submission wait");
            }
        }, linkedCts.Token);
    }
}
