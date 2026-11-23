$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../build/visual-studio.ps1'
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

# A stand-in for vswhere: the installation that has every id asked for, else nothing. Each query is
# added to $queries (a list, since the closure can't assign the test's own variables).
$queries = [System.Collections.Generic.List[string]]::new()
function New-Query([hashtable]$Installations) {
    $log = $queries
    return {
        param([string[]]$Ids)
        $log.Add($Ids -join ',')
        foreach ($path in $Installations.Keys) {
            $has = $Installations[$path]
            if (-not ($Ids | Where-Object { $has -notcontains $_ })) { return $path }
        }
    }.GetNewClosure()
}

$uwp = 'Microsoft.VisualStudio.ComponentGroup.UWP.BuildTools'
$universal = 'Microsoft.VisualStudio.Workload.Universal'
$msbuild = 'Microsoft.Component.MSBuild'
$buildTools = @{ 'C:\BuildTools' = @($msbuild, $uwp) }
$enterprise = @{ 'C:\Enterprise' = @($msbuild, $universal) }

# --- Plain ids: every one must be installed (the shape before alternatives) ---

$sets = Get-VisualStudioRequirementSets @($uwp)
Check-Output ($sets.Count -eq 1 -and ($sets[0] -join ',') -ceq "$msbuild,$uwp") 'a plain id is required, after MSBuild'
Check-Output ((Find-VisualStudioInstallation @($uwp) (New-Query $buildTools)) -ceq 'C:\BuildTools') 'a plain id finds the installation that has it'
$message = Get-ThrownMessage { Find-VisualStudioInstallation @($uwp) (New-Query $enterprise) }
Check-Output ($message -match [regex]::Escape("has $msbuild and $uwp;")) 'a plain id that no installation has is named'
Check-Output ((Find-VisualStudioInstallation @() (New-Query $enterprise)) -ceq 'C:\Enterprise') 'no requires needs only MSBuild'

# --- A nested array: any one of the alternatives will do ---

$either = @(, @($uwp, $universal))
$sets = Get-VisualStudioRequirementSets $either
Check-Output ($sets.Count -eq 2 -and ($sets[0] -join ',') -ceq "$msbuild,$uwp" -and ($sets[1] -join ',') -ceq "$msbuild,$universal") 'alternatives give one set each, in the order written'
Check-Output ((Find-VisualStudioInstallation $either (New-Query $buildTools)) -ceq 'C:\BuildTools') 'the first alternative finds Build Tools'
$queries.Clear()
Check-Output ((Find-VisualStudioInstallation $either (New-Query $enterprise)) -ceq 'C:\Enterprise') 'the second alternative finds the full Visual Studio'
Check-Output ($queries.Count -eq 2) 'vswhere is asked once per alternative until one matches'
$message = Get-ThrownMessage { Find-VisualStudioInstallation $either (New-Query @{ 'C:\Other' = @($msbuild) }) }
Check-Output ($message -match [regex]::Escape("has $msbuild and one of $uwp or $universal;")) 'when no alternative matches, every one is named'

# --- Plain and nested together ---

$mixed = @('Extra.Component', @($uwp, $universal))
$sets = Get-VisualStudioRequirementSets $mixed
Check-Output ($sets.Count -eq 2 -and ($sets[1] -join ',') -ceq "$msbuild,Extra.Component,$universal") 'a plain id stays in every alternative set'
Check-Output ((Format-VisualStudioRequirements $mixed) -ceq "$msbuild, Extra.Component and one of $uwp or $universal") 'the requirement reads as a sentence'

# --- The shape as windows-build.json gives it ---

$config = '{ "requires": [["' + $uwp + '", "' + $universal + '"]] }' | ConvertFrom-Json
Check-Output ((Find-VisualStudioInstallation @($config.requires) (New-Query $enterprise)) -ceq 'C:\Enterprise') 'a nested array read from JSON is alternatives'
$config = '{ "requires": ["' + $uwp + '"] }' | ConvertFrom-Json
Check-Output ((Find-VisualStudioInstallation @($config.requires) (New-Query $buildTools)) -ceq 'C:\BuildTools') 'a plain list read from JSON still works'

# --- Mistakes fail instead of matching anything ---

Check-Output ((Get-ThrownMessage { Get-VisualStudioRequirementSets @(, @()) }) -match 'alternatives list is empty') 'an empty alternatives list fails'
Check-Output ((Get-ThrownMessage { Get-VisualStudioRequirementSets @(42) }) -match 'not a Visual Studio component id') 'a non-string id fails'
