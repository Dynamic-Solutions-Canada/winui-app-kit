<#
.SYNOPSIS
Runs the app's pre_build commands for the Store workflow, failing on any failed command.
.DESCRIPTION
The commands run as one PowerShell script. A failing cmdlet stops it, and so does any native command
that exits non-zero, even when a later command succeeds: without $PSNativeCommandUseErrorActionPreference
a failed `npm run ...` followed by a successful command would pass unnoticed.
#>
param([Parameter(Mandatory)][string]$Commands)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

try {
    Invoke-Expression $Commands
} catch {
    throw "The pre_build step failed: $($_.Exception.Message)"
}
