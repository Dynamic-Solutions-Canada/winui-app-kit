$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/store/inspect-store-draft.ps1'
if ([IO.File]::ReadAllBytes((Resolve-Path $scriptPath)) | Where-Object { $_ -gt 127 }) { throw 'Script is not ASCII' }

# Record every call: inspecting must make only the token request and GET requests.
$global:inspectCalls = [System.Collections.Generic.List[string]]::new()
function global:Invoke-RestMethod {
    param($Method = 'Get', $Uri, $Headers, $Body, $ContentType)
    $global:inspectCalls.Add("$Method $Uri")
    if ($Uri -like '*oauth2/token') { return @{ access_token = 'test-only' } }
    if ($Uri -match '/submissions/456$') {
        return @{
            status = 'PendingCommit'
            targetPublishMode = 'Manual'
            pricing = @{ priceId = 'Tier1012'; isAdvancedPricingModel = $true }
            applicationPackages = @(@{ fileName = 'FixtureApp-0.1.47.msixupload'; fileStatus = 'PendingUpload'; version = '0.1.47.0'; architecture = 'Neutral' })
        }
    }
    return @{ pendingApplicationSubmission = @{ id = '456' } }
}
function Check-Inspect($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

$report = & $scriptPath -AppId 9TESTAPP0001 -SubmissionId 456 | Out-String | ConvertFrom-Json
Check-Inspect ($report.status -eq 'PendingCommit' -and $report.priceId -eq 'Tier1012' -and $report.targetPublishMode -eq 'Manual') 'reports status, price tier and publishing mode'
Check-Inspect ($report.isAdvancedPricingModel -eq $true -and $report.isPendingSubmission -eq $true) 'reports the pricing model and whether it is the pending submission'
Check-Inspect ($report.packages[0].fileName -eq 'FixtureApp-0.1.47.msixupload') 'reports the packages'
$writes = @($global:inspectCalls | Where-Object { $_ -notmatch '^(Get|Post https://login\.microsoftonline\.com/.*/oauth2/token)' })
Check-Inspect ($writes.Count -eq 0) 'makes no change: only the token request and GET requests'
