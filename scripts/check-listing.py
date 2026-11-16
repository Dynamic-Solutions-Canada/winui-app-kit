#!/usr/bin/env python3
"""The Microsoft Store listing check every Windows app runs as `check:listing` (LIST-5, LIST-10, LIST-12).

Run from the app's repo root (npm does this):
    python3 external/winui-app-kit/scripts/check-listing.py

It reads the `listing` block of the app's windows-build.json:
    "listing": {
      "product_name": "My App",                              required: the title in every locale (BRAND-1)
      "listing_path": "store-assets/microsoft/listings.json", default shown
      "catalog_dir": "MyApp/Localization/Catalogs",           default: localization.catalog_dir; none = English-only
      "notes_dir": "release-notes",                           default shown
      "readme": "README.md",                                  default shown
      "retired_names": ["Old Name"]                           names that must not appear in listing text
    }
and the version from version_file (default Directory.Build.props).

Listing (LIST-3, LIST-5, LIST-7, LIST-12):
  - schemaVersion 1. A localized app (one with catalogs) lists the family's nine identities from
    build/languages.json, in order; the identities that reuse another catalog are exactly
    {"copyFrom": "<that catalog>"}, and the full records are exactly the app's wording catalogs. An
    English-only app lists en-US alone (LOC-3).
  - Every full record has exactly title, shortDescription, fullDescription, features, keywords and
    screenshotCaptions: no releaseNotes (LIST-10). Every field is within the Microsoft Store's limits,
    including 21 keyword words combined; over-length text fails and nothing is truncated.
  - Every locale has the same number of screenshot captions. No text has an em dash or a retired name.
  - The title is the product name, and the README's header tagline (the first paragraph after its <h1>,
    tags removed) is the first sentence of the en-US shortDescription, word for word.

Release notes (REL-6, LIST-10), for the current <Version>:
  - <version>.md, plus <version>.<catalog>.md for each other wording catalog, exist, are not empty,
    have no heading, fit the 1,500-character limit and have no em dash. Identities that reuse a
    catalog have no file of their own.

Standard library only.
"""
import json
import re
import sys
from pathlib import Path

KIT_ROOT = Path(__file__).resolve().parent.parent
LANGUAGES = KIT_ROOT / "build" / "languages.json"

# Microsoft Store limits (LIST-5). When the store changes one, change it here and in LIST-5 together.
FIELDS = ["title", "shortDescription", "fullDescription", "features", "keywords", "screenshotCaptions"]
SHORT_MAX = 270
FULL_MAX = 4000
FEATURES_MAX, FEATURE_MAX = 20, 200
KEYWORDS_MAX, KEYWORD_MAX, KEYWORD_WORDS_MAX = 7, 40, 21
CAPTIONS_MAX, CAPTION_MAX = 10, 200
NOTES_MAX = 1500

EM_DASH = "\u2014"


def load_json(path: Path):
    with path.open(encoding="utf-8-sig") as f:
        return json.load(f)


class Checker:
    """Collects problems for one app's listing and release notes."""

    def __init__(self, product_name: str, retired_names: list[str]):
        self.problems: list[str] = []
        self.product_name = product_name
        self.retired_names = retired_names

    def text(self, where: str, value, limit: int) -> None:
        """A text field: present, within its limit, no em dash, no retired name."""
        if not isinstance(value, str) or not value.strip():
            self.problems.append(f"{where}: empty")
            return
        if len(value) > limit:
            self.problems.append(f"{where}: {len(value)} characters, over the {limit} limit")
        if EM_DASH in value:
            self.problems.append(f"{where}: contains an em dash")
        # Case-sensitive: a lower-case search keyword ("immich drive") is a search term, not the name.
        for name in self.retired_names:
            if name in value:
                self.problems.append(f"{where}: uses the retired name {name!r}; write {self.product_name!r}")

    def items(self, where: str, value, count_max: int, item_max: int) -> None:
        """A list field: a non-empty list, not too long, every item a valid text."""
        if not isinstance(value, list) or not value:
            self.problems.append(f"{where}: must be a non-empty list")
            return
        if len(value) > count_max:
            self.problems.append(f"{where}: {len(value)} items, over the {count_max} limit")
        for index, item in enumerate(value, 1):
            self.text(f"{where} {index}", item, item_max)

    def record(self, locale: str, record: dict) -> None:
        """One full locale record: exactly the fields, each within the store's limits."""
        missing = [f for f in FIELDS if f not in record]
        extra = [f for f in record if f not in FIELDS]
        if missing:
            self.problems.append(f"{locale}: missing {', '.join(missing)}")
        if extra:
            self.problems.append(f"{locale}: unexpected {', '.join(extra)} (release notes live in the release-notes folder, LIST-10)")

        if record.get("title") != self.product_name:
            self.problems.append(f"{locale}.title: must be {self.product_name!r}, found {record.get('title')!r}")
        self.text(f"{locale}.shortDescription", record.get("shortDescription"), SHORT_MAX)
        self.text(f"{locale}.fullDescription", record.get("fullDescription"), FULL_MAX)
        self.items(f"{locale}.features", record.get("features"), FEATURES_MAX, FEATURE_MAX)
        self.items(f"{locale}.keywords", record.get("keywords"), KEYWORDS_MAX, KEYWORD_MAX)
        self.items(f"{locale}.screenshotCaptions", record.get("screenshotCaptions"), CAPTIONS_MAX, CAPTION_MAX)

        keywords = record.get("keywords")
        if isinstance(keywords, list):
            words = sum(len(str(keyword).split()) for keyword in keywords)
            if words > KEYWORD_WORDS_MAX:
                self.problems.append(f"{locale}.keywords: {words} words combined, over the {KEYWORD_WORDS_MAX} limit")


