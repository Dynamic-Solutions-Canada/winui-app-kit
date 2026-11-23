# listing

Checks the hero fields of `scripts/check-listing.py`, the Microsoft Store listing check apps run as `check:listing`. `npm test` runs `check-listing.test.sh`, which runs `test_check_listing.py` with Python's standard `unittest`; it needs only Python 3.

## What it covers

- **Hero fields (D-123):** `heroHeadline` and `heroSubline` may sit in a record beside the six store fields; a listing without them still passes; a field set in some full records but not others fails, naming the locales; hero text follows the text rules (no em dash, a length limit).
- **Other extra fields:** still refused, `releaseNotes` included.

## What it does not prove

- **The rest of the listing check:** locales, store limits, the README tagline and the release notes; an app's `check:listing` covers those on its real listing.
- **Whether a headline fits the hero:** that is `devkit-store-hero`'s check.
