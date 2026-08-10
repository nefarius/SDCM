/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Net.Http.Headers;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Attaches a bearer token acquired via MSAL to every outgoing request. Shared by
///     <see cref="InteractiveDevCenterHandler" /> and the preprod submission handler so both HTTP
///     clients built for the <see cref="AuthMode.Interactive" /> auth mode source their token the
///     same way.
/// </summary>
internal sealed class BearerTokenHandler(
    IAadTokenProvider tokenProvider,
    string clientId,
    string authority,
    string redirectUri,
    string resource,
    AadPromptMode promptMode) : DelegatingHandler(new HttpClientHandler())
{
    private string? _accessToken;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // MSAL's own cache makes repeated acquisitions within a process cheap (silent cache hit),
        // so there is no need to hand-roll a token cache or a request-retry-on-401 here.
        _accessToken = await tokenProvider.AcquireTokenAsync(
            clientId, authority, redirectUri, resource, promptMode, cancellationToken).ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
