/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json;
using Microsoft.Devices.HardwareDevCenterManager;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     <see cref="IDevCenterPreprodHandler" /> implementation talking directly to the preprod
///     endpoints documented at
///     <see href="https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/manage-preprod-submissions" />,
///     since the <c>Microsoft.Devices.HardwareDevCenterManager</c> library has no support for them.
///     Mirrors <see cref="InteractiveDevCenterHandler" />'s shape: owns an already-authenticated
///     <see cref="HttpClient" /> (built by <see cref="DevCenterHandlerFactory" /> for whichever auth
///     mode is active) and delegates request/error handling to <see cref="HdcJsonRequestExecutor" />.
/// </summary>
public sealed class DevCenterPreprodHandler : IDevCenterPreprodHandler, IDisposable
{
    private const string PackagesUrl = "/hardware/preprod/packages/";
    private const string PackageUrl = "/hardware/preprod/packages/{0}";
    private const string PackageAssetsUrl = "/hardware/preprod/packages/{0}/assets";
    private const string PackageAssetUrl = "/hardware/preprod/packages/{0}/assets/{1}";
    private const string PackageAssetDownloadUrl = "/hardware/preprod/packages/{0}/assets/{1}/download";

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly HdcJsonRequestExecutor _executor;

    public DevCenterPreprodHandler(HttpClient client, string url, string urlPrefix, DevCenterOptions options)
    {
        _client = client;
        _baseUrl = new Uri(new Uri(url, UriKind.Absolute), urlPrefix).AbsoluteUri;
        _executor = new HdcJsonRequestExecutor(client, options.CorrelationId, options.LastCommand);
    }

    public async Task<DevCenterResponse<PreprodPackage>> SubmitPreprodPackage(string packagePath)
    {
        DevCenterResponse<PreprodPackage> response = new();
        (response.Error, response.Trace) = await _executor.PutBinaryAsync(_baseUrl + PackagesUrl, packagePath, content =>
        {
            PreprodPackage? parsed = JsonSerializer.Deserialize<PreprodPackage>(content);
            if (parsed?.Id != null)
            {
                response.ReturnValue = [parsed];
            }
        }).ConfigureAwait(false);

        return response;
    }

    public Task<DevCenterResponse<PreprodPackage>> GetPreprodPackage(
        string packageId, CancellationToken cancellationToken = default)
    {
        string url = _baseUrl + string.Format(PackageUrl, Uri.EscapeDataString(packageId));
        return _executor.HdcGet<PreprodPackage>(url, false, cancellationToken);
    }

    public async Task<DevCenterResponse<PreprodPackageAsset>> GetPreprodPackageAssets(
        string packageId, string? assetId = null)
    {
        if (!string.IsNullOrEmpty(assetId))
        {
            string singleUrl = _baseUrl + string.Format(
                PackageAssetUrl, Uri.EscapeDataString(packageId), Uri.EscapeDataString(assetId));
            return await _executor.HdcGet<PreprodPackageAsset>(singleUrl, false).ConfigureAwait(false);
        }

        // Unlike the library-backed list endpoints (products, submissions, shipping labels,
        // audiences), which wrap collections in a Response<T>/"value" envelope, this endpoint
        // returns a plain JSON array, so it can't go through HdcGet's isMany:true path.
        string url = _baseUrl + string.Format(PackageAssetsUrl, Uri.EscapeDataString(packageId));
        DevCenterResponse<PreprodPackageAsset> response = new();
        (response.Error, response.Trace) = await _executor.InvokeHdcServiceCore(HttpMethod.Get, url, null, content =>
        {
            response.ReturnValue = JsonSerializer.Deserialize<List<PreprodPackageAsset>>(content);
        }).ConfigureAwait(false);

        return response;
    }

    public Task<DevCenterErrorDetails?> DownloadPreprodPackageAsset(
        string packageId, string assetId, string outputFilePath)
    {
        string url = _baseUrl + string.Format(
            PackageAssetDownloadUrl, Uri.EscapeDataString(packageId), Uri.EscapeDataString(assetId));
        return _executor.DownloadBinaryAsync(url, outputFilePath);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
