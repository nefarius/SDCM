/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager;
using SurfaceDevCenterManager.Handlers;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Replay;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class SubmissionHandlerTests
{
    [Fact]
    public async Task Wait_ScanningCompleted_DoesNotSucceed()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "scanning", "initialPackage"));
        RecordingOutputWriter output = new();
        ExitCode exit = await Wait(store, output, waitTimeoutSeconds: 1);

        Assert.Equal(ExitCode.Canceled, exit);
        Assert.Empty(output.Models);
    }

    [Fact]
    public async Task Wait_NumericCurrentStepCompleted_DoesNotSucceedWithoutSignedPackage()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "7", "initialPackage"));
        ExitCode exit = await Wait(store, new RecordingOutputWriter(), waitTimeoutSeconds: 1);

        Assert.Equal(ExitCode.Canceled, exit);
    }

    [Fact]
    public async Task Wait_SignedPackage_SucceedsEvenIfStepIsSigning()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "started", "signing", "initialPackage", "signedPackage"));
        store.SeedBlob("https://replay.invalid/signedPackage", "signed"u8.ToArray());
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output);

        Assert.Equal(ExitCode.Success, exit);
        Assert.NotEmpty(output.Models);
    }

    [Fact]
    public async Task Wait_FinalizeIngestionCompleted_SucceedsWithoutSignedPackage()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "finalizeIngestion"));
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output);

        Assert.Equal(ExitCode.Success, exit);
    }

    [Fact]
    public async Task Wait_FailedAtAnyStep_IsTerminal()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "failed", "scanning"));
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output);

        Assert.Equal(ExitCode.WorkflowFailed, exit);
    }

    [Fact]
    public async Task Wait_CommitFailed_IsTerminal()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitFailed", "notStarted", "packageInfoValidation"));
        ExitCode exit = await Wait(store, new RecordingOutputWriter());

        Assert.Equal(ExitCode.WorkflowFailed, exit);
    }

    [Fact]
    public async Task Wait_FailedDoesNotWaitForMetadata()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitFailed", "failed", "validation"));
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output, waitMetadata: true, waitTimeoutSeconds: 1);

        Assert.Equal(ExitCode.WorkflowFailed, exit);
        Assert.NotEmpty(output.Models);
    }

    [Fact]
    public async Task Wait_PollsThroughIntermediateCompleted()
    {
        ReplaySubmission submission = HandlerTestSupport.Replay("commitComplete", "completed", "scanning");
        submission.Polls =
        [
            new ReplaySubmissionPoll
            {
                WorkflowStatus = new ReplayWorkflowStatus { State = "completed", CurrentStep = "scanning" }
            },
            new ReplaySubmissionPoll
            {
                WorkflowStatus = new ReplayWorkflowStatus { State = "completed", CurrentStep = "finalizeIngestion" },
                Downloads = [new ReplayDownload { Type = "signedPackage", Url = "https://replay.invalid/signedPackage" }]
            }
        ];
        ReplayStore store = HandlerTestSupport.StoreWith(submission);
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output, pollIntervalSeconds: 1);

        Assert.Equal(ExitCode.Success, exit);
    }

    [Fact]
    public async Task Wait_JsonIncludesErrorReportContent()
    {
        ReplaySubmission submission = HandlerTestSupport.Replay("commitFailed", "failed", "validation");
        submission.WorkflowStatus!.ErrorReport = "https://replay.invalid/errors/1";
        ReplayStore store = HandlerTestSupport.StoreWith(submission);
        store.SeedErrorReport("https://replay.invalid/errors/1", "INF failed logo tests");
        RecordingOutputWriter output = new();

        ExitCode exit = await Wait(store, output);

        Assert.Equal(ExitCode.WorkflowFailed, exit);
        JsonNode node = Assert.IsAssignableFrom<JsonNode>(output.Models[0]);
        Assert.Equal("INF failed logo tests", node["workflowStatus"]?["errorReportContent"]?.GetValue<string>());
    }

    [Fact]
    public async Task Commit_AlreadyComplete_IsNoOpRegardlessOfHttpError()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "started", "validation"));
        RecordingOutputWriter output = new();
        SubmissionCommitHandler handler = new(store, output, HandlerTestSupport.Errors(output));

        ExitCode exit = await handler.RunAsync(
            new SubmissionCommitInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        CommitResult result = Assert.IsType<CommitResult>(output.Models[0]);
        Assert.True(result.AlreadyCommitted);
        Assert.Equal("commitComplete", result.CommitStatus, ignoreCase: true);
    }

    [Fact]
    public async Task Commit_Pending_CommitsAndEmitsResult()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitPending", "notStarted", "packageInfoValidation", "initialPackage"));
        RecordingOutputWriter output = new();
        SubmissionCommitHandler handler = new(store, output, HandlerTestSupport.Errors(output));

        ExitCode exit = await handler.RunAsync(
            new SubmissionCommitInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        CommitResult result = Assert.IsType<CommitResult>(output.Models[0]);
        Assert.False(result.AlreadyCommitted);
        Assert.True(CommitStatuses.IsComplete(store.FindSubmission("1", "2")?.CommitStatus));
    }

    [Fact]
    public async Task Upload_RefusesWhenNotCommitPending()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "started", "scanning", "initialPackage"));
        RecordingOutputWriter output = new();
        string package = WriteTemp("pkg.zip", "cab");
        try
        {
            SubmissionUploadHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);
            ExitCode exit = await handler.RunAsync(
                new SubmissionUploadInput("1", "2", package, HandlerTestSupport.Global), CancellationToken.None);

            Assert.Equal(ExitCode.InvalidState, exit);
            Assert.Contains(output.Errors, e => e.Contains("commitStatus", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Fact]
    public async Task Upload_CommitPending_EmitsJsonResult()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("CommitPending", "notStarted", "packageInfoValidation", "initialPackage"));
        RecordingOutputWriter output = new();
        string package = WriteTemp("pkg.zip", "cab");
        try
        {
            SubmissionUploadHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);
            ExitCode exit = await handler.RunAsync(
                new SubmissionUploadInput("1", "2", package, HandlerTestSupport.Global), CancellationToken.None);

            Assert.Equal(ExitCode.Success, exit);
            UploadResult result = Assert.IsType<UploadResult>(output.Models[0]);
            Assert.Equal(package, result.PackagePath);
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Fact]
    public async Task Download_Overwrite_ReplacesExistingFile()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "finalizeIngestion", "signedPackage"));
        store.SeedBlob("https://replay.invalid/signedPackage", "signed-bytes"u8.ToArray());
        string dest = Path.Combine(Path.GetTempPath(), $"sdcm-dl-{Guid.NewGuid():N}", "out", "signed.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "old");
        RecordingOutputWriter output = new();
        try
        {
            SubmissionDownloadHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);
            ExitCode denied = await handler.RunAsync(
                new SubmissionDownloadInput("1", "2", dest, Overwrite: false, HandlerTestSupport.Global),
                CancellationToken.None);
            Assert.Equal(ExitCode.IoError, denied);

            ExitCode allowed = await handler.RunAsync(
                new SubmissionDownloadInput("1", "2", dest, Overwrite: true, HandlerTestSupport.Global),
                CancellationToken.None);
            Assert.Equal(ExitCode.Success, allowed);
            Assert.Equal("signed-bytes"u8.ToArray(), File.ReadAllBytes(dest));
            Assert.IsType<DownloadResult>(output.Models[0]);
        }
        finally
        {
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
    }

    [Fact]
    public async Task Get_ReturnsSingleObject()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "scanning"));
        RecordingOutputWriter output = new();
        SubmissionGetHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);

        ExitCode exit = await handler.RunAsync(
            new SubmissionGetInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        Assert.Single(output.Models);
        Assert.False(output.Models[0] is System.Collections.IList);
    }

    [Fact]
    public async Task Status_NormalizesProgressAndErrorReport()
    {
        ReplaySubmission submission = HandlerTestSupport.Replay("commitComplete", "completed", "scanning");
        submission.WorkflowStatus!.ErrorReport = "https://replay.invalid/errors/2";
        ReplayStore store = HandlerTestSupport.StoreWith(submission);
        store.SeedErrorReport("https://replay.invalid/errors/2", "still scanning");
        RecordingOutputWriter output = new();
        SubmissionStatusHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);

        ExitCode exit = await handler.RunAsync(
            new SubmissionStatusInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        SubmissionStatusDocument status = Assert.IsType<SubmissionStatusDocument>(output.Models[0]);
        Assert.Equal("processing", status.Progress);
        Assert.False(status.HasSignedPackage);
        Assert.Equal("still scanning", status.ErrorReportContent);
    }

    [Fact]
    public async Task List_WithDeprecatedId_StillReturnsArrayAndWarns()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitPending", "notStarted", "packageInfoValidation"));
        RecordingOutputWriter output = new();
        SubmissionListHandler handler = new(store, output, HandlerTestSupport.Errors(output));

        ExitCode exit = await handler.RunAsync(
            new SubmissionListInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        Assert.Contains(output.Errors, e => e.Contains("deprecated", StringComparison.OrdinalIgnoreCase));
        Assert.Single(output.Models);
        Assert.IsType<Submission>(output.Models[0]);
    }

    [Fact]
    public async Task MetadataCreate_EmitsResult()
    {
        ReplayStore store = HandlerTestSupport.StoreWith(
            HandlerTestSupport.Replay("commitComplete", "completed", "finalizeIngestion", "signedPackage"));
        RecordingOutputWriter output = new();
        SubmissionMetadataCreateHandler handler = new(store, output, HandlerTestSupport.Errors(output));

        ExitCode exit = await handler.RunAsync(
            new SubmissionMetadataCreateInput("1", "2", HandlerTestSupport.Global), CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        Assert.IsType<MetadataCreateResult>(output.Models[0]);
    }

    private static Task<ExitCode> Wait(
        ReplayStore store,
        RecordingOutputWriter output,
        uint pollIntervalSeconds = 1,
        uint? waitTimeoutSeconds = 5,
        bool waitMetadata = false)
    {
        SubmissionWaitHandler handler = new(store, output, HandlerTestSupport.Errors(output), store);
        return handler.RunAsync(
            new SubmissionWaitInput("1", "2", waitMetadata, pollIntervalSeconds, waitTimeoutSeconds, HandlerTestSupport.Global),
            CancellationToken.None);
    }

    private static string WriteTemp(string name, string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, contents);
        return path;
    }
}
