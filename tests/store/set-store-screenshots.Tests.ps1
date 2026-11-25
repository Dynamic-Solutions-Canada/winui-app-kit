$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/store/set-store-screenshots.ps1'
$repairPath = Join-Path $PSScriptRoot '../../scripts/store/repair-store-draft.ps1'
$assets = Join-Path $PSScriptRoot 'fixtures/store-assets'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('store-screenshots-test-' + [guid]::NewGuid().ToString('N'))

# Every script this path runs parses and is ASCII, as the other Store scripts are.
foreach ($name in @('set-store-screenshots', 'resolve-store-screenshots', 'merge-store-screenshots', 'new-store-upload-archive', 'confirm-store-screenshots', 'repair-store-draft')) {
    $path = Join-Path $PSScriptRoot "../../scripts/store/$name.ps1"
    $tokens = $null
    $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $path), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors) { throw ($parseErrors | Out-String) }
    if ([IO.File]::ReadAllBytes((Resolve-Path $path)) | Where-Object { $_ -gt 127 }) { throw "$path is not ASCII" }
}

# A draft as the Store CLI leaves it: the package pending, and each listing with a logo and the
# screenshots uploaded by hand. en-gb is a listing locale outside the family's listings.json.
function Reset-Fixture {
    $global:fixture = @'
{"status":"PendingCommit","fileUploadUrl":"https://fixture.blob.core.windows.net/upload?sig=secret","pricing":{"priceId":"Tier1012"},"targetPublishMode":"Immediate",
 "applicationPackages":[{"fileName":"FixtureApp-0.1.47.msixupload","fileStatus":"PendingUpload"}],
 "listings":{
  "en-us":{"baseListing":{"title":"Fixture","images":[{"fileName":"logo.png","fileStatus":"Uploaded","imageType":"StoreLogo9x16"},{"fileName":"old-1.png","fileStatus":"Uploaded","imageType":"Screenshot","description":"Old"}]},"platformOverrides":{}},
  "en-ca":{"baseListing":{"title":"Fixture","images":[{"fileName":"old-1.png","fileStatus":"Uploaded","imageType":"Screenshot"}]}},
  "fr-fr":{"baseListing":{"title":"Fixture FR","images":[]}},
  "en-gb":{"baseListing":{"title":"Fixture"}}}}
'@ | ConvertFrom-Json
    $global:puts = 0
    $global:uploads = 0
    $global:uploadedEntries = @()
    $global:dropImages = $false
    $global:reorder = $false
}
function global:Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body, $ContentType)
    if ($Uri -like '*oauth2/token') { return @{ access_token = 'test-only' } }
    if ($Uri -match '/submissions/123$') {
        if ($Method -eq 'Put') {
            $global:puts++
            $global:fixture = $Body | ConvertFrom-Json
            if ($global:dropImages) { $global:fixture.listings.'fr-fr'.baseListing.images = @() }
            # The order the DisplaySwitchr update hit: removed screenshots ahead of the new set.
            if ($global:reorder) {
                $images = @($global:fixture.listings.'en-us'.baseListing.images)
                $global:fixture.listings.'en-us'.baseListing.images = @($images | Sort-Object { $_.fileStatus -ne 'PendingDelete' })
            }
            return $global:fixture
        }
        return $global:fixture
    }
    return @{ pendingApplicationSubmission = @{ id = '123' } }
}
function global:Invoke-WebRequest {
    param($Method, $Uri, $InFile, $ContentType, $Headers)
    if ($Method -ne 'Put' -or $Headers.'x-ms-blob-type' -ne 'BlockBlob') { throw 'Unexpected upload call.' }
    $global:uploads++
    $zip = [IO.Compression.ZipFile]::OpenRead($InFile)
    try { $global:uploadedEntries = @($zip.Entries | ForEach-Object FullName) }
    finally { $zip.Dispose() }
}
function Check($Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    Write-Output "PASS: $Name"
}
function Pending([string]$Locale) {
    @($global:fixture.listings.$Locale.baseListing.images | Where-Object { $_.fileStatus -eq 'PendingUpload' })
}

