/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Net;
using Microsoft.Devices.HardwareDevCenterManager;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     An <see cref="IDevCenterHandler" /> implementation for the <see cref="AuthMode.Interactive" />
///     auth mode. The library's own <c>DevCenterHandler</c> only supports managed identity, client
///     secret, and certificate credentials internally, with no seam for a delegated user token, so
///     this mirrors its request shape while sourcing the bearer token from
///     <see cref="IAadTokenProvider" /> instead.
/// </summary>
public sealed class InteractiveDevCenterHandler : IDevCenterHandler, IDisposable
{
    private const string ProductsUrl = "/hardware/products";
    private const string ProductSubmissionUrl = "/hardware/products/{0}/submissions";

    private const string PartnerSubmissionUrl =
        "/hardware/products/relationships/sourcepubliherid/{0}/sourceproductid/{1}/sourcesubmissionid/{2}";

    private const string ProductSubmissionCommitUrl = "/hardware/products/{0}/submissions/{1}/commit";
    private const string ShippingLabelUrl = "/hardware/products/{0}/submissions/{1}/shippingLabels";
    private const string AudienceUrl = "/hardware/audiences";
    private const string CreateMetaDataUrl = "/hardware/products/{0}/submissions/{1}/createpublishermetadata";
    private const string CancelShippingLabelUrl = "/hardware/products/{0}/submissions/{1}/shippingLabels/{2}/cancel";

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly HdcJsonRequestExecutor _executor;

