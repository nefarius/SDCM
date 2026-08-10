/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Net;
using Microsoft.Devices.HardwareDevCenterManager;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;
using SurfaceDevCenterManager.Models;
using SurfaceDevCenterManager.Services;
using Xunit;

namespace SurfaceDevCenterManager.Tests;

/// <summary>
///     Unlike the library-backed list endpoints (products, submissions, shipping labels, audiences),
///     which wrap collections in a <c>{ "value": [...] }</c> envelope, the preprod "list assets"
///     endpoint returns a plain JSON array. Regression coverage for a bug where that array was fed
///     into the same <c>Response&lt;T&gt;</c>-wrapper deserialization path used for the other
///     endpoints, throwing a <see cref="System.Text.Json.JsonException" /> instead of returning the
///     assets.
/// </summary>
public class DevCenterPreprodHandlerTests
{
    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private static DevCenterOptions CreateOptions()
    {
        return new DevCenterOptions
        {
            CorrelationId = Guid.NewGuid(),
            HttpTimeoutSeconds = 30,
            RequestDelayMs = 0
        };
    }

    [Fact]
    public async Task GetPreprodPackageAssets_ListMode_ParsesRawJsonArray()
    {
        const string json = """
            [
                {
                    "id": "asset-1",
                    "packageId": "package-1",
                    "assetType": "SignedFilesZip",
                    "createdDate": "2022-03-28T23:45:25.501Z",
                    "contentHash": "abc123"
                }
            ]
            """;

        using StubHttpMessageHandler stub = new(HttpStatusCode.OK, json);
        using HttpClient client = new(stub) { BaseAddress = new Uri("https://manage.devcenter.microsoft.com") };
        using DevCenterPreprodHandler handler = new(
            client, "https://manage.devcenter.microsoft.com", "v2.0/my", CreateOptions());

        DevCenterResponse<PreprodPackageAsset> response = await handler.GetPreprodPackageAssets("package-1");

        Assert.Null(response.Error);
        PreprodPackageAsset asset = Assert.Single(response.ReturnValue ?? []);
        Assert.Equal("asset-1", asset.Id);
        Assert.Equal("SignedFilesZip", asset.AssetType);
    }

    [Fact]
    public async Task GetPreprodPackageAssets_SingleAssetMode_ParsesRawJsonObject()
    {
        const string json = """
            {
                "id": "asset-1",
                "packageId": "package-1",
                "assetType": "SignedFilesZip",
                "createdDate": "2022-03-28T23:45:25.501Z",
                "contentHash": "abc123"
            }
            """;

        using StubHttpMessageHandler stub = new(HttpStatusCode.OK, json);
        using HttpClient client = new(stub) { BaseAddress = new Uri("https://manage.devcenter.microsoft.com") };
        using DevCenterPreprodHandler handler = new(
            client, "https://manage.devcenter.microsoft.com", "v2.0/my", CreateOptions());

        DevCenterResponse<PreprodPackageAsset> response = await handler.GetPreprodPackageAssets(
            "package-1", "asset-1");

        Assert.Null(response.Error);
        PreprodPackageAsset asset = Assert.Single(response.ReturnValue ?? []);
        Assert.Equal("asset-1", asset.Id);
    }
}
