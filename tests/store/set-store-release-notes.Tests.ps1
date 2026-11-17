$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/store/set-store-release-notes.ps1'
$notesPath = Join-Path $PSScriptRoot 'fixtures/0.1.47.md'
$configPath = Join-Path $PSScriptRoot 'fixtures/windows-build.json'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('store-notes-test-' + [guid]::NewGuid().ToString('N'))
$parseErrors = $null
$tokens = $null
[Management.Automation.Language.Parser]::ParseFile((Resolve-Path $scriptPath), [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors) { throw ($parseErrors | Out-String) }
foreach ($script in @($scriptPath, (Join-Path $PSScriptRoot '../../scripts/store/resolve-store-release-notes.ps1'))) {
    $null = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors) { throw ($parseErrors | Out-String) }
    if ([IO.File]::ReadAllBytes((Resolve-Path $script)) | Where-Object { $_ -gt 127 }) { throw "$script is not ASCII" }
}
function Reset-Fixture {
    $global:fixture = @'
{"status":"PendingCommit","pricing":{"priceId":"Tier1012","marketSpecificPricings":{"CA":"keep"}},"targetPublishMode":"Immediate","applicationPackages":[{"fileName":"FixtureApp-0.1.47.msixupload","fileStatus":"PendingUpload"}],"listings":{"en-us":{"baseListing":{"title":"Fixture title","releaseNotes":"old"},"platformOverrides":{}},"en-ca":{"baseListing":{"title":"Fixture title","releaseNotes":"old"}},"fr-fr":{"baseListing":{"title":"French title","releaseNotes":"old"}},"fr-ca":{"baseListing":{"title":"French title","releaseNotes":"old"}},"es-es":{"baseListing":{"title":"Spanish title","releaseNotes":"old"}},"en-gb":{"baseListing":{"title":"Fixture title","releaseNotes":"old"}}}}
'@ | ConvertFrom-Json
    $global:puts = 0
    $global:commits = 0
    $global:storeStatus = 'Certification'
    $global:loseNotes = $null
}
function global:Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body, $ContentType)
    if ($Uri -like '*oauth2/token') { return @{access_token='test-only'} }
    if ($Uri -match '/commit$') {
        if ($ContentType -ne 'application/json') { throw 'Only JSON content is accepted' }
        $global:commits++
        return @{status='CommitStarted'}
    }
    if ($Uri -match '/status$') { return @{status=$global:storeStatus} }
    if ($Uri -match '/submissions/123$') {
        if ($Method -eq 'Put') {
            $global:puts++
            $global:fixture = $Body | ConvertFrom-Json
            if ($global:loseNotes) { $global:fixture.listings.($global:loseNotes).baseListing.releaseNotes = 'old' }
        }
        return $global:fixture
    }
    return @{pendingApplicationSubmission=@{id='123'}}
}
function Check($condition, $name) {
    if (-not $condition) { throw $name }
    Write-Output "PASS: $name"
}
function Run-Notes([switch]$Commit, [string]$Notes = $notesPath, [string]$Config = $configPath) {
    & $scriptPath -AppId 9TESTAPP0001 -UploadFileName FixtureApp-0.1.47.msixupload -NotesPath $Notes -WindowsBuildJson $Config -Commit:$Commit
}
function Notes-Of([string]$Locale) { $global:fixture.listings.$Locale.baseListing.releaseNotes }

$english = (Get-Content $notesPath -Raw).Trim()
$french = (Get-Content (Join-Path $PSScriptRoot 'fixtures/0.1.47.fr-FR.md') -Raw -Encoding utf8).Trim()

# A localized listing: each locale reads its catalog's notes, else the English notes.
Reset-Fixture
Run-Notes
Check ($global:puts -eq 1 -and $global:commits -eq 0) 'draft mode saves notes without submitting'
Check ((Notes-Of 'en-us') -eq $english -and (Notes-Of 'en-ca') -eq $english) 'English listings get the English notes'
Check ((Notes-Of 'fr-fr') -eq $french -and (Notes-Of 'fr-ca') -eq $french) 'French listings get the fr-FR notes, fr-CA through its catalog'
Check ((Notes-Of 'es-es') -eq $english) 'a catalog with no notes file falls back to English'
Check ((Notes-Of 'en-gb') -eq $english) 'a locale outside the family falls back to English'
Check ($global:fixture.listings.'fr-ca'.baseListing.title -eq 'French title') 'translated listing text preserved'
Check ($global:fixture.pricing.marketSpecificPricings.CA -eq 'keep' -and $global:fixture.targetPublishMode -eq 'Immediate') 'pricing and publication mode preserved'

