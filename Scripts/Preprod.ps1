#-------------------------------------------------------------------------------
# Copyright (c) Microsoft Corporation and Contributors. All rights reserved.
# Licensed under the MIT license.  See LICENSE file in the project root for full license information.
#-------------------------------------------------------------------------------
<#
.SYNOPSIS
    Script to use Surface Dev Center Manager to get a driver package signed for preproduction testing

.PARAMETER InputPath
    Path to the EV-signed cab/zip file to submit for preprod signing
    See steps here:
    https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/manage-preprod-submissions

.PARAMETER OutputPath
    Destination file path for the downloaded, preprod-signed package. Defaults to
    "<InputPath>.signed.zip" if not specified.

.NOTES
    Requires the sdcm dotnet tool to be installed and on PATH:
      dotnet tool install -g Nefarius.Tools.SDCM

    Unlike Attestation.ps1/HLKx.ps1, preprod signing has no product/submission concept - a package is
    submitted and signed on its own, identified only by the packageId returned from the submit step.
#>
#Requires -Version 7.0

param(
  [Parameter(Mandatory = $true, Position = 0)]
  [ValidateScript( { Test-Path -LiteralPath $_ -PathType Leaf })]
  [string] $InputPath,

  [Parameter(Mandatory = $false, Position = 1)]
  [string] $OutputPath = "$InputPath.signed.zip"
)

###################################################################################################
# Globals
###################################################################################################
$global:ErrorActionPreference = "stop"
Set-StrictMode -Version Latest

function Invoke-Sdcm {
  & sdcm --output json @args
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0) {
    [Console]::Error.WriteLine("sdcm $($args -join ' ') failed with exit code $exitCode")
    exit $exitCode
  }
}

###################################################################################################
# Main
###################################################################################################

Write-Output "Preprod Submission"
Write-Output ""

Write-Output "> Submit Package"
$submitResult = Invoke-Sdcm preprod-submission submit --package $InputPath | ConvertFrom-Json
$PackageId = $null
if ($submitResult -and $submitResult.PSObject.Properties['id']) {
  $PackageId = $submitResult.id
}
if ([string]::IsNullOrEmpty($PackageId)) {
  [Console]::Error.WriteLine("sdcm preprod-submission submit did not return a package id.")
  exit 1
}
Write-Output "    * PackageId: $PackageId"

Write-Output "> Wait for Signing to complete"
Invoke-Sdcm preprod-submission wait --package-id $PackageId | Out-Null

Write-Output "> List Assets"
$assets = @(Invoke-Sdcm preprod-submission assets --package-id $PackageId | ConvertFrom-Json)
$signedAsset = $assets | Where-Object { $_.assetType -eq "SignedFilesZip" } | Select-Object -First 1
if (-not $signedAsset) {
  [Console]::Error.WriteLine("No 'SignedFilesZip' asset was found for package $PackageId.")
  exit 1
}
Write-Output "    * AssetId: $($signedAsset.id)"

Write-Output "> Download Signed Package"
Invoke-Sdcm preprod-submission download --package-id $PackageId --asset-id $signedAsset.id --output-file $OutputPath

Write-Output "> Done"
Write-Output "    * Output: $OutputPath"
