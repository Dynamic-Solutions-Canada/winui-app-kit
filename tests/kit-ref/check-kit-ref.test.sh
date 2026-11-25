#!/usr/bin/env bash
# Checks scripts/check-kit-ref, which apps run in validate to keep their kit workflow `uses:` tags on
# the kit submodule's version, against throwaway repos with hand-written workflows. See README.md
# beside this file.
set -euo pipefail

kit_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
check="$kit_root/scripts/check-kit-ref"
version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$kit_root/Directory.Build.props" | head -n 1)"
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

# A fresh repo whose only workflow file holds the given text.
repo_with() {
    local name="$1" text="$2"
    mkdir -p "$work/$name/.github/workflows"
    printf '%s\n' "$text" > "$work/$name/.github/workflows/store-publish.yml"
    printf '%s' "$work/$name"
}

kit='Dynamic-Solutions-Canada/winui-app-kit/.github/workflows'

expect pass "the kit itself has no reference to check" "$check"
mkdir -p "$work/bare"
expect pass "a repo with no workflows passes" "$check" "$work/bare"
expect pass "a repo whose workflows never call the kit passes" "$check" "$(repo_with other '    uses: actions/checkout@v6')"

expect pass "the pinned tag passes" "$check" "$(repo_with match "    uses: $kit/store-publish.yml@v$version")"
expect pass "a quoted pinned tag with a comment passes" "$check" "$(repo_with quoted "    uses: '$kit/credential-expiry.yml@v$version' # kit")"
expect pass "a commented-out stale line is ignored" "$check" "$(repo_with commented "    # uses: $kit/store-publish.yml@v0.0.1
    uses: $kit/store-publish.yml@v$version")"

expect fail "an older tag fails" "$check" "$(repo_with stale "    uses: $kit/store-publish.yml@v0.0.1")"
grep -q 'calls the kit at v0.0.1' "$work/out.txt" && passed=$((passed + 1)) || { failed=$((failed + 1)); echo "FAIL: the failure names the stale tag"; sed 's/^/    /' "$work/out.txt"; }
expect fail "a branch reference fails" "$check" "$(repo_with branch "    uses: $kit/store-publish.yml@main")"
expect fail "an owner in another case is still checked" "$check" "$(repo_with case "    uses: dynamic-solutions-canada/WinUI-App-Kit/.github/workflows/store-publish.yml@v0.0.1")"

printf '%d passed, %d failed\n' "$passed" "$failed"
[[ $failed -eq 0 ]]
