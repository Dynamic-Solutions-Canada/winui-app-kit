# WinUIAppKit.Settings.Tests

Portable checks for the kit's settings store, protected store and atomic writes. The project compiles in the production source files (`AtomicFile.cs`, `ProtectedStore.cs`, `JsonSettingsStore.cs`) rather than referencing the WinUI library, so it runs on Linux. `npm test` runs it with every other harness under `tests/`.

## What it covers

- **Settings store:**
  - first run writes defaults with `SchemaVersion` first;
  - a saved `false` for a default-true setting loads back `false`;
  - migrations run in order from any older version, after a `.v<n>.bak` backup;
  - a file from a newer version is backed up and not rewritten;
  - unreadable files, wrong JSON shapes and a failing migration are all reported as unreadable, with the original kept as `.bad`;
  - reset renames the old file and writes defaults;
  - the unversioned store behaves as before.
- **Atomic writes:** replacement, and no temporary file left behind.
- **Protected store:**
  - round trip with the app's file-name format;
  - nothing stored in plain text;
  - an unreadable file is kept, and the error report leaves out the value;
  - names that could leave the folder are rejected;
  - the folder is resolved on every call.

## What it does not prove

- **DPAPI itself:** the checks use a reversible fake protector. Real encryption, and decryption failing under another Windows user, can only be checked on Windows.
- **Packaged apps:** behaviour under MSIX's redirected `%AppData%`.
- **Two processes:** concurrent readers, for example an app and its shell extension.
