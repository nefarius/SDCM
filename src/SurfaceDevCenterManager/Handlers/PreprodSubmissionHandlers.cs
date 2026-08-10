/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Cli;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Handlers;

public sealed record PreprodSubmitInput(string PackagePath, GlobalInvocationOptions Global);

public sealed class PreprodSubmitHandler(IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(PreprodSubmitInput input, CancellationToken cancellationToken)
    {
        if (!File.Exists(input.PackagePath))
        {
            output.Error($"Package file not found: {input.PackagePath}");
            return ExitCode.IoError;
        }

        return await factory.UsePreprodAsync(input.Global, output, async api =>
        {
            output.Progress($"Submitting '{input.PackagePath}' for preprod signing...");
            try
            {
                DevCenterResponse<PreprodPackage> response = await api
                    .SubmitPreprodPackage(input.PackagePath).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out PreprodPackage package))
                {
                    return ExitCode.InvalidState;
                }

                output.Result(package, p => p.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "preprod-submission submit");
            }
        }, cancellationToken);
    }
}

public sealed record PreprodStatusInput(string PackageId, GlobalInvocationOptions Global);

public sealed class PreprodStatusHandler(IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(PreprodStatusInput input, CancellationToken cancellationToken)
    {
        return await factory.UsePreprodAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<PreprodPackage> response = await api
                    .GetPreprodPackage(input.PackageId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                if (!response.TryGetSingle(output, out PreprodPackage package))
                {
                    return ExitCode.InvalidState;
                }

                output.Result(package, p => p.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "preprod-submission status");
            }
        }, cancellationToken);
    }
}

public sealed record PreprodAssetsInput(string PackageId, string? AssetId, GlobalInvocationOptions Global);

public sealed class PreprodAssetsHandler(IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(PreprodAssetsInput input, CancellationToken cancellationToken)
    {
        return await factory.UsePreprodAsync(input.Global, output, async api =>
        {
            try
            {
                DevCenterResponse<PreprodPackageAsset> response = await api
                    .GetPreprodPackageAssets(input.PackageId, input.AssetId).ConfigureAwait(false);
                if (response.Error != null)
                {
                    return errors.Report(response.Error);
                }

                output.Results(response.ReturnValue ?? [], a => a.Dump());
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "preprod-submission assets");
            }
        }, cancellationToken);
    }
}

public sealed record PreprodDownloadInput(
    string PackageId, string AssetId, string OutputFile, GlobalInvocationOptions Global);

public sealed class PreprodDownloadHandler(
    IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    public async Task<ExitCode> RunAsync(PreprodDownloadInput input, CancellationToken cancellationToken)
    {
        if (File.Exists(input.OutputFile))
        {
            output.Error($"Destination already exists: {input.OutputFile}");
            return ExitCode.IoError;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(input.OutputFile));
        if (directory != null && !Directory.Exists(directory))
        {
            output.Error($"Destination directory does not exist: {directory}");
            return ExitCode.IoError;
        }

        return await factory.UsePreprodAsync(input.Global, output, async api =>
        {
            try
            {
                output.Progress($"Downloading to '{input.OutputFile}'...");
                DevCenterErrorDetails? error = await api
                    .DownloadPreprodPackageAsset(input.PackageId, input.AssetId, input.OutputFile)
                    .ConfigureAwait(false);
                if (error != null)
                {
                    return errors.Report(error);
                }

                output.Progress("Download complete.");
                return ExitCode.Success;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "preprod-submission download");
            }
        }, cancellationToken);
    }
}

public sealed record PreprodWaitInput(
    string PackageId, uint PollIntervalSeconds, uint? WaitTimeoutSeconds, GlobalInvocationOptions Global);

public sealed class PreprodWaitHandler(IDevCenterHandlerFactory factory, IOutputWriter output, IErrorReporter errors)
{
    private const string Succeeded = "Succeeded";
    private const string Failed = "Failed";

    public async Task<ExitCode> RunAsync(PreprodWaitInput input, CancellationToken cancellationToken)
    {
        using CancellationTokenSource? timeoutCts = input.WaitTimeoutSeconds is { } seconds
            ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds))
            : null;
        using CancellationTokenSource linkedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return await factory.UsePreprodAsync(input.Global, output, async api =>
        {
            try
            {
                while (true)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    DevCenterResponse<PreprodPackage> response = await api
                        .GetPreprodPackage(input.PackageId).ConfigureAwait(false);
                    if (response.Error != null)
                    {
                        return errors.Report(response.Error);
                    }

                    if (!response.TryGetSingle(output, out PreprodPackage package))
                    {
                        return ExitCode.InvalidState;
                    }

                    if (output.Format == OutputFormat.Text)
                    {
                        output.Progress($"signingStatus: {package.SigningStatus}");
                    }

                    bool failed = string.Equals(package.SigningStatus, Failed, StringComparison.OrdinalIgnoreCase);
                    bool succeeded = string.Equals(package.SigningStatus, Succeeded, StringComparison.OrdinalIgnoreCase);

                    if (failed || succeeded)
                    {
                        output.Result(package, p => p.Dump());
                        return failed ? ExitCode.WorkflowFailed : ExitCode.Success;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(PollingDefaults.ClampPollInterval(input.PollIntervalSeconds)), linkedCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
            {
                output.Error($"Timed out after {input.WaitTimeoutSeconds}s waiting for the preprod package to reach a terminal state.");
                return ExitCode.Canceled;
            }
            catch (OperationCanceledException)
            {
                return ExitCode.Canceled;
            }
            catch (Exception ex)
            {
                return errors.ReportException(ex, "preprod-submission wait");
            }
        }, linkedCts.Token);
    }
}
