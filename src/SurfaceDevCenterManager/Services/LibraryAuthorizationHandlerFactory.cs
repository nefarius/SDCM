/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Reflection;
using Microsoft.Devices.HardwareDevCenterManager.Utility;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Builds the <c>Microsoft.Devices.HardwareDevCenterManager.Utility.AuthorizationHandler</c>
///     <see cref="DelegatingHandler" /> that the library's own <c>DevCenterHandler</c> uses
///     internally for managed identity / client secret / certificate token acquisition.
/// </summary>
/// <remarks>
///     That type is declared <c>internal</c> to the library assembly (confirmed via reflection: its
///     constructor is public, but <see cref="Type.IsPublic" /> on the type itself is
///     <see langword="false" />), so it can't be referenced directly from sdcm's source. Rather than
///     reimplementing its Azure.Identity-based token acquisition for managed identity / client secret
///     credentials (a second, unmaintained copy of security-sensitive logic that the library authors
///     already got right and keep updated), sdcm activates it via reflection - only its public base
///     type (<see cref="DelegatingHandler" />) and public constructor are relied upon, so this is
///     immune to the library renaming private members, but would need revisiting if a future version
///     renames/removes the <c>AuthorizationHandler</c> type itself or changes its constructor shape.
///     Pinned exactly to the version tested against - see
///     <c>Directory.Packages.props</c>' <c>Microsoft.Devices.HardwareDevCenterManager</c> entry.
/// </remarks>
internal static class LibraryAuthorizationHandlerFactory
{
    private const string TypeName = "Microsoft.Devices.HardwareDevCenterManager.Utility.AuthorizationHandler";

    public static DelegatingHandler Create(AuthorizationHandlerCredentials credentials, uint httpTimeoutSeconds)
    {
        Type handlerType = typeof(AuthorizationHandlerCredentials).Assembly.GetType(TypeName)
            ?? throw new InvalidOperationException(
                $"{TypeName} was not found in Microsoft.Devices.HardwareDevCenterManager; " +
                "the installed library version may no longer be compatible with sdcm's preprod support.");

        object instance = Activator.CreateInstance(
            handlerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [credentials, httpTimeoutSeconds],
            null) ?? throw new InvalidOperationException($"Failed to construct {TypeName}.");

        DelegatingHandler handler = (DelegatingHandler)instance;
        handler.InnerHandler = new HttpClientHandler();
        return handler;
    }
}
