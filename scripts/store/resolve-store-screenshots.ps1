<#
.SYNOPSIS
Works out the Store screenshot set for each listing locale from the app's store-assets, without any
Store call.
.DESCRIPTION
Used when a Store run owns the listing screenshots (store-publish's screenshots input). Every Store
screenshot in store-assets/manifest.json goes to every listing, in manifest order, captioned with that
locale's screenshotCaptions from store-assets/microsoft/listings.json. An entry whose file contains
{locale} has one image per listing locale: a generated hero (an entry with a "hero" block, D-123), or a
localized screenshot cropped from that locale's own capture. Its {locale} is the locale's own record, or
the target of a copyFrom locale, so en-CA shows the en-US images. An entry without {locale} is one image
shared by every listing. A listing locale with no record in listings.json (one outside the family) takes
the en-US screenshots and captions, as it takes the English release notes, and is reported in Fallback.

Each image gets an archive name of "<listing locale>/<file name>", so every listing has its own copy in
the upload archive and no two listings share a file.

Stops, before anything is uploaded, when an image is missing, a locale's caption count differs from the
manifest, or there are not 1 to 10 screenshots.

Returns a hashtable: Locales maps each lower-case listing locale to its images in order, each a hashtable
with Source (the PNG's path), FileName (its archive name) and Description (its caption); Files lists
every image once; Fallback lists the locales that took the en-US set.
#>
param(
    [Parameter(Mandatory)][string]$AssetsPath,
    # The listing locales to plan for: the draft's listings, or the listing's own locales for a dry run.
    [Parameter(Mandatory)][string[]]$ListingLocales
)

$ErrorActionPreference = 'Stop'
$localeToken = '{locale}'

$manifestPath = Join-Path $AssetsPath 'manifest.json'
$listingPath = Join-Path $AssetsPath 'microsoft/listings.json'
foreach ($path in @($manifestPath, $listingPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Store screenshots need $path." }
}
$entries = @((Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json).microsoft)
if ($entries.Count -lt 1 -or $entries.Count -gt 10) {
    throw "The Store takes 1 to 10 screenshots; $manifestPath lists $($entries.Count)."
}
$records = (Get-Content -LiteralPath $listingPath -Raw -Encoding utf8 | ConvertFrom-Json).locales

# The listings.json record for a locale (any case), following one copyFrom, with the name of the record
# that holds the text; null when the listing has no such locale.
function Find-Record([string]$Locale) {
    $property = $records.PSObject.Properties | Where-Object { $_.Name -ieq $Locale } | Select-Object -First 1
    if (-not $property) { return $null }
    $record = $property.Value
    $name = $property.Name
    if ($record.PSObject.Properties['copyFrom']) {
        $name = $record.copyFrom
        $record = $records.PSObject.Properties[$name].Value
        if (-not $record) { throw "listings.json: $($property.Name) copies $name, which it does not have." }
    }
    return @{ Name = $name; Record = $record }
}

$english = Find-Record 'en-US'
if (-not $english) { throw 'listings.json has no en-US record.' }

$plan = @{}
$files = [Collections.Generic.List[hashtable]]::new()
$fallback = [Collections.Generic.List[string]]::new()
foreach ($locale in $ListingLocales) {
    $key = $locale.ToLowerInvariant()
    $found = Find-Record $locale
    if (-not $found) {
        $found = $english
        $fallback.Add($key)
    }

    $captions = @($found.Record.screenshotCaptions)
    if ($captions.Count -ne $entries.Count) {
        throw "listings.json: $($found.Name) has $($captions.Count) screenshot captions for $($entries.Count) screenshots."
    }

    $images = for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        $file = [string]$entry.file
        # A hero or localized screenshot has one image per listing locale; any other is shared.
        $file = $file.Replace($localeToken, $found.Name)
        $source = Join-Path (Join-Path $AssetsPath 'microsoft') $file
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing Store screenshot for ${key}: $source" }
        @{
            Source = (Resolve-Path -LiteralPath $source).Path
            FileName = "$key/$([IO.Path]::GetFileName($file))"
            Description = [string]$captions[$index]
        }
    }
    $images = @($images)
    if (@($images.FileName | Select-Object -Unique).Count -ne $images.Count) {
        throw "Two screenshots for $key share a file name; give each a distinct name."
    }
    $plan[$key] = $images
    foreach ($image in $images) { $files.Add($image) }
}

return @{ Locales = $plan; Files = $files.ToArray(); Fallback = $fallback.ToArray() }
