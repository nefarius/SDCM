/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Downloads the blob behind <c>workflowStatus.errorReport</c> so JSON output can include the
///     contents, not just the URL. Text mode already does this via <c>WorkflowStatus.Dump()</c>.
/// </summary>
public interface IErrorReportFetcher
{
    Task<string?> FetchAsync(string? errorReport, CancellationToken cancellationToken);
}
