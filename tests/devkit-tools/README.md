# devkit tools

Checks `scripts/devkit`, the wrapper Windows app repos run for devkit's `devkit-icons`, `devkit-store-hero` and `devkit-precommit`, against a throwaway copy of a tiny app in `fixture/`. `npm test` runs it with every other harness under `tests/`; it needs Node (22.12 or later, below 25), and its first run installs the pinned tools from `tools/devkit/package-lock.json`, fetching devkit from GitHub.

## What it covers

- **Icons from an app with no Node dependencies:** `icons` writes the Windows icons from the masters, `--check` fails before they exist and after a master changes, and passes in between. The app gains no `node_modules`.
- **Pre-commit:** a clean index passes, staged signing material is refused, and `--allow` reaches devkit unchanged.
- **Store hero:** `hero --check` reaches `devkit-store-hero`, shown by its missing-manifest message, which it gives before downloading any font, so the test stays offline after the first install.
- **Usage:** an unknown command is refused.

## What it does not prove

- **Icon artwork:** that the sizes look right in Windows; devkit's own tests and an installed app cover that.
- **Other targets:** the web and Android targets; the fixture has only `windows`.
- **Drawing heroes:** layouts, fonts and the pixel check; devkit's own tests cover those.