try {
    $null = New-Item -ItemType Directory -Path "$temporary/source" -Force
    [IO.File]::WriteAllText("$temporary/source/FixtureApp-0.1.47.msixbundle", 'fixture')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $upload = "$temporary/FixtureApp-0.1.47.msixupload"
    [IO.Compression.ZipFile]::CreateFromDirectory("$temporary/source", $upload)

    # Plan only: no Store call at all, and every locale in listings.json planned.
    Reset-Fixture
    $planned = & $scriptPath -PlanOnly -UploadPath $upload -AssetsPath $assets
    Check ($global:puts -eq 0 -and $global:uploads -eq 0) 'plan only makes no Store call'
    Check ("$planned" -match '3 locales, 2 screenshots each') 'plan only covers every listing locale'

    # A real run replaces each listing's screenshots and keeps everything else.
    Reset-Fixture
    $output = & $scriptPath -AppId 9TESTAPP0001 -UploadPath $upload -AssetsPath $assets
    Check ($global:puts -eq 1 -and $global:uploads -eq 1) 'one draft update and one upload'
    $us = Pending 'en-us'
    Check (($us.fileName -join ',') -eq 'en-us/00-hero-dark.png,en-us/01-settings-dark.png') 'en-us: the hero first, then the shared screenshot'
    Check (($us.description -join '|') -eq 'Everything from the tray.|Settings.') 'en-us: captions from listings.json, in order'
    Check (($global:fixture.listings.'en-us'.baseListing.images | Where-Object fileName -EQ 'old-1.png').fileStatus -eq 'PendingDelete') 'the old screenshot is removed'
    Check (($global:fixture.listings.'en-us'.baseListing.images | Where-Object imageType -EQ 'StoreLogo9x16').fileStatus -eq 'Uploaded') 'the logo is kept'
    $order = @($global:fixture.listings.'en-us'.baseListing.images | ForEach-Object { "$($_.fileName):$($_.fileStatus)" })
    Check (($order -join ',') -eq 'logo.png:Uploaded,en-us/00-hero-dark.png:PendingUpload,en-us/01-settings-dark.png:PendingUpload,old-1.png:PendingDelete') 'display order: logo, the new set in order, then the removed screenshots'
    Check ((Pending 'fr-fr').description[0] -eq 'Tout depuis la zone de notification.') 'fr-fr: French captions'
    Check (@($global:fixture.listings.'fr-fr'.baseListing.images).Count -eq 2) 'fr-fr: an empty image list gains only the new screenshots'
    Check ((Pending 'en-gb').Count -eq 2 -and "$output" -match 'en-US screenshots for en-gb') 'a listing outside listings.json takes the en-US set and is reported'
    Check ($global:fixture.listings.'en-us'.baseListing.title -eq 'Fixture' -and $global:fixture.pricing.priceId -eq 'Tier1012' -and $global:fixture.targetPublishMode -eq 'Immediate') 'listing text, pricing and publishing mode are kept'
    Check ($global:uploadedEntries -contains 'FixtureApp-0.1.47.msixupload') 'the archive holds the package under its registered name'
    Check ($global:uploadedEntries -contains 'fr-fr/00-hero-dark.png' -and $global:uploadedEntries -contains 'en-ca/00-hero-dark.png') 'the archive holds every listing image'
    Check ("$output" -notmatch 'sig=secret' -and "$output" -match 'NOT committed') 'the upload URL is never printed, and nothing is committed'

    # en-CA copies en-US: same hero bytes as en-US, not its own.
    $images = (& (Join-Path $PSScriptRoot '../../scripts/store/resolve-store-screenshots.ps1') -AssetsPath $assets -ListingLocales @('en-CA', 'fr-FR')).Locales
    Check ($images['en-ca'][0].Source -like '*en-US*00-hero-dark.png' -and $images['fr-fr'][0].Source -like '*fr-FR*00-hero-dark.png') 'a copyFrom locale uses its target hero'

    # A localized manifest: every locale uploads its own hero and its own copy of each localized
    # screenshot, in manifest order; en-CA copies en-US's images.
    $localized = Join-Path $PSScriptRoot 'fixtures/store-assets-localized'
    $resolve = Join-Path $PSScriptRoot '../../scripts/store/resolve-store-screenshots.ps1'
    $images = (& $resolve -AssetsPath $localized -ListingLocales @('en-US', 'en-CA', 'fr-FR')).Locales
    $sources = @($images['fr-fr'] | ForEach-Object { $_.Source.Replace('\', '/') })
    Check ($sources[0] -like '*/screenshots/fr-FR/00-hero-dark.png' -and $sources[1] -like '*/screenshots/fr-FR/01-settings-dark.png') 'localized: fr-fr uploads its own hero, then its own screenshot'
    Check (($images['fr-fr'].Description -join '|') -eq 'Tout depuis la zone de notification.|Param' + [char]0xE8 + 'tres.') 'localized: fr-fr captions in order'
    Check ($images['en-ca'][1].Source.Replace('\', '/') -like '*/screenshots/en-US/01-settings-dark.png') 'localized: a copyFrom locale uploads its target images'
    Check (($images['en-ca'].FileName -join ',') -eq 'en-ca/00-hero-dark.png,en-ca/01-settings-dark.png') 'localized: each listing gets its own archive names'
    $localizedPlan = & $scriptPath -PlanOnly -UploadPath $upload -AssetsPath $localized
    Check ("$localizedPlan" -match '3 locales, 2 screenshots each') 'localized: plan only covers every listing locale'

    # Anything the Store drops fails the run.
    Reset-Fixture
    $global:dropImages = $true
    $failed = $false
    try { & $scriptPath -AppId 9TESTAPP0001 -UploadPath $upload -AssetsPath $assets } catch { $failed = $_.Exception.Message -match 'fr-fr' }
    Check $failed 'screenshots the Store did not keep fail the run'

    # Every screenshot present but in the wrong display order fails too.
    Reset-Fixture
    $global:reorder = $true
    $failed = $false
    try { & $scriptPath -AppId 9TESTAPP0001 -UploadPath $upload -AssetsPath $assets } catch { $failed = $_.Exception.Message -match 'display order, for en-us' }
    Check $failed 'screenshots kept in the wrong display order fail the run'

    # A draft that does not expect this package is never changed.
    Reset-Fixture
    $global:fixture.applicationPackages[0].fileName = 'Other-1.0.0.msixupload'
    $failed = $false
    try { & $scriptPath -AppId 9TESTAPP0001 -UploadPath $upload -AssetsPath $assets } catch { $failed = $true }
    Check ($failed -and $global:puts -eq 0 -and $global:uploads -eq 0) 'an unrelated draft is not touched'

    # A caption count that does not match the manifest stops before any change.
    $broken = "$temporary/assets"
    Copy-Item -Recurse $assets $broken
    $listing = Get-Content "$broken/microsoft/listings.json" -Raw -Encoding utf8 | ConvertFrom-Json
    $listing.locales.'fr-FR'.screenshotCaptions = @('Seulement une.')
    $listing | ConvertTo-Json -Depth 10 | Set-Content "$broken/microsoft/listings.json" -Encoding utf8
    Reset-Fixture
    $failed = $false
    try { & $scriptPath -AppId 9TESTAPP0001 -UploadPath $upload -AssetsPath $broken } catch { $failed = $_.Exception.Message -match 'fr-FR has 1 screenshot captions for 2' }
    Check ($failed -and $global:puts -eq 0) 'a caption count mismatch stops before any change'

    # A missing hero stops before any change, in a dry run too.
    Remove-Item "$broken/microsoft/screenshots/fr-FR/00-hero-dark.png"
    $listing.locales.'fr-FR'.screenshotCaptions = @('Une.', 'Deux.')
    $listing | ConvertTo-Json -Depth 10 | Set-Content "$broken/microsoft/listings.json" -Encoding utf8
    $failed = $false
    try { & $scriptPath -PlanOnly -UploadPath $upload -AssetsPath $broken } catch { $failed = $_.Exception.Message -match 'Missing Store screenshot for fr-fr' }
    Check $failed 'a missing hero fails the dry run'

    # Repair with the screenshots: one archive with the corrected package and the images.
    Reset-Fixture
    $global:fixture.applicationPackages[0].fileName = 'old.msixupload'
    $repairOutput = & $repairPath -AppId 9TESTAPP0001 -SubmissionId 123 -UploadPath $upload -AssetsPath $assets
    Check ($global:puts -eq 1 -and $global:uploads -eq 1) 'repair: one draft update and one upload'
    Check ($global:uploadedEntries -contains 'FixtureApp-0.1.47.msixupload' -and $global:uploadedEntries -contains 'en-us/00-hero-dark.png') 'repair: the archive holds the package and the images'
    Check ((Pending 'en-us').Count -eq 2 -and $global:fixture.applicationPackages[0].fileStatus -eq 'PendingDelete') 'repair: screenshots replaced and the old package removed'
    Check ("$repairOutput" -match 'store-assets screenshots') 'repair: reports the screenshots'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
