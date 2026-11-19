$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../build/runtime-dependency.ps1'
if ([IO.File]::ReadAllBytes((Resolve-Path $scriptPath)) | Where-Object { $_ -gt 127 }) { throw "$scriptPath is not ASCII" }
. $scriptPath

function Check-Output($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

# The message a call throws, or '' when it does not throw.
function Get-ThrownMessage([scriptblock]$Call) {
    try { & $Call; return '' } catch { return $_.Exception.Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ("runtime-dependency-" + [guid]::NewGuid().ToString('N'))
try {
    # --- A restore with the runtime package, shaped like NuGet's ---

    $packages = Join-Path $root 'packages'
    $runtimeDir = Join-Path $packages 'microsoft.windowsappsdk.runtime/2.7.0'
    New-Item $runtimeDir -ItemType Directory -Force | Out-Null
    $info = '{ "Runtime": { "Version": { "DotQuadNumber": "2.7.0.0" }, "Packages": { "Framework": { "PackageFamilyName": "Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe" } } } }'
    # The real file starts with a byte order mark.
    [IO.File]::WriteAllText((Join-Path $runtimeDir 'WindowsAppSDK-VersionInfo.json'), $info, [Text.UTF8Encoding]::new($true))

    function New-Assets([string]$Name, [string[]]$Libraries) {
        $entries = ($Libraries | ForEach-Object { "`"$_`": { `"path`": `"$($_.ToLowerInvariant())`" }" }) -join ', '
        $folder = ($packages -replace '\\', '\\') + '/'
        $path = Join-Path $root $Name
        Set-Content -LiteralPath $path -Value "{ `"libraries`": { $entries }, `"packageFolders`": { `"$folder`": {} } }"
        return $path
    }

    # --- Reading the requirement ---

    $assets = New-Assets 'assets.json' @('Microsoft.WindowsAppSDK/2.7.0', 'Microsoft.WindowsAppSDK.Runtime/2.7.0')
    $requirement = Get-WindowsAppRuntimeRequirement $assets
    Check-Output ($requirement.Name -ceq 'Microsoft.WindowsAppRuntime.2') 'the framework name is the package family name without its publisher id'
    Check-Output ($requirement.Version -eq [version]'2.7.0.0') 'the runtime version is the version file DotQuadNumber'

    Check-Output ((Get-ThrownMessage { Get-WindowsAppRuntimeRequirement (Join-Path $root 'missing.json') }) -match 'restore the app first') 'a missing restore output fails'
    $noRuntime = New-Assets 'no-runtime.json' @('Microsoft.WindowsAppSDK/2.7.0')
    Check-Output ((Get-ThrownMessage { Get-WindowsAppRuntimeRequirement $noRuntime }) -match 'found 0') 'a restore without the runtime package fails'
    $noFile = New-Assets 'no-file.json' @('Microsoft.WindowsAppSDK.Runtime/2.8.0')
    Check-Output ((Get-ThrownMessage { Get-WindowsAppRuntimeRequirement $noFile }) -match 'VersionInfo.json not found') 'a runtime package without its version file fails'

    # --- Comparing the manifest ---

    function New-Manifest([string]$Name, [string]$Dependency) {
        $path = Join-Path $root $Name
        Set-Content -LiteralPath $path -Value "<Package xmlns=`"http://schemas.microsoft.com/appx/manifest/foundation/windows10`"><Dependencies><TargetDeviceFamily Name=`"Windows.Desktop`" MinVersion=`"10.0.19045.0`" MaxVersionTested=`"10.0.26100.0`" />$Dependency</Dependencies></Package>"
        return $path
    }
    function Get-Dependency([string]$Name, [string]$MinVersion) {
        return "<PackageDependency Name=`"$Name`" MinVersion=`"$MinVersion`" Publisher=`"CN=Microsoft Corporation`" />"
    }

    $equal = New-Manifest 'equal.xml' (Get-Dependency 'Microsoft.WindowsAppRuntime.2' '2.7.0.0')
    Check-Output ($null -eq (Test-WindowsAppRuntimeDependency $equal $requirement)) 'a MinVersion equal to the runtime passes'
    $higher = New-Manifest 'higher.xml' (Get-Dependency 'Microsoft.WindowsAppRuntime.2' '2.10.0.0')
    Check-Output ($null -eq (Test-WindowsAppRuntimeDependency $higher $requirement)) 'a higher MinVersion passes, compared as a version, not text'
    $lower = New-Manifest 'lower.xml' (Get-Dependency 'Microsoft.WindowsAppRuntime.2' '2.6.5.0')
    Check-Output ((Test-WindowsAppRuntimeDependency $lower $requirement) -match 'MinVersion 2\.6\.5\.0.*Set MinVersion="2\.7\.0\.0"') 'a lower MinVersion fails and names the edit'
    $other = New-Manifest 'other.xml' (Get-Dependency 'Microsoft.WindowsAppRuntime.1.8' '8000.0.0.0')
    Check-Output ((Test-WindowsAppRuntimeDependency $other $requirement) -match 'no PackageDependency on Microsoft\.WindowsAppRuntime\.2') 'a dependency on another framework line fails'
    $none = New-Manifest 'none.xml' ''
    Check-Output ((Test-WindowsAppRuntimeDependency $none $requirement) -match 'no PackageDependency') 'a manifest without the dependency fails'
    $bad = New-Manifest 'bad.xml' (Get-Dependency 'Microsoft.WindowsAppRuntime.2' 'latest')
    Check-Output ((Test-WindowsAppRuntimeDependency $bad $requirement) -match 'unreadable MinVersion') 'an unreadable MinVersion fails'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
