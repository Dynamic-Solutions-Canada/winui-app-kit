<#
.SYNOPSIS
Replaces every listing's screenshots in the pending Store draft with the app's store-assets set, sending
them in one archive with the package.
.DESCRIPTION
Run by store-publish when its screenshots input is on, after the Store CLI has staged the package and
before the release notes are set. Each listing locale gets the screenshots resolve-store-screenshots.ps1
plans: the locale's generated hero first when the manifest has one, then the other screenshots in
manifest order (the locale's own copy of each localized one), each captioned in that locale. Logos and all other listing content are kept (merge-store-screenshots.ps1).

The draft has one upload location, which already holds the CLI's package, so this uploads one archive
holding the package again plus every image (new-store-upload-archive.ps1). The draft must still expect
exactly that package as PendingUpload, so an unrelated draft is never changed. It never commits.

-PlanOnly makes no Store call and needs no credentials: it plans every locale in listings.json and
builds the archive, so a dry run fails on a missing image or caption the same way a real run would.
#>
param(
    [ValidatePattern('^[A-Z0-9]*$')][string]$AppId = '',
    # The .msixupload the CLI staged; its file name is the package name the draft registered.
    [Parameter(Mandatory)][string]$UploadPath,
    [string]$AssetsPath = 'store-assets',
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ('store-screenshots-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work -Force

try {
    if ($PlanOnly) {
        # Every listing locale the repo has; the draft's own locales are only known with a Store call.
        $records = (Get-Content -LiteralPath (Join-Path $AssetsPath 'microsoft/listings.json') -Raw -Encoding utf8 | ConvertFrom-Json).locales
        $plan = & (Join-Path $PSScriptRoot 'resolve-store-screenshots.ps1') -AssetsPath $AssetsPath -ListingLocales @($records.PSObject.Properties.Name)
        $archive = & (Join-Path $PSScriptRoot 'new-store-upload-archive.ps1') -UploadPath $UploadPath -Images $plan.Files -ArchivePath (Join-Path $work 'store-upload.zip')
        $size = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
        Write-Output "Store screenshots planned: $($plan.Locales.Count) locales, $(@($plan.Locales.Values)[0].Count) screenshots each; upload archive $size MB."
        return
    }
    if (-not $AppId) { throw 'AppId is required unless -PlanOnly is set.' }

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
    $uri = "$api/submissions/$submissionId"

    # Only the draft the CLI just staged: still editable, and expecting exactly this package.
    $draft = Invoke-RestMethod -Uri $uri -Headers $headers
    if ($draft.status -ne 'PendingCommit') { throw "Draft is not editable: $($draft.status)" }
    $packageName = [IO.Path]::GetFileName($UploadPath)
    if (@($draft.applicationPackages | Where-Object { $_.fileName -eq $packageName -and $_.fileStatus -eq 'PendingUpload' }).Count -ne 1) {
        throw 'The draft does not contain the expected release upload.'
    }

    # Plan and build everything before the first change, so a missing file changes nothing.
    $plan = & (Join-Path $PSScriptRoot 'resolve-store-screenshots.ps1') -AssetsPath $AssetsPath -ListingLocales @($draft.listings.PSObject.Properties.Name)
    $archive = & (Join-Path $PSScriptRoot 'new-store-upload-archive.ps1') -UploadPath $UploadPath -Images $plan.Files -ArchivePath (Join-Path $work 'store-upload.zip')
    $draft = & (Join-Path $PSScriptRoot 'merge-store-screenshots.ps1') -Draft $draft -Plan $plan

    $updated = Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -ContentType 'application/json' -Body ($draft | ConvertTo-Json -Depth 100)
    $uploadUrl = if ($updated.fileUploadUrl) { $updated.fileUploadUrl } else { $draft.fileUploadUrl }
    # The URL carries a write token: check where it points, and never print it.
    $destination = $null
    if (-not [Uri]::TryCreate([string]$uploadUrl, [UriKind]::Absolute, [ref]$destination) -or
        $destination.Scheme -ne 'https' -or -not $destination.Host.EndsWith('.blob.core.windows.net')) {
        throw 'The Store draft returned an unexpected upload location.'
    }
    try {
        Invoke-WebRequest -Method Put -Uri $uploadUrl -InFile $archive -ContentType 'application/zip' -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } | Out-Null
    } catch {
        throw 'The Store upload of the package and screenshots failed. Inspect the pending draft before retrying.'
    }

    # Every listing must now expect exactly the planned screenshots, in order.
    $verified = Invoke-RestMethod -Uri $uri -Headers $headers
    & (Join-Path $PSScriptRoot 'confirm-store-screenshots.ps1') -Submission $verified -Plan $plan
    if (@($verified.applicationPackages | Where-Object { $_.fileName -eq $packageName -and $_.fileStatus -eq 'PendingUpload' }).Count -ne 1) {
        throw 'The Store did not keep the release upload.'
    }

    $fallback = if ($plan.Fallback) { "; en-US screenshots for $($plan.Fallback -join ', ')" } else { '' }
    Write-Output "Submission ${submissionId}: screenshots replaced in $($plan.Locales.Count) listings$fallback; NOT committed."
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
