/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using Microsoft.Extensions.Logging;
using SurfaceDevCenterManager.Cli;
using SurfaceDevCenterManager.Replay;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Tests;

internal static class HandlerTestSupport
{
    public static readonly GlobalInvocationOptions Global = new("default", AuthMode.Auto, AadPromptMode.Never, 30);

    public static Submission Submission(
        string? commitStatus, string? state, string? currentStep, params string[] downloadTypes)
    {
        return new Submission
        {
            Id = "2",
            ProductId = "1",
            Name = "test",
            CommitStatus = commitStatus,
            WorkflowStatus = new WorkflowStatus { State = state, CurrentStep = currentStep },
            Downloads = downloadTypes.Length == 0
                ? null
                : new Download
                {
                    Items = downloadTypes.Select(t => new Download.Item
                    {
                        Type = t,
                        Url = new Uri($"https://replay.invalid/{t}")
                    }).ToList()
                }
        };
    }

    public static ReplayStore StoreWith(ReplaySubmission submission, Action<ReplayStore>? configure = null)
    {
        ReplayStore store = ReplayStore.Empty();
        store.Products.Add(new ReplayProduct { Id = submission.ProductId, ProductName = "test" });
        store.AddSubmission(submission);
        configure?.Invoke(store);
        return store;
    }

    public static ErrorReporter Errors(IOutputWriter output)
    {
        return new ErrorReporter(output, new RunContext(), new SilentLogger());
    }

    public static ReplaySubmission Replay(
        string commitStatus, string state, string step, params string[] downloads)
    {
        return new ReplaySubmission
        {
            Id = "2",
            ProductId = "1",
            Name = "test",
            CommitStatus = commitStatus,
            WorkflowStatus = new ReplayWorkflowStatus { State = state, CurrentStep = step },
            Downloads = downloads.Select(t => new ReplayDownload
            {
                Type = t,
                Url = $"https://replay.invalid/{t}"
            }).ToList()
        };
    }

    private sealed class SilentLogger : ILogger<ErrorReporter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
