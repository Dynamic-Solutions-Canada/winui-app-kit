# kit-ref

Checks `scripts/check-kit-ref`, the check apps run in `validate` so a workflow's `uses:` tag for the kit's reusable workflows cannot drift from the kit submodule (KIT-6). `npm test` runs `check-kit-ref.test.sh`, which builds throwaway repos with one workflow file each; it needs only bash and coreutils.

## What it covers

- **Passing cases:** the kit itself, a repo with no workflows, workflows that never call the kit, the pinned tag (plain, quoted, with a trailing comment), and a commented-out stale line beside a current one.
- **Failing cases:** an older tag (and the message names it), a branch reference, and an owner and repo name in another letter case.

## What it does not prove

- **That an app runs it:** each app adds `external/winui-app-kit/scripts/check-kit-ref .` to its own `validate`.
- **The Store workflow's own run-time comparison:** that runs on GitHub, at publish time.
