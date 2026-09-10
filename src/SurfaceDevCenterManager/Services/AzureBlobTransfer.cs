/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.Utility;

namespace SurfaceDevCenterManager.Services;

public sealed class AzureBlobTransfer : IBlobTransfer
{
    public Task UploadAsync(Uri url, string localPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new BlobStorageHandler(url.ToString()).Upload(localPath);
    }

    public Task DownloadAsync(Uri url, string localPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new BlobStorageHandler(url.ToString()).Download(localPath);
    }

    public Task<string> DownloadToStringAsync(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new BlobStorageHandler(url.ToString()).DownloadToString();
    }
}
