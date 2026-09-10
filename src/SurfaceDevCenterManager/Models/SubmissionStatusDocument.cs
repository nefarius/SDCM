/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json.Serialization;

namespace SurfaceDevCenterManager.Models;

/// <summary>
///     Normalized submission progress. <c>state</c> is the state of <c>currentStep</c>, not of the
///     submission; use <see cref="Progress"/> instead of treating <c>completed</c> as done.
/// </summary>
public sealed class SubmissionStatusDocument
{
    [JsonPropertyName("progress")]
    public string Progress { get; init; } = "";

    [JsonPropertyName("commitStatus")]
    public string? CommitStatus { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("currentStep")]
    public string? CurrentStep { get; init; }

    [JsonPropertyName("hasSignedPackage")]
    public bool HasSignedPackage { get; init; }

    [JsonPropertyName("errorReport")]
    public string? ErrorReport { get; init; }

    [JsonPropertyName("errorReportContent")]
    public string? ErrorReportContent { get; init; }
}
