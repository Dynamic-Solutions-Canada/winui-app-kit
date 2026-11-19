# store screenshots

Checks the crop geometry of `scripts/store/render-screenshots.py`, the Microsoft Store screenshot renderer apps run in `validate` and to crop their captures. `npm test` runs `crops.test.sh`, which runs `test_crops.py` with Python's standard `unittest`; it needs only Python 3.

## What it covers

- **`tray`:** a 420 x 960 flyout fails in the default 1366 x 768 crop, with a message naming `"size": "1920x1080"` and the limit on manifests that also have `window` crops, and fits a 1920 x 1080 output. A flyout a few pixels from, or flush with, the screen's right edge passes, since that edge is the crop's own. A flyout too close to the crop's left edge, a window partly off the screen, and a screen smaller than the output all fail.
- **`window` and `bottom`:** unchanged: a window crop centres on the window and refuses a window without room for its margin; a bottom crop centres on its popup and still needs the margin on the screen's sides.
- **The shared margin test:** only the sides a crop names are checked.

## What it does not prove

- **Cropping and the PNG checks:** the ImageMagick crop, the output format, the darkness check and the manifest and caption checks; an app's `render:screenshots` and `check:screenshots` cover those on its real captures.
