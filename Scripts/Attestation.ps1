#-------------------------------------------------------------------------------
# Copyright (c) Microsoft Corporation and Contributors. All rights reserved.
# Licensed under the MIT license.  See LICENSE file in the project root for full license information.
#-------------------------------------------------------------------------------
<#
.SYNOPSIS
    Script to use Surface Dev Center Manager to Attestation sign a driver package

.PARAMETER ProductName
    Product Name to use for the driver, visible in Hardware Dev Center

.PARAMETER Signatures
    OS Version and Architecture to submit the driver for

.PARAMETER InputPath
    Path to the EV-signed cab file needed for an Attestation-signed driver
    See steps here:
    https://docs.microsoft.com/en-us/windows-hardware/drivers/dashboard/attestation-signing-a-kernel-driver-for-public-release

.PARAMETER ProductId
    Resume this existing product instead of creating a new one (requires SubmissionId)

.PARAMETER SubmissionId
    Resume this existing submission (requires ProductId)

.PARAMETER WaitTimeoutSeconds
    Give up waiting after this many seconds (default 3600)

.NOTES
    Requires the sdcm dotnet tool to be installed and on PATH:
      dotnet tool install -g Nefarius.Tools.SDCM

    Prefer resuming (-ProductId/-SubmissionId) over creating a new product. Every
    product create consumes a Partner Center product.
#>
#Requires -Version 7.0

param(
  [Parameter(Mandatory = $true, Position = 0)]
  [string] $ProductName,

  [Parameter(Mandatory = $true, Position = 1)]
  [ValidateSet(
    "WINDOWS_v100_X64_RS3_FULL", "WINDOWS_v100_X64_RS4_FULL", "WINDOWS_v100_X64_RS5_FULL",
    "WINDOWS_v100_X64_19H1_FULL", "WINDOWS_v100_X64_VB_FULL", "WINDOWS_v100_X64_CO_FULL",
    "WINDOWS_v100_X64_NI_FULL",
    "WINDOWS_v100_ARM64_RS5_FULL", "WINDOWS_v100_ARM64_19H1_FULL", "WINDOWS_v100_ARM64_VB_FULL",
    "WINDOWS_v100_ARM64_CO_FULL", "WINDOWS_v100_ARM64_NI_FULL"
  )]
  [string[]] $Signatures,

  [Parameter(Mandatory = $true, Position = 2)]
  [ValidateScript( { Test-Path -Path $_ -PathType Leaf })]
  [string] $InputPath,

  [Parameter(Mandatory = $false)]
  [string] $ProductId,

  [Parameter(Mandatory = $false)]
  [string] $SubmissionId,

  [Parameter(Mandatory = $false)]
  [uint] $WaitTimeoutSeconds = 3600
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

function Get-SelectedProductTypes {
  param([string[]] $Requested)
  $map = @{}
  foreach ($signature in $Requested) {
    if ($signature -match 'WINDOWS_v100_.*_(RS3)_') { $map['windows_v100_RS3'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(RS4)_') { $map['windows_v100_RS4'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(RS5)_') { $map['windows_v100_RS5'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(19H1)_') { $map['windows_v100_19H1'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(VB)_') { $map['windows_v100_VB'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(CO)_') { $map['windows_v100_CO'] = 'Unclassified' }
    elseif ($signature -match 'WINDOWS_v100_.*_(NI)_') { $map['windows_v100_NI'] = 'Unclassified' }
    else { $map['windows_v100_RS5'] = 'Unclassified' }
  }
  return $map
}

function Get-SubmissionProgress {
  param([string] $HardwareProductId, [string] $HardwareSubmissionId)
  $raw = Invoke-Sdcm submission status --product-id $HardwareProductId --submission-id $HardwareSubmissionId
  return $raw | ConvertFrom-Json
}

###################################################################################################
# Main
###################################################################################################

Write-Output "Attestation Submission"
Write-Output ""

$resume = [bool]$ProductId -or [bool]$SubmissionId
if ([bool]$ProductId -ne [bool]$SubmissionId) {
  throw "ProductId and SubmissionId must be supplied together to resume."
}

if ($resume) {
  $SdcmProductId = $ProductId
  $SdcmSubmissionId = $SubmissionId
  Write-Output "> Resume Product $SdcmProductId Submission $SdcmSubmissionId"
}
else {
  Write-Output "> Create Product"
  $product = [ordered]@{
    productName          = "$($ProductName)_Attestation"
    testHarness          = "Attestation"
    announcementDate     = (Get-Date).AddDays(7).ToString("s")
    firmwareVersion      = "0"
    deviceType           = "external"
    isTestSign           = $false
    isFlightSign         = $false
    selectedProductTypes = Get-SelectedProductTypes -Requested $Signatures
    requestedSignatures  = $Signatures
  }
  $product | ConvertTo-Json | Out-File -Encoding utf8 -FilePath "CreateAttest.json"
  $productResult = Invoke-Sdcm product create --input "CreateAttest.json" | ConvertFrom-Json
  $SdcmProductId = $productResult.id
  Write-Output "    * ProductId: $SdcmProductId"

  Write-Output "> Create Submission"
  $submission = [ordered]@{
    name = "$($ProductName)_Attestation_Submission"
    type = "initial"
  }
  $submission | ConvertTo-Json | Out-File -Encoding utf8 -FilePath "CreateSubmissionAttest.json"
  $submissionResult = Invoke-Sdcm submission create --product-id $SdcmProductId --input "CreateSubmissionAttest.json" | ConvertFrom-Json
  $SdcmSubmissionId = $submissionResult.id
  Write-Output "    * SubmissionId: $SdcmSubmissionId"
}

Write-Output "    * Dev Center URL: https://developer.microsoft.com/en-us/dashboard/hardware/driver/$SdcmProductId"
$status = Get-SubmissionProgress -HardwareProductId $SdcmProductId -HardwareSubmissionId $SdcmSubmissionId
Write-Output "    * progress: $($status.progress) (commitStatus=$($status.commitStatus); state=$($status.state); currentStep=$($status.currentStep))"

if ($status.progress -eq 'failed') {
  if ($status.errorReportContent) { Write-Output $status.errorReportContent }
  throw "Submission $SdcmSubmissionId already failed."
}

if ($status.progress -eq 'created') {
  Write-Output "> Upload File"
  Invoke-Sdcm submission upload --product-id $SdcmProductId --submission-id $SdcmSubmissionId --package $InputPath
  Write-Output "> Commit Submission"
  Invoke-Sdcm submission commit --product-id $SdcmProductId --submission-id $SdcmSubmissionId
  $status = Get-SubmissionProgress -HardwareProductId $SdcmProductId -HardwareSubmissionId $SdcmSubmissionId
}

if ($status.progress -eq 'processing') {
  Write-Output "> Wait for Submission to complete"
  Invoke-Sdcm submission wait --product-id $SdcmProductId --submission-id $SdcmSubmissionId --wait-timeout $WaitTimeoutSeconds
  $status = Get-SubmissionProgress -HardwareProductId $SdcmProductId -HardwareSubmissionId $SdcmSubmissionId
}

if ($status.progress -eq 'failed') {
  if ($status.errorReportContent) { Write-Output $status.errorReportContent }
  throw "Submission $SdcmSubmissionId failed during processing."
}

Write-Output "> Download File"
$signedPackagePath = "$InputPath.signed.zip"
Invoke-Sdcm submission download --product-id $SdcmProductId --submission-id $SdcmSubmissionId --output-file $signedPackagePath --overwrite

Write-Output "> Done"
Write-Output "    * Output: $signedPackagePath"
