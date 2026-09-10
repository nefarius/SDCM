/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Replay;

public sealed class ReplayDevCenterHandler(ReplayStore store) : IDevCenterHandler
{
    public Task<DevCenterErrorDetails?> InvokeHdcService(
        HttpMethod method, string uri, object? input, Action<string>? processContent)
    {
        return Task.FromResult<DevCenterErrorDetails?>(new DevCenterErrorDetails
        {
            Code = "notSupported",
            Message = "Replay backend does not implement raw InvokeHdcService."
        });
    }

    public Task<DevCenterResponse<TOutput>> InvokeHdcService<TOutput>(
        HttpMethod method, string uri, object? input, bool isMany) where TOutput : IArtifact
    {
        return Task.FromResult(ReplayStore.InvalidState<TOutput>("Replay backend does not implement raw InvokeHdcService."));
    }

    public Task<DevCenterResponse<TOutput>> HdcGet<TOutput>(string uri, bool isMany) where TOutput : IArtifact
    {
        return Task.FromResult(ReplayStore.InvalidState<TOutput>("Replay backend does not implement raw HdcGet."));
    }

    public Task<DevCenterResponse<TOutput>> HdcPost<TOutput>(string uri, object input) where TOutput : IArtifact
    {
        return Task.FromResult(ReplayStore.InvalidState<TOutput>("Replay backend does not implement raw HdcPost."));
    }

    public Task<DevCenterResponse<Product>> NewProduct(NewProduct input)
    {
        ReplayProduct product = new()
        {
            Id = store.NextId(),
            ProductName = input.ProductName,
            TestHarness = input.TestHarness
        };
        store.Products.Add(product);
        return Task.FromResult(ReplayStore.Ok(ReplayStore.ToProduct(product)));
    }

    public Task<DevCenterResponse<Product>> GetProducts(string? productId = null)
    {
        if (string.IsNullOrEmpty(productId))
        {
            return Task.FromResult(ReplayStore.Ok(store.Products.Select(ReplayStore.ToProduct).ToArray()));
        }

        ReplayProduct? product = store.Products.FirstOrDefault(p => ReplayStore.IdsEqual(p.Id, productId));
        return Task.FromResult(product == null
            ? ReplayStore.NotFound<Product>($"Product {productId} not found.")
            : ReplayStore.Ok(ReplayStore.ToProduct(product)));
    }

    public Task<DevCenterResponse<Submission>> NewSubmission(string productId, NewSubmission submissionInfo)
    {
        ReplayProduct? product = store.Products.FirstOrDefault(p => ReplayStore.IdsEqual(p.Id, productId));
        if (product == null)
        {
            return Task.FromResult(ReplayStore.NotFound<Submission>($"Product {productId} not found."));
        }

        string id = store.NextId();
        ReplaySubmission submission = store.AddSubmission(new ReplaySubmission
        {
            Id = id,
            ProductId = productId,
            Name = submissionInfo.Name,
            Type = submissionInfo.Type,
            CommitStatus = "commitPending",
            WorkflowStatus = new ReplayWorkflowStatus { CurrentStep = "packageInfoValidation", State = "notStarted" },
            Downloads =
            [
                new ReplayDownload { Type = "initialPackage", Url = $"https://replay.invalid/upload/{productId}/{id}" }
            ]
        });
        return Task.FromResult(ReplayStore.Ok(ReplayStore.ToSubmission(submission)));
    }

    public Task<DevCenterResponse<Submission>> GetSubmission(string productId, string? submissionId = null)
    {
        if (string.IsNullOrEmpty(submissionId))
        {
            return Task.FromResult(ReplayStore.Ok(
                store.ListSubmissions(productId).Select(ReplayStore.ToSubmission).ToArray()));
        }

        try
        {
            ReplaySubmission snapshot = store.SnapshotForGet(productId, submissionId);
            return Task.FromResult(ReplayStore.Ok(ReplayStore.ToSubmission(snapshot)));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(ReplayStore.NotFound<Submission>(
                $"Submission {productId}/{submissionId} not found."));
        }
    }

    public Task<DevCenterResponse<Submission>> GetPartnerSubmission(
        string publisherId, string productId, string submissionId)
    {
        return GetSubmission(productId, submissionId);
    }

    public Task<DevCenterResponse<bool>> CommitSubmission(string productId, string submissionId)
    {
        ReplaySubmission? live = store.FindSubmission(productId, submissionId);
        if (live == null)
        {
            return Task.FromResult(ReplayStore.NotFound<bool>($"Submission {productId}/{submissionId} not found."));
        }

        if (string.Equals(live.CommitStatus, "commitComplete", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ReplayStore.InvalidState<bool>("Submission is already committed."));
        }

        store.MarkCommitted(productId, submissionId);
        return Task.FromResult(ReplayStore.Ok(true));
    }

    public Task<DevCenterResponse<ShippingLabel>> NewShippingLabel(
        string productId, string submissionId, NewShippingLabel shippingLabelInfo)
    {
        ReplayShippingLabel label = new()
        {
            Id = store.NextId(),
            ProductId = productId,
            SubmissionId = submissionId,
            Name = shippingLabelInfo.Name,
            WorkflowStatus = new ReplayWorkflowStatus { CurrentStep = "processing", State = "started" }
        };
        store.ShippingLabels.Add(label);
        return Task.FromResult(ReplayStore.Ok(ReplayStore.ToShippingLabel(label)));
    }

    public Task<DevCenterResponse<ShippingLabel>> GetShippingLabels(
        string productId, string submissionId, string? shippingLabelId = null)
    {
        IEnumerable<ReplayShippingLabel> matches = store.ShippingLabels.Where(l =>
            ReplayStore.IdsEqual(l.ProductId, productId) && ReplayStore.IdsEqual(l.SubmissionId, submissionId));
        if (!string.IsNullOrEmpty(shippingLabelId))
        {
            ReplayShippingLabel? one = matches.FirstOrDefault(l => ReplayStore.IdsEqual(l.Id, shippingLabelId));
            return Task.FromResult(one == null
                ? ReplayStore.NotFound<ShippingLabel>($"Shipping label {shippingLabelId} not found.")
                : ReplayStore.Ok(ReplayStore.ToShippingLabel(one)));
        }

        return Task.FromResult(ReplayStore.Ok(matches.Select(ReplayStore.ToShippingLabel).ToArray()));
    }

    public Task<DevCenterResponse<Audience>> GetAudiences()
    {
        return Task.FromResult(ReplayStore.Ok<Audience>());
    }

    public Task<DevCenterResponse<bool>> CreateMetaData(string productId, string submissionId)
    {
        ReplaySubmission? live = store.FindSubmission(productId, submissionId);
        if (live == null)
        {
            return Task.FromResult(ReplayStore.NotFound<bool>($"Submission {productId}/{submissionId} not found."));
        }

        if (!live.Downloads.Any(d => string.Equals(d.Type, "driverMetadata", StringComparison.OrdinalIgnoreCase)))
        {
            live.Downloads.Add(new ReplayDownload
            {
                Type = "driverMetadata",
                Url = $"https://replay.invalid/metadata/{productId}/{submissionId}"
            });
        }

        return Task.FromResult(ReplayStore.Ok(true));
    }

    public Task<DevCenterResponse<bool>> CancelShippingLabel(
        string productId, string submissionId, string shippingLabelId)
    {
        return Task.FromResult(ReplayStore.Ok(true));
    }
}
