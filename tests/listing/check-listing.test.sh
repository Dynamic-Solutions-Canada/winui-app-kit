#!/usr/bin/env bash
# Checks the listing fields scripts/check-listing.py accepts, with Python's standard unittest. See
# README.md beside this file.
set -euo pipefail

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 -I "$here/test_check_listing.py" -v
