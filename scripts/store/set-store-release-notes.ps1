<#
.SYNOPSIS
Verifies a newly uploaded Store draft, sets the release notes of every listing locale, and optionally
submits it.
.DESCRIPTION
Requires the expected versioned upload so an unrelated pending draft cannot be submitted. Each locale's
"What's new" comes from resolve-store-release-notes.ps1: its catalog's notes file, else the English notes
(always the English notes for an English-only listing). Preserves all other listing, pricing, and
publication settings. Credentials come from CI.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]+$')][string]$AppId,
    [Parameter(Mandatory)][string]$UploadFileName,
    [Parameter(Mandatory)][string]$NotesPath,
    [string]$WindowsBuildJson = 'windows-build.json',
    [ValidatePattern('^[0-9]+$')][string]$ExpectedSubmissionId,
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
# Resolve and check every locale's notes before touching the Store, so a bad file changes nothing.
$notes = & (Join-Path $PSScriptRoot 'resolve-store-release-notes.ps1') -NotesPath $NotesPath -WindowsBuildJson $WindowsBuildJson
function Get-LocaleNotes([string]$Locale) {
    $key = $Locale.ToLowerInvariant()
    if ($notes.Locales.ContainsKey($key)) { return $notes.Locales[$key] }
    return $notes.Default
}

$api = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$AppId"
$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$env:AZURE_AD_TENANT_ID/oauth2/token" -Body @{
    grant_type = 'client_credentials'
    client_id = $env:AZURE_AD_APPLICATION_CLIENT_ID
    client_secret = $env:AZURE_AD_APPLICATION_SECRET
    resource = 'https://manage.devcenter.microsoft.com'
}
$headers = @{ Authorization = "Bearer $($token.access_token)" }
$app = Invoke-RestMethod -Uri $api -Headers $headers
$submissionId = [string]$app.pendingApplicationSubmission.id
if ($submissionId -notmatch '^[0-9]+$') { throw 'No pending Store submission was found.' }
if ($ExpectedSubmissionId -and $submissionId -ne $ExpectedSubmissionId) {
    throw 'The selected draft is not the current pending submission.'
}

$uri = "$api/submissions/$submissionId"
$draft = Invoke-RestMethod -Uri $uri -Headers $headers
if ($draft.status -ne 'PendingCommit') { throw "Draft is not editable: $($draft.status)" }
if (@($draft.applicationPackages | Where-Object {
    $_.fileName -eq $UploadFileName -and $_.fileStatus -eq 'PendingUpload'
}).Count -ne 1) {
    throw 'The draft does not contain the expected release upload.'
}

# Set every listing's notes; everything else in the listing and its platform overrides is kept.
$listings = @($draft.listings.PSObject.Properties)
if (@($listings | Where-Object Name -Match '^en(-|$)').Count -eq 0) { throw 'No English Store listing was found.' }
foreach ($listing in $listings) {
    $listing.Value.baseListing | Add-Member -NotePropertyName releaseNotes -NotePropertyValue (Get-LocaleNotes $listing.Name) -Force
}
Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -ContentType 'application/json' -Body ($draft | ConvertTo-Json -Depth 100) | Out-Null

$verified = Invoke-RestMethod -Uri $uri -Headers $headers
foreach ($listing in $listings) {
    if ($verified.listings.($listing.Name).baseListing.releaseNotes -cne (Get-LocaleNotes $listing.Name)) {
        throw "Release notes were not retained for $($listing.Name)."
    }
}
$fromEnglish = @($listings | Where-Object { (Get-LocaleNotes $_.Name) -ceq $notes.Default } | ForEach-Object Name)
$translated = @($listings | Where-Object { (Get-LocaleNotes $_.Name) -cne $notes.Default } | ForEach-Object Name)
Write-Output "Submission ${submissionId}: verified What's new: $($notes.Default)"
Write-Output "English notes: $($fromEnglish -join ', '); translated notes: $(if ($translated) { $translated -join ', ' } else { 'none' })$(if ($notes.EnglishOnly) { ' (English-only listing)' })"
Write-Output "Price tier: $($verified.pricing.priceId); publishing mode: $($verified.targetPublishMode)"

if ($Commit) {
    Invoke-RestMethod -Method Post -Uri "$uri/commit" -Headers $headers -ContentType 'application/json' | Out-Null
    # Wait for ingestion to accept the submission, not for the full certification process.
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $status = Invoke-RestMethod -Uri "$uri/status" -Headers $headers
        Write-Output "Submission $submissionId status: $($status.status)"
        if ($status.status -match 'Failed$' -or $status.status -eq 'Canceled') {
            throw "Store submission failed: $($status.status). Review Partner Center."
        }
        if ($status.status -in @('Certification', 'PendingPublication', 'Publishing', 'Published', 'Release')) {
            break
        }
        Start-Sleep -Seconds 10
    }
    if ($status.status -notin @('Certification', 'PendingPublication', 'Publishing', 'Published', 'Release')) {
        throw 'Store ingestion is still pending. Inspect this submission before retrying.'
    }
    $submitted = Invoke-RestMethod -Uri $uri -Headers $headers
    $submitted.applicationPackages | Select-Object fileName, version, architecture, fileStatus | Format-Table -AutoSize
}
