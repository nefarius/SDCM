/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Helpers for handling <see cref="DevCenterResponse{T}" /> shapes that are only well-formed when
///     the API actually returned an entity, guarding against a success response with a null/empty
///     <c>ReturnValue</c> (seen in practice from a handful of Hardware Dev Center endpoints).
/// </summary>
internal static class DevCenterResponseExtensions
{
    public static bool TryGetSingle<T>(this DevCenterResponse<T> response, IOutputWriter output, out T value)
    {
        if (response.ReturnValue is { Count: > 0 })
        {
            value = response.ReturnValue[0];
            return true;
        }

        output.Error("Hardware Dev Center returned a success response with no data.");
        value = default!;
        return false;
    }
}
