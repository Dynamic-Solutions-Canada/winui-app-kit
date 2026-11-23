<#
.SYNOPSIS
Builds the one ZIP a Store draft takes: the package upload and every listing image, by registered name.
.DESCRIPTION
Partner Center gives a draft a single upload location, and the ZIP there must hold every file the draft
registers as PendingUpload under exactly that name. The Store CLI's own upload holds only the package,
so a run that also sends images replaces it with this archive: the .msixupload at the root, under its own
file name (the one the draft registered), and each image at its FileName from
resolve-store-screenshots.ps1.

Returns the archive's path.
#>
param(
    [Parameter(Mandatory)][string]$UploadPath,
    # Each image as a hashtable with Source (a file path) and FileName (its name in the archive).
    [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Images,
    [Parameter(Mandatory)][string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $UploadPath)) { throw "Missing Store upload: $UploadPath" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $ArchivePath) { Remove-Item -LiteralPath $ArchivePath -Force }
$archive = [IO.Compression.ZipFile]::Open($ArchivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $packageName = [IO.Path]::GetFileName($UploadPath)
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $UploadPath, $packageName) | Out-Null
    $null = $names.Add($packageName)
    foreach ($image in $Images) {
        if (-not $names.Add($image.FileName)) { throw "Two files in the Store upload are named $($image.FileName)." }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $image.Source, $image.FileName) | Out-Null
    }
} finally {
    $archive.Dispose()
}
return $ArchivePath
