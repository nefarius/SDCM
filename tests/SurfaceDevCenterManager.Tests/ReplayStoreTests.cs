/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using SurfaceDevCenterManager.Replay;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class ReplayStoreTests
{
    [Fact]
    public void NextId_SkipsNumericIdsLoadedFromCatalog()
    {
        ReplayStore store = ReplayStore.FromCatalog(new ReplayCatalog
        {
            Products = [new ReplayProduct { Id = "1" }],
            Submissions = [new ReplaySubmission { Id = "2", ProductId = "1" }]
        });

        Assert.Equal("3", store.NextId());
        Assert.Equal("4", store.NextId());
    }

    [Fact]
    public void SnapshotForGet_AppliesPollsSequentially()
    {
        ReplaySubmission submission = HandlerTestSupport.Replay("commitPending", "notStarted", "packageInfoValidation", "initialPackage");
        submission.Polls =
        [
            new ReplaySubmissionPoll
            {
                CommitStatus = "commitComplete",
                WorkflowStatus = new ReplayWorkflowStatus { State = "completed", CurrentStep = "scanning" }
            },
            new ReplaySubmissionPoll
            {
                WorkflowStatus = new ReplayWorkflowStatus { State = "completed", CurrentStep = "finalizeIngestion" }
            }
        ];
        ReplayStore store = HandlerTestSupport.StoreWith(submission);

        ReplaySubmission first = store.SnapshotForGet("1", "2");
        Assert.Equal("commitComplete", first.CommitStatus);
        Assert.Equal("scanning", first.WorkflowStatus?.CurrentStep);
        Assert.Contains(first.Downloads, d => d.Type == "initialPackage");

        ReplaySubmission second = store.SnapshotForGet("1", "2");
        Assert.Equal("commitComplete", second.CommitStatus);
        Assert.Equal("finalizeIngestion", second.WorkflowStatus?.CurrentStep);
        Assert.Contains(second.Downloads, d => d.Type == "initialPackage");
    }
}
