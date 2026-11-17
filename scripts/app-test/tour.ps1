<#
Tour a WinUI App Kit app's main window in the desktop session: every navigation page in the app's
light and dark themes and in Windows high contrast, plus the "Our other apps" page at a narrow
width. Uses windows-build-tools' app-test recipe (vm/app-test): copy this file and that recipe's
ui-lib.ps1 to C:\apptest, write C:\apptest\tour.json, then run it with app_test_ui:
    { "package": "MyCompany.MyApp", "process": "MyApp", "prefix": "myapp", "skip": [] }
  package  the installed package's name (Get-AppxPackage)
  process  the app's process name, without .exe
  prefix   the start of every screenshot's file name
  skip     navigation items the tour must not click, because clicking one acts (creates or
           changes something) instead of opening a page
Screenshots go to C:\apptest\out\<prefix>-<theme>-<page>.png. It relies on the kit's Settings page
("App theme" combo box) and its "Our other apps" item.
#>
. C:\apptest\ui-lib.ps1
$config = Get-Content C:\apptest\tour.json -Raw | ConvertFrom-Json
$skip = @($config.skip)

<# The app's main window: its widest window on screen. #>
function Get-MainWindow {
    Get-AppWindows $config.process |
        Where-Object { $_.Current.BoundingRectangle.Width -gt 400 -and $_.Current.BoundingRectangle.X -gt -1000 } |
        Sort-Object { $_.Current.BoundingRectangle.Width } -Descending |
        Select-Object -First 1
}

<# Click a navigation item and give the page time to load (the promo page downloads its catalogue). #>
function Open-Page([string]$Name) {
    $item = Find-ByName (Get-MainWindow) $Name 'ListItem'
    if (-not $item) { Write-Output "no navigation item '$Name'"; return $false }
    Invoke-Click $item
    Start-Sleep -Seconds 3
    return $true
}

<# Choose the app's own theme on its Settings page, by a regex on the option text. #>
function Set-AppTheme([string]$Match) {
    Open-Page 'Settings' | Out-Null
    $combo = Find-ByName (Get-MainWindow) 'App theme' 'ComboBox'
    if (-not $combo) { Write-Output "no 'App theme' combo box"; return }
    Select-ComboOption $combo $Match
}

<# Screenshot every page under one theme name. #>
function Save-AllPages([string]$Theme, [string[]]$Pages) {
    foreach ($page in $Pages) {
        if (Open-Page $page) {
            $safe = ($page -replace '[^A-Za-z0-9]+', '-').Trim('-')
            Save-Shot "$($config.prefix)-$Theme-$safe" (Get-MainWindow)
        }
    }
}

# -- Launch --------------------------------------------------------------
$pkg = Get-AppxPackage $config.package | Select-Object -First 1
if (-not $pkg) { throw "$($config.package) is not installed" }
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
Start-Sleep -Seconds 8
if (-not (Get-MainWindow)) {
    Write-Output ("no main window after launch; windows: " + ((Get-AppWindows $config.process | ForEach-Object { $_.Current.Name }) -join ', '))
    return
}
Set-WindowFitted (Get-MainWindow)

$cond = New-Object System.Windows.Automation.PropertyCondition($UiA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$pages = @((Get-MainWindow).FindAll("Descendants", $cond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $skip -notcontains $_ })
Write-Output ("pages: " + ($pages -join ' | '))

# -- App themes, then Windows high contrast with the app following Windows --
Set-AppTheme '^Light$'
Save-AllPages 'light' $pages
Set-AppTheme '^Dark$'
Save-AllPages 'dark' $pages
Set-AppTheme '^Use system'
Set-HighContrast $true
try {
    Save-AllPages 'hc' $pages
    Open-Page 'Our other apps' | Out-Null
    Set-WindowFitted (Get-MainWindow) 560
    Save-Shot "$($config.prefix)-hc-narrow-promo" (Get-MainWindow)
}
finally {
    Set-HighContrast $false
}

# -- Narrow width in light -----------------------------------------------
Set-WindowFitted (Get-MainWindow)
Set-AppTheme '^Light$'
Open-Page 'Our other apps' | Out-Null
Set-WindowFitted (Get-MainWindow) 560
Save-Shot "$($config.prefix)-light-narrow-promo" (Get-MainWindow)

# -- Leave the app as found ----------------------------------------------
Set-WindowFitted (Get-MainWindow)
Set-AppTheme '^Use system'
Get-Process $config.process | Stop-Process
