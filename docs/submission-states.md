# Submission state machine

Hardware Dev Center's submission resource mixes three independent signals. Pipelines that treat
`workflowStatus.state == completed` as "the submission is done" will download too early.

`sdcm submission status` folds these into one `progress` value. Prefer that over rebuilding the
rules below.


## `commitStatus`

Documented values: `commitPending`, `commitComplete`, `commitFailed`.

The API reference shows PascalCase (`CommitPending`). The service usually returns camelCase
(`commitPending`). Compare case-insensitively.

| Value | Meaning |
|-------|---------|
| `commitPending` | Package slot is open. `submission upload` is allowed. `submission commit` is required. |
| `commitComplete` | Hardware Dev Center owns the package. Upload is refused (`InvalidState`). Commit is a no-op. |
| `commitFailed` | Terminal failure. `submission status` / `wait` return `WorkflowFailed` (exit 7). |

`submission commit` is idempotent: if a re-fetch shows `commitComplete`, the verb succeeds regardless
of the HTTP status the commit POST returned. The library only special-cases HTTP 502 +
`requestInvalidForCurrentState`; sdcm widens that in `SubmissionCommitHandler`.


## `workflowStatus.state`

One of: `notStarted`, `started`, `failed`, `completed`.

**`state` describes `currentStep` only, not the submission.** A payload of

```json
{ "currentStep": "scanning", "state": "completed" }
```

means the malware scan finished, not that a signed package exists.


## `workflowStatus.currentStep`

Ingestion steps, in order:

| Step | Meaning |
|------|---------|
| `packageInfoValidation` | Validating package metadata and contents |
| `preparation` | Getting the package ready for processing |
| `scanning` | Malware scan |
| `validation` | Validation of test results |
| `catalogCreation` | Creating a security catalog |
| `manualReview` | Undergoing manual review |
| `signing` | Signing binaries |
| `finalizeIngestion` | Completing ingestion; signed files becoming downloadable |

The service sometimes returns `currentStep` as a **number** rather than a name. The client library
stringifies it (`LongToStringJsonConverter`). A step-name comparison cannot stand alone.


## `downloads.items[].type`

Common types:

| Type | When it appears |
|------|-----------------|
| `initialPackage` | Created with the submission; SAS URL for `submission upload` |
| `signedPackage` | Present once signing finished; SAS URL for `submission download` |
| `driverMetadata` | After `submission metadata create` completes |
| `certificationReport` | WHQL / attestation report, when the service attaches one |

`signedPackage` is the only unambiguous "submission succeeded" signal.


## What `submission wait` treats as done

- **Failure** (terminal at any step): `commitStatus == commitFailed` or `state == failed`.
- **Success:** a `signedPackage` download item, **or** `state == completed` on step
  `finalizeIngestion`. The download item is the primary gate.
- `--wait-metadata` additionally requires a `driverMetadata` item.

`WorkflowStatusExtensions.IsTerminal` is **not** used here. It still treats `completed` and
`published` as terminal for shipping labels, where `published` is the success signal. Intermediate
`completed` steps on a shipping label do **not** end `shipping-label wait`.


## `submission status` progress

```json
{
  "progress": "created",
  "commitStatus": "commitPending",
  "state": "notStarted",
  "currentStep": "packageInfoValidation",
  "hasSignedPackage": false
}
```

| `progress` | Rule |
|------------|------|
| `failed` | `commitFailed` or `state == failed` |
| `completed` | `signedPackage` present, or `finalizeIngestion` + `completed` |
| `created` | `commitPending` (or missing) and the workflow has not started |
| `processing` | everything else, including `completed` on an intermediate step |

On failure, `--output json` includes `errorReportContent` (the blob behind
`workflowStatus.errorReport`), not just the URL.


## Offline replay

`--replay fixtures.json` or `SDCM_REPLAY` drives the same fake backend the tests use. No
credentials, no Partner Center product. See [CI signing](ci-signing.md#offline-replay).
