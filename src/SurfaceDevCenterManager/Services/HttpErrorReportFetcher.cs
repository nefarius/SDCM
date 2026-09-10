/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

namespace SurfaceDevCenterManager.Services;

public sealed class HttpErrorReportFetcher(IBlobTransfer blobs) : IErrorReportFetcher
{
    public async Task<string?> FetchAsync(string? errorReport, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(errorReport) ||
            !Uri.TryCreate(errorReport, UriKind.Absolute, out Uri? url) ||
            url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            return await blobs.DownloadToStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(failed to download error report: {ex.Message})";
        }
    }
}
