# build

Checks `build/package-output.ps1`, what `build/build-msix.ps1` does with a finished package. `npm test` runs it with PowerShell, on Linux, without packing anything.

## What it covers

- **Deliverable names (LAYOUT-12):** `<Product>-<version>-windows-<arch>.msix`, lowercase arch, a four-part dev version kept as written, and `-store` `.msixbundle` for a Store-identity build; a product that isn't PascalCase is reported.
- **The copy:** it lands in `.artifacts/packages/<version>/`, `package_output` keeps its own file, and a later build of the same version replaces the earlier copy.
- **Pruning `package_output`:** older versions of the app's packages go, both platforms of the current version and every other file stay, and a missing folder is fine.

## What it does not prove

- **The build itself:** that `build-msix.ps1` reaches this step and passes the right values; `npm run verify` and an app's `package:desktop` cover that.
- **Builds through the Windows build tooling:** those run in a staged copy, so `.artifacts/packages/` reaches the repo only once windows-build-tools copies it back.
