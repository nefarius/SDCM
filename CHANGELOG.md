# Changelog

All notable changes to this fork (`Nefarius.Tools.SDCM`) are documented here. The project is
pre-1.0; output-shape changes are intentional.

## Unreleased

### Fixed

- `submission wait` no longer treats `workflowStatus.state == completed` as success. Success
  requires a `signedPackage` download, or `completed` on `finalizeIngestion`. Failure is still
  terminal at any step. The shared `IsTerminal` helper (used by shipping labels) is unchanged.
- `shipping-label wait` now requires `state == published` (or `failed`). Intermediate `completed`
  steps keep polling.
- `submission commit` is a no-op when the submission is already `commitComplete`, regardless of the
  HTTP status the commit POST returned (the library only special-cased HTTP 502).
- `submission upload` refuses with `InvalidState` unless `commitStatus` is `commitPending`.
- Download verbs create missing parent directories. `--overwrite` replaces an existing destination
  (`submission download`, `submission metadata download`, `preprod-submission download`).

### Added

- `submission get` and `product get` return a single JSON object. `list --submission-id` /
  `list --product-id` still return a one-element array and print a deprecation warning.
- `submission status` emits `{ progress, commitStatus, state, currentStep, hasSignedPackage }`
  (`created` | `processing` | `completed` | `failed`).
- `upload`, `commit`, `download`, and `metadata create` emit a JSON result in `--output json`.
- Failed JSON results include `workflowStatus.errorReportContent` (the blob), not just the URL.
- `--replay <fixtures.json>` / `SDCM_REPLAY` runs against the same in-memory backend as the handler
  tests. No credentials, no Partner Center product.
- `docs/submission-states.md` documents `commitStatus`, `state`, `currentStep`, and download types.
- Handler tests cover the state / step / commitStatus / downloads matrix.

### Changed

- [Scripts/Attestation.ps1](Scripts/Attestation.ps1) and [Scripts/HLKx.ps1](Scripts/HLKx.ps1) are
  status-aware and resumable, accept modern OS signature codes, and pass `--wait-timeout` /
  `--overwrite`.
