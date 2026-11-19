<#
.SYNOPSIS
    Builds an app's MSIX package. The one packaging script for every app on WinUI App Kit (KIT-9, DIST-1).
.DESCRIPTION
    Driven entirely by the app's windows-build.json; nothing here names an app. Normally run by the
    shared Windows build tooling (npm run build:desktop, package:desktop, install:desktop), which
    stages the app's source and runs this from the staged repo root.

    1. Publishes the app self-contained, then any companion projects, and fails when the manifest's
       Windows App Runtime MinVersion is below the runtime the app restored (runtime-dependency.ps1).
    2. Assembles the layout: published files, compiled XAML, the stamped manifest, Images, extra folders.
    3. Builds one resources.pri for every declared language, including the manifest's translated
       strings from the app's JSON catalogs, and checks it.
    4. Packs with makeappx (a bundle in Store-identity mode) and signs with signtool.
    5. Copies the package into .artifacts/packages/<version>/ under its LAYOUT-12 name
       (<package_name>-<version>-windows-<arch>[-store].<ext>) and removes older versions' packages
       from package_output. package_output keeps today's names, which the Store workflow reads.

    Signing material is only ever a parameter (SEC-11, DIST-2): there is no fallback certificate, no
    generated certificate and no written password. -NoSign builds the unsigned Store upload.
    signtool takes the password on its command line, where other processes of this user can see it
    while it runs; that is accepted for the local development certificate and the per-run CI
    certificate. Store packages are unsigned.

    ASCII only (Windows PowerShell 5.1 reads BOM-less scripts as ANSI). Requires PowerShell 7.

    windows-build.json keys read here (paths are relative to the repo root):
      project                    The app's csproj.                                          Required.
      msix_dir                   Folder holding Package.appxmanifest and Images.             Required.
      package_output             Output folder.          Default: <msix_dir>/bin/msix-output
      package_name               Output file name stem.  Default: the project's file name
      version_file               Holds <Version>.        Default: Directory.Build.props
      dev_identity               Identity Name stamped on signed, non-Store builds.
      store_identity, store_publisher
                                 Identity used with -StoreIdentity (a signed test package that
                                 updates a Store install in place; packed as a bundle).
      publish_properties         MSBuild properties for the app's publish, as {"Name": "value"}.
                                 Default: {"WindowsPackageType": "MSIX"}.
      companions                 Extra projects, each {"project", "publish_args": [], "files": "*" or
                                 ["a.exe"], "destination": "", "remove": []}, published self-contained
                                 for the same platform and copied into the layout after the app.
      manifest_strings           {"catalog_dir": "..."}: the app's JSON catalogs, from which the
                                 manifest's ms-resource:Resources/<Key> strings are written for every
                                 declared language (regional identities alias by the kit's languages.json).
      resw_dir                   Folder of <catalog>/*.resw files indexed into resources.pri.
      pri_probe_keys             Extra resource paths that must resolve, such as "Resources/NavHome/Content".
      extra_folders              [{"source", "destination"}] copied into the layout.
      sign_embedded_executables  true to sign every .exe in the layout before packing.
      timestamp_url              RFC 3161 server.        Default: http://timestamp.digicert.com
.EXAMPLE
    pwsh -File external/winui-app-kit/build/build-msix.ps1 -Platform x64 -NoSign
#>
param(
    [ValidateSet("x64", "ARM64")]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" }),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$TrustedPfxPath = "",
    [string]$TrustedPfxPassword = "",
    [switch]$NoSign,
    [switch]$StoreIdentity,
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Deliverable naming and package_output pruning, kept in their own file so tests/build can check them.
. (Join-Path $PSScriptRoot "package-output.ps1")
# The Windows App Runtime gate (manifest MinVersion against the restored SDK), kept apart for the same reason.
. (Join-Path $PSScriptRoot "runtime-dependency.ps1")

# ---------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------

# A value from windows-build.json, or the default when the key is absent. Under StrictMode a missing
# property throws, so look it up through PSObject.Properties.
function Get-Setting([object]$Config, [string]$Name, $Default = $null) {
    $property = $Config.PSObject.Properties[$Name]
    if ($property -and $null -ne $property.Value) { return $property.Value }
    return $Default
}

# A repo-relative path from windows-build.json, refused when it could leave the repo.
function Resolve-RepoPath([string]$Relative, [string]$Key) {
    if ([System.IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "windows-build.json '$Key' must be a path inside the repo: $Relative"
    }
    return Join-Path $RepoRoot $Relative
}

# Run a native tool and fail on a non-zero exit code.
function Invoke-Tool([string]$Description, [string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)" }
}

function New-CleanDirectory([string]$Path) {
    if (Test-Path $Path) { Remove-Item $Path -Recurse -Force }
    New-Item $Path -ItemType Directory -Force | Out-Null
}

# Copy a tree's files into a destination, keeping their relative paths.
function Copy-Tree([string]$Source, [string]$Destination) {
    New-Item $Destination -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $Source "*") $Destination -Recurse -Force
}

# The packaging tools (makeappx, makepri, signtool) for the host architecture: an installed Windows SDK,
# else the Microsoft.Windows.SDK.BuildTools NuGet package, restored on demand at a pinned version.
function Find-SdkBin([string]$HostArch) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $hit = Get-ChildItem $kitsRoot -Directory |
            Where-Object { $_.Name -match '^10\.' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName $HostArch } |
            Where-Object { Test-Path (Join-Path $_ "makeappx.exe") } |
            Select-Object -First 1
        if ($hit) { return $hit }
    }

    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
    $buildToolsRoot = Join-Path $nugetRoot "microsoft.windows.sdk.buildtools"
    if (Test-Path $buildToolsRoot) {
        foreach ($package in (Get-ChildItem $buildToolsRoot -Directory | Sort-Object { [version]$_.Name } -Descending)) {
            $binRoot = Join-Path $package.FullName "bin"
            if (-not (Test-Path $binRoot)) { continue }
            $hit = Get-ChildItem $binRoot -Directory |
                Sort-Object { [version]$_.Name } -Descending |
                ForEach-Object { Join-Path $_.FullName $HostArch } |
                Where-Object { Test-Path (Join-Path $_ "makeappx.exe") } |
                Select-Object -First 1
            if ($hit) { return $hit }
        }
    }

    return $null
}

# Sign one file with the trusted certificate and a timestamp.
function Invoke-Sign([string]$Path) {
    Invoke-Tool "signtool sign $Path" $signtool @("sign", "/fd", "SHA256", "/f", $TrustedPfxPath, "/p", $TrustedPfxPassword, "/tr", $timestampUrl, "/td", "SHA256", $Path)
}

# ---------------------------------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------------------------------

$configPath = Join-Path $RepoRoot "windows-build.json"
if (-not (Test-Path $configPath)) { throw "No windows-build.json in $RepoRoot" }
$config = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json

$projectPath = Resolve-RepoPath (Get-Setting $config "project") "project"
$msixDir = Resolve-RepoPath (Get-Setting $config "msix_dir") "msix_dir"
if (-not (Test-Path $projectPath)) { throw "Project not found: $projectPath" }
$manifestSrc = Join-Path $msixDir "Package.appxmanifest"
if (-not (Test-Path $manifestSrc)) { throw "No Package.appxmanifest in $msixDir" }

$outputDir = Resolve-RepoPath (Get-Setting $config "package_output" ((Get-Setting $config "msix_dir") + "/bin/msix-output")) "package_output"
$packageName = Get-Setting $config "package_name" ([System.IO.Path]::GetFileNameWithoutExtension($projectPath))
$versionFile = Resolve-RepoPath (Get-Setting $config "version_file" "Directory.Build.props") "version_file"
$timestampUrl = Get-Setting $config "timestamp_url" "http://timestamp.digicert.com"
$publishProperties = Get-Setting $config "publish_properties" ([pscustomobject]@{ WindowsPackageType = "MSIX" })

$rid = if ($Platform -eq "ARM64") { "win-arm64" } else { "win-x64" }
$msixArch = if ($Platform -eq "ARM64") { "arm64" } else { "x64" }
$workDir = Join-Path $msixDir "bin"
$layoutDir = Join-Path $workDir "msix-layout\$Platform"
$publishDir = Join-Path $workDir "publish\$Platform"

# The kit's language list: which wording catalog each regional identity reads (LOC-9).
$languageData = Get-Content (Join-Path $PSScriptRoot "languages.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogFor = @{}
foreach ($entry in $languageData.languages) { $catalogFor[$entry.identity] = $entry.catalog }

# ---------------------------------------------------------------------------------------------------
# Signing and identity checks, before any work
# ---------------------------------------------------------------------------------------------------

$signCert = $null
if ($NoSign) {
    Write-Host "Unsigned build (Store upload)" -ForegroundColor Yellow
} else {
    if ([string]::IsNullOrEmpty($TrustedPfxPath) -or -not (Test-Path $TrustedPfxPath)) {
        throw "No signing certificate. Pass -TrustedPfxPath and -TrustedPfxPassword (the build tooling does), or use -NoSign."
    }
    $signCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($TrustedPfxPath, $TrustedPfxPassword)
}

if ($StoreIdentity) {
    $storeName = Get-Setting $config "store_identity"
    $storePublisher = Get-Setting $config "store_publisher"
    if (-not $storeName -or -not $storePublisher) { throw "-StoreIdentity needs store_identity and store_publisher in windows-build.json" }
    if ($signCert -and $signCert.Subject -ne $storePublisher) {
        throw "Store-identity packages must be signed by a certificate whose subject is the Store publisher ($storePublisher), not $($signCert.Subject)."
    }
}

# ---------------------------------------------------------------------------------------------------
# Tools and version
# ---------------------------------------------------------------------------------------------------

# Native AOT companions find the C++ linker through vswhere, which lives in the Visual Studio Installer folder.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }

$sdkHostArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$buildToolsVersion = "10.0.26100.4654"
$sdkBin = Find-SdkBin $sdkHostArch
if (-not $sdkBin) {
    Write-Host "Packaging tools not found; restoring Microsoft.Windows.SDK.BuildTools $buildToolsVersion..." -ForegroundColor Yellow
    $toolsProject = Join-Path ([System.IO.Path]::GetTempPath()) "winui-app-kit-sdktools"
    New-Item $toolsProject -ItemType Directory -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="$buildToolsVersion" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $toolsProject "tools.csproj")
    Invoke-Tool "Restoring the packaging tools" "dotnet" @("restore", (Join-Path $toolsProject "tools.csproj"))
    $sdkBin = Find-SdkBin $sdkHostArch
}
if (-not $sdkBin) { throw "Could not find or restore the packaging tools (makeappx, makepri, signtool)." }
Write-Host "Packaging tools: $sdkBin" -ForegroundColor DarkCyan
$makeappx = Join-Path $sdkBin "makeappx.exe"
$makepri = Join-Path $sdkBin "makepri.exe"
$signtool = Join-Path $sdkBin "signtool.exe"

$version = ([xml](Get-Content $versionFile -Raw)).SelectSingleNode("//Version").InnerText
if (-not $version) { throw "No <Version> in $versionFile" }
$msixVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
Write-Host "Version: $msixVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------------------------------------------
# Step 1: publish the app and its companions
# ---------------------------------------------------------------------------------------------------

Write-Host "`n=== Publishing $packageName ($Platform $Configuration) ===" -ForegroundColor Cyan
New-CleanDirectory $publishDir
$publishArgs = @("publish", $projectPath, "-c", $Configuration, "-r", $rid, "-p:Platform=$Platform",
    "--self-contained", "-p:PublishSingleFile=false", "-o", $publishDir)
foreach ($property in $publishProperties.PSObject.Properties) { $publishArgs += "-p:$($property.Name)=$($property.Value)" }
Invoke-Tool "dotnet publish $packageName" "dotnet" $publishArgs

# Refuse a manifest whose Windows App Runtime MinVersion is below the runtime this build restored, so
# a kit update that raises the SDK cannot ship a package that installs against the older framework.
$assetsPath = (& dotnet msbuild $projectPath -getProperty:ProjectAssetsFile "-p:Platform=$Platform" | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $assetsPath) { throw "Could not find the restore output (ProjectAssetsFile) of $projectPath" }
$runtime = Get-WindowsAppRuntimeRequirement $assetsPath
$runtimeError = Test-WindowsAppRuntimeDependency $manifestSrc $runtime
if ($runtimeError) { throw $runtimeError }
Write-Host "  Windows App Runtime: $($runtime.Name) $($runtime.Version), manifest MinVersion is at least that"

Write-Host "`n=== Assembling the layout ===" -ForegroundColor Cyan
New-CleanDirectory $layoutDir
Copy-Tree $publishDir $layoutDir

# dotnet publish leaves out compiled XAML (.xbf): copy it from the RID build folder, keeping paths.
$projectBin = Join-Path (Split-Path $projectPath -Parent) "bin\$Platform\$Configuration"
$ridBuildDirs = @(Get-ChildItem $projectBin -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName $rid } | Where-Object { Test-Path $_ })
if ($ridBuildDirs.Count -ne 1) { throw "Expected one build folder for $rid under $projectBin, found $($ridBuildDirs.Count)." }
$ridBuildDir = $ridBuildDirs[0]
$xbfFiles = @(Get-ChildItem $ridBuildDir -Filter "*.xbf" -Recurse | Where-Object { $_.FullName -notlike "*\publish\*" })
if ($xbfFiles.Count -eq 0) { throw "No compiled XAML (.xbf) under $ridBuildDir; the app's XAML did not build." }
foreach ($xbf in $xbfFiles) {
    $destination = Join-Path $layoutDir $xbf.FullName.Substring($ridBuildDir.Length + 1)
    New-Item (Split-Path $destination -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item $xbf.FullName $destination -Force
}
Write-Host "  Compiled XAML: $($xbfFiles.Count) file(s)"

foreach ($companion in @(Get-Setting $config "companions" @())) {
    $companionProject = Resolve-RepoPath $companion.project "companions.project"
    $companionName = [System.IO.Path]::GetFileNameWithoutExtension($companionProject)
    $companionOut = Join-Path $workDir "companions\$companionName\$Platform"
    Write-Host "`n=== Publishing companion $companionName ===" -ForegroundColor Cyan
    New-CleanDirectory $companionOut
    $companionArgs = @("publish", $companionProject, "-c", $Configuration, "-r", $rid, "-p:Platform=$Platform",
        "--self-contained", "-o", $companionOut) + @(Get-Setting $companion "publish_args" @())
    Invoke-Tool "dotnet publish $companionName" "dotnet" $companionArgs

    $destination = Join-Path $layoutDir (Get-Setting $companion "destination" "")
    New-Item $destination -ItemType Directory -Force | Out-Null
    $files = Get-Setting $companion "files" "*"
    if ($files -is [string] -and $files -eq "*") {
        Copy-Tree $companionOut $destination
    } else {
        foreach ($file in @($files)) {
            $source = Join-Path $companionOut $file
            if (-not (Test-Path $source)) { throw "Companion $companionName produced no $file" }
            Copy-Item $source (Join-Path $destination $file) -Force
        }
    }
    foreach ($file in @(Get-Setting $companion "remove" @())) {
        Remove-Item (Join-Path $destination $file) -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------------
# Step 2: manifest, images and extra folders
# ---------------------------------------------------------------------------------------------------

# Placeholders first, then identity edits through the XML DOM (a string replace can hit other Name= attributes).
$manifestText = (Get-Content $manifestSrc -Raw -Encoding UTF8) -replace 'ARCH_PLACEHOLDER', $msixArch -replace 'VERSION_PLACEHOLDER', $msixVersion
$manifest = New-Object System.Xml.XmlDocument
$manifest.PreserveWhitespace = $true
$manifest.LoadXml($manifestText)
$identity = $manifest.DocumentElement.SelectSingleNode("*[local-name()='Identity']")
if (-not $identity) { throw "Package.appxmanifest has no Identity element" }

if ($StoreIdentity) {
    $identity.SetAttribute("Name", $storeName)
    $identity.SetAttribute("Publisher", $storePublisher)
} elseif ($signCert) {
    $devIdentity = Get-Setting $config "dev_identity"
    if ($devIdentity) { $identity.SetAttribute("Name", $devIdentity) }
    $identity.SetAttribute("Publisher", $signCert.Subject)
}
Write-Host "  Identity: $($identity.GetAttribute('Name')) / $($identity.GetAttribute('Publisher'))"

$manifestPath = Join-Path $layoutDir "AppxManifest.xml"
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $writerSettings)
try { $manifest.Save($writer) } finally { $writer.Close() }

$imagesDir = Join-Path $msixDir "Images"
if (-not (Test-Path $imagesDir)) { throw "No Images folder in $msixDir" }
Copy-Tree $imagesDir (Join-Path $layoutDir "Images")

foreach ($folder in @(Get-Setting $config "extra_folders" @())) {
    Copy-Tree (Resolve-RepoPath $folder.source "extra_folders.source") (Join-Path $layoutDir $folder.destination)
}

# ---------------------------------------------------------------------------------------------------
# Step 3: resources.pri
# ---------------------------------------------------------------------------------------------------

Write-Host "`n=== Generating resources.pri ===" -ForegroundColor Cyan
$languages = @($manifest.DocumentElement.SelectNodes("*[local-name()='Resources']/*[local-name()='Resource']") | ForEach-Object { $_.GetAttribute("Language") })
if ($languages.Count -eq 0) { throw "Package.appxmanifest declares no <Resource Language>" }
$identityName = $identity.GetAttribute("Name")
$reswRoot = Join-Path $layoutDir "Strings"

# Manifest strings (ms-resource:Resources/<Key>) from the app's JSON catalogs, one RESW per language.
$manifestKeys = @([regex]::Matches($manifest.OuterXml, 'ms-resource:Resources/(\w+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$manifestStrings = Get-Setting $config "manifest_strings"
if ($manifestStrings) {
    $catalogDir = Resolve-RepoPath $manifestStrings.catalog_dir "manifest_strings.catalog_dir"
    foreach ($language in $languages) {
        $catalogName = if ($catalogFor.ContainsKey($language)) { $catalogFor[$language] } else { $language }
        $catalogPath = Join-Path $catalogDir "$catalogName.json"
        if (-not (Test-Path $catalogPath)) { throw "No catalog $catalogName.json for manifest language $language" }
        $catalog = [System.IO.File]::ReadAllText($catalogPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

        $reswDir = Join-Path $reswRoot $language
        New-Item $reswDir -ItemType Directory -Force | Out-Null
        $reswSettings = New-Object System.Xml.XmlWriterSettings
        $reswSettings.Indent = $true
        $reswSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $resw = [System.Xml.XmlWriter]::Create((Join-Path $reswDir "Resources.resw"), $reswSettings)
        try {
            $resw.WriteStartElement("root")
            foreach ($header in @(@("resmimetype", "text/microsoft-resx"), @("version", "2.0"))) {
                $resw.WriteStartElement("resheader")
                $resw.WriteAttributeString("name", $header[0])
                $resw.WriteElementString("value", $header[1])
                $resw.WriteEndElement()
            }
            foreach ($key in $manifestKeys) {
                $property = $catalog.PSObject.Properties[$key]
                $value = if ($property) { [string]$property.Value } else { "" }
                if ([string]::IsNullOrWhiteSpace($value)) { throw "Catalog $catalogName.json has no '$key' for the manifest" }
                $resw.WriteStartElement("data")
                $resw.WriteAttributeString("name", $key)
                $resw.WriteAttributeString("xml", "space", $null, "preserve")
                $resw.WriteElementString("value", $value)
                $resw.WriteEndElement()
            }
            $resw.WriteEndElement()
        } finally {
            $resw.Close()
        }
    }
    Write-Host "  Manifest strings ($($manifestKeys -join ', ')) for: $($languages -join ', ')"
} elseif ($manifestKeys.Count -gt 0 -and -not (Get-Setting $config "resw_dir")) {
    throw "The manifest uses ms-resource strings but windows-build.json sets neither manifest_strings nor resw_dir."
}

# The app's own RESW files, per catalog, indexed for every declared language that reads that catalog.
$reswSource = Get-Setting $config "resw_dir"
if ($reswSource) {
    $reswSourceDir = Resolve-RepoPath $reswSource "resw_dir"
    foreach ($language in $languages) {
        $catalogName = if ($catalogFor.ContainsKey($language)) { $catalogFor[$language] } else { $language }
        $source = Join-Path $reswSourceDir $catalogName
        if (-not (Test-Path $source)) { throw "No $catalogName folder in $reswSourceDir for language $language" }
        $destination = Join-Path $reswRoot $language
        foreach ($file in (Get-ChildItem $source -Filter "*.resw")) {
            $target = Join-Path $destination $file.Name
            if (Test-Path $target) { throw "$($file.Name) for $language exists in both resw_dir and the manifest strings; merge them in one place." }
            New-Item $destination -ItemType Directory -Force | Out-Null
            Copy-Item $file.FullName $target
        }
    }
}

Get-ChildItem $layoutDir -Filter "resources*.pri" | Remove-Item -Force
$priConfigPath = Join-Path $layoutDir "priconfig.xml"
Invoke-Tool "makepri createconfig" $makepri @("createconfig", "/cf", $priConfigPath, "/dq", $languageData.default, "/o")
# The default config splits languages and scales into resources.<qualifier>.pri files, which only take
# effect as resource packs in a bundle. This is a single package: keep every candidate in resources.pri.
$priConfig = [xml](Get-Content $priConfigPath -Raw)
foreach ($node in @($priConfig.SelectNodes("//packaging"))) { [void]$node.ParentNode.RemoveChild($node) }
$priConfig.Save($priConfigPath)
$priPath = Join-Path $layoutDir "resources.pri"
Invoke-Tool "makepri new" $makepri @("new", "/pr", $layoutDir, "/cf", $priConfigPath, "/mn", $manifestPath, "/of", $priPath, "/o")
Remove-Item $priConfigPath -Force
if (Test-Path $reswRoot) { Remove-Item $reswRoot -Recurse -Force }
$splitFiles = @(Get-ChildItem $layoutDir -Filter "resources.*.pri")
if ($splitFiles.Count -gt 0) { throw "makepri split resources into $($splitFiles.Name -join ', '); a single package needs one resources.pri." }

# Check the primary map resolves every manifest string in every language, and every probe key, so a
# broken index fails here rather than in Store certification.
$dumpPath = Join-Path $workDir "resources-dump-$Platform.xml"
Invoke-Tool "makepri dump" $makepri @("dump", "/if", $priPath, "/of", $dumpPath, "/dt", "detailed", "/o")
$dump = [xml](Get-Content $dumpPath -Raw -Encoding UTF8)
$primaryMap = @($dump.SelectNodes("//ResourceMap[@primary='true']") | Where-Object { $_.name -ieq $identityName })
if ($primaryMap.Count -ne 1) { throw "resources.pri has no primary map named '$identityName'" }

function Assert-Resource([string]$Path, [string[]]$RequiredLanguages) {
    $uri = "ms-resource://$identityName/$Path"
    $named = @($primaryMap[0].SelectNodes(".//NamedResource") | Where-Object { $_.uri -ieq $uri })
    if ($named.Count -ne 1) { throw "resources.pri can't resolve $uri" }
    $found = @($named[0].SelectNodes("Candidate/QualifierSet/Qualifier[@name='Language']") | ForEach-Object { $_.value })
    foreach ($language in $RequiredLanguages) {
        if (-not ($found | Where-Object { $_ -ieq $language })) { throw "resources.pri has no $language candidate for $uri (found: $($found -join ', '))" }
    }
}
foreach ($key in $manifestKeys) { Assert-Resource "Resources/$key" $languages }
foreach ($probe in @(Get-Setting $config "pri_probe_keys" @())) { Assert-Resource $probe $languages }
Write-Host "  resources.pri: map $identityName, $($manifestKeys.Count) manifest string(s) x $($languages.Count) languages"

# ---------------------------------------------------------------------------------------------------
# Step 4: pack and sign
# ---------------------------------------------------------------------------------------------------

if ($signCert -and (Get-Setting $config "sign_embedded_executables" $false)) {
    Write-Host "`n=== Signing embedded executables ===" -ForegroundColor Cyan
    foreach ($exe in (Get-ChildItem $layoutDir -Filter "*.exe" -Recurse)) { Invoke-Sign $exe.FullName }
}

Write-Host "`n=== Packing ===" -ForegroundColor Cyan
New-Item $outputDir -ItemType Directory -Force | Out-Null
$msixName = "$packageName-$version-$Platform.msix"
if ($StoreIdentity) {
    # A Store install is updated in place only by a bundle, so wrap the package in one.
    $bundleInput = Join-Path $workDir "bundle-input\$Platform"
    New-CleanDirectory $bundleInput
    Invoke-Tool "makeappx pack" $makeappx @("pack", "/d", $layoutDir, "/p", (Join-Path $bundleInput $msixName), "/o")
    $packagePath = Join-Path $outputDir "$packageName-$version-$Platform.msixbundle"
    Invoke-Tool "makeappx bundle" $makeappx @("bundle", "/d", $bundleInput, "/p", $packagePath, "/bv", $msixVersion, "/o")
} else {
    $packagePath = Join-Path $outputDir $msixName
    Invoke-Tool "makeappx pack" $makeappx @("pack", "/d", $layoutDir, "/p", $packagePath, "/o")
}

if ($signCert) {
    Write-Host "`n=== Signing ===" -ForegroundColor Cyan
    Invoke-Sign $packagePath
}

# The finished package also goes to .artifacts/packages/<version>/ under its LAYOUT-12 name, and older
# versions' packages leave package_output, whose newest pair is then the one to upload by hand.
$productWarning = Test-DeliverableProduct $packageName
if ($productWarning) { Write-Warning $productWarning }
$deliverable = Copy-Deliverable $packagePath $RepoRoot $packageName $version $Platform ([bool]$StoreIdentity)
foreach ($old in (Remove-OlderPackages $outputDir $packageName $version)) {
    Write-Host "  Removed older package: $old" -ForegroundColor DarkGray
}

$size = [math]::Round((Get-Item $packagePath).Length / 1MB, 1)
Write-Host "`n=== SUCCESS ===" -ForegroundColor Green
Write-Host "  Package:  $packagePath ($size MB)"
Write-Host "  Deliverable: $deliverable"
Write-Host "  Version:  $msixVersion  Platform: $Platform  Signed: $([bool]$signCert)"
