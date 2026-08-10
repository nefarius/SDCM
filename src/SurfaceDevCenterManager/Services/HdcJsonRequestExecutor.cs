/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Devices.HardwareDevCenterManager;
using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Request/response plumbing shared by every hand-rolled Hardware Dev Center HTTP client sdcm
///     builds (as opposed to the ones the <c>Microsoft.Devices.HardwareDevCenterManager</c> library
///     builds internally). Originally lived inline in <see cref="InteractiveDevCenterHandler" />;
///     extracted so the preprod submission handler (<see cref="DevCenterPreprodHandler" />) can reuse
///     the same JSON request/error-parsing semantics instead of duplicating them, and extended with
///     binary PUT/GET support for preprod's package upload/download endpoints, which the library's
///     JSON-only request shape has no equivalent for.
/// </summary>
internal sealed class HdcJsonRequestExecutor(HttpClient client, Guid correlationId, LastCommandDelegate? lastCommand)
{
    private const string DefaultErrorCode = "InvalidInput";

    public async Task<(DevCenterErrorDetails? Error, DevCenterTrace Trace)> InvokeHdcServiceCore(
        HttpMethod method, string uri, object? input, Action<string>? processContent,
        CancellationToken cancellationToken = default)
    {
        string requestId = Guid.NewGuid().ToString();
        string json = JsonSerializer.Serialize(input ?? new object());

        DevCenterTrace trace = new()
        {
            CorrelationId = correlationId.ToString(),
            RequestId = requestId,
            Method = method.ToString(),
            Url = uri,
            Content = json
        };

        lastCommand?.Invoke(new DevCenterErrorDetails { Trace = trace });

        if (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Put)
        {
            return (new DevCenterErrorDetails
            {
                HttpErrorCode = -1,
                Code = DefaultErrorCode,
                Message = "Unsupported HTTP method",
                Trace = trace
            }, trace);
        }

        using HttpRequestMessage request = new(method, uri);
        request.Headers.Add("MS-CorrelationId", correlationId.ToString());
        request.Headers.Add("MS-RequestId", requestId);

        // POST always sends a body (even an empty "{}"), matching the original semantics. PUT only
        // sends a body when the caller actually supplied one, preserving the null-body behavior that
        // callers such as CancelShippingLabel rely on.
        if (method == HttpMethod.Post || (method == HttpMethod.Put && input != null))
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            processContent?.Invoke(body);
            return (null, trace);
        }

