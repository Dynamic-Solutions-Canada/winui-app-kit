<#
.SYNOPSIS
Works out the Store "What's new" text for each listing locale, from the release notes for one version.
.DESCRIPTION
Each identity in the kit's build/languages.json reads the notes of its wording catalog: the English
catalog's are <version>.md (the NotesPath file), every other catalog's are <version>.<catalog>.md beside
it (REL-6). This is the same identity-to-catalog mapping check-listing.py uses, so en-CA takes the en-US
notes and fr-CA the fr-FR notes. A catalog with no notes file of its own falls back to the English notes,
and so does any listing locale outside the family's list.

When the app's windows-build.json sets "listing": {"catalog_dir": null}, its listing is English only on
purpose (D-111): the English notes go to every locale and no other notes file is read.

Every text that will be used must be 1 to 1500 characters (the Store's limit); anything else stops the
run, and nothing is truncated.

Returns a hashtable: Default is the English notes; Locales maps each lower-case identity to its text;
EnglishOnly says whether the listing is English only. Use Default for a locale missing from Locales.
#>
param(
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$WindowsBuildJson,
    [string]$LanguagesPath = (Join-Path $PSScriptRoot '../../build/languages.json')
)

$ErrorActionPreference = 'Stop'

# Read one notes file, trimmed, and stop unless it fits the Store.
function Read-Notes([string]$Path) {
    $text = (Get-Content -LiteralPath $Path -Raw -Encoding utf8).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $text.Length -gt 1500) {
        throw "Release notes must contain between 1 and 1500 characters: $Path has $($text.Length)."
    }
    return $text
}

if (-not (Test-Path -LiteralPath $NotesPath)) { throw "Missing release notes: $NotesPath" }
$english = Read-Notes $NotesPath

# An explicit null listing.catalog_dir means an English-only listing (D-111); an absent key does not.
$config = Get-Content -LiteralPath $WindowsBuildJson -Raw | ConvertFrom-Json
$listing = $config.PSObject.Properties['listing']
$englishOnly = $false
if ($listing -and $listing.Value -and $listing.Value.PSObject.Properties['catalog_dir']) {
    $englishOnly = $null -eq $listing.Value.catalog_dir
}

# <version>.md names the version; its siblings are <version>.<catalog>.md in the same folder.
$folder = Split-Path -Parent $NotesPath
$version = [IO.Path]::GetFileNameWithoutExtension($NotesPath)
$languages = (Get-Content -LiteralPath $LanguagesPath -Raw | ConvertFrom-Json).languages

$locales = @{}
foreach ($language in $languages) {
    $text = $english
    if (-not $englishOnly -and $language.catalog -ne 'en-US') {
        $sibling = Join-Path $folder "$version.$($language.catalog).md"
        if (Test-Path -LiteralPath $sibling) { $text = Read-Notes $sibling }
    }
    $locales[$language.identity.ToLowerInvariant()] = $text
}

return @{ Default = $english; Locales = $locales; EnglishOnly = $englishOnly }
