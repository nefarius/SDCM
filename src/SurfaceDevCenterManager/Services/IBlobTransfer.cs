/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Uploads and downloads the SAS-backed blobs on a submission. Isolated from
///     <c>BlobStorageHandler</c> so handler tests and <c>--replay</c> can run without Azure.
/// </summary>
public interface IBlobTransfer
{
    Task UploadAsync(Uri url, string localPath, CancellationToken cancellationToken);

    Task DownloadAsync(Uri url, string localPath, CancellationToken cancellationToken);

    Task<string> DownloadToStringAsync(Uri url, CancellationToken cancellationToken);
}
