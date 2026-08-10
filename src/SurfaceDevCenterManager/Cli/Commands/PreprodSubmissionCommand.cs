/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SurfaceDevCenterManager.Handlers;

namespace SurfaceDevCenterManager.Cli.Commands;

internal static class PreprodSubmissionCommand
{
    public static Command Build(ServiceProviderAccessor accessor)
    {
        Option<string> packageId = Opt.Str("--package-id", "Preprod package id", true);

        // submit
        Option<string> submitPackage = Opt.Str("--package", "Path to the EV-signed package to submit for preprod signing", true);
        Command submit = new("submit", "Submit a package for preprod signing");
        submit.Options.Add(submitPackage);
        submit.SetHandlerAction(
            accessor,
            (pr, global) => new PreprodSubmitInput(pr.Required(submitPackage), global),
            (sp, i, ct) => sp.GetRequiredService<PreprodSubmitHandler>().RunAsync(i, ct));

        // status
        Command status = new("status", "Get a preprod package's metadata and signing status");
        status.Options.Add(packageId);
        status.SetHandlerAction(
            accessor,
            (pr, global) => new PreprodStatusInput(pr.Required(packageId), global),
            (sp, i, ct) => sp.GetRequiredService<PreprodStatusHandler>().RunAsync(i, ct));

        // assets
        Option<string?> assetId = Opt.OptionalStr("--asset-id", "Asset id to fetch; omit to list every asset for the package");
        Command assets = new("assets", "List a preprod package's available assets, or get one by id");
        assets.Options.Add(packageId);
        assets.Options.Add(assetId);
        assets.SetHandlerAction(
            accessor,
            (pr, global) => new PreprodAssetsInput(pr.Required(packageId), pr.GetValue(assetId), global),
            (sp, i, ct) => sp.GetRequiredService<PreprodAssetsHandler>().RunAsync(i, ct));

        // download
        Option<string> downloadAssetId = Opt.Str("--asset-id", "Asset id to download", true);
        Option<string> downloadOutputFile = Opt.Str("--output-file", "Destination file path for the downloaded asset", true);
        Command download = new("download", "Download a preprod package's signed asset");
        download.Options.Add(packageId);
        download.Options.Add(downloadAssetId);
        download.Options.Add(downloadOutputFile);
        download.SetHandlerAction(
            accessor,
            (pr, global) => new PreprodDownloadInput(
                pr.Required(packageId), pr.Required(downloadAssetId), pr.Required(downloadOutputFile), global),
            (sp, i, ct) => sp.GetRequiredService<PreprodDownloadHandler>().RunAsync(i, ct));

        // wait
        Option<uint> pollInterval = Opt.UInt("--poll-interval", "Seconds between status checks", 5);
        Option<uint?> waitTimeout = new("--wait-timeout") { Description = "Give up after this many seconds (default: wait indefinitely)" };
        Command wait = new("wait", "Wait for a preprod package to reach a terminal signing status");
        wait.Options.Add(packageId);
        wait.Options.Add(pollInterval);
        wait.Options.Add(waitTimeout);
        wait.SetHandlerAction(
            accessor,
            (pr, global) => new PreprodWaitInput(
                pr.Required(packageId), pr.GetValue(pollInterval), pr.GetValue(waitTimeout), global),
            (sp, i, ct) => sp.GetRequiredService<PreprodWaitHandler>().RunAsync(i, ct));

        Command preprodSubmission = new("preprod-submission", "Manage preprod driver submissions");
        preprodSubmission.Subcommands.Add(submit);
        preprodSubmission.Subcommands.Add(status);
        preprodSubmission.Subcommands.Add(assets);
        preprodSubmission.Subcommands.Add(download);
        preprodSubmission.Subcommands.Add(wait);
        return preprodSubmission;
    }
}
