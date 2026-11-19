# build

Checks `build/package-output.ps1`, what `build/build-msix.ps1` does with a finished package, and `build/runtime-dependency.ps1`, its gate on the manifest's Windows App Runtime dependency. `npm test` runs both with PowerShell, on Linux, without packing anything.

## What it covers

- **Deliverable names (LAYOUT-12):** `<Product>-<version>-windows-<arch>.msix`, lowercase arch, a four-part dev version kept as written, and `-store` `.msixbundle` for a Store-identity build; a product that isn't PascalCase is reported.
- **The copy:** it lands in `.artifacts/packages/<version>/`, `package_output` keeps its own file, and a later build of the same version replaces the earlier copy.
- **The Windows App Runtime gate:** the framework name and runtime version come from the restored runtime package's `WindowsAppSDK-VersionInfo.json` (with its byte order mark); a missing restore output, runtime package or version file fails; a manifest `MinVersion` equal to or above the runtime passes, compared as a version; a lower one, a dependency on another framework line, no dependency, or an unreadable `MinVersion` fails, naming the value to set.
- **Pruning `package_output`:** older versions of the app's packages go, both platforms of the current version and every other file stay, and a missing folder is fine.

## What it does not prove

- **The build itself:** that `build-msix.ps1` reaches these steps and passes the right values (including the real `project.assets.json`); `npm run verify` and an app's `package:desktop` cover that.
- **Builds through the Windows build tooling:** those run in a staged copy, so `.artifacts/packages/` reaches the repo only once windows-build-tools copies it back.
