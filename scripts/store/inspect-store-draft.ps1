<#
.SYNOPSIS
Reports a Store submission's status, pricing and publishing mode. Read-only: it changes nothing.
.DESCRIPTION
For a draft or a pending submission: its status, the pricing model (whether the account uses the
advanced pricing model, which decides the tier range), the price tier and the publishing mode.
Credentials come from CI.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]+$')][string]$AppId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$SubmissionId
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

# Only GET requests from here on.
$app = Invoke-RestMethod -Uri $api -Headers $headers
$submission = Invoke-RestMethod -Uri "$api/submissions/$SubmissionId" -Headers $headers

[pscustomobject]@{
    submission = $SubmissionId
    isPendingSubmission = [string]$app.pendingApplicationSubmission.id -eq $SubmissionId
    status = $submission.status
    isAdvancedPricingModel = $submission.pricing.isAdvancedPricingModel
    priceId = $submission.pricing.priceId
    targetPublishMode = $submission.targetPublishMode
    packages = @($submission.applicationPackages | Select-Object fileName, fileStatus, version, architecture)
} | ConvertTo-Json -Depth 5 | Write-Output
