<#
.SYNOPSIS
    Finds the Visual Studio installation whose MSBuild builds a companion ("builder": "msbuild").
.DESCRIPTION
    A companion's "requires" lists Visual Studio Installer component ids. A plain string must be
    installed; a nested array names alternatives, any one of which will do. The same tools can carry
    different ids in different products: the UWP XAML compiler is
    Microsoft.VisualStudio.ComponentGroup.UWP.BuildTools in Build Tools but comes with
    Microsoft.VisualStudio.Workload.Universal in the full Visual Studio (GitHub's hosted images).

    Dot-sourced by build-msix.ps1; defines functions only. Kept apart so tests/build can check it on
    Linux without Visual Studio. ASCII only, for Windows PowerShell 5.1 readers.
#>

# Every id list that satisfies $Requires, MSBuild first in each: one list per choice of alternatives,
# in the order written. A string entry is required; an array entry is "any one of these".
# Throws on an empty alternatives array or a non-string id, so a typo fails the build instead of
# matching anything.
function Get-VisualStudioRequirementSets([object[]]$Requires) {
    $sets = @(, [string[]]@("Microsoft.Component.MSBuild"))
    foreach ($entry in @($Requires)) {
        $choices = if ($entry -is [System.Array]) { @($entry) } else { @(, $entry) }
        if ($choices.Count -eq 0) { throw "companions.requires: an alternatives list is empty" }
        foreach ($choice in $choices) {
            if ($choice -isnot [string] -or [string]::IsNullOrWhiteSpace($choice)) {
                throw "companions.requires: '$choice' is not a Visual Studio component id"
            }
        }

        # Each set so far, extended by each choice in turn.
        $next = @()
        foreach ($set in $sets) {
            foreach ($choice in $choices) {
                $next += , [string[]]($set + $choice)
            }
        }
        $sets = $next
    }
    return , $sets
}

# The requirement in words, for the error when no installation matches:
# "Microsoft.Component.MSBuild, A and one of B or C".
function Format-VisualStudioRequirements([object[]]$Requires) {
    $parts = @("Microsoft.Component.MSBuild")
    foreach ($entry in @($Requires)) {
        if ($entry -is [System.Array]) {
            $parts += "one of " + (@($entry) -join " or ")
        } else {
            $parts += $entry
        }
    }
    if ($parts.Count -eq 1) { return $parts[0] }
    return ($parts[0..($parts.Count - 2)] -join ", ") + " and " + $parts[-1]
}

# The installation path of the first Visual Studio that has every requirement, trying each set of
# alternatives in order. $Query takes one id list and returns an installation path or nothing (vswhere
# in a build; a stub in tests). Throws naming every alternative when none matches.
function Find-VisualStudioInstallation([object[]]$Requires, [scriptblock]$Query) {
    foreach ($set in (Get-VisualStudioRequirementSets $Requires)) {
        $installation = & $Query $set
        if ($installation) { return $installation }
    }
    throw "No Visual Studio installation has $(Format-VisualStudioRequirements $Requires); add them with the Visual Studio Installer."
}

# Visual Studio's MSBuild.exe for the host architecture, from an installation that meets $Requires.
function Find-VisualStudioMSBuild([object[]]$Requires) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "No Visual Studio installation (vswhere.exe not found); install Visual Studio with $(Format-VisualStudioRequirements $Requires)."
    }
    $query = { param([string[]]$Ids) & $vswhere -latest -products * -requires $Ids -property installationPath }
    $installation = Find-VisualStudioInstallation $Requires $query
    $binArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "amd64" }
    $msbuild = Join-Path $installation "MSBuild\Current\Bin\$binArch\MSBuild.exe"
    if (-not (Test-Path $msbuild)) { throw "MSBuild.exe not found at $msbuild" }
    return $msbuild
}
