/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Shared terminal-state matching for the submission and shipping-label wait loops. Matches only
///     the documented state names exactly (case-insensitively) rather than a loose substring check, so
///     transient states such as "publishing" are not mistaken for the terminal "published"/"completed".
/// </summary>
internal static class WorkflowStatusExtensions
{
    private static readonly string[] FailedStates = ["failed"];
    private static readonly string[] SuccessTerminalStates = ["completed", "published"];

    public static bool IsFailed(this WorkflowStatus? status)
    {
        return status?.State != null && FailedStates.Contains(status.State, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTerminal(this WorkflowStatus? status)
    {
        return status.IsFailed()
            || (status?.State != null && SuccessTerminalStates.Contains(status.State, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Enforces a sane minimum for <c>--poll-interval</c> so it can never produce a busy loop.</summary>
internal static class PollingDefaults
{
    public const uint MinPollIntervalSeconds = 1;

    public static uint ClampPollInterval(uint requestedSeconds)
    {
        return Math.Max(MinPollIntervalSeconds, requestedSeconds);
    }
}
