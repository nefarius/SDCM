/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using Microsoft.Extensions.Options;
using SurfaceDevCenterManager.Configuration;

namespace SurfaceDevCenterManager.Services;

public sealed class DevCenterHandlerFactory(
    ICredentialsProvider credentialsProvider,
    IAadTokenProvider tokenProvider,
    IOptions<DevCenterAppOptions> appOptions,
    RunContext runContext) : IDevCenterHandlerFactory
{
    public Task<IDevCenterHandler> CreateAsync(
        string profileName, AuthMode authMode, AadPromptMode promptMode, uint httpTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ResolvedCredentials resolved = credentialsProvider.Resolve(profileName, authMode);
        DevCenterOptions options = BuildOptions(httpTimeoutSeconds);

        if (resolved.LibraryCredentials != null)
        {
            return Task.FromResult<IDevCenterHandler>(new DevCenterHandler(resolved.LibraryCredentials, options));
        }

        IDevCenterHandler handler = new InteractiveDevCenterHandler(
            tokenProvider,
            resolved.Profile.ClientId!,
            resolved.Authority,
            appOptions.Value.RedirectUri,
            resolved.Url,
            resolved.UrlPrefix,
            promptMode,
            options);

        return Task.FromResult(handler);
    }

    public Task<IDevCenterPreprodHandler> CreatePreprodAsync(
        string profileName, AuthMode authMode, AadPromptMode promptMode, uint httpTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ResolvedCredentials resolved = credentialsProvider.Resolve(profileName, authMode);
        DevCenterOptions options = BuildOptions(httpTimeoutSeconds);

        HttpClient client = resolved.LibraryCredentials != null
            ? new HttpClient(LibraryAuthorizationHandlerFactory.Create(resolved.LibraryCredentials, httpTimeoutSeconds))
            : new HttpClient(new BearerTokenHandler(
                tokenProvider,
                resolved.Profile.ClientId!,
                resolved.Authority,
                appOptions.Value.RedirectUri,
                resolved.Url,
                promptMode), true);

        client.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);

        IDevCenterPreprodHandler handler =
            new DevCenterPreprodHandler(client, resolved.Url, resolved.UrlPrefix, options);

        return Task.FromResult(handler);
    }

    private DevCenterOptions BuildOptions(uint httpTimeoutSeconds)
    {
        return new DevCenterOptions
        {
            CorrelationId = runContext.CorrelationId,
            HttpTimeoutSeconds = httpTimeoutSeconds,
            RequestDelayMs = 250,
            LastCommand = runContext.SetLastCommand
        };
    }
}
