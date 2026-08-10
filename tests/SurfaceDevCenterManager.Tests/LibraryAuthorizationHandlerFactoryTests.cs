/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.Utility;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

/// <summary>
///     <see cref="LibraryAuthorizationHandlerFactory" /> reaches into an internal library type via
///     reflection (see its remarks for why), which is exactly the kind of thing that silently breaks
///     on a library version bump. This test is the tripwire: if the type is renamed/removed or its
///     constructor shape changes, this fails loudly instead of only surfacing as a runtime
///     <c>AuthenticationFailed</c> exit code the first time someone runs a non-interactive preprod
///     command.
/// </summary>
public class LibraryAuthorizationHandlerFactoryTests
{
    [Fact]
    public void Create_ReturnsUsableDelegatingHandlerWithInnerHandlerSet()
    {
        AuthorizationHandlerCredentials credentials = new()
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            ClientId = "22222222-2222-2222-2222-222222222222",
            Key = "not-a-real-secret",
            Authority = "https://login.microsoftonline.com/organizations/",
            Url = new Uri("https://manage.devcenter.microsoft.com"),
            UrlPrefix = new Uri("v2.0/my", UriKind.Relative)
        };

        using DelegatingHandler handler = LibraryAuthorizationHandlerFactory.Create(credentials, 300);

        Assert.NotNull(handler);
        Assert.NotNull(handler.InnerHandler);
        Assert.Equal(
            "Microsoft.Devices.HardwareDevCenterManager.Utility.AuthorizationHandler",
            handler.GetType().FullName);
    }
}
