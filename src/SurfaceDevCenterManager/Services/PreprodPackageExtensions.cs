/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using SurfaceDevCenterManager.Models;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Human-readable text output for <see cref="PreprodPackage" />/<see cref="PreprodPackageAsset" />,
///     mirroring how the library's own <c>Submission</c>/<c>WorkflowStatus</c> types print themselves
///     via their <c>Dump()</c> methods.
/// </summary>
public static class PreprodPackageExtensions
{
    public static void Dump(this PreprodPackage package)
    {
        Console.WriteLine($"Id:             {package.Id}");
        Console.WriteLine($"SigningStatus:  {package.SigningStatus}");
        Console.WriteLine($"LastModified:   {package.LastModified}");

        if (package.Error?.Message != null)
        {
            Console.WriteLine($"Error:          {package.Error.Message}");
        }

        if (package.Assets is { Count: > 0 })
        {
            Console.WriteLine("Assets:");
            foreach (PreprodPackageAsset asset in package.Assets)
            {
                asset.Dump("  ");
            }
        }
    }

    public static void Dump(this PreprodPackageAsset asset)
    {
        asset.Dump(string.Empty);
    }

    private static void Dump(this PreprodPackageAsset asset, string indent)
    {
        Console.WriteLine($"{indent}Id:          {asset.Id}");
        Console.WriteLine($"{indent}AssetType:   {asset.AssetType}");
        Console.WriteLine($"{indent}CreatedDate: {asset.CreatedDate}");
        Console.WriteLine($"{indent}ContentHash: {asset.ContentHash}");
    }
}
