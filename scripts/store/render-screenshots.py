#!/usr/bin/env python3
"""Render and check an app's Microsoft Store screenshots (LIST-8, ASSET-9, ASSET-10).

Run from the app's repo root (npm does this):
    python3 external/winui-app-kit/scripts/store/render-screenshots.py           render, then check
    python3 external/winui-app-kit/scripts/store/render-screenshots.py --check   check only (validate)

Reads store-assets/manifest.json:
    {
      "size": "1366x768",                                  optional; the output size, default shown
      "microsoft": [                                       every Store screenshot, in listing order
        { "file": "screenshots/00-home.png",               output, under store-assets/microsoft/
          "source": "raw/microsoft/00-home.png",           the raw window capture, under store-assets/
          "theme": "dark",                                 always "dark": the listing shows dark mode (LIST-8)
          "caption": "...", "alt": "..." }                 the en-US caption, and alt text
      ]
    }

Render: each raw capture (the app's window at 1:1, taken on the test VM) has its 1-pixel window border
shaved (it shows the desktop through), gets a soft shadow, and is centred at 1:1, never scaled, on the
Windows 11 default dark wallpaper, which is scaled to cover the output and centre-cropped. The result is
8-bit RGB with no alpha. The wallpaper is read from the Windows install at render time
(/mnt/c/Windows/Web/Wallpaper/Windows/img19.jpg) or passed with --wallpaper; it is Microsoft's image and
is never committed, and there is no fallback background. A capture too large to sit
inside the output with a margin fails rather than being scaled or cropped. ImageMagick runs in a
disposable container with a memory cap, so the host needs only Docker; files stay owned by the caller.

Check (no Docker): every manifest entry is dark, exists at exactly the output size as 8-bit RGB without
alpha, has a caption and alt text, no stray PNG is in the screenshots folder, there are 1 to 10, and the captions
are the en-US screenshotCaptions in store-assets/microsoft/listings.json, in order, when that file exists.

Standard library only.
"""
import argparse
import json
import os
import struct
import subprocess
import sys
from pathlib import Path

IMAGE = "alpine:3.24.4"
DEFAULT_SIZE = "1366x768"
MARGIN = 24  # minimum wallpaper showing on each side of the window, in pixels
WALLPAPERS = Path("/mnt/c/Windows/Web/Wallpaper/Windows")
DEFAULT_WALLPAPER = WALLPAPERS / "img19.jpg"
MAX_SCREENSHOTS = 10


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


def render(assets: Path, entries: list[dict], size: tuple[int, int], wallpaper: Path) -> None:
    """Frame every raw capture in one container run."""
    width, height = size
    if not wallpaper.is_file():
        fail(f"no wallpaper at {wallpaper}; pass --wallpaper with the Windows 11 dark wallpaper (img19.jpg)")
    commands = []
    for entry in entries:
        if entry.get("theme") != "dark":
            fail(f"{entry.get('file')}: theme {entry.get('theme')!r}; Store screenshots are dark (LIST-8)")
        source = assets / entry["source"]
        if not source.is_file():
            fail(f"{entry['source']}: missing raw capture")

        # The window must sit inside the output with a margin: never scale or crop the app.
        capture_width, capture_height, _, _ = png_header(source)
        if capture_width - 2 > width - 2 * MARGIN or capture_height - 2 > height - 2 * MARGIN:
            fail(f"{entry['source']}: {capture_width} x {capture_height} does not fit {width} x {height} "
                 f"with a {MARGIN} px margin; capture the window smaller")

        commands.append(
            f'magick /wallpaper.jpg -resize {width}x{height}^ -gravity center -extent {width}x{height} '
            f'\\( "{entry["source"]}" -shave 1x1 \\( +clone -background black -shadow 35x14+0+10 \\) +swap '
            f'-background none -layers merge +repage \\) -gravity center -composite '
            f'-alpha off -depth 8 -strip -define png:color-type=2 "microsoft/{entry["file"]}"'
        )

    # The wallpaper is mounted read-only under a fixed name, so its path never reaches the shell.
    mounts = ["-v", f"{wallpaper}:/wallpaper.jpg:ro"]

    folders = sorted({str(Path("microsoft") / Path(entry["file"]).parent) for entry in entries})
    owner = f"{os.getuid()}:{os.getgid()}"
    script = (
        "apk add -q --no-cache imagemagick imagemagick-jpeg >/dev/null && cd /assets && "
        + " && ".join(f'mkdir -p "{folder}"' for folder in folders) + " && "
        + " && ".join(commands)
        + " && " + " && ".join(f'chown -R {owner} "{folder}"' for folder in folders)
    )
    subprocess.run(
        ["docker", "run", "--rm", "--memory", "1g", "--memory-swap", "1g", "-v", f"{assets}:/assets", *mounts,
         IMAGE, "sh", "-c", script],
        check=True,
    )


def check(assets: Path, entries: list[dict], size: tuple[int, int]) -> int:
    """Every rendered screenshot is present, the right size and format, captioned, and nothing is extra."""
    width, height = size
    problems = []
    rendered = set()
    for entry in entries:
        path = assets / "microsoft" / entry["file"]
        rendered.add(path.resolve())
        for field in ("caption", "alt"):
            if not entry.get(field):
                problems.append(f"{entry['file']}: no {field}")
        if entry.get("theme") != "dark":
            problems.append(f"{entry['file']}: theme {entry.get('theme')!r}; Store screenshots are dark (LIST-8)")
        if not path.is_file():
            problems.append(f"{entry['file']}: missing; run the renderer")
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

    for folder in {(assets / "microsoft" / entry["file"]).parent for entry in entries}:
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
        actual = [entry.get("caption", "") for entry in entries]
        if actual != expected:
            problems.append("captions differ from the en-US screenshotCaptions in microsoft/listings.json (same text, same order)")

    for problem in problems:
        print("error: " + problem, file=sys.stderr)
    print(f"check:screenshots: {'failed' if problems else 'passed'} ({len(entries)} screenshots, {width} x {height})")
    return 1 if problems else 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Render and check the Microsoft Store screenshots.")
    parser.add_argument("--check", action="store_true", help="only check the rendered screenshots")
    parser.add_argument("--wallpaper", type=Path, default=DEFAULT_WALLPAPER,
                        help="the Windows 11 dark wallpaper (img19.jpg); default reads it from the Windows install")
    args = parser.parse_args()

    assets = Path.cwd() / "store-assets"
    entries, size = load_manifest(assets)
    if not args.check:
        render(assets, entries, size, args.wallpaper.resolve())
    return check(assets, entries, size)


if __name__ == "__main__":
    sys.exit(main())
