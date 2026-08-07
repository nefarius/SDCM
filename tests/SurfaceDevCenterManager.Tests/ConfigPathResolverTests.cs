/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using SurfaceDevCenterManager.Configuration;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

public class ConfigPathResolverTests
{
    [Fact]
    public void EnumerateCandidates_ExplicitPathComesFirst()
    {
        IReadOnlyList<string> candidates = ConfigPathResolver.EnumerateCandidates(@"C:\explicit\authconfig.json");

        Assert.Equal(Path.GetFullPath(@"C:\explicit\authconfig.json"), candidates[0]);
        Assert.Equal(4, candidates.Count);
    }

    [Fact]
    public void EnumerateCandidates_WithoutExplicitPath_StartsWithCurrentDirectory()
    {
        IReadOnlyList<string> candidates = ConfigPathResolver.EnumerateCandidates(null);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "authconfig.json"), candidates[0]);
    }

    [Fact]
    public void EnumerateCandidates_IncludesPerUserAndBaseDirectory()
    {
        IReadOnlyList<string> candidates = ConfigPathResolver.EnumerateCandidates(null);

        Assert.Equal(GetUserConfigPathForAssertion(), candidates[1]);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "authconfig.json"), candidates[2]);
    }

    [Fact]
    public void Resolve_ReturnsFirstExistingCandidate()
    {
        string tempDir = Directory.CreateTempSubdirectory("sdcm-config-test-").FullName;
        try
        {
            string explicitPath = Path.Combine(tempDir, "authconfig.json");
            File.WriteAllText(explicitPath, "{}");

            string? resolved = ConfigPathResolver.Resolve(explicitPath);

            Assert.Equal(explicitPath, resolved);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Resolve_DoesNotReturnExplicitPath_WhenItDoesNotExist()
    {
        // ConfigPathResolver.Resolve also falls back to per-user and application-base-directory
        // candidates, which aren't injectable and may legitimately contain a real authconfig.json
        // on a developer's machine. Asserting Resolve returns null outright is therefore flaky; the
        // one thing we can assert deterministically is that a guaranteed-nonexistent explicit path
        // is never itself returned as if it existed.
        string nonexistent = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.json");

        string? resolved = ConfigPathResolver.Resolve(nonexistent);

        Assert.NotEqual(nonexistent, resolved);
    }

    [Fact]
    public void GetUserConfigPath_EndsWithSdcmAuthconfig()
    {
        string path = ConfigPathResolver.GetUserConfigPath();

        Assert.EndsWith(Path.Combine("sdcm", "authconfig.json"), path);
    }

    private static string GetUserConfigPathForAssertion()
    {
        return ConfigPathResolver.GetUserConfigPath();
    }
}