try {
    $null = New-Item -ItemType Directory -Path $temporary -Force

    # An English-only listing (D-111): English everywhere, even with a French notes file present.
    $englishOnlyConfig = Join-Path $temporary 'english-only.json'
    '{"listing":{"product_name":"Fixture","catalog_dir":null}}' | Set-Content -LiteralPath $englishOnlyConfig
    Reset-Fixture
    Run-Notes -Config $englishOnlyConfig
    Check ((Notes-Of 'fr-fr') -eq $english -and (Notes-Of 'fr-ca') -eq $english -and (Notes-Of 'en-us') -eq $english) 'English-only listing: English notes everywhere'

    # A set catalog_dir behaves like an absent one.
    $setConfig = Join-Path $temporary 'set.json'
    '{"listing":{"product_name":"Fixture","catalog_dir":"App/Catalogs"}}' | Set-Content -LiteralPath $setConfig
    Reset-Fixture
    Run-Notes -Config $setConfig
    Check ((Notes-Of 'fr-ca') -eq $french) 'a set catalog_dir uses the translated notes'

    # The 1500-character limit holds for every locale's file, and fails before any Store change.
    Copy-Item $notesPath (Join-Path $temporary '0.1.47.md')
    ('x' * 1501) | Set-Content -LiteralPath (Join-Path $temporary '0.1.47.fr-FR.md')
    Reset-Fixture
    try { Run-Notes -Notes (Join-Path $temporary '0.1.47.md'); throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*between 1 and 1500*0.1.47.fr-FR.md*') 'over-long translated notes rejected' }
    Check ($global:puts -eq 0) 'over-long translated notes change nothing'
    Reset-Fixture
    Run-Notes -Notes (Join-Path $temporary '0.1.47.md') -Config $englishOnlyConfig
    Check ((Notes-Of 'fr-fr') -eq $english) 'English-only listing never reads the translated file'

    '   ' | Set-Content -LiteralPath (Join-Path $temporary '0.1.47.fr-FR.md')
    Reset-Fixture
    try { Run-Notes -Notes (Join-Path $temporary '0.1.47.md'); throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*between 1 and 1500*') 'empty translated notes rejected' }
    Check ($global:puts -eq 0) 'empty translated notes change nothing'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
Reset-Fixture
Run-Notes -Commit
Check ($global:commits -eq 1) 'verified notes submitted once'
Reset-Fixture
$global:fixture.status = 'CommitFailed'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*not editable*') 'failed submissions require replacement, not editing' }
Check ($global:puts -eq 0 -and $global:commits -eq 0) 'failed submission is not modified'
Reset-Fixture
try {
    & $scriptPath -AppId 9TESTAPP0001 -ExpectedSubmissionId 999 -UploadFileName FixtureApp-0.1.47.msixupload -NotesPath $notesPath -WindowsBuildJson $configPath -Commit
    throw 'Expected rejection'
} catch { Check ($_.Exception.Message -like '*not the current pending submission*') 'resuming rejects a different pending draft' }
Check ($global:puts -eq 0 -and $global:commits -eq 0) 'mismatched resume leaves draft untouched'
Reset-Fixture
$global:fixture.applicationPackages[0].fileName = 'unrelated.msixupload'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*expected release upload*') 'unrelated draft rejected' }
Check ($global:puts -eq 0 -and $global:commits -eq 0) 'unrelated draft not changed'
Reset-Fixture
$global:loseNotes = 'en-us'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*not retained*') 'failed verification prevents submission' }
Check ($global:commits -eq 0) 'unverified notes never committed'
Reset-Fixture
$global:loseNotes = 'fr-ca'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*not retained for fr-ca*') 'a translated listing that loses its notes prevents submission' }
Check ($global:commits -eq 0) 'unverified translated notes never committed'
Reset-Fixture
$global:fixture.status = 'Certification'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*not editable*') 'non-editable submission rejected' }
Reset-Fixture
$global:storeStatus = 'CommitFailed'
try { Run-Notes -Commit; throw 'Expected rejection' } catch { Check ($_.Exception.Message -like '*Store submission failed*') 'ingestion failure reported' }

