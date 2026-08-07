/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Replaces the old ADAL-based interactive flow. Instead of using the acquired token to call a
///     companion web API that hands back separately-configured Hardware Dev Center credentials, the
///     token is used directly against Hardware Dev Center via <see cref="InteractiveDevCenterHandler" />.
/// </summary>
public sealed class MsalAadTokenProvider(ILogger<MsalAadTokenProvider> logger) : IAadTokenProvider
{
    public async Task<string> AcquireTokenAsync(
        string clientId,
        string authority,
        string redirectUri,
        string resource,
        AadPromptMode promptMode,
        CancellationToken cancellationToken)
    {
        IPublicClientApplication app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(authority)
            .WithRedirectUri(redirectUri)
            .Build();

        await RegisterTokenCacheAsync(app).ConfigureAwait(false);

        string[] scopes = [resource.TrimEnd('/') + "/.default"];

        // "always" and "select-account" are explicit requests to bypass any cached/silent session,
        // so they should go straight to the interactive flow rather than trying silently first.
        bool forceInteractive = promptMode is AadPromptMode.Always or AadPromptMode.SelectAccount;

        AuthenticationResult? result = forceInteractive
            ? null
            : await TryAcquireSilentAsync(app, scopes, promptMode, cancellationToken).ConfigureAwait(false);

        if (result is null && promptMode != AadPromptMode.Never)
        {
            result = await AcquireInteractiveAsync(app, scopes, promptMode, cancellationToken).ConfigureAwait(false);
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                "Unable to acquire an Azure AD access token. No cached session was available and interactive sign-in was not attempted (--aad never). Use --aad prompt to allow an interactive sign-in.");
        }

        return result.AccessToken;
    }

    private async Task<AuthenticationResult?> TryAcquireSilentAsync(
        IPublicClientApplication app, string[] scopes, AadPromptMode promptMode, CancellationToken cancellationToken)
    {
        IEnumerable<IAccount> accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        IAccount? account = accounts.FirstOrDefault();
        if (account is null)
        {
            return null;
        }

        try
        {
            var silentBuilder = app.AcquireTokenSilent(scopes, account);
            if (promptMode == AadPromptMode.RefreshSession)
            {
                silentBuilder = silentBuilder.WithForceRefresh(true);
            }

            return await silentBuilder.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException ex)
        {
            logger.LogDebug(ex, "Silent token acquisition requires interaction");
            return null;
        }
    }

    private static async Task<AuthenticationResult?> AcquireInteractiveAsync(
        IPublicClientApplication app, string[] scopes, AadPromptMode promptMode, CancellationToken cancellationToken)
    {
        Prompt prompt = promptMode switch
        {
            AadPromptMode.Always => Prompt.ForceLogin,
            AadPromptMode.RefreshSession => Prompt.ForceLogin,
            AadPromptMode.SelectAccount => Prompt.SelectAccount,
            AadPromptMode.Prompt => Prompt.SelectAccount,
            _ => Prompt.SelectAccount
        };

        // Let MsalException (and OperationCanceledException) propagate with their original error code
        // and description instead of being swallowed into a generic "not attempted" message.
        return await app.AcquireTokenInteractive(scopes)
            .WithPrompt(prompt)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RegisterTokenCacheAsync(IPublicClientApplication app)
    {
        string cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sdcm", "msal-cache");
        Directory.CreateDirectory(cacheDir);

        StorageCreationPropertiesBuilder builder = new StorageCreationPropertiesBuilder("sdcm.msal.cache", cacheDir);
        if (OperatingSystem.IsMacOS())
        {
            builder = builder.WithMacKeyChain("com.nefarius.sdcm", "sdcm.msal.cache");
        }
        else if (OperatingSystem.IsLinux())
        {
            // No native keyring integration on Linux yet; fall back to a plain file rather than
            // failing outright.
            builder = builder.WithLinuxUnprotectedFile();
        }

        MsalCacheHelper cacheHelper = await MsalCacheHelper.CreateAsync(builder.Build()).ConfigureAwait(false);
        cacheHelper.RegisterCache(app.UserTokenCache);
    }
}
