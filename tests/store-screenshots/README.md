# store screenshots

Checks the crop geometry of `scripts/store/render-screenshots.py`, the Microsoft Store screenshot renderer apps run in `validate` and to crop their captures, how it treats a generated hero, and how it crops localized entries and 200% captures. `npm test` runs `crops.test.sh`, `heroes.test.sh` and `localized.test.sh`, which run `test_crops.py`, `test_heroes.py` and `test_localized.py` with Python's standard `unittest`; they need only Python 3.

## What it covers

- **`tray`:** a 420 x 960 flyout fails in the default 1366 x 768 crop, with a message naming `"size": "1920x1080"` and the limit on manifests that also have `window` crops, and fits a 1920 x 1080 output. A flyout a few pixels from, or flush with, the screen's right edge passes, since that edge is the crop's own. A flyout too close to the crop's left edge, a window partly off the screen, and a screen smaller than the output all fail.
- **`window` and `bottom`:** unchanged: a window crop centres on the window and refuses a window without room for its margin; a bottom crop centres on its popup and still needs the margin on the screen's sides.
- **The shared margin test:** only the sides a crop names are checked.
- **Localized entries:** an entry with `{locale}` in `file` and `source` is planned once per listing locale with its own record, from that locale's capture; a missing capture fails rather than falling back to en-US; `{locale}` in only one of the two fails; a full set passes beside the heroes in the same folders; and a PNG in the folder of a dropped or `copyFrom` locale is named.
- **200% captures:** a 200% sidecar gives the same logical crop corner as the 100% one; an odd physical window edge rounds outward; the ImageMagick command crops twice the output size and reduces it with `-scale`, while a 100% capture is not resampled; only 100% and 200% are accepted.
- **Hero entries (D-123):** a hero is never cropped and its capture is not held to the capture rules; every listing locale with its own record needs its PNG at the output size, while a `copyFrom` locale needs none; a hero that is not first, or whose `file` has no `{locale}`, fails.

## What it does not prove

- **A hero's pixels:** `devkit-store-hero --check` compares them with what its inputs draw.

- **Cropping and the PNG checks:** the ImageMagick crop (a 2 x 2 checkerboard reducing to its exact average was checked by hand once, through Docker), the output format, the darkness check and the manifest and caption checks; an app's `render:screenshots` and `check:screenshots` cover those on its real captures.
