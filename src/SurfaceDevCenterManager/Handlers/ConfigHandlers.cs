/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SurfaceDevCenterManager.Configuration;
using SurfaceDevCenterManager.Services;

namespace SurfaceDevCenterManager.Handlers;

public sealed record ConfigPathInput(string? ExplicitConfigPath);

/// <summary>Prints the config discovery chain sdcm probes, and which entry (if any) currently resolves.</summary>
public sealed class ConfigPathHandler(IOutputWriter output)
{
    public Task<ExitCode> RunAsync(ConfigPathInput input, CancellationToken cancellationToken)
    {
        string? resolved = ConfigPathResolver.Resolve(input.ExplicitConfigPath);

        output.Progress("authconfig.json is probed for, in order:");
        int i = 1;
        foreach (string candidate in ConfigPathResolver.EnumerateCandidates(input.ExplicitConfigPath))
        {
            bool exists = File.Exists(candidate);
            bool isResolved = candidate == resolved;
            string marker = isResolved ? " <- using this one" : exists ? " (exists, shadowed)" : "";
            output.Progress($"  {i++}. {candidate}{marker}");
        }

        if (resolved == null)
        {
            output.Progress("");
            output.Progress("No authconfig.json was found. Run 'sdcm config init' to create one.");
        }

        return Task.FromResult(ExitCode.Success);
    }
}

public sealed record ConfigInitInput(bool Force);

/// <summary>Writes a starter authconfig.json into the per-user config directory.</summary>
public sealed class ConfigInitHandler(IOutputWriter output)
{
    public Task<ExitCode> RunAsync(ConfigInitInput input, CancellationToken cancellationToken)
    {
        string path = ConfigPathResolver.GetUserConfigPath();

        if (File.Exists(path) && !input.Force)
        {
            output.Error($"'{path}' already exists. Pass --force to overwrite it.");
            return Task.FromResult(ExitCode.IoError);
        }

        string sampleSourcePath = Path.Combine(AppContext.BaseDirectory, "authconfig.sample.json");
        string content = File.Exists(sampleSourcePath) ? File.ReadAllText(sampleSourcePath) : DefaultSample;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);

            // authconfig.json can contain a client secret; keep it readable only by the current user
            // on platforms where that isn't already the filesystem default.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            output.Error($"Failed to write '{path}': {ex.Message}");
            return Task.FromResult(ExitCode.IoError);
        }

        output.Progress($"Wrote a starter authconfig.json to '{path}'.");
        output.Progress("Edit it to fill in tenantId/clientId (and key or managedIdentityClientId) for the 'default' profile.");

        return Task.FromResult(ExitCode.Success);
    }

    private const string DefaultSample = """
        {
          "profiles": {
            "default": {
              "tenantId": "00000000-0000-0000-0000-000000000000",
              "clientId": "00000000-0000-0000-0000-000000000000",
              "key": null,
              "managedIdentityClientId": null,
              "url": "https://manage.devcenter.microsoft.com",
              "urlPrefix": "v2.0/my"
            }
          }
        }
        """;
}

public sealed record ConfigSetInput(
    string? ExplicitConfigPath,
    string ProfileName,
    string? TenantId,
    string? ClientId,
    string? Key);

/// <summary>
///     Creates or merges tenantId/clientId/key on a named profile in authconfig.json. Never prints
///     the key value; only reports the path and which fields were written.
/// </summary>
public sealed class ConfigSetHandler(IOutputWriter output)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Task<ExitCode> RunAsync(ConfigSetInput input, CancellationToken cancellationToken)
    {
        string? tenantId = Normalize(input.TenantId);
        string? clientId = Normalize(input.ClientId);
        string? key = Normalize(input.Key);

        if (tenantId == null && clientId == null && key == null)
        {
            output.Error("Specify at least one of --tenant-id, --client-id, or --key.");
            return Task.FromResult(ExitCode.InvalidArguments);
        }

        string profileName = string.IsNullOrWhiteSpace(input.ProfileName) ? "default" : input.ProfileName;
        string path = ConfigPathResolver.ResolveWriteTarget(input.ExplicitConfigPath);

        AuthConfigEntry config;
        if (File.Exists(path))
        {
            try
            {
                string existing = File.ReadAllText(path);
                AuthConfigEntry? parsed = JsonSerializer.Deserialize<AuthConfigEntry>(existing, JsonOptions);
                config = parsed ?? new AuthConfigEntry();
            }
            catch (JsonException ex)
            {
                output.Error($"'{path}' is not valid authconfig.json: {ex.Message}");
                return Task.FromResult(ExitCode.InvalidArguments);
            }
        }
        else
        {
            config = new AuthConfigEntry();
        }

        Dictionary<string, AuthProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, AuthProfile> pair in config.Profiles)
        {
            profiles[pair.Key] = pair.Value;
        }

        if (!profiles.TryGetValue(profileName, out AuthProfile? profile))
        {
            profile = new AuthProfile();
            profiles[profileName] = profile;
        }

        List<string> updated = [];
        if (tenantId != null)
        {
            profile.TenantId = tenantId;
            updated.Add("tenantId");
        }

        if (clientId != null)
        {
            profile.ClientId = clientId;
            updated.Add("clientId");
        }

        if (key != null)
        {
            profile.Key = key;
            updated.Add("key");
        }

        config.Profiles = profiles;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions), Utf8NoBom);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            output.Error($"Failed to write '{path}': {ex.Message}");
            return Task.FromResult(ExitCode.IoError);
        }

        output.Progress($"Saved profile '{profileName}' to '{path}'.");
        output.Progress($"Updated fields: {string.Join(", ", updated)}.");
        return Task.FromResult(ExitCode.Success);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
