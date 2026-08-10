/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Manages preprod driver submissions - getting a package signed by Microsoft for
///     preproduction testing, rather than for public release. See
///     <see href="https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/manage-preprod-submissions" />.
///     The <c>Microsoft.Devices.HardwareDevCenterManager</c> library has no support for these
///     endpoints, so sdcm implements them itself; see <see cref="DevCenterPreprodHandler" />.
/// </summary>
public interface IDevCenterPreprodHandler
{
    /// <summary>Submits a package for preprod signing, creating a new in-progress preprod submission.</summary>
    Task<DevCenterResponse<PreprodPackage>> SubmitPreprodPackage(string packagePath);

    /// <summary>Gets package metadata, including <c>signingStatus</c> and, once succeeded, its assets.</summary>
    Task<DevCenterResponse<PreprodPackage>> GetPreprodPackage(string packageId);

    /// <summary>Lists every available asset for a preprod package, or gets a single asset's metadata by id.</summary>
    Task<DevCenterResponse<PreprodPackageAsset>> GetPreprodPackageAssets(string packageId, string? assetId = null);

    /// <summary>Downloads a signed asset (a zip of the signed driver files) to <paramref name="outputFilePath" />.</summary>
    Task<DevCenterErrorDetails?> DownloadPreprodPackageAsset(string packageId, string assetId, string outputFilePath);
}
