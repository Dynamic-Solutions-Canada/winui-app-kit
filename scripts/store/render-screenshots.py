#!/usr/bin/env python3
"""Crop and check an app's Microsoft Store screenshots from real desktop captures (LIST-8, ASSET-9, ASSET-10).

Run from the app's repo root (npm does this):
    python3 external/winui-app-kit/scripts/store/render-screenshots.py           crop, then check
    python3 external/winui-app-kit/scripts/store/render-screenshots.py --check   check only (validate)

Reads store-assets/manifest.json:
    {
      "size": "1366x768",                                  optional; the output size, default shown
      "microsoft": [                                       every Store screenshot, in listing order
        { "file": "screenshots/00-home-dark.png",          output, under store-assets/microsoft/
          "source": "raw/microsoft/00-home-dark.png",      the desktop capture, under store-assets/
          "theme": "dark",                                 always "dark" (LIST-8)
          "crop": "window",                                optional: "window" (default), "tray" or "bottom"
          "caption": "...", "alt": "..." }                 the en-US caption, and alt text
      ]
    }

Localized screenshots: "file" and "source" may both contain {locale} (never one without the other), as
    { "file": "screenshots/{locale}/01-home-dark.png", "source": "raw/microsoft/{locale}/01-home-dark.png", ... }
and the entry is then cropped once per listing locale with its own record in microsoft/listings.json
(a copyFrom locale uses its target's images), from that locale's capture: Windows, the app and its
sample data in that language. Every such locale needs its capture; there is no fallback to en-US here,
since a missing one would ship English UI in a translated listing. Entries without {locale} are one
image for every locale, as before, and the two kinds can be mixed while an app moves over.

The first entry may instead be a generated hero (D-123, LIST-8): an entry with a "hero" block, whose
"file" contains {locale}. devkit-store-hero draws it, one PNG per listing locale, from its capture
and the listings' heroHeadline, and its own --check compares the pixels. This script never crops a
hero and does not apply the capture rules to its source (devkit checks it; its source may contain {locale}); it checks
only that the hero is first, that every listing locale with its own record has its PNG at the output
size in 8-bit RGB, and the caption and alt text, as for any entry.

Capture (on the test VM, with windows-build-tools' app-test helpers, from the app's repo root): the
whole desktop, at 100% or 200% display scale, with the real wallpaper (the Windows 11 dark default, img19.jpg), window effects on
and the app's window focused and centred on the work area. Beside each <name>.png the helper writes
<name>.json, describing the capture in its own (physical) pixels:
    {"screen": {"width": 1920, "height": 1080}, "scale": 100,
     "window": {"x": .., "y": .., "width": .., "height": ..},     DWM extended frame bounds, no shadow
     "workArea": {"x": 0, "y": 0, "width": 1920, "height": 1032},  the screen without the taskbar
     "theme": {"apps": "dark", "system": "dark"}, "wallpaper": "img19.jpg", "effects": true,
     "os": "10.0.26200", "capturedUtc": "2026-11-17T23:10:00Z"}

Crop: every rule below is in logical pixels (the sidecar's pixels divided by scale / 100), so a 200%
capture of a 1920 x 1080 screen crops exactly like a 100% one. Nothing is composited or shaved; a 200%
capture's crop, twice the output size, is reduced to the output size by averaging each 2 x 2 block, so
the screenshot keeps its layout and gains sharper text. "window" takes an output-size rectangle centred on the
window, moved only as far as needed to stay inside the work area (so the taskbar stays out); the window
and a margin for its shadow must fit, or the crop fails. "tray" takes the bottom-right output-size
rectangle of the screen, taskbar included, for a tray flyout; the window must be on screen, with its
margin on the left and top (the crop's right and bottom are the screen's edges), or the crop fails. "bottom" takes the bottom strip of the
screen, taskbar included, centred on the window and moved only as far as needed to stay on the screen,
for a popup placed above the taskbar; the window and its margin must fit it, as for "window". The
result is written as 8-bit RGB without alpha and with unfiltered rows. ImageMagick (cropping only) runs in a disposable container with a memory cap, so the host needs only Docker;
files stay owned by the caller.

Check (no Docker) fails when:
  - an entry isn't dark, its capture has no sidecar, or the sidecar says the capture was taken at a
    display scale other than 100% or 200%, with apps or the system in light mode, without window effects, on another wallpaper, or at
    a size other than the PNG's own;
  - the window doesn't fit the crop with its margin (for "tray", on its left and top, and on screen);
  - a screenshot is missing, isn't exactly the output size, or isn't 8-bit RGB without alpha;
  - a localized entry has {locale} in only one of "file" and "source", or a listing locale has no capture;
  - a caption or alt text is empty, a PNG in the screenshots folder (or in a localized entry's locale
    folders, including one for a locale the listing no longer has) isn't in the manifest, there are
    fewer than 1 or more than 10, or the captions differ from the en-US screenshotCaptions in
    store-assets/microsoft/listings.json (same text, same order).
The sidecar is evidence of how a capture was made, not proof that its pixels weren't edited; the raw
captures are committed beside the screenshots so that stays reviewable.

Standard library only.
"""
import argparse
import json
import math
import os
import struct
import subprocess
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path

