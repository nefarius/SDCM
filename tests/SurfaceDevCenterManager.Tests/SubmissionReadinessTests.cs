/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class SubmissionReadinessTests
{
    public static TheoryData<string, string, string[], string, bool, bool> Matrix()
    {
        string[] steps =
        [
            "packageInfoValidation", "preparation", "scanning", "validation",
            "catalogCreation", "manualReview", "signing", "finalizeIngestion"
        ];
        string[] states = ["notStarted", "started", "completed", "failed"];

        TheoryData<string, string, string[], string, bool, bool> data = [];
        foreach (string state in states)
        {
            foreach (string step in steps)
            {
                bool failed = string.Equals(state, "failed", StringComparison.Ordinal);
                bool ready = !failed &&
                             (string.Equals(step, "finalizeIngestion", StringComparison.Ordinal) &&
                              string.Equals(state, "completed", StringComparison.Ordinal));
                data.Add(state, step, [], "commitComplete", failed, ready);
                data.Add(state, step, ["signedPackage"], "commitComplete", failed, !failed);
            }
        }

        data.Add("completed", "scanning", [], "commitPending", false, false);
        data.Add("completed", "7", [], "commitComplete", false, false);
        data.Add("completed", "finalizeIngestion", ["signedPackage"], "CommitComplete", false, true);
        data.Add("failed", "signing", ["signedPackage"], "commitFailed", true, false);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void WaitDone_MatchesSignedPackageOrFinalizeIngestion(
        string state, string step, string[] downloads, string commit, bool failed, bool ready)
    {
        Submission submission = HandlerTestSupport.Submission(commit, state, step, downloads);

        Assert.Equal(failed, SubmissionReadiness.IsFailed(submission));
        Assert.Equal(ready, SubmissionReadiness.IsReady(submission));
        Assert.Equal(failed || ready, SubmissionReadiness.IsWaitDone(submission, waitMetadata: false));
    }

    [Fact]
    public void SharedIsTerminal_StillTreatsCompletedAsTerminal()
    {
        WorkflowStatus status = new() { State = "completed", CurrentStep = "scanning" };
        Assert.True(status.IsTerminal());
        Assert.False(SubmissionReadiness.IsReady(HandlerTestSupport.Submission("commitComplete", "completed", "scanning")));
    }

    [Theory]
    [InlineData("commitPending", "notStarted", "packageInfoValidation", new string[0], "created")]
    [InlineData("CommitPending", "notStarted", "packageInfoValidation", new string[0], "created")]
    [InlineData("commitComplete", "started", "validation", new string[0], "processing")]
    [InlineData("commitComplete", "completed", "scanning", new string[0], "processing")]
    [InlineData("commitComplete", "completed", "finalizeIngestion", new string[0], "completed")]
    [InlineData("commitComplete", "started", "signing", new[] { "signedPackage" }, "completed")]
    [InlineData("commitFailed", "failed", "scanning", new string[0], "failed")]
    [InlineData("commitComplete", "failed", "signing", new string[0], "failed")]
    public void Progress_UsesDownloadsAndStepNotStateAlone(
        string commit, string state, string step, string[] downloads, string expected)
    {
        Submission submission = HandlerTestSupport.Submission(commit, state, step, downloads);
        Assert.Equal(expected, SubmissionReadiness.ToProgress(submission));
    }
}