        return (ParseError(body, response, trace), trace);
    }

    private static DevCenterErrorDetails ParseError(
        string body, HttpResponseMessage response, DevCenterTrace trace)
    {
        DevCenterErrorReturn? returnError;
        try
        {
            returnError = JsonSerializer.Deserialize<DevCenterErrorReturn>(body);
        }
        catch (JsonException)
        {
            returnError = new DevCenterErrorReturn
            {
                HttpErrorCode = (int)response.StatusCode,
                StatusCode = ((int)response.StatusCode) + " " + response.StatusCode,
                Message = body
            };
        }

        if (returnError is null || (returnError.HttpErrorCode.HasValue && returnError.HttpErrorCode.Value == 0))
        {
            returnError = new DevCenterErrorReturn
            {
                HttpErrorCode = (int)response.StatusCode,
                StatusCode = ((int)response.StatusCode) + " " + response.StatusCode,
                Message = response.ReasonPhrase
            };
        }

        if (returnError.Error != null)
        {
            returnError.Error.HttpErrorCode = (int)response.StatusCode;
            return returnError.Error;
        }

        return new DevCenterErrorDetails
        {
            Headers = response.Headers,
            HttpErrorCode = (int)response.StatusCode,
            Code = returnError.StatusCode,
            Message = returnError.Message,
            ValidationErrors = returnError.ValidationErrors,
            Trace = trace
        };
    }

    public async Task<DevCenterErrorDetails?> InvokeHdcService(
        HttpMethod method, string uri, object? input, Action<string>? processContent)
    {
        (DevCenterErrorDetails? error, _) = await InvokeHdcServiceCore(method, uri, input, processContent)
            .ConfigureAwait(false);
        return error;
    }

    public async Task<DevCenterResponse<TOutput>> InvokeHdcService<TOutput>(
        HttpMethod method, string uri, object? input, bool isMany,
        CancellationToken cancellationToken = default) where TOutput : IArtifact
    {
        DevCenterResponse<TOutput> response = new();
        (response.Error, DevCenterTrace trace) = await InvokeHdcServiceCore(method, uri, input, content =>
        {
            if (isMany)
            {
                Response<TOutput>? parsed = JsonSerializer.Deserialize<Response<TOutput>>(content);
                response.ReturnValue = parsed?.Value;
            }
            else
            {
                TOutput? parsed = JsonSerializer.Deserialize<TOutput>(content);
                if (parsed?.Id != null)
                {
                    response.ReturnValue = [parsed];
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        response.Trace = trace;
        return response;
    }

    public Task<DevCenterResponse<TOutput>> HdcGet<TOutput>(
        string uri, bool isMany, CancellationToken cancellationToken = default) where TOutput : IArtifact
    {
        return InvokeHdcService<TOutput>(HttpMethod.Get, uri, null, isMany, cancellationToken);
    }

    public Task<DevCenterResponse<TOutput>> HdcPost<TOutput>(string uri, object input) where TOutput : IArtifact
    {
        return InvokeHdcService<TOutput>(HttpMethod.Post, uri, input, false);
    }

    /// <summary>
    ///     PUTs a file's raw bytes as <c>application/octet-stream</c>, streaming directly from disk
    ///     rather than buffering the whole file in memory. Used for the preprod package upload
    ///     endpoint, which (unlike submission uploads) takes the package body directly rather than a
    ///     blob storage SAS URL.
    /// </summary>
    public async Task<(DevCenterErrorDetails? Error, DevCenterTrace Trace)> PutBinaryAsync(
        string uri, string filePath, Action<string>? processContent)
    {
        string requestId = Guid.NewGuid().ToString();
        DevCenterTrace trace = new()
        {
            CorrelationId = correlationId.ToString(),
            RequestId = requestId,
            Method = HttpMethod.Put.ToString(),
            Url = uri,
            Content = $"<binary file content: {filePath}>"
        };

        lastCommand?.Invoke(new DevCenterErrorDetails { Trace = trace });

        // Buffered (rather than streamed straight from disk) so the request body survives the
        // AuthorizationHandler's retry-on-401 path, which resends the same HttpRequestMessage: a
        // StreamContent over a FileStream would already be exhausted (position at EOF) on retry and
        // upload zero bytes.
        byte[] fileContent = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
        using HttpRequestMessage request = new(HttpMethod.Put, uri);
        request.Headers.Add("MS-CorrelationId", correlationId.ToString());
        request.Headers.Add("MS-RequestId", requestId);
        request.Content = new ByteArrayContent(fileContent);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            processContent?.Invoke(body);
            return (null, trace);
        }

        return (ParseError(body, response, trace), trace);
    }

    /// <summary>
    ///     GETs a binary asset and streams the response body straight to <paramref name="outputFilePath" />
    ///     on success, or parses the JSON error body on failure. Used for the preprod signed-asset
    ///     download endpoint, which (unlike submission downloads) serves the file directly from the
    ///     Dev Center API rather than from a blob storage SAS URL.
    /// </summary>
    public async Task<DevCenterErrorDetails?> DownloadBinaryAsync(string uri, string outputFilePath)
    {
        string requestId = Guid.NewGuid().ToString();
        DevCenterTrace trace = new()
        {
            CorrelationId = correlationId.ToString(),
            RequestId = requestId,
            Method = HttpMethod.Get.ToString(),
            Url = uri
        };

        lastCommand?.Invoke(new DevCenterErrorDetails { Trace = trace });

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Add("MS-CorrelationId", correlationId.ToString());
        request.Headers.Add("MS-RequestId", requestId);

        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            string tempFilePath = outputFilePath + ".tmp" + Guid.NewGuid().ToString("N");
            try
            {
                await using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (FileStream destination = File.Create(tempFilePath))
                {
                    await source.CopyToAsync(destination).ConfigureAwait(false);
                }

                File.Move(tempFilePath, outputFilePath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }

                throw;
            }

            return null;
        }

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseError(body, response, trace);
    }
}