    public InteractiveDevCenterHandler(
        IAadTokenProvider tokenProvider,
        string clientId,
        string authority,
        string redirectUri,
        string url,
        string urlPrefix,
        AadPromptMode promptMode,
        DevCenterOptions options)
    {
        _baseUrl = new Uri(new Uri(url, UriKind.Absolute), urlPrefix).AbsoluteUri;

        BearerTokenHandler bearerHandler = new(tokenProvider, clientId, authority, redirectUri, url, promptMode);
        _client = new HttpClient(bearerHandler, true)
        {
            Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds)
        };
        _executor = new HdcJsonRequestExecutor(_client, options.CorrelationId, options.LastCommand);
    }

    public Task<DevCenterErrorDetails?> InvokeHdcService(
        HttpMethod method, string uri, object? input, Action<string>? processContent)
    {
        return _executor.InvokeHdcService(method, uri, input, processContent);
    }

    public Task<DevCenterResponse<TOutput>> InvokeHdcService<TOutput>(
        HttpMethod method, string uri, object? input, bool isMany) where TOutput : IArtifact
    {
        return _executor.InvokeHdcService<TOutput>(method, uri, input, isMany);
    }

    public Task<DevCenterResponse<TOutput>> HdcGet<TOutput>(string uri, bool isMany) where TOutput : IArtifact
    {
        return _executor.HdcGet<TOutput>(uri, isMany);
    }

    public Task<DevCenterResponse<TOutput>> HdcPost<TOutput>(string uri, object input) where TOutput : IArtifact
    {
        return _executor.HdcPost<TOutput>(uri, input);
    }

    public Task<DevCenterResponse<Product>> NewProduct(NewProduct input)
    {
        return _executor.HdcPost<Product>(_baseUrl + ProductsUrl, input);
    }

    public Task<DevCenterResponse<Product>> GetProducts(string? productId = null)
    {
        string url = _baseUrl + ProductsUrl;
        bool isMany = string.IsNullOrEmpty(productId);
        if (!isMany)
        {
            url += "/" + Uri.EscapeDataString(productId!);
        }

        return _executor.HdcGet<Product>(url, isMany);
    }

    public Task<DevCenterResponse<Submission>> NewSubmission(string productId, NewSubmission submissionInfo)
    {
        string url = _baseUrl + string.Format(ProductSubmissionUrl, Uri.EscapeDataString(productId));
        return _executor.HdcPost<Submission>(url, submissionInfo);
    }

    public Task<DevCenterResponse<Submission>> GetSubmission(string productId, string? submissionId = null)
    {
        string url = _baseUrl + string.Format(ProductSubmissionUrl, Uri.EscapeDataString(productId));
        bool isMany = string.IsNullOrEmpty(submissionId);
        if (!isMany)
        {
            url += "/" + Uri.EscapeDataString(submissionId!);
        }

        return _executor.HdcGet<Submission>(url, isMany);
    }

    public Task<DevCenterResponse<Submission>> GetPartnerSubmission(
        string publisherId, string productId, string submissionId)
    {
        string url = _baseUrl + string.Format(
            PartnerSubmissionUrl,
            Uri.EscapeDataString(publisherId),
            Uri.EscapeDataString(productId),
            Uri.EscapeDataString(submissionId));
        return _executor.HdcGet<Submission>(url, string.IsNullOrEmpty(submissionId));
    }

    public async Task<DevCenterResponse<bool>> CommitSubmission(string productId, string submissionId)
    {
        string url = _baseUrl + string.Format(
            ProductSubmissionCommitUrl, Uri.EscapeDataString(productId), Uri.EscapeDataString(submissionId));
        DevCenterErrorDetails? error = await _executor
            .InvokeHdcService(HttpMethod.Post, url, null, null).ConfigureAwait(false);

        DevCenterResponse<bool> result = new()
        {
            Error = error,
            ReturnValue = [error == null]
        };

        if (error is { HttpErrorCode: (int)HttpStatusCode.BadGateway } &&
            string.Equals(error.Code, "requestInvalidForCurrentState", StringComparison.OrdinalIgnoreCase))
        {
            DevCenterResponse<Submission> status = await GetSubmission(productId, submissionId).ConfigureAwait(false);
            if (status.Error == null && status.ReturnValue is { Count: > 0 } &&
                string.Equals(status.ReturnValue[0].CommitStatus, "commitComplete", StringComparison.OrdinalIgnoreCase))
            {
                result.Error = null;
                result.ReturnValue = [true];
            }
        }

        return result;
    }

    public Task<DevCenterResponse<ShippingLabel>> NewShippingLabel(
        string productId, string submissionId, NewShippingLabel shippingLabelInfo)
    {
        string url = _baseUrl + string.Format(
            ShippingLabelUrl, Uri.EscapeDataString(productId), Uri.EscapeDataString(submissionId));
        return _executor.HdcPost<ShippingLabel>(url, shippingLabelInfo);
    }

    public Task<DevCenterResponse<ShippingLabel>> GetShippingLabels(
        string productId, string submissionId, string? shippingLabelId = null)
    {
        string url = _baseUrl + string.Format(
            ShippingLabelUrl, Uri.EscapeDataString(productId), Uri.EscapeDataString(submissionId));
        bool isMany = string.IsNullOrEmpty(shippingLabelId);
        if (!isMany)
        {
            url += "/" + Uri.EscapeDataString(shippingLabelId!);
        }

        url += "?includeTargetingInfo=true";
        return _executor.HdcGet<ShippingLabel>(url, isMany);
    }

    public Task<DevCenterResponse<Audience>> GetAudiences()
    {
        return _executor.HdcGet<Audience>(_baseUrl + AudienceUrl, true);
    }

    public async Task<DevCenterResponse<bool>> CreateMetaData(string productId, string submissionId)
    {
        string url = _baseUrl + string.Format(
            CreateMetaDataUrl, Uri.EscapeDataString(productId), Uri.EscapeDataString(submissionId));
        DevCenterErrorDetails? error = await _executor
            .InvokeHdcService(HttpMethod.Post, url, null, null).ConfigureAwait(false);
        return new DevCenterResponse<bool>
        {
            Error = error,
            ReturnValue = [error == null]
        };
    }

    public async Task<DevCenterResponse<bool>> CancelShippingLabel(
        string productId, string submissionId, string shippingLabelId)
    {
        string url = _baseUrl + string.Format(
            CancelShippingLabelUrl,
            Uri.EscapeDataString(productId),
            Uri.EscapeDataString(submissionId),
            Uri.EscapeDataString(shippingLabelId));
        DevCenterErrorDetails? error = await _executor
            .InvokeHdcService(HttpMethod.Put, url, null, null).ConfigureAwait(false);
        return new DevCenterResponse<bool>
        {
            Error = error,
            ReturnValue = [error == null]
        };
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