IMAGE = "alpine:3.24.4"
DEFAULT_SIZE = "1366x768"
MARGIN = 24  # desktop kept around the window frame on every side, for its shadow
WALLPAPER = "img19.jpg"
MAX_SCREENSHOTS = 10
LOCALE_TOKEN = "{locale}"
CROPS = ("window", "tray", "bottom")
# Display scales a capture may be taken at: 1:1, or 2:1 reduced to the output size by 2 x 2 averaging.
SCALES = (100, 200)
# Mean luma (0-255, Rec. 709) of the window in a screenshot must be below this, so an app set to its own
# light theme fails even on a dark Windows. Calibrated on little-launcher's real dark captures (windows
# and tray flyouts measured 40-58) and the same windows inverted to light (197-215).
DARK_LUMA_LIMIT = 100


def fail(message: str) -> None:
    """Print an error and stop with a failing exit code."""
    print(f"render-screenshots: {message}", file=sys.stderr)
    sys.exit(1)


def png_header(path: Path) -> tuple[int, int, int, int]:
    """(width, height, bit depth, colour type) from a PNG's IHDR; raises if the file is not a PNG."""
    with path.open("rb") as handle:
        header = handle.read(26)
    if header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"{path.name} is not a PNG")
    width, height = struct.unpack(">II", header[16:24])
    return width, height, header[24], header[25]


def load_manifest(assets: Path) -> tuple[list[dict], tuple[int, int]]:
    """The Microsoft Store entries in listing order, and the output size."""
    path = assets / "manifest.json"
    if not path.is_file():
        fail(f"no {path}; run from the app's repo root")
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    try:
        width, height = (int(part) for part in manifest.get("size", DEFAULT_SIZE).lower().split("x"))
    except ValueError:
        fail(f"manifest size {manifest.get('size')!r} is not WIDTHxHEIGHT")
    return manifest.get("microsoft", []), (width, height)


def capture_problems(assets: Path, name: str) -> list[str]:
    """What is wrong with a capture (`name`, under store-assets/) and its sidecar, as messages; empty when usable."""
    source = assets / name
    if not source.is_file():
        return [f"{name}: missing capture"]

    sidecar_path = source.with_suffix(".json")
    if not sidecar_path.is_file():
        return [f"{name}: no {sidecar_path.name} beside it; capture with the windows-build-tools app-test helper"]
    try:
        sidecar = json.loads(sidecar_path.read_text(encoding="utf-8-sig"))
        screen, window, work = sidecar["screen"], sidecar["window"], sidecar["workArea"]
    except (ValueError, KeyError) as error:
        return [f"{sidecar_path.name}: unreadable capture description ({error})"]

    problems = []
    width, height, _, _ = png_header(source)
    if (width, height) != (screen.get("width"), screen.get("height")):
        problems.append(f"{name}: {width} x {height}, but its sidecar says the screen was {screen.get('width')} x {screen.get('height')}")
    if sidecar.get("scale") not in SCALES:
        problems.append(f"{name}: captured at {sidecar.get('scale')}% display scale; captures must be at 100% or 200%")
    theme = sidecar.get("theme") or {}
    for part in ("apps", "system"):
        if theme.get(part) != "dark":
            problems.append(f"{name}: captured with {part} in {theme.get(part)!r} mode; Store screenshots are fully dark (LIST-8)")
    if sidecar.get("effects") is not True:
        problems.append(f"{name}: captured without window effects (rounded corners, Mica)")
    if sidecar.get("wallpaper") != WALLPAPER:
        problems.append(f"{name}: captured on wallpaper {sidecar.get('wallpaper')!r}, not {WALLPAPER}")
    inside = (window.get("x", -1) >= 0 and window.get("y", -1) >= 0
              and window.get("x", 0) + window.get("width", 0) <= screen.get("width", 0)
              and window.get("y", 0) + window.get("height", 0) <= screen.get("height", 0))
    if not inside:
        problems.append(f"{name}: the window is not wholly on the screen")
    return problems


