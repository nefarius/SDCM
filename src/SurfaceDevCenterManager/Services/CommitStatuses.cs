/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Case-insensitive matching for Hardware Dev Center <c>commitStatus</c>. The API reference
///     documents <c>CommitPending</c> / <c>CommitComplete</c> / <c>CommitFailed</c>, but the service
///     commonly returns the camelCase forms. Callers must never compare with <c>==</c>.
/// </summary>
internal static class CommitStatuses
{
    public const string Pending = "commitPending";
    public const string Complete = "commitComplete";
    public const string Failed = "commitFailed";

    public static bool Is(string? actual, string expected)
    {
        return !string.IsNullOrEmpty(actual) &&
               string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPending(string? actual) => Is(actual, Pending);

    public static bool IsComplete(string? actual) => Is(actual, Complete);

    public static bool IsFailed(string? actual) => Is(actual, Failed);
}
