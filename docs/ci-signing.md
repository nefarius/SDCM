# Submit and wait for signed artifacts from CI

sdcm is meant to run non-interactively in a pipeline: submit an already EV-signed package, poll
until Hardware Dev Center finishes, then download the signed result.

Credentials and the Partner Center API key are covered in [Authentication](authentication.md). This
page is the submit / wait / download sequence.


## Credentials in CI

Do not use `--auth interactive`. Prefer environment variables so nothing writes `authconfig.json`
into the workspace:

```text
SDCM_PROFILES__DEFAULT__TENANTID
SDCM_PROFILES__DEFAULT__CLIENTID
SDCM_PROFILES__DEFAULT__KEY
```

With those set, `auto` selects client-secret. Pass `--auth client-secret` if you want a hard failure
when `key` is missing instead of falling through to interactive.

Alternatively, write a job-scoped `authconfig.json` with `sdcm config set --config ...` and pass
that path as `--config`. Environment variables are the usual choice.

sdcm does not hold or apply your [EV certificate](https://docs.microsoft.com/en-us/windows-hardware/drivers/dashboard/get-a-code-signing-certificate).
The package you upload must already be EV-signed with the certificate registered on the Hardware
account.


## Exit codes that matter in a job

| Code | Meaning in this flow |
|------|----------------------|
| 0    | Success |
| 2    | AuthenticationFailed - missing or unusable credentials |
| 7    | WorkflowFailed - Hardware Dev Center rejected or failed the submission |
| 9    | Canceled - Ctrl+C, or `--wait-timeout` was exceeded |

Always pass `--output json` and fail the job on a non-zero exit. `--wait-timeout` should be shorter
than the CI job's own timeout so a stuck submission fails the step instead of hanging the runner.


## Attestation / WHQL

Create a product and submission, upload the EV-signed package, commit, wait, download:

```bash
product=$(sdcm product create --input product.json --output json --auth client-secret)
productId=$(echo "$product" | jq -r .id)

submission=$(sdcm submission create --product-id "$productId" --input submission.json --output json --auth client-secret)
submissionId=$(echo "$submission" | jq -r .id)

sdcm submission upload --product-id "$productId" --submission-id "$submissionId" --package "$PACKAGE" --auth client-secret
sdcm submission commit --product-id "$productId" --submission-id "$submissionId" --auth client-secret
sdcm submission wait --product-id "$productId" --submission-id "$submissionId" --wait-timeout 3600 --auth client-secret
sdcm submission download --product-id "$productId" --submission-id "$submissionId" --output-file signed.zip --auth client-secret
```

`--wait-timeout` is seconds. Add `--wait-metadata` if you also need publisher metadata to be ready
before the wait returns.

The same sequence, already scripted for a local PowerShell 7 shell:

- [Scripts/Attestation.ps1](../Scripts/Attestation.ps1)
- [Scripts/HLKx.ps1](../Scripts/HLKx.ps1)


## Preproduction signing

No product or submission: submit the EV-signed cab/zip, wait, pick the `SignedFilesZip` asset,
download:

```bash
submit=$(sdcm preprod-submission submit --package "$PACKAGE" --output json --auth client-secret)
packageId=$(echo "$submit" | jq -r .id)

sdcm preprod-submission wait --package-id "$packageId" --wait-timeout 1800 --auth client-secret
assets=$(sdcm preprod-submission assets --package-id "$packageId" --output json --auth client-secret)
assetId=$(echo "$assets" | jq -r '.[] | select(.assetType=="SignedFilesZip") | .id')
sdcm preprod-submission download --package-id "$packageId" --asset-id "$assetId" --output-file signed.zip --auth client-secret
```

Ready-made script: [Scripts/Preprod.ps1](../Scripts/Preprod.ps1).


## GitHub Actions

Map repository secrets onto the `SDCM_*` variables, install the tool, then run the wait with a
timeout that fits the job:

```yaml
env:
  SDCM_PROFILES__DEFAULT__TENANTID: ${{ secrets.SDCM_TENANT_ID }}
  SDCM_PROFILES__DEFAULT__CLIENTID: ${{ secrets.SDCM_CLIENT_ID }}
  SDCM_PROFILES__DEFAULT__KEY: ${{ secrets.SDCM_KEY }}

jobs:
  sign:
    runs-on: windows-latest
    timeout-minutes: 90
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet tool install -g Nefarius.Tools.SDCM
      - name: Submit and wait
        run: |
          $submit = sdcm preprod-submission submit --package "${{ github.workspace }}\out\package.cab" --output json --auth client-secret
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          $packageId = ($submit | ConvertFrom-Json).id
          sdcm preprod-submission wait --package-id $packageId --wait-timeout 3600 --auth client-secret
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Swap the submit/wait pair for the Attestation/WHQL commands when you need a full product
submission. Shipping labels use the same wait pattern via `sdcm shipping-label wait` and
[Scripts/ShippingLabel.ps1](../Scripts/ShippingLabel.ps1).
