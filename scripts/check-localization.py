#!/usr/bin/env python3
"""The interface text check every Windows app runs as `check:l10n` (LOC-9), and the kit runs on itself.

App mode (from the app's repo root; npm does this):
    python3 external/winui-app-kit/scripts/check-localization.py

reads the `localization` block of the app's windows-build.json:
    "localization": {
      "catalog_dir": "MyApp/Localization/Catalogs",   required: the app's <catalog>.json files
      "source_dirs": ["MyApp"],                        default: the folder of "project"
      "english_keys": false,                           true when the en-US text is the key (en-US value == key)
      "extra_key_patterns": [],                        regexes with one group, for keys built in code
      "unused_ok": [],                                 key globs used by text matching, not literals
      "catalog_layout": "files"                        "files": <catalog>.json; "folders": <catalog>/<area>.json,
                                                       merged, and a key in two areas fails
    }

Kit mode (`--kit`) checks the kit's own catalogs against the kit's code and shared XAML.

Fails when:
  - a catalog is missing a key that en-US has, has an extra one, or has an empty value;
  - a translation's {{placeholders}} differ from the en-US text;
  - a plural key (UiText.Plural reads key.one and key.other) lacks one of its two forms; a form may
    leave out {{count}};
  - with catalog_layout "folders", a key is defined in two area files of one catalog;
  - any text contains an em dash (house writing rule);
  - code or XAML uses a key no catalog defines, or a catalog key is never used. Keys the manifest uses
    as ms-resource:Resources/<Key> count as used, and are exempt from english_keys;
  - an app catalog defines a `kit.` key (the kit owns those, LOC-7), or app code uses a `kit.` key
    the kit does not define;
  - the manifest's <Resource Language> list, or the catalog files on disk, differ from the family's
    language list (build/languages.json), or from en-US alone for an English-only app (LOC-3).

Standard library only, so it runs anywhere Python 3.10+ does.
"""
import fnmatch
import json
import re
import sys
from pathlib import Path

KIT_ROOT = Path(__file__).resolve().parent.parent
KIT_CATALOGS = KIT_ROOT / "WinUIAppKit" / "Localization" / "Catalogs"
LANGUAGES = KIT_ROOT / "build" / "languages.json"
KIT_PREFIX = "kit."

STRING = r'"((?:[^"\\]|\\.)*)"'
PLACEHOLDER = re.compile(r"\{\{\s*(\w+)\s*\}\}")
XAML_KEY = re.compile(r'l10n:Localize\.\w+="([^"]*)"')
SET_CALL = re.compile(r"Localize\.Set\w+\(\s*[^,]+,\s*" + STRING)
LOOKUP_CALL = re.compile(r"UiText\.(?:Get|Format|Source)\(")
# UiText.Plural("key", count, ...) reads key.one and key.other; only its first literal is a key stem.
PLURAL_CALL = re.compile(r"UiText\.Plural\(\s*" + STRING)
PLURAL_FORMS = ("one", "other")
# Dotted keys passed around as plain literals (state keys, labels chosen in code). Only counted as
# "used", never reported as undefined, because file names and other dotted strings look the same.
DOTTED_LITERAL = re.compile(r'"([a-z][a-z0-9]*(?:\.[a-z0-9][a-z0-9-]*)+)"')


def load_json(path: Path) -> dict:
    with path.open(encoding="utf-8-sig") as f:
        return json.load(f)


def unescape(literal: str) -> str:
    """A C# string literal's text (the escapes JSON shares with C#)."""
    return json.loads('"' + literal + '"')


def call_arguments(text: str, start: int) -> str:
    """The text of a call's arguments, from just after its "(" to the matching ")", skipping strings."""
    depth, i, in_string = 1, start, False
    while i < len(text) and depth:
        c = text[i]
        if in_string:
            if c == "\\":
                i += 1
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        i += 1
    return text[start:i - 1]


def top_level_literals(arguments: str) -> list[str]:
    """The string literals in a call's arguments that are not inside nested parentheses."""
    literals, depth, i = [], 0, 0
    while i < len(arguments):
        c = arguments[i]
        if c == '"':
            match = re.compile(STRING).match(arguments, i)
            if match is None:
                break
            if depth == 0:
                literals.append(match.group(1))
            i = match.end()
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        i += 1
    return literals


def source_files(dirs: list[Path], suffix: str):
    """Files under the source folders, skipping build output."""
    for folder in dirs:
        for path in folder.rglob(f"*{suffix}"):
            if not any(part in ("bin", "obj") for part in path.parts):
                yield path


