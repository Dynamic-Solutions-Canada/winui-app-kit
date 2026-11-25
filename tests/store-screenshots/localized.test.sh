#!/usr/bin/env bash
# Checks how scripts/store/render-screenshots.py crops localized entries and 200% captures, with
# Python's standard unittest. See README.md beside this file.
set -euo pipefail

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 -I "$here/test_localized.py" -v