def readme_tagline(readme: str) -> str | None:
    """The README header tagline: the first paragraph after the <h1>, tags removed, spaces collapsed."""
    match = re.search(r"</h1>\s*<p[^>]*>(.*?)</p>", readme, re.S)
    if not match:
        return None
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", "", match.group(1))).strip()


def first_sentence(text: str) -> str:
    match = re.match(r".*?[.!?](?=\s|$)", text, re.S)
    return (match.group(0) if match else text).strip()


def check_listing(checker: Checker, listing_path: Path, wording: list[str] | None, readme_path: Path) -> None:
    """listings.json against the family's locales, the store limits and the README."""
    data = load_json(listing_path)
    if data.get("schemaVersion") != 1:
        checker.problems.append("listings.json: schemaVersion must be 1")
    locales = data.get("locales", {})

    family = load_json(LANGUAGES)
    if wording is None:
        # English-only app (LOC-3): en-US alone.
        expected_order = ["en-US"]
        aliases = {}
    else:
        expected_order = [entry["identity"] for entry in family["languages"]]
        aliases = {e["identity"]: e["catalog"] for e in family["languages"] if e["identity"] != e["catalog"]}
    if list(locales) != expected_order:
        checker.problems.append(f"listings.json: locales must be {expected_order} in that order, found {list(locales)}")

    for alias, target in aliases.items():
        if locales.get(alias) != {"copyFrom": target}:
            checker.problems.append(f"{alias}: must be {{\"copyFrom\": \"{target}\"}}")

    full = [locale for locale, record in locales.items() if isinstance(record, dict) and "copyFrom" not in record]
    for locale in full:
        checker.record(locale, locales[locale])
    if wording is not None and sorted(full) != sorted(wording):
        checker.problems.append(f"listings.json: full records {sorted(full)} differ from the app's catalogs {sorted(wording)} (LIST-7)")

    counts = {locale: len(locales[locale].get("screenshotCaptions") or []) for locale in full}
    if len(set(counts.values())) > 1:
        checker.problems.append(f"screenshotCaptions: counts differ between locales {counts}")

    # LIST-12: the README tagline is the en-US shortDescription's first sentence.
    expected = first_sentence(locales.get("en-US", {}).get("shortDescription", "") or "")
    tagline = readme_tagline(readme_path.read_text(encoding="utf-8-sig")) if readme_path.exists() else None
    if tagline != expected:
        checker.problems.append(f"README tagline {tagline!r} must be the first en-US shortDescription sentence {expected!r}")


def check_notes(checker: Checker, notes_dir: Path, version: str, wording: list[str] | None) -> None:
    """REL-6: notes for the current version, one per wording catalog, fit for the store."""
    catalogs = wording or ["en-US"]
    expected = {f"{version}.md"} | {f"{version}.{c}.md" for c in catalogs if c != "en-US"}
    for name in sorted(expected):
        path = notes_dir / name
        if not path.is_file():
            checker.problems.append(f"{notes_dir.name}/{name}: missing (notes for {version})")
            continue
        text = path.read_text(encoding="utf-8-sig").strip()
        checker.text(f"{notes_dir.name}/{name}", text, NOTES_MAX)
        if any(line.lstrip().startswith("#") for line in text.splitlines()):
            checker.problems.append(f"{notes_dir.name}/{name}: has a heading; CI adds it")

    family = load_json(LANGUAGES)
    for entry in family["languages"]:
        if entry["identity"] != entry["catalog"]:
            stray = notes_dir / f"{version}.{entry['identity']}.md"
            if stray.exists():
                checker.problems.append(f"{notes_dir.name}/{stray.name}: {entry['identity']} reuses {entry['catalog']}; remove this file")


def main() -> int:
    root = Path.cwd()
    config_path = root / "windows-build.json"
    if not config_path.exists():
        print(f"check-listing: no windows-build.json in {root}; run from the app's repo root", file=sys.stderr)
        return 1
    config = load_json(config_path)
    settings = config.get("listing")
    if not settings or not settings.get("product_name"):
        print("check-listing: windows-build.json has no listing.product_name", file=sys.stderr)
        return 1

    listing_path = root / settings.get("listing_path", "store-assets/microsoft/listings.json")
    catalog_setting = settings.get("catalog_dir") or (config.get("localization") or {}).get("catalog_dir")
    wording = sorted(p.stem for p in (root / catalog_setting).glob("*.json")) if catalog_setting else None
    notes_dir = root / settings.get("notes_dir", "release-notes")
    readme_path = root / settings.get("readme", "README.md")
    version_file = root / config.get("version_file", "Directory.Build.props")

    checker = Checker(settings["product_name"], settings.get("retired_names", []))
    if not listing_path.exists():
        checker.problems.append(f"{listing_path.relative_to(root)}: missing")
    else:
        check_listing(checker, listing_path, wording, readme_path)

    match = re.search(r"<Version>([^<]+)</Version>", version_file.read_text(encoding="utf-8-sig")) if version_file.exists() else None
    if not match:
        checker.problems.append(f"{version_file.name}: no <Version>")
    else:
        check_notes(checker, notes_dir, match.group(1).strip(), wording)

    for problem in checker.problems:
        print("error:", problem, file=sys.stderr)
    locales = "English only" if wording is None else f"{len(wording)} wording catalogs"
    print(f"check:listing: {'failed' if checker.problems else 'passed'} ({locales}, {len(checker.problems)} problem(s))")
    return 1 if checker.problems else 0


if __name__ == "__main__":
    sys.exit(main())