def used_keys(dirs: list[Path], extra_patterns: list[str]) -> tuple[set[str], set[str]]:
    """
    Keys the code and XAML use: (certain, possible). Certain keys come from lookups (UiText.Get, Format,
    Source), Localize.SetX calls, XAML attached properties and the app's extra patterns, and must exist.
    Possible keys are dotted literals elsewhere; they only keep a catalog key from counting as unused.
    """
    certain: set[str] = set()
    possible: set[str] = set()
    patterns = [re.compile(p) for p in extra_patterns]
    for path in source_files(dirs, ".cs"):
        text = path.read_text(encoding="utf-8-sig")
        for match in LOOKUP_CALL.finditer(text):
            arguments = call_arguments(text, match.end())
            # Every literal at the call's own level is a key (a conditional can pick one of two).
            # Literals inside parentheses are Format's ("name", value) pairs or values built by other
            # calls (a number format, a fixed product name), never keys.
            for literal in top_level_literals(arguments):
                if literal:
                    certain.add(unescape(literal))
        for literal in PLURAL_CALL.findall(text):
            stem = unescape(literal)
            certain.update(f"{stem}.{form}" for form in PLURAL_FORMS)
        for literal in SET_CALL.findall(text):
            certain.add(unescape(literal))
        for pattern in patterns:
            for literal in pattern.findall(text):
                certain.add(unescape(literal))
        possible.update(DOTTED_LITERAL.findall(text))
    for path in source_files(dirs, ".xaml"):
        for value in XAML_KEY.findall(path.read_text(encoding="utf-8-sig")):
            certain.add(value.replace("&quot;", '"').replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">"))
    return certain, possible


def check_catalogs(catalogs: dict[str, dict], errors: list[str], label: str) -> None:
    """Same keys as en-US everywhere, no empty values, matching placeholders, no em dashes."""
    english = catalogs["en-US"]
    for name, catalog in catalogs.items():
        for key in sorted(english.keys() - catalog.keys()):
            errors.append(f"{label} {name}: missing {key}")
        for key in sorted(catalog.keys() - english.keys()):
            errors.append(f"{label} {name}: extra key {key}")
        for key, text in catalog.items():
            if not isinstance(text, str) or not text.strip():
                errors.append(f"{label} {name}: {key} is empty")
                continue
            if "—" in text:
                errors.append(f"{label} {name}: {key} contains an em dash")
            if key in english and placeholders(key, text) != placeholders(key, english[key]):
                errors.append(f"{label} {name}: {key} placeholders differ from en-US")
    # A plural key needs both forms in en-US (the catalogs above must then match it).
    for key in sorted(english):
        stem, _, form = key.rpartition(".")
        if form in PLURAL_FORMS and stem:
            for other in PLURAL_FORMS:
                if f"{stem}.{other}" not in english:
                    errors.append(f"{label} en-US: {key} has no {stem}.{other}; a plural key needs both forms")


def placeholders(key: str, text: str) -> list[str]:
    """A text's placeholder names, sorted. A plural form may leave out {{count}} ("un fichier")."""
    names = sorted(PLACEHOLDER.findall(text))
    if key.rpartition(".")[2] in PLURAL_FORMS:
        names = [name for name in names if name != "count"]
    return names


def read_catalogs(folder: Path, errors: list[str], label: str, layout: str = "files") -> dict[str, dict]:
    """
    Each catalog's text: <catalog>.json ("files"), or every <catalog>/<area>.json merged ("folders"),
    the same merge UiText makes at load. A key defined in two areas is an error naming both.
    """
    if layout == "folders":
        catalogs = {}
        for catalog_dir in sorted(p for p in folder.iterdir() if p.is_dir()):
            merged: dict[str, str] = {}
            origin: dict[str, str] = {}
            for area in sorted(catalog_dir.glob("*.json")):
                for key, value in load_json(area).items():
                    if key in merged:
                        errors.append(f"{label} {catalog_dir.name}: {key} is in both {origin[key]} and {area.name}")
                        continue
                    merged[key] = value
                    origin[key] = area.name
            catalogs[catalog_dir.name] = merged
    else:
        catalogs = {path.stem: load_json(path) for path in sorted(folder.glob("*.json"))}
    if "en-US" not in catalogs:
        errors.append(f"{label}: no en-US catalog in {folder}")
    return catalogs