def logical(sidecar: dict) -> tuple[dict, dict, dict]:
    """
    The sidecar's screen, window and work area in logical pixels: its physical pixels divided by
    scale / 100 (a sidecar without a scale is 100%). At 200% a rectangle can start or end on an odd
    physical pixel, so the window is rounded outward (the crop must still hold all of it) and the screen
    and work area inward (the crop must stay inside them).
    """
    factor = sidecar.get("scale", 100) / 100
    screen, window, work = sidecar["screen"], sidecar["window"], sidecar["workArea"]

    def outward(rect: dict) -> dict:
        left, top = math.floor(rect["x"] / factor), math.floor(rect["y"] / factor)
        right = math.ceil((rect["x"] + rect["width"]) / factor)
        bottom = math.ceil((rect["y"] + rect["height"]) / factor)
        return {"x": left, "y": top, "width": right - left, "height": bottom - top}

    def inward(rect: dict) -> dict:
        left, top = math.ceil(rect.get("x", 0) / factor), math.ceil(rect.get("y", 0) / factor)
        right = math.floor((rect.get("x", 0) + rect["width"]) / factor)
        bottom = math.floor((rect.get("y", 0) + rect["height"]) / factor)
        return {"x": left, "y": top, "width": right - left, "height": bottom - top}

    logical_screen = inward(screen)
    return {"width": logical_screen["width"], "height": logical_screen["height"]}, outward(window), inward(work)


