/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text.Json;
using SurfaceDevCenterManager;
using SurfaceDevCenterManager.Configuration;
using SurfaceDevCenterManager.Handlers;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class ConfigSetHandlerTests
{
    [Fact]
    public async Task EmptyUpdate_ReturnsInvalidArguments()
    {
        using TempConfigFile temp = new();
        RecordingOutputWriter output = new();
        ConfigSetHandler handler = new(output);

        ExitCode exit = await handler.RunAsync(new ConfigSetInput(temp.Path, "default", null, null, null), CancellationToken.None);

        Assert.Equal(ExitCode.InvalidArguments, exit);
        Assert.False(File.Exists(temp.Path));
        Assert.Contains(output.Errors, e => e.Contains("--tenant-id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreatesNewFile_WithRequestedFields()
    {
        using TempConfigFile temp = new();
        RecordingOutputWriter output = new();
        ConfigSetHandler handler = new(output);

        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "tenant-1", "client-1", "secret-1"),
            CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        AuthProfile profile = ReadProfile(temp.Path, "default");
        Assert.Equal("tenant-1", profile.TenantId);
        Assert.Equal("client-1", profile.ClientId);
        Assert.Equal("secret-1", profile.Key);
        Assert.DoesNotContain("secret-1", string.Join('\n', output.ProgressLines));
        Assert.Contains(output.ProgressLines, line => line.Contains(temp.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_OverwritesOnlyPassedFields()
    {
        using TempConfigFile temp = new();
        WriteProfiles(temp.Path, new AuthConfigEntry
        {
            Profiles =
            {
                ["default"] = new AuthProfile
                {
                    TenantId = "tenant-keep",
                    ClientId = "client-keep",
                    Key = "secret-old"
                }
            }
        });

        ConfigSetHandler handler = new(new RecordingOutputWriter());
        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", null, null, "secret-new"),
            CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        AuthProfile profile = ReadProfile(temp.Path, "default");
        Assert.Equal("tenant-keep", profile.TenantId);
        Assert.Equal("client-keep", profile.ClientId);
        Assert.Equal("secret-new", profile.Key);
    }

    [Fact]
    public async Task PreservesOtherProfiles()
    {
        using TempConfigFile temp = new();
        WriteProfiles(temp.Path, new AuthConfigEntry
        {
            Profiles =
            {
                ["default"] = new AuthProfile { TenantId = "t-default", ClientId = "c-default" },
                ["ci"] = new AuthProfile { TenantId = "t-ci", ClientId = "c-ci", Key = "ci-secret" }
            }
        });

        ConfigSetHandler handler = new(new RecordingOutputWriter());
        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "t-updated", null, null),
            CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        AuthProfile ci = ReadProfile(temp.Path, "ci");
        Assert.Equal("t-ci", ci.TenantId);
        Assert.Equal("c-ci", ci.ClientId);
        Assert.Equal("ci-secret", ci.Key);
        Assert.Equal("t-updated", ReadProfile(temp.Path, "default").TenantId);
    }

    [Fact]
    public async Task UnreadableExistingFile_ReturnsIoError()
    {
        using TempConfigFile temp = new();
        File.WriteAllText(temp.Path, "{}");
        using FileStream locked = new(temp.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        RecordingOutputWriter output = new();
        ConfigSetHandler handler = new(output);

        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "tenant-1", null, null),
            CancellationToken.None);

        Assert.Equal(ExitCode.IoError, exit);
        Assert.Contains(output.Errors, e => e.Contains("Failed to read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NullProfiles_TreatedAsEmpty()
    {
        using TempConfigFile temp = new();
        File.WriteAllText(temp.Path, """{"profiles":null}""");
        ConfigSetHandler handler = new(new RecordingOutputWriter());

        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "tenant-1", "client-1", null),
            CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        AuthProfile profile = ReadProfile(temp.Path, "default");
        Assert.Equal("tenant-1", profile.TenantId);
        Assert.Equal("client-1", profile.ClientId);
    }

    [Fact]
    public async Task NullProfileValue_IsNormalized()
    {
        using TempConfigFile temp = new();
        File.WriteAllText(temp.Path, """{"profiles":{"default":null}}""");
        ConfigSetHandler handler = new(new RecordingOutputWriter());

        ExitCode exit = await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "tenant-1", null, null),
            CancellationToken.None);

        Assert.Equal(ExitCode.Success, exit);
        Assert.Equal("tenant-1", ReadProfile(temp.Path, "default").TenantId);
    }

    [Fact]
    public async Task WritesUtf8WithoutBom()
    {
        using TempConfigFile temp = new();
        ConfigSetHandler handler = new(new RecordingOutputWriter());

        await handler.RunAsync(
            new ConfigSetInput(temp.Path, "default", "tenant-1", "client-1", "secret-1"),
            CancellationToken.None);

        byte[] bytes = File.ReadAllBytes(temp.Path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain((byte)0, bytes.Take(32));
    }

    private static AuthProfile ReadProfile(string path, string name)
    {
        AuthConfigEntry config = JsonSerializer.Deserialize<AuthConfigEntry>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to read authconfig.json");

        return config.Profiles[name];
    }

    private static void WriteProfiles(string path, AuthConfigEntry config)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private sealed class TempConfigFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"sdcm-config-set-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    private sealed class RecordingOutputWriter : IOutputWriter
    {
        public OutputFormat Format => OutputFormat.Text;

        public List<string> ProgressLines { get; } = [];

        public List<string> Errors { get; } = [];

        public void Progress(string message) => ProgressLines.Add(message);

        public void Result<T>(T model, Action<T> textDump)
        {
        }

        public void Results<T>(IReadOnlyList<T> models, Action<T> textDump)
        {
        }

        public void Error(string message) => Errors.Add(message);
    }
}
