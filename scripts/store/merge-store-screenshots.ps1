<#
.SYNOPSIS
Replaces the screenshots of every listing in a Store draft with a planned set, keeping everything else.
.DESCRIPTION
For each listing in the draft, every existing image of type Screenshot is marked PendingDelete and the
locale's planned screenshots (from resolve-store-screenshots.ps1) are added as PendingUpload, in order,
each with its caption as the description. The Store shows screenshots in array order, so the array is
the other images (logos) as they were, then the new screenshots, then the ones being removed: removed
entries placed first scramble the displayed order. Logos and every other image type, the listing text,
platform overrides, packages, pricing and publication settings are left as they are. The draft object
is changed in place and returned.

Stops when a listing in the draft has no planned set.
#>
param(
    [Parameter(Mandatory)][object]$Draft,
    [Parameter(Mandatory)][hashtable]$Plan
)

$ErrorActionPreference = 'Stop'

foreach ($listing in @($Draft.listings.PSObject.Properties)) {
    $key = $listing.Name.ToLowerInvariant()
    if (-not $Plan.Locales.ContainsKey($key)) { throw "No screenshots were planned for the $($listing.Name) listing." }
    $base = $listing.Value.baseListing

    # The logos stay exactly as they are; the current screenshots go, after the new set.
    $existing = @($base.images | Where-Object { $null -ne $_ })
    $kept = @($existing | Where-Object { [string]$_.imageType -ine 'Screenshot' })
    $removed = foreach ($image in @($existing | Where-Object { [string]$_.imageType -ieq 'Screenshot' })) {
        $image | Add-Member -NotePropertyName fileStatus -NotePropertyValue 'PendingDelete' -Force
        $image
    }
    $added = foreach ($planned in $Plan.Locales[$key]) {
        [pscustomobject]@{
            fileName = $planned.FileName
            fileStatus = 'PendingUpload'
            imageType = 'Screenshot'
            description = $planned.Description
        }
    }
    $base | Add-Member -NotePropertyName images -NotePropertyValue @($kept + @($added) + @($removed | Where-Object { $null -ne $_ })) -Force
}
return $Draft
