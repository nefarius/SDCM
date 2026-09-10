/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json.Serialization;

namespace SurfaceDevCenterManager.Models;

public sealed class UploadResult
{
    [JsonPropertyName("productId")]
    public string ProductId { get; init; } = "";

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; init; } = "";

    [JsonPropertyName("packagePath")]
    public string PackagePath { get; init; } = "";
}

public sealed class CommitResult
{
    [JsonPropertyName("productId")]
    public string ProductId { get; init; } = "";

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; init; } = "";

    [JsonPropertyName("commitStatus")]
    public string? CommitStatus { get; init; }

    [JsonPropertyName("alreadyCommitted")]
    public bool AlreadyCommitted { get; init; }
}

public sealed class DownloadResult
{
    [JsonPropertyName("productId")]
    public string? ProductId { get; init; }

    [JsonPropertyName("submissionId")]
    public string? SubmissionId { get; init; }

    [JsonPropertyName("packageId")]
    public string? PackageId { get; init; }

    [JsonPropertyName("assetId")]
    public string? AssetId { get; init; }

    [JsonPropertyName("outputFile")]
    public string OutputFile { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";
}

public sealed class MetadataCreateResult
{
    [JsonPropertyName("productId")]
    public string ProductId { get; init; } = "";

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; init; } = "";

    [JsonPropertyName("requested")]
    public bool Requested { get; init; } = true;
}
