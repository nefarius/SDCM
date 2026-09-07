/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Text;

namespace SurfaceDevCenterManager.Cli;

/// <summary>
///     Reads a secret without putting it on the command line: one line from stdin when redirected,
///     otherwise a no-echo prompt on stderr.
/// </summary>
internal static class SecretPrompt
{
    public static string? ReadApiKey()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        Console.Error.Write("Partner Center API key: ");
        StringBuilder builder = new();
        while (true)
        {
            ConsoleKeyInfo info = Console.ReadKey(intercept: true);
            if (info.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                break;
            }

            if (info.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }

                continue;
            }

            if (!char.IsControl(info.KeyChar))
            {
                builder.Append(info.KeyChar);
            }
        }

        return builder.ToString();
    }
}
