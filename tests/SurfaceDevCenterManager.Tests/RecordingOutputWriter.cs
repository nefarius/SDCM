/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Tests;

internal sealed class RecordingOutputWriter(OutputFormat format = OutputFormat.Json) : IOutputWriter
{
    public OutputFormat Format { get; } = format;

    public List<string> ProgressLines { get; } = [];

    public List<string> Errors { get; } = [];

    public List<object?> Models { get; } = [];

    public void Progress(string message) => ProgressLines.Add(message);

    public void Result<T>(T model, Action<T> textDump) => Models.Add(model);

    public void Results<T>(IReadOnlyList<T> models, Action<T> textDump)
    {
        foreach (T model in models)
        {
            Models.Add(model);
        }
    }

    public void Error(string message) => Errors.Add(message);
}
