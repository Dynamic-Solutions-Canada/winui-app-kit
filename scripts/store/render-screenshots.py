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
          "crop": "window",                                optional: "window" (default) or "tray"
          "caption": "...", "alt": "..." }                 the en-US caption, and alt text
      ]
    }

Capture (on the test VM, with windows-build-tools' app-test helpers, from the app's repo root): the
whole desktop at 1:1 with the real wallpaper (the Windows 11 dark default, img19.jpg), window effects on
and the app's window focused and centred on the work area. Beside each <name>.png the helper writes
<name>.json, describing the capture:
    {"screen": {"width": 1920, "height": 1080}, "scale": 100,
     "window": {"x": .., "y": .., "width": .., "height": ..},     DWM extended frame bounds, no shadow
     "workArea": {"x": 0, "y": 0, "width": 1920, "height": 1032},  the screen without the taskbar
     "theme": {"apps": "dark", "system": "dark"}, "wallpaper": "img19.jpg", "effects": true,
     "os": "10.0.26200", "capturedUtc": "2026-11-17T23:10:00Z"}

Crop: nothing is composited, shaved or scaled. "window" takes an output-size rectangle centred on the
window, moved only as far as needed to stay inside the work area (so the taskbar stays out); the window
and a margin for its shadow must fit, or the crop fails. "tray" takes the bottom-right output-size
rectangle of the screen, taskbar included, for a tray flyout. The result is written as 8-bit RGB without
alpha and with unfiltered rows. ImageMagick (cropping only) runs in a disposable container with a memory cap, so the host needs only Docker;
files stay owned by the caller.

Check (no Docker) fails when:
  - an entry isn't dark, its capture has no sidecar, or the sidecar says the capture was scaled (scale
    other than 100), taken with apps or the system in light mode, without window effects, on another wallpaper, or at
    a size other than the PNG's own;
  - the window doesn't fit the crop with its margin;
  - a screenshot is missing, isn't exactly the output size, or isn't 8-bit RGB without alpha;
  - a caption or alt text is empty, a PNG in the screenshots folder isn't in the manifest, there are
    fewer than 1 or more than 10, or the captions differ from the en-US screenshotCaptions in
    store-assets/microsoft/listings.json (same text, same order).
The sidecar is evidence of how a capture was made, not proof that its pixels weren't edited; the raw
captures are committed beside the screenshots so that stays reviewable.

Standard library only.
"""
import argparse
import json
import os
import struct
import subprocess
import sys
import zlib
from pathlib import Path

IMAGE = "alpine:3.24.4"
DEFAULT_SIZE = "1366x768"
MARGIN = 24  # desktop kept around the window frame on every side, for its shadow
WALLPAPER = "img19.jpg"
MAX_SCREENSHOTS = 10
CROPS = ("window", "tray")
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


def capture_problems(assets: Path, entry: dict) -> list[str]:
    """What is wrong with an entry's capture and its sidecar, as messages; empty when it is usable."""
    name = entry.get("source", "?")
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
    if sidecar.get("scale") != 100:
        problems.append(f"{name}: captured at {sidecar.get('scale')}% display scale; captures must be 1:1 (100%)")
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


