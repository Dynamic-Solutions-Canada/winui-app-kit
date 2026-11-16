# WinUIAppKit.Startup.Tests

Portable checks for when an unpackaged copy re-points its start-with-Windows entry. The project compiles in `WinUIAppKit/Startup/RunEntry.cs` and runs on Linux. `npm test` runs it with every other harness under `tests/`.

## What it covers

- **The value written:** the quoted executable path, then the silent-launch argument.
- **When to re-point:**
  - an entry is never created;
  - an entry that already points at this copy is left alone, whatever its case or spacing;
  - an entry for another folder's copy, or in an older format, is rewritten.

## What it does not prove

- **The registry:** reading and writing HKCU Run.
- **Windows' own record:** that a choice the user made in Task Manager or Windows Settings still applies after a rewrite. Windows keeps that choice by the entry's name, which re-pointing never changes, so it can only be checked on Windows with a moved portable copy.
