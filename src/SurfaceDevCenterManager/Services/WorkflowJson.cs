/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;

namespace SurfaceDevCenterManager.Services;

internal static class WorkflowJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<object> SubmissionWithErrorReportAsync(
        Submission submission, IErrorReportFetcher fetcher, CancellationToken cancellationToken)
    {
        JsonNode node = JsonSerializer.SerializeToNode(submission, Options) ?? new JsonObject();
        await EmbedErrorReportAsync(node["workflowStatus"] as JsonObject, submission.WorkflowStatus?.ErrorReport,
            fetcher, cancellationToken).ConfigureAwait(false);
        return node;
    }

    public static async Task<SubmissionStatusDocument> StatusAsync(
        Submission submission, IErrorReportFetcher fetcher, CancellationToken cancellationToken)
    {
        WorkflowStatus? status = submission.WorkflowStatus;
        string? content = await fetcher.FetchAsync(status?.ErrorReport, cancellationToken).ConfigureAwait(false);
        return new SubmissionStatusDocument
        {
            Progress = SubmissionReadiness.ToProgress(submission),
            CommitStatus = submission.CommitStatus,
            State = status?.State,
            CurrentStep = status?.CurrentStep,
            HasSignedPackage = SubmissionReadiness.HasSignedPackage(submission),
            ErrorReport = status?.ErrorReport,
            ErrorReportContent = content
        };
    }

    public static void DumpStatus(SubmissionStatusDocument status)
    {
        Console.WriteLine($"progress: {status.Progress}");
        Console.WriteLine($"commitStatus: {status.CommitStatus ?? "-"}");
        Console.WriteLine($"state: {status.State ?? "-"}");
        Console.WriteLine($"currentStep: {status.CurrentStep ?? "-"}");
        Console.WriteLine($"hasSignedPackage: {status.HasSignedPackage}");
        if (!string.IsNullOrEmpty(status.ErrorReportContent))
        {
            Console.WriteLine("errorReport:");
            Console.WriteLine(status.ErrorReportContent);
        }
        else if (!string.IsNullOrEmpty(status.ErrorReport))
        {
            Console.WriteLine($"errorReport: {status.ErrorReport}");
        }
    }

    private static async Task EmbedErrorReportAsync(
        JsonObject? workflowStatus, string? errorReport, IErrorReportFetcher fetcher,
        CancellationToken cancellationToken)
    {
        if (workflowStatus == null)
        {
            return;
        }

        string? content = await fetcher.FetchAsync(errorReport, cancellationToken).ConfigureAwait(false);
        if (content != null)
        {
            workflowStatus["errorReportContent"] = content;
        }
    }
}
