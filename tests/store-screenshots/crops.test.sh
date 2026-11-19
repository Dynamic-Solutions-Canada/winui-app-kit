#!/usr/bin/env bash
# Checks the crop geometry of scripts/store/render-screenshots.py, the Store screenshot renderer apps
# run in validate, with Python's standard unittest. See README.md beside this file.
set -euo pipefail

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 -I "$here/test_crops.py" -v