def crop_box(sidecar: dict, crop: str, size: tuple[int, int]) -> tuple[int, int] | str:
    """
    The crop's top-left corner, in logical pixels, for an output of `size`, or a message saying why it
    can't be taken. "window" centres on the window and moves only as far as needed to stay inside the work
    area; "tray" is the bottom-right corner of the screen; "bottom" is the bottom strip of the screen,
    taskbar included, centred on the window and moved only as far as needed to stay on the screen. Every
    rule is in logical pixels (see `logical`), so a 200% capture crops like a 100% one.
    """
    out_width, out_height = size
    screen, window, work = logical(sidecar)

    if crop == "tray":
        return tray_crop(screen, window, size)

    if crop == "bottom":
        return bottom_crop(screen, window, size)

    if work["width"] < out_width or work["height"] < out_height:
        return f"the work area ({work['width']} x {work['height']}) is smaller than {out_width} x {out_height}"
    centre_x = window["x"] + window["width"] // 2
    centre_y = window["y"] + window["height"] // 2
    left = min(max(centre_x - out_width // 2, work["x"]), work["x"] + work["width"] - out_width)
    top = min(max(centre_y - out_height // 2, work["y"]), work["y"] + work["height"] - out_height)

    # The window and the margin for its shadow must be inside the crop: never clip the app.
    if not margin_fits(window, (left, top), size, ("left", "top", "right", "bottom")):
        return (f"the window ({window['width']} x {window['height']}) and a {MARGIN} px margin don't fit a "
                f"{out_width} x {out_height} crop; capture it smaller, centred on the work area")
    return left, top


def margin_fits(window: dict, corner: tuple[int, int], size: tuple[int, int], sides: tuple[str, ...]) -> bool:
    """
    Whether the window, with MARGIN px of desktop for its shadow on each of `sides` ("left", "top",
    "right", "bottom"), lies inside the crop whose top-left is `corner` and whose size is `size`. A side
    left out is not checked here; its caller decides what that edge needs instead.
    """
    left, top = corner
    out_width, out_height = size
    limits = {
        "left": window["x"] - MARGIN >= left,
        "top": window["y"] - MARGIN >= top,
        "right": window["x"] + window["width"] + MARGIN <= left + out_width,
        "bottom": window["y"] + window["height"] + MARGIN <= top + out_height,
    }
    return all(limits[side] for side in sides)


def tray_crop(screen: dict, window: dict, size: tuple[int, int]) -> tuple[int, int] | str:
    """
    The "tray" crop's corner: the bottom-right output-size rectangle of the screen, taskbar included.
    A tray flyout sits against the screen's corner by design, so the crop's right and bottom edges are
    the screen's own: there the window only has to be on screen, since no output size can add desktop
    beyond the screen. On the inner sides, left and top, the window and its margin must fit, or a tall
    or wide flyout would lose its header (a 420 x 960 flyout in a 1366 x 768 crop did).
    """
    out_width, out_height = size
    if screen["width"] < out_width or screen["height"] < out_height:
        return f"the screen ({screen['width']} x {screen['height']}) is smaller than {out_width} x {out_height}"
    left, top = screen["width"] - out_width, screen["height"] - out_height

    if (window["x"] < 0 or window["y"] < 0
            or window["x"] + window["width"] > screen["width"]
            or window["y"] + window["height"] > screen["height"]):
        return (f"the window ({window['width']} x {window['height']} at {window['x']}, {window['y']}) is not "
                f"entirely on the screen; capture it where the app places it")
    if not margin_fits(window, (left, top), size, ("left", "top")):
        return (f"the window ({window['width']} x {window['height']}) and a {MARGIN} px margin don't fit the "
                f"bottom-right {out_width} x {out_height} of the screen; set \"size\" to \"1920x1080\" in "
                f"manifest.json (for a 1920 x 1080 capture), or capture the flyout smaller. \"size\" applies "
                f"to every screenshot, and a \"window\" crop can't be as large as the screen, so a manifest "
                f"that also has \"window\" crops needs the smaller flyout")
    return left, top


def bottom_crop(screen: dict, window: dict, size: tuple[int, int]) -> tuple[int, int] | str:
    """
    The "bottom" crop's corner: the screen's bottom strip, taskbar included, centred on the window and
    clamped to the screen (not the work area). The window and its margin must fit inside it sideways and
    not stick out above it; the bottom isn't held to the work area, since the taskbar is meant to show.
    """
    out_width, out_height = size
    if screen["width"] < out_width or screen["height"] < out_height:
        return f"the screen ({screen['width']} x {screen['height']}) is smaller than {out_width} x {out_height}"

    centre_x = window["x"] + window["width"] // 2
    left = min(max(centre_x - out_width // 2, 0), screen["width"] - out_width)
    top = screen["height"] - out_height

    if not margin_fits(window, (left, top), size, ("left", "right", "top")):
        return (f"the window ({window['width']} x {window['height']}) and a {MARGIN} px margin don't fit the "
                f"bottom {out_width} x {out_height} of the screen; capture it smaller, nearer the bottom and "
                f"at least {MARGIN} px from the screen's sides")
    return left, top


def is_hero(entry: dict) -> bool:
    """True for the generated first screenshot, which devkit-store-hero draws (D-123)."""
    return "hero" in entry


def hero_problems(entries: list[dict]) -> list[str]:
    """A hero must be the first entry and the only one, with a per-locale file path."""
    problems = []
    for index, entry in enumerate(entries):
        if not is_hero(entry):
            continue
        label = entry.get("file", "?")
        if index != 0:
            problems.append(f"{label}: a hero must be the first screenshot (LIST-8)")
        if LOCALE_TOKEN not in str(entry.get("file", "")):
            problems.append(f"{label}: a hero's file must contain {LOCALE_TOKEN}, one image per listing locale")
    return problems


def listing_locales(assets: Path) -> list[str]:
    """
    The listing locales with their own record, which each get a hero and their own copy of every
    localized screenshot; en-US without a listing. A copyFrom locale uses its target's images.
    """
    listings = assets / "microsoft" / "listings.json"
    if not listings.is_file():
        return ["en-US"]
    locales = json.loads(listings.read_text(encoding="utf-8-sig")).get("locales", {})
    return [locale for locale, record in locales.items() if not (isinstance(record, dict) and "copyFrom" in record)]


def is_localized(entry: dict) -> bool:
    """True for a captured entry with one image per listing locale ({locale} in its file and source)."""
    return not is_hero(entry) and LOCALE_TOKEN in str(entry.get("file", ""))


def localized_problems(entries: list[dict]) -> list[str]:
    """A captured entry localizes both its file and its source, or neither."""
    problems = []
    for entry in entries:
        if is_hero(entry):
            continue
        in_file = LOCALE_TOKEN in str(entry.get("file", ""))
        in_source = LOCALE_TOKEN in str(entry.get("source", ""))
        if in_file != in_source:
            problems.append(f"{entry.get('file', '?')}: {LOCALE_TOKEN} must be in both file and source, or in neither")
    return problems


@dataclass
class Shot:
    """One screenshot to crop: a captured entry, for one locale when it is localized."""
    entry: dict
    locale: str | None  # None for an entry shared by every locale
    source: str  # the capture, under store-assets/
    file: str  # the screenshot, under store-assets/microsoft/
    left: int = 0  # the crop's corner, in logical pixels, once planned
    top: int = 0
    scale: int = 100  # the capture's display scale


def shots(entry: dict, locales: list[str]) -> list[Shot]:
    """The screenshots a captured entry gives: one, or one per listing locale when it is localized."""
    source, file = str(entry.get("source", "?")), str(entry.get("file", ""))
    if not is_localized(entry):
        return [Shot(entry, None, source, file)]
    return [Shot(entry, locale, source.replace(LOCALE_TOKEN, locale), file.replace(LOCALE_TOKEN, locale))
            for locale in locales]


def plan(assets: Path, entries: list[dict], size: tuple[int, int]) -> tuple[list[Shot], list[str]]:
    """Each usable screenshot with its crop corner, and every problem found with the captures."""
    jobs, problems = [], hero_problems(entries) + localized_problems(entries)
    locales = listing_locales(assets)
    for entry in entries:
        label = entry.get("file", "?")
        if entry.get("theme") != "dark":
            problems.append(f"{label}: theme {entry.get('theme')!r}; Store screenshots are dark (LIST-8)")
        # devkit-store-hero draws the hero and checks its capture.
        if is_hero(entry):
            continue
        crop = entry.get("crop", "window")
        if crop not in CROPS:
            problems.append(f"{label}: crop {crop!r} is not window, tray or bottom")
            continue
        for shot in shots(entry, locales):
            found = capture_problems(assets, shot.source)
            if found:
                problems += found
                continue
            sidecar = json.loads((assets / shot.source).with_suffix(".json").read_text(encoding="utf-8-sig"))
            corner = crop_box(sidecar, crop, size)
            if isinstance(corner, str):
                problems.append(f"{shot.source}: {corner}")
                continue
            shot.left, shot.top = corner
            shot.scale = sidecar.get("scale", 100)
            jobs.append(shot)
    return jobs, problems


def render(assets: Path, entries: list[dict], size: tuple[int, int]) -> None:
    """Crop every capture in one container run; stop first if any capture is unusable."""
    jobs, problems = plan(assets, entries, size)
    if problems:
        for problem in problems:
            print("error: " + problem, file=sys.stderr)
        fail("nothing was rendered")

    if not jobs:
        print("render-screenshots: no captured screenshots to crop")
        return

    # ImageMagick crops each capture to raw 8-bit RGB (it ignores PNG filter settings); this script then
    # writes the PNG itself with unfiltered rows, which the check decodes without Docker.
    width, height = size
    scratch = assets / ".render"
    scratch.mkdir(exist_ok=True)
    commands = [magick_command(shot, size, f".render/{index}.rgb") for index, shot in enumerate(jobs)]
    owner = f"{os.getuid()}:{os.getgid()}"
    script = (
        "apk add -q --no-cache imagemagick >/dev/null && cd /assets && "
        + " && ".join(commands)
        + f" && chown -R {owner} .render"
    )
    try:
        subprocess.run(
            ["docker", "run", "--rm", "--memory", "1g", "--memory-swap", "1g", "-v", f"{assets}:/assets",
             IMAGE, "sh", "-c", script],
            check=True,
        )
        for index, shot in enumerate(jobs):
            raw_path = scratch / f"{index}.rgb"
            output = assets / "microsoft" / shot.file
            output.parent.mkdir(parents=True, exist_ok=True)
            write_rgb_png(output, raw_path.read_bytes(), width, height)
            raw_path.unlink()
    finally:
        for leftover in scratch.glob("*.rgb"):
            leftover.unlink()
        scratch.rmdir()


def magick_command(shot: Shot, size: tuple[int, int], raw: str) -> str:
    """
    The ImageMagick command that crops one screenshot to raw RGB at `raw`. The crop is the logical
    rectangle in the capture's pixels; a 200% capture's is then reduced to the output size with -scale,
    which averages each 2 x 2 block exactly. A 100% capture is not resampled at all, so its screenshot is
    byte-for-byte what it was before 200% captures were accepted.
    """
    width, height = size
    factor = shot.scale // 100
    reduce = f"-scale {width}x{height}! " if factor != 1 else ""
    return (f'magick "{shot.source}" -crop {width * factor}x{height * factor}+{shot.left * factor}+{shot.top * factor} '
            f'+repage {reduce}-alpha off -depth 8 "RGB:{raw}"')


def write_rgb_png(path: Path, pixels: bytes, width: int, height: int) -> None:
    """Write 8-bit RGB pixels as a PNG with every row unfiltered (filter type 0) and no other chunks."""
    if len(pixels) != width * height * 3:
        fail(f"{path.name}: the crop came back {len(pixels)} bytes, not {width} x {height} RGB")
    stride = width * 3
    rows = b"".join(b"\x00" + pixels[y * stride:(y + 1) * stride] for y in range(height))

    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def read_unfiltered_rgb(path: Path, width: int, height: int) -> list[bytes] | str:
    """
    The rows of an 8-bit RGB PNG the renderer wrote with no row filters (see write_rgb_png), or a message.
    The renderer always writes them that way, so a stdlib decode is enough; a file with filtered rows
    was not written by this renderer.
    """
    data = path.read_bytes()
    position, compressed = 8, bytearray()
    while position < len(data):
        length = struct.unpack(">I", data[position:position + 4])[0]
        kind = data[position + 4:position + 8]
        if kind == b"IDAT":
            compressed += data[position + 8:position + 8 + length]
        position += 12 + length
    raw = zlib.decompress(bytes(compressed))
    stride = width * 3 + 1
    if len(raw) != stride * height:
        return "image data does not match its size"
    rows = []
    for y in range(height):
        row = raw[y * stride:(y + 1) * stride]
        if row[0] != 0:
            return "not written by the kit's renderer (its rows are filtered); render it again"
        rows.append(row[1:])
    return rows


def window_luma(rows: list[bytes], box: tuple[int, int, int, int]) -> float:
    """Mean Rec. 709 luma (0-255) of the rectangle (left, top, width, height) in RGB rows."""
    left, top, width, height = box
    red = green = blue = 0
    for row in rows[top:top + height]:
        segment = row[left * 3:(left + width) * 3]
        red += sum(segment[0::3])
        green += sum(segment[1::3])
        blue += sum(segment[2::3])
    count = width * height
    return (0.2126 * red + 0.7152 * green + 0.0722 * blue) / count


def png_format_problem(path: Path, label: str, size: tuple[int, int]) -> str | None:
    """Why a screenshot is not a PNG of the output size in 8-bit RGB without alpha, or None."""
    width, height = size
    try:
        actual_width, actual_height, depth, colour = png_header(path)
    except ValueError as error:
        return str(error)
    if (actual_width, actual_height) != (width, height):
        return f"{label}: {actual_width} x {actual_height}, not {width} x {height}"
    if depth != 8 or colour != 2:
        return f"{label}: not 8-bit RGB without alpha (bit depth {depth}, colour type {colour})"
    return None


def hero_file_problems(assets: Path, entry: dict, size: tuple[int, int]) -> list[str]:
    """Every listing locale's hero exists in the listing's format; its pixels are devkit's to check."""
    problems = []
    for locale in listing_locales(assets):
        label = entry.get("file", "").replace(LOCALE_TOKEN, locale)
        path = assets / "microsoft" / label
        if not path.is_file():
            problems.append(f"{label}: missing; run devkit-store-hero")
            continue
        format_problem = png_format_problem(path, label, size)
        if format_problem:
            problems.append(format_problem)
    return problems


def shot_problems(assets: Path, shot: Shot, planned: Shot | None, size: tuple[int, int]) -> list[str]:
    """
    What is wrong with one cropped screenshot: missing, in another format, or showing a bright window.
    `planned` is the same screenshot with its crop corner when its capture was usable, else None.
    """
    width, height = size
    path = assets / "microsoft" / shot.file
    if not path.is_file():
        return [f"{shot.file}: missing; run the renderer"]
    format_problem = png_format_problem(path, shot.file, size)
    if format_problem:
        return [format_problem]
    if planned is None:
        return []

    # The app itself must be dark: its own theme setting can be light while Windows is dark.
    rows = read_unfiltered_rgb(path, width, height)
    if isinstance(rows, str):
        return [f"{shot.file}: {rows}"]
    sidecar = json.loads((assets / shot.source).with_suffix(".json").read_text(encoding="utf-8-sig"))
    _, window, _ = logical(sidecar)
    luma = window_luma(rows, (window["x"] - planned.left, window["y"] - planned.top, window["width"], window["height"]))
    if luma >= DARK_LUMA_LIMIT:
        return [f"{shot.file}: the window's mean brightness is {luma:.0f} of 255 (dark is under {DARK_LUMA_LIMIT}); is the app set to its light theme?"]
    return []


def extra_files(assets: Path, entries: list[dict], expected: set[Path]) -> list[str]:
    """
    PNGs beside the screenshots that the manifest doesn't list: in each captured entry's folder, and, for
    a localized entry whose {locale} is a folder, in the folder of any locale the listing doesn't give its
    own record (dropped, or now a copyFrom). Heroes count as listed; a hero-only folder is devkit's to check.
    """
    folders = set()
    for entry in entries:
        if is_hero(entry):
            continue
        file = str(entry.get("file", ""))
        if not is_localized(entry):
            folders.add((assets / "microsoft" / file).parent)
            continue
        segments = Path(file).parts
        if LOCALE_TOKEN not in segments:
            folders.update((assets / "microsoft" / path).parent for path in expected_for(entry, assets))
            continue
        # Every folder in the {locale} position, configured or not, so a dropped locale's images are named.
        parent = assets / "microsoft" / Path(*segments[:segments.index(LOCALE_TOKEN)])
        rest = segments[segments.index(LOCALE_TOKEN) + 1:]
        if parent.is_dir():
            folders.update((child / Path(*rest)).parent for child in parent.iterdir() if child.is_dir())

    problems = []
    for folder in sorted(folders):
        for path in sorted(folder.glob("*.png")):
            if path.resolve() not in expected:
                problems.append(f"{path.relative_to(assets).as_posix()}: not in manifest.json")
    return problems


def expected_for(entry: dict, assets: Path) -> list[str]:
    """Every screenshot file an entry gives, under store-assets/microsoft/: one, or one per listing locale."""
    file = str(entry.get("file", ""))
    if is_hero(entry) or is_localized(entry):
        return [file.replace(LOCALE_TOKEN, locale) for locale in listing_locales(assets)]
    return [file]


def check(assets: Path, entries: list[dict], size: tuple[int, int]) -> int:
    """Every capture and screenshot is as the Store and LIST-8 require, and nothing is extra."""
    width, height = size
    jobs, problems = plan(assets, entries, size)
    planned = {(id(shot.entry), shot.locale): shot for shot in jobs}
    locales = listing_locales(assets)
    for entry in entries:
        for field in ("caption", "alt"):
            if not entry.get(field):
                problems.append(f"{entry.get('file')}: no {field}")
        if is_hero(entry):
            problems += hero_file_problems(assets, entry, size)
            continue
        for shot in shots(entry, locales):
            problems += shot_problems(assets, shot, planned.get((id(entry), shot.locale)), size)

    expected = {(assets / "microsoft" / file).resolve() for entry in entries for file in expected_for(entry, assets)}
    problems += extra_files(assets, entries, expected)

    if not 1 <= len(entries) <= MAX_SCREENSHOTS:
        problems.append(f"{len(entries)} screenshots; the Store takes 1 to {MAX_SCREENSHOTS}")

    # The manifest's captions are the en-US listing's, in the same order (LIST-8).
    listings = assets / "microsoft" / "listings.json"
    if listings.is_file():
        english = json.loads(listings.read_text(encoding="utf-8-sig")).get("locales", {}).get("en-US", {})
        expected = english.get("screenshotCaptions") or []
        if [entry.get("caption", "") for entry in entries] != expected:
            problems.append("captions differ from the en-US screenshotCaptions in microsoft/listings.json (same text, same order)")

    for problem in problems:
        print("error: " + problem, file=sys.stderr)
    print(f"check:screenshots: {'failed' if problems else 'passed'} ({len(entries)} screenshots, {width} x {height})")
    return 1 if problems else 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Crop and check the Microsoft Store screenshots.")
    parser.add_argument("--check", action="store_true", help="only check the captures and screenshots")
    args = parser.parse_args()

    assets = Path.cwd() / "store-assets"
    entries, size = load_manifest(assets)
    if not args.check:
        render(assets, entries, size)
    return check(assets, entries, size)


if __name__ == "__main__":
    sys.exit(main())