def crop_box(sidecar: dict, crop: str, size: tuple[int, int]) -> tuple[int, int] | str:
    """
    The crop's top-left corner for an output of `size`, or a message saying why it can't be taken.
    "window" centres on the window and moves only as far as needed to stay inside the work area;
    "tray" is the bottom-right corner of the screen.
    """
    out_width, out_height = size
    screen, window, work = sidecar["screen"], sidecar["window"], sidecar["workArea"]

    if crop == "tray":
        if screen["width"] < out_width or screen["height"] < out_height:
            return f"the screen ({screen['width']} x {screen['height']}) is smaller than {out_width} x {out_height}"
        return screen["width"] - out_width, screen["height"] - out_height

    if work["width"] < out_width or work["height"] < out_height:
        return f"the work area ({work['width']} x {work['height']}) is smaller than {out_width} x {out_height}"
    centre_x = window["x"] + window["width"] // 2
    centre_y = window["y"] + window["height"] // 2
    left = min(max(centre_x - out_width // 2, work["x"]), work["x"] + work["width"] - out_width)
    top = min(max(centre_y - out_height // 2, work["y"]), work["y"] + work["height"] - out_height)

    # The window and the margin for its shadow must be inside the crop: never clip the app.
    if (window["x"] - MARGIN < left or window["y"] - MARGIN < top
            or window["x"] + window["width"] + MARGIN > left + out_width
            or window["y"] + window["height"] + MARGIN > top + out_height):
        return (f"the window ({window['width']} x {window['height']}) and a {MARGIN} px margin don't fit a "
                f"{out_width} x {out_height} crop; capture it smaller, centred on the work area")
    return left, top


def plan(assets: Path, entries: list[dict], size: tuple[int, int]) -> tuple[list[tuple[dict, int, int]], list[str]]:
    """Each usable entry with its crop corner, and every problem found with the captures."""
    jobs, problems = [], []
    for entry in entries:
        label = entry.get("file", "?")
        if entry.get("theme") != "dark":
            problems.append(f"{label}: theme {entry.get('theme')!r}; Store screenshots are dark (LIST-8)")
        crop = entry.get("crop", "window")
        if crop not in CROPS:
            problems.append(f"{label}: crop {crop!r} is not window or tray")
            continue
        found = capture_problems(assets, entry)
        if found:
            problems += found
            continue
        sidecar = json.loads((assets / entry["source"]).with_suffix(".json").read_text(encoding="utf-8-sig"))
        corner = crop_box(sidecar, crop, size)
        if isinstance(corner, str):
            problems.append(f"{entry['source']}: {corner}")
            continue
        jobs.append((entry, corner[0], corner[1]))
    return jobs, problems


def render(assets: Path, entries: list[dict], size: tuple[int, int]) -> None:
    """Crop every capture in one container run; stop first if any capture is unusable."""
    jobs, problems = plan(assets, entries, size)
    if problems:
        for problem in problems:
            print("error: " + problem, file=sys.stderr)
        fail("nothing was rendered")

    # ImageMagick crops each capture to raw 8-bit RGB (it ignores PNG filter settings); this script then
    # writes the PNG itself with unfiltered rows, which the check decodes without Docker.
    width, height = size
    scratch = assets / ".render"
    scratch.mkdir(exist_ok=True)
    commands = [
        f'magick "{entry["source"]}" -crop {width}x{height}+{left}+{top} +repage '
        f'-alpha off -depth 8 "RGB:.render/{index}.rgb"'
        for index, (entry, left, top) in enumerate(jobs)
    ]
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
        for index, (entry, _, _) in enumerate(jobs):
            raw_path = scratch / f"{index}.rgb"
            output = assets / "microsoft" / entry["file"]
            output.parent.mkdir(parents=True, exist_ok=True)
            write_rgb_png(output, raw_path.read_bytes(), width, height)
            raw_path.unlink()
    finally:
        for leftover in scratch.glob("*.rgb"):
            leftover.unlink()
        scratch.rmdir()


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


def check(assets: Path, entries: list[dict], size: tuple[int, int]) -> int:
    """Every capture and screenshot is as the Store and LIST-8 require, and nothing is extra."""
    width, height = size
    jobs, problems = plan(assets, entries, size)
    corners = {id(entry): (left, top) for entry, left, top in jobs}
    rendered = set()
    for entry in entries:
        path = assets / "microsoft" / entry.get("file", "")
        rendered.add(path.resolve())
        for field in ("caption", "alt"):
            if not entry.get(field):
                problems.append(f"{entry.get('file')}: no {field}")
        if not path.is_file():
            problems.append(f"{entry.get('file')}: missing; run the renderer")
            continue
        try:
            actual_width, actual_height, depth, colour = png_header(path)
        except ValueError as error:
            problems.append(str(error))
            continue
        if (actual_width, actual_height) != (width, height):
            problems.append(f"{entry['file']}: {actual_width} x {actual_height}, not {width} x {height}")
        if depth != 8 or colour != 2:
            problems.append(f"{entry['file']}: not 8-bit RGB without alpha (bit depth {depth}, colour type {colour})")
            continue
        if (actual_width, actual_height) != (width, height) or id(entry) not in corners:
            continue

        # The app itself must be dark: its own theme setting can be light while Windows is dark.
        rows = read_unfiltered_rgb(path, width, height)
        if isinstance(rows, str):
            problems.append(f"{entry['file']}: {rows}")
            continue
        sidecar = json.loads((assets / entry["source"]).with_suffix(".json").read_text(encoding="utf-8-sig"))
        window = sidecar["window"]
        left, top = corners[id(entry)]
        luma = window_luma(rows, (window["x"] - left, window["y"] - top, window["width"], window["height"]))
        if luma >= DARK_LUMA_LIMIT:
            problems.append(f"{entry['file']}: the window's mean brightness is {luma:.0f} of 255 (dark is under {DARK_LUMA_LIMIT}); is the app set to its light theme?")

    for folder in {(assets / "microsoft" / entry.get("file", "")).parent for entry in entries}:
        for path in folder.glob("*.png"):
            if path.resolve() not in rendered:
                problems.append(f"{path.relative_to(assets).as_posix()}: not in manifest.json")

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
