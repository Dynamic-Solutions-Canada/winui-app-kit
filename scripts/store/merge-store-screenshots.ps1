<#
.SYNOPSIS
Replaces the screenshots of every listing in a Store draft with a planned set, keeping everything else.
.DESCRIPTION
For each listing in the draft, every existing image of type Screenshot is marked PendingDelete and the
locale's planned screenshots (from resolve-store-screenshots.ps1) are added after them as PendingUpload,
in order, each with its caption as the description. Logos and every other image type, the listing text,
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

    # The current screenshots go; the logos stay exactly as they are.
    $images = foreach ($image in @($base.images | Where-Object { $null -ne $_ })) {
        if ([string]$image.imageType -ieq 'Screenshot') {
            $image | Add-Member -NotePropertyName fileStatus -NotePropertyValue 'PendingDelete' -Force
        }
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
    $base | Add-Member -NotePropertyName images -NotePropertyValue @(@($images | Where-Object { $null -ne $_ }) + @($added)) -Force
}
return $Draft
