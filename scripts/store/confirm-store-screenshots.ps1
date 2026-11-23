<#
.SYNOPSIS
Stops unless every listing in a Store submission expects exactly its planned screenshots, in order.
.DESCRIPTION
Read back after merge-store-screenshots.ps1's changes were saved, so a field the Store dropped or
reordered fails the run instead of going unnoticed. Only screenshots marked PendingUpload are compared;
the ones being removed and every other image type are ignored.
#>
param(
    [Parameter(Mandatory)][object]$Submission,
    [Parameter(Mandatory)][hashtable]$Plan
)

$ErrorActionPreference = 'Stop'
foreach ($listing in @($Submission.listings.PSObject.Properties)) {
    $pending = @($listing.Value.baseListing.images | Where-Object { $_.imageType -ieq 'Screenshot' -and $_.fileStatus -eq 'PendingUpload' } | ForEach-Object fileName)
    $expected = @($Plan.Locales[$listing.Name.ToLowerInvariant()] | ForEach-Object FileName)
    if (($pending -join '|') -cne ($expected -join '|')) {
        throw "The Store did not keep the planned screenshots for $($listing.Name)."
    }
}
