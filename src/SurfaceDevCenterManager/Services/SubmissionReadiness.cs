/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Submission-specific wait/status rules. <see cref="WorkflowStatus.State"/> describes
///     <see cref="WorkflowStatus.CurrentStep"/> only; a step of <c>completed</c> is not a finished
///     submission. Shared <see cref="WorkflowStatusExtensions.IsTerminal"/> stays shipping-label
///     oriented (<c>published</c> is terminal there) and must not be widened.
/// </summary>
internal static class SubmissionReadiness
{
    public const string SignedPackage = "signedPackage";
    public const string DriverMetadata = "driverMetadata";
    public const string InitialPackage = "initialPackage";
    public const string FinalizeIngestion = "finalizeIngestion";

    public const string ProgressCreated = "created";
    public const string ProgressProcessing = "processing";
    public const string ProgressCompleted = "completed";
    public const string ProgressFailed = "failed";

    public static bool HasDownload(Submission? submission, string type)
    {
        return submission?.Downloads?.Items?.Any(
            i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public static bool HasSignedPackage(Submission? submission) => HasDownload(submission, SignedPackage);

    public static bool HasDriverMetadata(Submission? submission) => HasDownload(submission, DriverMetadata);

    public static bool IsFailed(Submission? submission)
    {
        return CommitStatuses.IsFailed(submission?.CommitStatus) || submission?.WorkflowStatus.IsFailed() == true;
    }

    /// <summary>
    ///     Success gate for <c>submission wait</c>. A <c>signedPackage</c> download is authoritative.
    ///     <c>finalizeIngestion</c> + <c>completed</c> is a fallback only when
    ///     <see cref="WorkflowStatus.CurrentStep"/> is actually that step name. The service sometimes
    ///     returns <c>currentStep</c> as a number (library long-to-string converter), so a step-name
    ///     comparison cannot stand alone.
    /// </summary>
    public static bool IsReady(Submission? submission)
    {
        if (submission == null || IsFailed(submission))
        {
            return false;
        }

        if (HasSignedPackage(submission))
        {
            return true;
        }

        WorkflowStatus? status = submission.WorkflowStatus;
        return status != null &&
               string.Equals(status.State, "completed", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(status.CurrentStep, FinalizeIngestion, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWaitDone(Submission? submission, bool waitMetadata)
    {
        if (IsFailed(submission))
        {
            return !waitMetadata || HasDriverMetadata(submission);
        }

        return IsReady(submission) && (!waitMetadata || HasDriverMetadata(submission));
    }

    public static string ToProgress(Submission? submission)
    {
        if (IsFailed(submission))
        {
            return ProgressFailed;
        }

        if (IsReady(submission))
        {
            return ProgressCompleted;
        }

        if (CommitStatuses.IsPending(submission?.CommitStatus) ||
            string.IsNullOrEmpty(submission?.CommitStatus))
        {
            WorkflowStatus? status = submission?.WorkflowStatus;
            if (status != null &&
                (string.Equals(status.State, "started", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status.State, "completed", StringComparison.OrdinalIgnoreCase)))
            {
                return ProgressProcessing;
            }

            return ProgressCreated;
        }

        return ProgressProcessing;
    }
}
