$ErrorActionPreference = 'Stop'

# Checks the WinUIAppKitPublishWithoutFrameworkMLNatives target in build/WinUIAppKit.targets by asking
# MSBuild for the publish file list of Xaml/CompileCheck, a framework-dependent MSIX app like every app's,
# with build-msix.ps1's publish settings. Nothing is compiled or packed. Needs the .NET SDK and NuGet access.

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repoRoot 'Xaml/CompileCheck/WinUIAppKit.XamlCompileCheck.csproj'
$mlPackage = 'Microsoft.Windows.AI.MachineLearning'
$natives = @('DirectML.dll', 'onnxruntime.dll', 'Microsoft.Windows.AI.MachineLearning.dll')

function Check-Output($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}

# The file names of the ML package's files in the publish list, for the given extra MSBuild properties.
function Get-PublishedMLFiles([string[]]$Properties) {
    $arguments = @('msbuild', $project, '-nologo', '-p:Configuration=Release', '-p:Platform=x64',
        '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true', '-p:PublishSingleFile=false',
        '-p:WindowsPackageType=MSIX', '-t:ComputeResolvedFilesToPublishList', '-getItem:ResolvedFileToPublish') + $Properties
    $output = & dotnet @arguments | Out-String
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild failed:`n$output" }
    $items = ($output | ConvertFrom-Json).Items.ResolvedFileToPublish
    return @($items | Where-Object { $_.NuGetPackageId -eq $mlPackage } | ForEach-Object { Split-Path $_.Identity -Leaf })
}

& dotnet restore $project -nologo -p:Platform=x64 -p:RuntimeIdentifier=win-x64 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet restore of $project failed" }

# The kit target removes the natives the Windows App Runtime framework supplies.
$published = Get-PublishedMLFiles @()
foreach ($native in $natives) {
    Check-Output ($published -notcontains $native) "a framework-dependent publish leaves out $native"
}
Check-Output ($published -contains 'Microsoft.Windows.AI.MachineLearning.Projection.dll') 'the managed ML projection is still published, so the ML package is in the graph'

# Without it the SDK still publishes them. When this fails, Microsoft fixed the gap: remove the target,
# its README gotcha and this test.
$unfixed = Get-PublishedMLFiles @('-p:_WinUIAppKitKeepMLPublishNatives=true')
foreach ($native in $natives) {
    Check-Output ($unfixed -contains $native) "without the kit target the SDK still publishes $native (if not, the target can go)"
}
