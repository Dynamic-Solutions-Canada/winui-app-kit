<#
.SYNOPSIS
    The packaging gate for the Windows App SDK runtime: build-msix.ps1 refuses to pack an app whose
    Package.appxmanifest depends on an older Windows App Runtime framework than the SDK it was built with.
.DESCRIPTION
    A kit minor version can raise the Windows App SDK within its major line (for example 2.6.5 to 2.7.0).
    The app's code then needs the newer framework, but Windows installs the package against whatever
    MinVersion its manifest declares, so a manifest left at the old floor installs and then fails on a
    PC that has only the older framework. This gate turns that into a build error with the fix in it.

    Both values come from what the app actually restored: the Microsoft.WindowsAppSDK.Runtime package's
    WindowsAppSDK-VersionInfo.json names the framework (its package family) and the runtime version.
    The gate fails, never skips, when it cannot decide.

    Dot-sourced by build-msix.ps1; defines functions only. Kept apart so tests/build can check it on
    Linux without packing anything. ASCII only, for Windows PowerShell 5.1 readers.
#>

# The runtime package the app restored, as recorded in its project.assets.json:
# @{ Name = "Microsoft.WindowsAppRuntime.2"; Version = [version]"2.7.0.0"; Source = <VersionInfo path> }.
# Throws when the assets file, the runtime package or its version file is missing or unreadable.
function Get-WindowsAppRuntimeRequirement([string]$AssetsPath) {
    if (-not (Test-Path -LiteralPath $AssetsPath)) {
        throw "Windows App Runtime check: no restore output at $AssetsPath; restore the app first"
    }
    $assets = Get-Content -LiteralPath $AssetsPath -Raw -Encoding UTF8 | ConvertFrom-Json

    # libraries holds one "<id>/<version>" entry per package in the graph; ids are case-insensitive.
    $library = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.WindowsAppSDK.Runtime/*' })
    if ($library.Count -ne 1) {
        throw "Windows App Runtime check: expected one Microsoft.WindowsAppSDK.Runtime package in $AssetsPath, found $($library.Count)"
    }

    # The package sits under one of the restore's package folders, at the library's relative path.
    $source = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path (Join-Path $folder $library[0].Value.path) 'WindowsAppSDK-VersionInfo.json'
        if (Test-Path -LiteralPath $candidate) { $source = $candidate; break }
    }
    if (-not $source) {
        throw "Windows App Runtime check: WindowsAppSDK-VersionInfo.json not found for $($library[0].Name) in the restore's package folders"
    }

    # The file starts with a byte order mark, which the UTF8 reader drops.
    $info = Get-Content -LiteralPath $source -Raw -Encoding UTF8 | ConvertFrom-Json
    $family = [string]$info.Runtime.Packages.Framework.PackageFamilyName
    $versionText = [string]$info.Runtime.Version.DotQuadNumber
    $version = $null
    if (-not ($family -match '^(.+)_[a-z0-9]+$') -or -not [version]::TryParse($versionText, [ref]$version)) {
        throw "Windows App Runtime check: $source has no readable framework family and runtime version"
    }
    return @{ Name = $Matches[1]; Version = $version; Source = $source }
}

# Compare the manifest's PackageDependency on the framework with the requirement. Returns $null when the
# manifest's MinVersion is at least the runtime version, otherwise the error message, which names the
# edit to make. A missing dependency or an unreadable MinVersion is an error too.
function Test-WindowsAppRuntimeDependency([string]$ManifestPath, [hashtable]$Requirement) {
    $manifest = [xml](Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8)
    $dependencies = @($manifest.SelectNodes("//*[local-name()='PackageDependency']") |
        Where-Object { $_.GetAttribute('Name') -eq $Requirement.Name })
    $wanted = "$($Requirement.Version)"

    if ($dependencies.Count -eq 0) {
        return "Package.appxmanifest has no PackageDependency on $($Requirement.Name), the Windows App Runtime framework this build needs. Add one with MinVersion=`"$wanted`" (kit README, Versioning)."
    }

    $declared = $null
    $minVersion = $dependencies[0].GetAttribute('MinVersion')
    if (-not [version]::TryParse($minVersion, [ref]$declared)) {
        return "Package.appxmanifest's PackageDependency on $($Requirement.Name) has an unreadable MinVersion '$minVersion'; set it to `"$wanted`"."
    }
    if ($declared -lt $Requirement.Version) {
        return "Package.appxmanifest depends on $($Requirement.Name) MinVersion $minVersion, but this build uses Windows App SDK runtime $wanted. Set MinVersion=`"$wanted`" so the package cannot install on a PC with only the older framework (kit README, Versioning)."
    }
    return $null
}
