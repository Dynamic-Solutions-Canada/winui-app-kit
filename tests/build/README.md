# build

Checks `build/package-output.ps1`, what `build/build-msix.ps1` does with a finished package, `build/runtime-dependency.ps1`, its gate on the manifest's Windows App Runtime dependency, and the publish file list `build/WinUIAppKit.targets` leaves for Windows ML. `npm test` runs all three with PowerShell, on Linux, without packing anything; the Windows ML check restores `Xaml/CompileCheck`, so it needs NuGet access on its first run.

## What it covers

- **Deliverable names (LAYOUT-12):** `<Product>-<version>-windows-<arch>.msix`, lowercase arch, a four-part dev version kept as written, and `-store` `.msixbundle` for a Store-identity build; a product that isn't PascalCase is reported.
- **The copy:** it lands in `.artifacts/packages/<version>/`, `package_output` keeps its own file, and a later build of the same version replaces the earlier copy.
- **The Windows App Runtime gate:** the framework name and runtime version come from the restored runtime package's `WindowsAppSDK-VersionInfo.json` (with its byte order mark); a missing restore output, runtime package or version file fails; a manifest `MinVersion` equal to or above the runtime passes, compared as a version; a lower one, a dependency on another framework line, no dependency, or an unreadable `MinVersion` fails, naming the value to set.
- **Windows ML natives:** MSBuild's publish list for `Xaml/CompileCheck`, with `build-msix.ps1`'s publish settings, leaves out `DirectML.dll`, `onnxruntime.dll` and `Microsoft.Windows.AI.MachineLearning.dll` but keeps the managed ML projection; without the kit's target the SDK still lists all three, and the check fails once it no longer does, so the target can be removed.
- **Pruning `package_output`:** older versions of the app's packages go, both platforms of the current version and every other file stay, and a missing folder is fine.

## What it does not prove

- **That the framework supplies the natives at run time:** checked by hand in a VM with the Windows App Runtime installed.
- **The build itself:** that `build-msix.ps1` reaches these steps and passes the right values (including the real `project.assets.json`); `npm run verify` and an app's `package:desktop` cover that.
- **Builds through the Windows build tooling:** those run in a staged copy, so `.artifacts/packages/` reaches the repo only once windows-build-tools copies it back.
