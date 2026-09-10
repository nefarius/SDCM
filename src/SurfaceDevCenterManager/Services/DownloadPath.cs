/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

namespace SurfaceDevCenterManager.Services;

internal static class DownloadPath
{
    public static ExitCode Prepare(string outputFile, bool overwrite, IOutputWriter output)
    {
        string fullPath = Path.GetFullPath(outputFile);
        if (File.Exists(fullPath) && !overwrite)
        {
            output.Error($"Destination already exists: {outputFile}");
            return ExitCode.IoError;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.Error($"Could not create destination directory '{directory}': {ex.Message}");
                return ExitCode.IoError;
            }
        }

        return ExitCode.Success;
    }
}
