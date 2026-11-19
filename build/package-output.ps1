<#
.SYNOPSIS
    What build-msix.ps1 does with a finished package: copy it into .artifacts/packages/<version>/ under its
    deliverable name (LAYOUT-12), and remove older versions' packages from package_output (issue 3).
.DESCRIPTION
    Dot-sourced by build-msix.ps1; defines functions only. Kept apart so tests/build can check the naming
    and pruning on Linux without packing anything. ASCII only, for Windows PowerShell 5.1 readers.
#>

# The LAYOUT-12 file name for a package: <Product>-<version>-windows-<arch>[-store].<ext>.
#   Product   the package_name stem; LAYOUT-12 wants PascalCase with no dots or spaces, which
#             Test-DeliverableProduct reports on, but the name is used as given.
#   Version   as in the version file: three parts for a release, four for a dev build.
#   Platform  x64 or ARM64, written lowercase.
#   Store     a -StoreIdentity build, which is a bundle: "-store" and .msixbundle.
function Get-DeliverableName([string]$Product, [string]$Version, [string]$Platform, [bool]$Store) {
    $arch = $Platform.ToLowerInvariant()
    if ($Store) {
        return "$Product-$Version-windows-$arch-store.msixbundle"
    }
    return "$Product-$Version-windows-$arch.msix"
}

# A warning (never a failure, which would break a clean app build) when the product isn't LAYOUT-12
# PascalCase. Returns the message, or $null when the name is fine.
function Test-DeliverableProduct([string]$Product) {
    if ($Product -cmatch '^[A-Z][A-Za-z0-9]*$') { return $null }
    return "package_name '$Product' is not PascalCase without dots or spaces (LAYOUT-12); its deliverable keeps the name as given"
}

# Copy the finished package to <RepoRoot>/.artifacts/packages/<version>/<deliverable name>, replacing an
# earlier build of the same version, platform and kind. package_output keeps its own copy and name,
# because the Store workflow and the build tooling read those. Returns the deliverable's path.
function Copy-Deliverable([string]$PackagePath, [string]$RepoRoot, [string]$Product, [string]$Version, [string]$Platform, [bool]$Store) {
    $folder = Join-Path (Join-Path (Join-Path $RepoRoot ".artifacts") "packages") $Version
    New-Item $folder -ItemType Directory -Force | Out-Null
    $destination = Join-Path $folder (Get-DeliverableName $Product $Version $Platform $Store)
    Copy-Item -LiteralPath $PackagePath -Destination $destination -Force
    return $destination
}

# Remove this app's packages of other versions from package_output, so a folder used for manual uploads
# holds only the current version (both platforms, built one after the other). Only files this script
# names are touched: <package_name>-<version>-<x64|ARM64>.<msix|msixbundle>. Every removed version's
# deliverable stays in .artifacts/packages/. Returns the names removed.
function Remove-OlderPackages([string]$OutputDir, [string]$PackageName, [string]$Version) {
    $removed = @()
    if (-not (Test-Path -LiteralPath $OutputDir)) { return $removed }

    $pattern = '^' + [regex]::Escape($PackageName) + '-(\d+(\.\d+){2,3})-(x64|ARM64)\.(msix|msixbundle)$'
    foreach ($file in (Get-ChildItem -LiteralPath $OutputDir -File)) {
        $match = [regex]::Match($file.Name, $pattern)
        if (-not $match.Success) { continue }
        if ($match.Groups[1].Value -eq $Version) { continue }

        Remove-Item -LiteralPath $file.FullName -Force
        $removed += $file.Name
    }
    return $removed
}
