$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../build/package-output.ps1'
foreach ($path in @($scriptPath, (Join-Path $PSScriptRoot '../../build/build-msix.ps1'))) {
    if ([IO.File]::ReadAllBytes((Resolve-Path $path)) | Where-Object { $_ -gt 127 }) { throw "$path is not ASCII" }
}
. $scriptPath

function Check-Output($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

# --- Deliverable names (LAYOUT-12) ---

Check-Output ((Get-DeliverableName 'ImmichDrive' '0.2.0' 'x64' $false) -ceq 'ImmichDrive-0.2.0-windows-x64.msix') 'a release package is <Product>-<version>-windows-<arch>.msix'
Check-Output ((Get-DeliverableName 'ImmichDrive' '0.2.0' 'ARM64' $false) -ceq 'ImmichDrive-0.2.0-windows-arm64.msix') 'the arch is written lowercase'
Check-Output ((Get-DeliverableName 'ImmichDrive' '0.2.0.7' 'x64' $false) -ceq 'ImmichDrive-0.2.0.7-windows-x64.msix') 'a four-part dev version is kept as written'
Check-Output ((Get-DeliverableName 'ImmichDrive' '0.2.0' 'x64' $true) -ceq 'ImmichDrive-0.2.0-windows-x64-store.msixbundle') 'a Store-identity build is a -store bundle'
Check-Output ($null -eq (Test-DeliverableProduct 'ImmichDrive')) 'a PascalCase product is accepted'
Check-Output ((Test-DeliverableProduct 'immich-drive') -match 'not PascalCase') 'a product with a dash is reported'
Check-Output ((Test-DeliverableProduct 'Immich.Drive') -match 'not PascalCase') 'a product with a dot is reported'

# --- Copy into .artifacts/packages/<version>/ ---

$root = Join-Path ([IO.Path]::GetTempPath()) ("package-output-" + [guid]::NewGuid().ToString('N'))
try {
    $output = Join-Path $root 'AppMSIX/bin/msix-output'
    New-Item $output -ItemType Directory -Force | Out-Null
    $package = Join-Path $output 'ImmichDrive-0.2.0-x64.msix'
    Set-Content -LiteralPath $package -Value 'first'

    $deliverable = Copy-Deliverable $package $root 'ImmichDrive' '0.2.0' 'x64' $false
    Check-Output ($deliverable -eq (Join-Path $root '.artifacts/packages/0.2.0/ImmichDrive-0.2.0-windows-x64.msix')) 'the deliverable goes to .artifacts/packages/<version>/'
    Check-Output ((Test-Path -LiteralPath $package) -and (Get-Content -LiteralPath $deliverable) -eq 'first') 'it is a copy; package_output keeps its own file'

    Set-Content -LiteralPath $package -Value 'second'
    [void](Copy-Deliverable $package $root 'ImmichDrive' '0.2.0' 'x64' $false)
    Check-Output ((Get-Content -LiteralPath $deliverable) -eq 'second') 'a later build of the same version replaces the earlier deliverable'

    # --- Pruning package_output (issue 3) ---

    foreach ($name in @('ImmichDrive-0.1.9-x64.msix', 'ImmichDrive-0.1.9-ARM64.msix', 'ImmichDrive-0.1.9.4-x64.msixbundle',
                        'ImmichDrive-0.2.0-ARM64.msix', 'OtherApp-0.1.0-x64.msix', 'ImmichDrive-notes.txt')) {
        Set-Content -LiteralPath (Join-Path $output $name) -Value 'x'
    }
    $removed = @(Remove-OlderPackages $output 'ImmichDrive' '0.2.0')
    $left = @(Get-ChildItem -LiteralPath $output -File | ForEach-Object Name | Sort-Object)
    Check-Output ($removed.Count -eq 3) 'every older version of this app is removed, msix and bundle'
    Check-Output (($left -join ',') -eq 'ImmichDrive-0.2.0-ARM64.msix,ImmichDrive-0.2.0-x64.msix,ImmichDrive-notes.txt,OtherApp-0.1.0-x64.msix') 'both platforms of the current version and every other file are kept'
    Check-Output (@(Remove-OlderPackages (Join-Path $root 'missing') 'ImmichDrive' '0.2.0').Count -eq 0) 'a missing package_output removes nothing'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
