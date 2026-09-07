/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SurfaceDevCenterManager.Handlers;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Cli.Commands;

internal static class ConfigCommand
{
    public static Command Build(ServiceProviderAccessor accessor)
    {
        Command path = new("path", "Show where sdcm looks for authconfig.json, and which file it resolved");
        path.SetAction(async (parseResult, cancellationToken) =>
        {
            string? explicitConfig = parseResult.GetValue(GlobalOptions.Config);
            ConfigPathInput input = new(explicitConfig);
            ExitCode exitCode = await accessor.Provider.GetRequiredService<ConfigPathHandler>()
                .RunAsync(input, cancellationToken).ConfigureAwait(false);
            return (int)exitCode;
        });

        Option<bool> force = Opt.Flag("--force", "Overwrite an existing authconfig.json");
        Command init = new("init", "Write a starter authconfig.json into the per-user config directory");
        init.Options.Add(force);
        init.SetAction(async (parseResult, cancellationToken) =>
        {
            ConfigInitInput input = new(parseResult.GetValue(force));
            ExitCode exitCode = await accessor.Provider.GetRequiredService<ConfigInitHandler>()
                .RunAsync(input, cancellationToken).ConfigureAwait(false);
            return (int)exitCode;
        });

        Option<string?> tenantId = Opt.OptionalStr("--tenant-id", "Entra tenant id");
        Option<string?> clientId = Opt.OptionalStr("--client-id", "Entra application (client) id");
        Option<string?> key = Opt.OptionalStr("--key", "Partner Center API key (client secret)");
        Command set = new("set", "Write tenantId, clientId, and/or key into an authconfig.json profile");
        set.Options.Add(tenantId);
        set.Options.Add(clientId);
        set.Options.Add(key);
        set.SetAction(async (parseResult, cancellationToken) =>
        {
            ConfigSetInput input = new(
                parseResult.GetValue(GlobalOptions.Config),
                parseResult.GetValue(GlobalOptions.Profile) ?? "default",
                parseResult.GetValue(tenantId),
                parseResult.GetValue(clientId),
                parseResult.GetValue(key));
            ExitCode exitCode = await accessor.Provider.GetRequiredService<ConfigSetHandler>()
                .RunAsync(input, cancellationToken).ConfigureAwait(false);
            return (int)exitCode;
        });

        Command config = new("config", "Inspect, initialize, and update sdcm's configuration");
        config.Subcommands.Add(path);
        config.Subcommands.Add(init);
        config.Subcommands.Add(set);
        return config;
    }
}
