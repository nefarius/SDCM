/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json.Serialization;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Models;

/// <summary>
///     Package metadata resource returned by the preprod submission endpoints. Modeled after the
///     JSON shapes documented in
///     <see href="https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/manage-preprod-submissions" />,
///     since the <c>Microsoft.Devices.HardwareDevCenterManager</c> library has no equivalent type.
///     Every property carries an explicit <see cref="JsonPropertyNameAttribute" /> rather than
///     relying on an ambient naming policy, since the executor that (de)serializes these calls
///     <c>JsonSerializer</c> with no options.
/// </summary>
public sealed class PreprodPackage : IArtifact
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>NotStarted, Processing, Succeeded, or Failed.</summary>
    [JsonPropertyName("signingStatus")]
    public string? SigningStatus { get; set; }

    [JsonPropertyName("error")]
    public PreprodPackageError? Error { get; set; }

    [JsonPropertyName("assets")]
    public List<PreprodPackageAsset>? Assets { get; set; }

    [JsonPropertyName("assetsContinuationToken")]
    public string? AssetsContinuationToken { get; set; }
}

public sealed class PreprodPackageError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>Asset metadata resource - one signed/available download for a preprod package.</summary>
public sealed class PreprodPackageAsset : IArtifact
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    /// <summary>E.g. "SignedFilesZip" - the package signed by Microsoft.</summary>
    [JsonPropertyName("assetType")]
    public string? AssetType { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>SHA-256 hash of the asset content.</summary>
    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}
