$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/store/run-pre-build.ps1'
if ([IO.File]::ReadAllBytes((Resolve-Path $scriptPath)) | Where-Object { $_ -gt 127 }) { throw 'Script is not ASCII' }

# A native command that fails, using the PowerShell running this test so it works on every host.
$pwshPath = (Get-Process -Id $PID).Path
$failingNative = "& '$pwshPath' -NoProfile -Command 'exit 3'"
$passingNative = "& '$pwshPath' -NoProfile -Command 'exit 0'"

function Check-PreBuild($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

# Each case runs in a child PowerShell, as the workflow step does, and reports its exit code.
function Invoke-PreBuild([string]$Commands) {
    & $pwshPath -NoProfile -File $scriptPath -Commands $Commands *> $null
    return $LASTEXITCODE
}

Check-PreBuild ((Invoke-PreBuild "$passingNative; Write-Output done") -eq 0) 'successful commands pass'
Check-PreBuild ((Invoke-PreBuild "$failingNative; Write-Output done") -ne 0) 'a failed native command followed by a successful one fails'
Check-PreBuild ((Invoke-PreBuild "$passingNative; $failingNative") -ne 0) 'a failed last native command fails'
Check-PreBuild ((Invoke-PreBuild "Get-Item -LiteralPath '/no/such/path'; Write-Output done") -ne 0) 'a failed cmdlet followed by a successful one fails'
Check-PreBuild ((Invoke-PreBuild "throw 'boom'") -ne 0) 'a thrown error fails'
