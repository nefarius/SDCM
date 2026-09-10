/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Replay;

public sealed class ReplayDevCenterPreprodHandler(ReplayStore store) : IDevCenterPreprodHandler
{
    public Task<DevCenterResponse<PreprodPackage>> SubmitPreprodPackage(string packagePath)
    {
        ReplayPreprodPackage package = new()
        {
            Id = store.NextId(),
            SigningStatus = "Succeeded",
            Assets =
            [
                new ReplayPreprodAsset
                {
                    Id = store.NextId(),
                    AssetType = "SignedFilesZip",
                    Url = $"https://replay.invalid/preprod/{Path.GetFileName(packagePath)}"
                }
            ]
        };
        store.Preprod.Add(package);
        return Task.FromResult(ReplayStore.Ok(ToPackage(package)));
    }

    public Task<DevCenterResponse<PreprodPackage>> GetPreprodPackage(
        string packageId, CancellationToken cancellationToken = default)
    {
        ReplayPreprodPackage? package = store.Preprod.FirstOrDefault(p => ReplayStore.IdsEqual(p.Id, packageId));
        return Task.FromResult(package == null
            ? ReplayStore.NotFound<PreprodPackage>($"Preprod package {packageId} not found.")
            : ReplayStore.Ok(ToPackage(package)));
    }

    public Task<DevCenterResponse<PreprodPackageAsset>> GetPreprodPackageAssets(string packageId, string? assetId = null)
    {
        ReplayPreprodPackage? package = store.Preprod.FirstOrDefault(p => ReplayStore.IdsEqual(p.Id, packageId));
        if (package == null)
        {
            return Task.FromResult(ReplayStore.NotFound<PreprodPackageAsset>($"Preprod package {packageId} not found."));
        }

        IEnumerable<ReplayPreprodAsset> assets = package.Assets;
        if (!string.IsNullOrEmpty(assetId))
        {
            assets = assets.Where(a => ReplayStore.IdsEqual(a.Id, assetId));
        }

        return Task.FromResult(ReplayStore.Ok(assets.Select(ToAsset).ToArray()));
    }

    public async Task<DevCenterErrorDetails?> DownloadPreprodPackageAsset(
        string packageId, string assetId, string outputFilePath)
    {
        ReplayPreprodPackage? package = store.Preprod.FirstOrDefault(p => ReplayStore.IdsEqual(p.Id, packageId));
        ReplayPreprodAsset? asset = package?.Assets.FirstOrDefault(a => ReplayStore.IdsEqual(a.Id, assetId));
        if (asset?.Url == null)
        {
            return new DevCenterErrorDetails
            {
                Code = "entityNotFound",
                Message = $"Preprod asset {packageId}/{assetId} not found."
            };
        }

        await store.DownloadAsync(new Uri(asset.Url, UriKind.RelativeOrAbsolute), outputFilePath, CancellationToken.None)
            .ConfigureAwait(false);
        return null;
    }

    public void Dispose()
    {
    }

    private static PreprodPackage ToPackage(ReplayPreprodPackage source)
    {
        return new PreprodPackage
        {
            Id = source.Id,
            SigningStatus = source.SigningStatus,
            Assets = source.Assets.Select(ToAsset).ToList()
        };
    }

    private static PreprodPackageAsset ToAsset(ReplayPreprodAsset source)
    {
        return new PreprodPackageAsset
        {
            Id = source.Id,
            AssetType = source.AssetType
        };
    }
}