def check_kit() -> list[str]:
    """The kit's own catalogs, against the kit library and shared XAML."""
    errors: list[str] = []
    family = load_json(LANGUAGES)
    catalogs = read_catalogs(KIT_CATALOGS, errors, "kit")
    if errors:
        return errors

    expected = {entry["catalog"] for entry in family["languages"]}
    if set(catalogs) != expected:
        errors.append(f"kit catalogs {sorted(catalogs)} differ from the family's catalogs {sorted(expected)}")
    check_catalogs(catalogs, errors, "kit")

    for key in sorted(k for k in catalogs["en-US"] if not k.startswith(KIT_PREFIX)):
        errors.append(f"kit en-US: {key} lacks the {KIT_PREFIX} prefix")
    certain, possible = used_keys([KIT_ROOT / "WinUIAppKit", KIT_ROOT / "Xaml"], [])
    kit_used = {k for k in certain if k.startswith(KIT_PREFIX)}
    for key in sorted(kit_used - catalogs["en-US"].keys()):
        errors.append(f"kit code uses undefined key {key}")
    for key in sorted(catalogs["en-US"].keys() - kit_used - possible):
        errors.append(f"kit en-US: {key} is never used by the kit")
    print(f"kit: {len(catalogs)} catalogs, {len(catalogs['en-US'])} keys")
    return errors


def check_app(root: Path) -> list[str]:
    """An app's catalogs, code, XAML and manifest, as configured in its windows-build.json."""
    errors: list[str] = []
    config_path = root / "windows-build.json"
    if not config_path.exists():
        return [f"no windows-build.json in {root}; run from the app's repo root"]
    config = load_json(config_path)
    settings = config.get("localization")
    if not settings or "catalog_dir" not in settings:
        return ["windows-build.json has no localization.catalog_dir"]

    layout = settings.get("catalog_layout", "files")
    if layout not in ("files", "folders"):
        return [f"localization.catalog_layout is {layout!r}; use \"files\" or \"folders\""]
    catalogs = read_catalogs(root / settings["catalog_dir"], errors, "app", layout)
    if errors:
        return errors
    english = catalogs["en-US"]

    # Languages: the family's list, or en-US alone for an English-only app (LOC-3).
    family = load_json(LANGUAGES)
    identities = [entry["identity"] for entry in family["languages"]]
    family_catalogs = {entry["catalog"] for entry in family["languages"]}
    manifest = root / config["msix_dir"] / "Package.appxmanifest" if "msix_dir" in config else None
    if set(catalogs) == {"en-US"}:
        expected_identities = ["en-US"]
    else:
        expected_identities = identities
        if set(catalogs) != family_catalogs:
            errors.append(f"catalog files {sorted(catalogs)} differ from the family's catalogs {sorted(family_catalogs)}")
    # Keys the manifest uses as ms-resource:Resources/<Key>, the same match build-msix.ps1 makes for
    # manifest_strings. They count as used, and are exempt from english_keys: the syntax needs an
    # identifier, so they can never be English text. A missing one fails packaging, not this check.
    manifest_keys: set[str] = set()
    if manifest and manifest.exists():
        manifest_text = manifest.read_text(encoding="utf-8-sig")
        manifest_keys = set(re.findall(r"ms-resource:Resources/(\w+)", manifest_text))
        declared = re.findall(r'<Resource\s+Language="([^"]+)"', manifest_text)
        if sorted(declared) != sorted(expected_identities):
            errors.append(f"manifest languages {declared} differ from {expected_identities}")

    check_catalogs(catalogs, errors, "app")
    for key in sorted(k for k in english if k.startswith(KIT_PREFIX)):
        errors.append(f"app en-US: {key} is a kit key; the kit owns kit. labels (LOC-7), remove it")
    if settings.get("english_keys"):
        for key, text in english.items():
            if key != text and key not in manifest_keys:
                errors.append(f"app en-US: {key!r} has different text; with english_keys the text is the key")

    # Keys used in code: app keys must exist in the app's catalog, kit keys in the kit's.
    project_dir = (root / config["project"]).parent if "project" in config else root
    dirs = [root / d for d in settings.get("source_dirs", [])] or [project_dir]
    certain, possible = used_keys(dirs, settings.get("extra_key_patterns", []))
    kit_english = load_json(KIT_CATALOGS / "en-US.json")
    for key in sorted(certain):
        if key.startswith(KIT_PREFIX):
            if key not in kit_english:
                errors.append(f"code uses {key}, which the kit does not define")
        elif key not in english:
            errors.append(f"code uses undefined key {key!r}")

    unused_ok = settings.get("unused_ok", [])
    for key in sorted(english.keys() - certain - possible - manifest_keys):
        if key.startswith(KIT_PREFIX):
            continue   # already reported above as a kit key the app must not define
        if not any(fnmatch.fnmatchcase(key, glob) for glob in unused_ok):
            errors.append(f"app en-US: {key!r} is never used (list it in localization.unused_ok if text matching uses it)")
    print(f"app: {len(catalogs)} catalogs, {len(english)} keys, {len(certain)} keys used in code")
    return errors


def main() -> int:
    errors = check_kit() if "--kit" in sys.argv else check_app(Path.cwd())
    for line in errors:
        print("error:", line, file=sys.stderr)
    print("check:l10n:", "failed" if errors else "passed", f"({len(errors)} error(s))")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
