<#
.SYNOPSIS
Replaces the packages in one explicitly selected Store draft without committing it.
.DESCRIPTION
Used after a submission is withdrawn for an incomplete architecture upload. It
preserves the listing, availability, and publication settings in the draft.

With -AssetsPath (store-publish's screenshots input), it also replaces every listing's screenshots with
the app's store-assets set, as set-store-screenshots.ps1 does, and uploads one archive holding the
corrected package and the images, since the draft takes a single upload. Logos are kept.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]+$')][string]$AppId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$SubmissionId,
    # Empty for a free app: pricing is left as it is.
    [ValidatePattern('^(Tier[0-9]+)?$')][string]$PriceId = '',
    [Parameter(Mandatory)][string]$UploadPath,
    # The app's store-assets folder, when the run owns the listing screenshots; empty leaves them alone.
    [string]$AssetsPath = ''
)

$ErrorActionPreference = 'Stop'
$api = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$AppId"
$token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$env:AZURE_AD_TENANT_ID/oauth2/token" -Body @{
    grant_type = 'client_credentials'
    client_id = $env:AZURE_AD_APPLICATION_CLIENT_ID
    client_secret = $env:AZURE_AD_APPLICATION_SECRET
    resource = 'https://manage.devcenter.microsoft.com'
}
$headers = @{ Authorization = "Bearer $($token.access_token)" }
$app = Invoke-RestMethod -Uri $api -Headers $headers
if ([string]$app.pendingApplicationSubmission.id -ne $SubmissionId) {
    throw 'The selected draft is not the current pending submission.'
}

$uri = "$api/submissions/$SubmissionId"
$draft = Invoke-RestMethod -Uri $uri -Headers $headers
if ($draft.status -ne 'PendingCommit') { throw "Draft is not editable: $($draft.status)" }

# The upload must contain a single real bundle, validated by build-store-upload.ps1.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($UploadPath)
try { $names = @($zip.Entries | ForEach-Object FullName) }
finally { $zip.Dispose() }
if ($names.Count -ne 1 -or $names[0] -notmatch '\.msixbundle$') {
    throw 'Expected one MSIX bundle inside the Store upload.'
}

# Mark only existing package entries for removal and upload the corrected bundle.
foreach ($package in $draft.applicationPackages) {
    $package.fileStatus = 'PendingDelete'
}
$draft.applicationPackages = @($draft.applicationPackages) + @(@{
    fileName = [IO.Path]::GetFileName($UploadPath)
    fileStatus = 'PendingUpload'
})
if ($PriceId) { $draft.pricing.priceId = $PriceId }

# Owning the screenshots: plan and build the one archive (package plus images) before any change.
$sendPath = $UploadPath
$work = $null
$plan = $null
if ($AssetsPath) {
    $work = Join-Path ([IO.Path]::GetTempPath()) ('store-repair-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $work -Force
    $plan = & (Join-Path $PSScriptRoot 'resolve-store-screenshots.ps1') -AssetsPath $AssetsPath -ListingLocales @($draft.listings.PSObject.Properties.Name)
    $sendPath = & (Join-Path $PSScriptRoot 'new-store-upload-archive.ps1') -UploadPath $UploadPath -Images $plan.Files -ArchivePath (Join-Path $work 'store-upload.zip')
    $draft = & (Join-Path $PSScriptRoot 'merge-store-screenshots.ps1') -Draft $draft -Plan $plan
}

try {
    $updated = Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -ContentType 'application/json' -Body ($draft | ConvertTo-Json -Depth 100)
    try {
        Invoke-WebRequest -Method Put -Uri $updated.fileUploadUrl -InFile $sendPath -ContentType 'application/zip' -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } | Out-Null
    } catch {
        throw 'Store bundle upload failed. Inspect the pending draft before retrying.'
    }
} finally {
    if ($work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

$verified = Invoke-RestMethod -Uri $uri -Headers $headers
$fileName = [IO.Path]::GetFileName($UploadPath)
if ($PriceId -and $verified.pricing.priceId -ne $PriceId) { throw 'The Store did not retain the requested tier.' }
if (@($verified.applicationPackages | Where-Object { $_.fileName -eq $fileName -and $_.fileStatus -eq 'PendingUpload' }).Count -ne 1) {
    throw 'The Store did not retain the corrected bundle entry.'
}
if ($plan) { & (Join-Path $PSScriptRoot 'confirm-store-screenshots.ps1') -Submission $verified -Plan $plan }
$tier = if ($PriceId) { " at $PriceId" } else { '' }
$shots = if ($plan) { ' and the store-assets screenshots' } else { '' }
Write-Output "Draft $SubmissionId staged with $fileName$tier$shots; NOT committed."
