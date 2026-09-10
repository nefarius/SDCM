/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using SurfaceDevCenterManager;
using SurfaceDevCenterManager.Handlers;
using SurfaceDevCenterManager.Replay;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class ShippingLabelHandlerTests
{
    [Fact]
    public async Task Wait_CompletedOnIntermediateStep_IsNotSuccess()
    {
        ReplayStore store = ReplayStore.Empty();
        store.Products.Add(new ReplayProduct { Id = "1" });
        store.AddSubmission(HandlerTestSupport.Replay("commitComplete", "completed", "finalizeIngestion", "signedPackage"));
        store.ShippingLabels.Add(new ReplayShippingLabel
        {
            Id = "3",
            ProductId = "1",
            SubmissionId = "2",
            WorkflowStatus = new ReplayWorkflowStatus { State = "completed", CurrentStep = "processing" }
        });

        ExitCode exit = await Wait(store, waitTimeoutSeconds: 1);
        Assert.Equal(ExitCode.Canceled, exit);
    }

    [Fact]
    public async Task Wait_Published_Succeeds()
    {
        ReplayStore store = ReplayStore.Empty();
        store.ShippingLabels.Add(new ReplayShippingLabel
        {
            Id = "3",
            ProductId = "1",
            SubmissionId = "2",
            WorkflowStatus = new ReplayWorkflowStatus { State = "published", CurrentStep = "publishing" }
        });

        ExitCode exit = await Wait(store);
        Assert.Equal(ExitCode.Success, exit);
    }

    [Fact]
    public async Task Wait_Failed_IsTerminal()
    {
        ReplayStore store = ReplayStore.Empty();
        store.ShippingLabels.Add(new ReplayShippingLabel
        {
            Id = "3",
            ProductId = "1",
            SubmissionId = "2",
            WorkflowStatus = new ReplayWorkflowStatus { State = "failed", CurrentStep = "processing" }
        });

        ExitCode exit = await Wait(store);
        Assert.Equal(ExitCode.WorkflowFailed, exit);
    }

    private static Task<ExitCode> Wait(ReplayStore store, uint? waitTimeoutSeconds = 5)
    {
        RecordingOutputWriter output = new();
        ShippingLabelWaitHandler handler = new(store, output, HandlerTestSupport.Errors(output));
        return handler.RunAsync(
            new ShippingLabelWaitInput("1", "2", "3", 1, waitTimeoutSeconds, HandlerTestSupport.Global),
            CancellationToken.None);
    }
}
