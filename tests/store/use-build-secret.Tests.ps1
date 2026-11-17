$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/store/use-build-secret.ps1'
if ([IO.File]::ReadAllBytes((Resolve-Path $scriptPath)) | Where-Object { $_ -gt 127 }) { throw 'Script is not ASCII' }
$pwshPath = (Get-Process -Id $PID).Path

function Check-Secret($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

# Run the helper in a child PowerShell, as a workflow step does, then report what the step would see
# for $Probe. Returns the exit code and the child's output.
function Invoke-Helper([string]$Name, [string]$Value, [string]$Probe = 'FixtureBuildSecret') {
    $env:BUILD_SECRET_ENV_NAME = $Name
    $env:BUILD_SECRET = $Value
    try {
        $command = "`$PSStyle.OutputRendering = 'PlainText'; & '$scriptPath'; 'seen=' + [string]([Environment]::GetEnvironmentVariable('$Probe') -eq 'fixture-value') + ';set=' + [string](`$null -ne [Environment]::GetEnvironmentVariable('$Probe'))"
        $output = & $pwshPath -NoProfile -Command $command 2>&1 | Out-String
        # The child wraps long error lines; join them so messages can be matched whole.
        return @{ Exit = $LASTEXITCODE; Output = ($output -replace '\s*\|\s*', ' ' -replace '\s+', ' ') }
    } finally {
        Remove-Item Env:BUILD_SECRET_ENV_NAME, Env:BUILD_SECRET -ErrorAction SilentlyContinue
    }
}

$result = Invoke-Helper 'FixtureBuildSecret' 'fixture-value'
Check-Secret ($result.Exit -eq 0 -and $result.Output -match 'seen=True') 'the secret reaches the step under the named variable'
Check-Secret ($result.Output -notmatch 'fixture-value') 'the value is never printed'

$result = Invoke-Helper '' ''
Check-Secret ($result.Exit -eq 0 -and $result.Output -match 'set=False') 'no secret and no name sets nothing'

$result = Invoke-Helper 'FixtureBuildSecret' ''
Check-Secret ($result.Exit -ne 0 -and $result.Output -match 'no BUILD_SECRET was passed') 'a name without a secret stops the run'

$result = Invoke-Helper '' 'fixture-value'
Check-Secret ($result.Exit -ne 0 -and $result.Output -match 'build_secret_env_name is empty' -and $result.Output -notmatch 'fixture-value') 'a secret without a name stops the run, without printing it'

$result = Invoke-Helper 'Bad-Name' 'fixture-value'
Check-Secret ($result.Exit -ne 0 -and $result.Output -match 'not a valid') 'an invalid name is refused'

$result = Invoke-Helper 'PATH' 'fixture-value' 'PATH'
Check-Secret ($result.Exit -ne 0 -and $result.Output -match 'already set') 'an existing variable such as PATH is never overwritten'
