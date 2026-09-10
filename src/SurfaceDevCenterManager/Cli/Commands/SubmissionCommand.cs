/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SurfaceDevCenterManager.Handlers;

namespace SurfaceDevCenterManager.Cli.Commands;

internal static class SubmissionCommand
{
    public static Command Build(ServiceProviderAccessor accessor)
    {
        Option<string> productId = Opt.Str("--product-id", "Product id the submission belongs to", true);

        // create
        Option<string> createInput = Opt.Str("--input", "Path to a JSON file with the NewSubmission payload", true);
        Command create = new("create", "Create a new submission for a product");
        create.Options.Add(productId);
        create.Options.Add(createInput);
        create.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionCreateInput(pr.Required(productId), pr.Required(createInput), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionCreateHandler>().RunAsync(i, ct));

        // list
        Option<string?> listSubmissionId = Opt.OptionalStr("--submission-id",
            "Deprecated: still returns a one-element array. Prefer 'submission get'.");
        Command list = new("list", "List every submission for a product");
        list.Options.Add(productId);
        list.Options.Add(listSubmissionId);
        list.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionListInput(pr.Required(productId), pr.GetValue(listSubmissionId), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionListHandler>().RunAsync(i, ct));

        // get
        Option<string> getSubmissionId = Opt.Str("--submission-id", "Submission id to fetch", true);
        Command get = new("get", "Get a single submission by id");
        get.Options.Add(productId);
        get.Options.Add(getSubmissionId);
        get.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionGetInput(pr.Required(productId), pr.Required(getSubmissionId), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionGetHandler>().RunAsync(i, ct));

        // status
        Option<string> statusSubmissionId = Opt.Str("--submission-id", "Submission id to inspect", true);
        Command status = new("status",
            "Normalized progress (created|processing|completed|failed). state describes currentStep only.");
        status.Options.Add(productId);
        status.Options.Add(statusSubmissionId);
        status.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionStatusInput(pr.Required(productId), pr.Required(statusSubmissionId), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionStatusHandler>().RunAsync(i, ct));

        // commit — idempotent: an already-committed submission is a no-op
        Option<string> commitSubmissionId = Opt.Str("--submission-id", "Submission id to commit", true);
        Command commit = new("commit",
            "Commit a submission, finalizing its package set. Already-committed submissions are a no-op.");
        commit.Options.Add(productId);
        commit.Options.Add(commitSubmissionId);
        commit.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionCommitInput(pr.Required(productId), pr.Required(commitSubmissionId), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionCommitHandler>().RunAsync(i, ct));

        // upload
        Option<string> uploadSubmissionId = Opt.Str("--submission-id", "Submission id to upload the package to", true);
        Option<string> package = Opt.Str("--package", "Path to the package file to upload", true);
        Command upload = new("upload", "Upload a submission's package (refused unless commitStatus is commitPending)");
        upload.Options.Add(productId);
        upload.Options.Add(uploadSubmissionId);
        upload.Options.Add(package);
        upload.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionUploadInput(
                pr.Required(productId), pr.Required(uploadSubmissionId), pr.Required(package), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionUploadHandler>().RunAsync(i, ct));

        // download
        Option<string> downloadSubmissionId = Opt.Str("--submission-id", "Submission id to download the signed package from", true);
        Option<string> downloadOutputFile = Opt.Str("--output-file", "Destination file path for the downloaded package", true);
        Option<bool> downloadOverwrite = Opt.Flag("--overwrite", "Replace the destination file if it already exists");
        Command download = new("download", "Download a submission's signed package");
        download.Options.Add(productId);
        download.Options.Add(downloadSubmissionId);
        download.Options.Add(downloadOutputFile);
        download.Options.Add(downloadOverwrite);
        download.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionDownloadInput(
                pr.Required(productId), pr.Required(downloadSubmissionId), pr.Required(downloadOutputFile),
                pr.GetValue(downloadOverwrite), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionDownloadHandler>().RunAsync(i, ct));

        // wait
        Option<string> waitSubmissionId = Opt.Str("--submission-id", "Submission id to wait on", true);
        Option<bool> waitMetadata = Opt.Flag("--wait-metadata", "Also wait until publisher metadata is available for download");
        Option<uint> pollInterval = Opt.UInt("--poll-interval", "Seconds between status checks", 5);
        Option<uint?> waitTimeout = new("--wait-timeout") { Description = "Give up after this many seconds (default: wait indefinitely)" };
        Command wait = new("wait",
            "Wait until a signedPackage is available (or finalizeIngestion completed). Failure is terminal at any step.");
        wait.Options.Add(productId);
        wait.Options.Add(waitSubmissionId);
        wait.Options.Add(waitMetadata);
        wait.Options.Add(pollInterval);
        wait.Options.Add(waitTimeout);
        wait.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionWaitInput(
                pr.Required(productId), pr.Required(waitSubmissionId), pr.GetValue(waitMetadata),
                pr.GetValue(pollInterval), pr.GetValue(waitTimeout), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionWaitHandler>().RunAsync(i, ct));

        // metadata download / create
        Option<string> metaSubmissionId1 = Opt.Str("--submission-id", "Submission id", true);
        Option<string> metaOutputFile = Opt.Str("--output-file", "Destination file path for the downloaded metadata package", true);
        Option<bool> metaOverwrite = Opt.Flag("--overwrite", "Replace the destination file if it already exists");
        Command metadataDownload = new("download", "Download a submission's publisher metadata package");
        metadataDownload.Options.Add(productId);
        metadataDownload.Options.Add(metaSubmissionId1);
        metadataDownload.Options.Add(metaOutputFile);
        metadataDownload.Options.Add(metaOverwrite);
        metadataDownload.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionMetadataDownloadInput(
                pr.Required(productId), pr.Required(metaSubmissionId1), pr.Required(metaOutputFile),
                pr.GetValue(metaOverwrite), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionMetadataDownloadHandler>().RunAsync(i, ct));

        Option<string> metaSubmissionId2 = Opt.Str("--submission-id", "Submission id", true);
        Command metadataCreate = new("create", "Request generation of a submission's publisher metadata package");
        metadataCreate.Options.Add(productId);
        metadataCreate.Options.Add(metaSubmissionId2);
        metadataCreate.SetHandlerAction(
            accessor,
            (pr, global) => new SubmissionMetadataCreateInput(pr.Required(productId), pr.Required(metaSubmissionId2), global),
            (sp, i, ct) => sp.GetRequiredService<SubmissionMetadataCreateHandler>().RunAsync(i, ct));

        Command metadata = new("metadata", "Publisher metadata for a submission");
        metadata.Subcommands.Add(metadataDownload);
        metadata.Subcommands.Add(metadataCreate);

        Command submission = new("submission", "Manage submissions");
        submission.Subcommands.Add(create);
        submission.Subcommands.Add(list);
        submission.Subcommands.Add(get);
        submission.Subcommands.Add(status);
        submission.Subcommands.Add(commit);
        submission.Subcommands.Add(upload);
        submission.Subcommands.Add(download);
        submission.Subcommands.Add(wait);
        submission.Subcommands.Add(metadata);
        return submission;
    }
}
