/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json.Serialization;

namespace SurfaceDevCenterManager.Replay;

/// <summary>
///     Offline fixture file for <c>--replay</c> / <c>SDCM_REPLAY</c>. Paths in <see cref="Blobs"/>
///     are resolved relative to the fixture file.
/// </summary>
public sealed class ReplayCatalog
{
    [JsonPropertyName("products")]
    public List<ReplayProduct> Products { get; set; } = [];

    [JsonPropertyName("submissions")]
    public List<ReplaySubmission> Submissions { get; set; } = [];

    [JsonPropertyName("shippingLabels")]
    public List<ReplayShippingLabel> ShippingLabels { get; set; } = [];

    [JsonPropertyName("preprod")]
    public List<ReplayPreprodPackage> Preprod { get; set; } = [];

    [JsonPropertyName("blobs")]
    public Dictionary<string, ReplayBlob> Blobs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("errorReports")]
    public Dictionary<string, string> ErrorReports { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReplayProduct
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }

    [JsonPropertyName("testHarness")]
    public string? TestHarness { get; set; }
}

public sealed class ReplaySubmission
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("commitStatus")]
    public string? CommitStatus { get; set; }

    [JsonPropertyName("workflowStatus")]
    public ReplayWorkflowStatus? WorkflowStatus { get; set; }

    [JsonPropertyName("downloads")]
    public List<ReplayDownload> Downloads { get; set; } = [];

    /// <summary>Optional snapshots returned in order by each <c>GetSubmission</c> (for wait sequences).</summary>
    [JsonPropertyName("polls")]
    public List<ReplaySubmissionPoll>? Polls { get; set; }
}

public sealed class ReplaySubmissionPoll
{
    [JsonPropertyName("commitStatus")]
    public string? CommitStatus { get; set; }

    [JsonPropertyName("workflowStatus")]
    public ReplayWorkflowStatus? WorkflowStatus { get; set; }

    [JsonPropertyName("downloads")]
    public List<ReplayDownload>? Downloads { get; set; }
}

public sealed class ReplayWorkflowStatus
{
    [JsonPropertyName("currentStep")]
    public string? CurrentStep { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }

    [JsonPropertyName("errorReport")]
    public string? ErrorReport { get; set; }
}

public sealed class ReplayDownload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class ReplayShippingLabel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = "";

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("workflowStatus")]
    public ReplayWorkflowStatus? WorkflowStatus { get; set; }
}

public sealed class ReplayPreprodPackage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("signingStatus")]
    public string? SigningStatus { get; set; }

    [JsonPropertyName("assets")]
    public List<ReplayPreprodAsset> Assets { get; set; } = [];
}

public sealed class ReplayPreprodAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("assetType")]
    public string? AssetType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class ReplayBlob
{
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
