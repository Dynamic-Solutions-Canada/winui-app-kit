# WinUIAppKit.Osd.Tests

Portable checks for the on-screen display's arithmetic. The project compiles in `WinUIAppKit/Windowing/OsdLayout.cs` and runs on Linux. `npm test` runs it with every other harness under `tests/`.

## What it covers

- **Width:** a fixed-width popup stays fixed; otherwise the content's width plus a little room, within the minimum and maximum.
- **Placement:** bottom centre of the display, 64 DIPs above its bottom edge, scaled with the display, on displays with negative coordinates too, rounding sizes up.
- **Fade:** opaque to transparent in steps that only ever fall, without wrapping, in under half a second.

## What it does not prove

- **The window:** that `OsdController` never activates, passes clicks through, stays above other windows, or parks off-screen; and its look (acrylic and rounded corners on Windows 11, the solid background on Windows 10). Check those in an app on both, for example in the test VMs.
