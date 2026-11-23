<#
.SYNOPSIS
Stops unless every listing in a Store submission expects exactly its planned screenshots, in order.
.DESCRIPTION
Read back after merge-store-screenshots.ps1's changes were saved, so a field the Store dropped or
reordered fails the run instead of going unnoticed. The Store shows screenshots in array order, so the
check is on display order: the listing's screenshots, in array order, must start with exactly the
planned ones (PendingUpload) and have nothing after them but screenshots being removed (PendingDelete).
Other image types are ignored.
#>
param(
    [Parameter(Mandatory)][object]$Submission,
    [Parameter(Mandatory)][hashtable]$Plan
)

$ErrorActionPreference = 'Stop'
foreach ($listing in @($Submission.listings.PSObject.Properties)) {
    $screenshots = @($listing.Value.baseListing.images | Where-Object { $_.imageType -ieq 'Screenshot' })
    $expected = @($Plan.Locales[$listing.Name.ToLowerInvariant()] | ForEach-Object FileName)
    $leading = @($screenshots | Select-Object -First $expected.Count)
    $inOrder = ($leading.fileName -join '|') -ceq ($expected -join '|') -and
        @($leading | Where-Object fileStatus -NE 'PendingUpload').Count -eq 0
    $restRemoved = @($screenshots | Select-Object -Skip $expected.Count | Where-Object fileStatus -NE 'PendingDelete').Count -eq 0
    if (-not ($inOrder -and $restRemoved)) {
        throw "The Store did not keep the planned screenshots, in display order, for $($listing.Name)."
    }
}
