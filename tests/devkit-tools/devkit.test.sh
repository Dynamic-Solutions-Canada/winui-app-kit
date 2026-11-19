#!/usr/bin/env bash
# Checks scripts/devkit, the wrapper Windows app repos run for devkit's icon and pre-commit commands,
# against a throwaway copy of a tiny app (fixture/: two SVG masters and a Windows icons.json). The app
# copy has no node_modules, as a real Windows app repo has none. See README.md beside this file.
set -euo pipefail

kit_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
devkit="$kit_root/scripts/devkit"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT

passed=0
failed=0

# Record one result: pass when the command's exit status matches the expectation.
expect() {
    local want="$1" description="$2"
    shift 2
    local status=0
    "$@" >"$work/out.txt" 2>&1 || status=$?
    if { [[ "$want" == pass && $status -eq 0 ]] || [[ "$want" == fail && $status -ne 0 ]]; }; then
        passed=$((passed + 1))
    else
        failed=$((failed + 1))
        printf 'FAIL: %s (exit %s)\n' "$description" "$status"
        sed 's/^/    /' "$work/out.txt"
    fi
}

# A fresh app repo from the fixture.
app="$work/app"
cp -r "$kit_root/tests/devkit-tools/fixture" "$app"
mkdir -p "$app/AppMSIX/Images" "$app/App"
git -C "$app" init -q
git -C "$app" config user.email test@example.invalid
git -C "$app" config user.name test
cd "$app"

# --- Icons ---
expect fail "icons: --check fails before any icon is written" "$devkit" icons --check
expect pass "icons: writes every size from the masters, with no node_modules in the app" "$devkit" icons
expect pass "icons: --check passes once written" "$devkit" icons --check
[[ -f App/App.ico && -n "$(ls AppMSIX/Images)" ]] && passed=$((passed + 1)) || { failed=$((failed + 1)); echo "FAIL: icons: the .ico and MSIX images exist"; }
[[ ! -e node_modules ]] && passed=$((passed + 1)) || { failed=$((failed + 1)); echo "FAIL: icons: the app gained no node_modules"; }
sed -i 's/#3a7bd5/#d53a3a/' assets/brand/icon.svg
expect fail "icons: --check fails when a master changes" "$devkit" icons --check

# --- Pre-commit ---
git add -A && git commit -qm fixture
expect pass "precommit: a clean index passes" "$devkit" precommit
touch signing.pfx && git add -f signing.pfx
expect fail "precommit: staged signing material is refused" "$devkit" precommit
expect pass "precommit: --allow passes through to devkit" "$devkit" precommit --allow signing.pfx
git rm -q --cached signing.pfx

# --- Usage ---
expect fail "usage: an unknown command is refused" "$devkit" lint

printf '%d passed, %d failed\n' "$passed" "$failed"
[[ $failed -eq 0 ]]
