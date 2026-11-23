"""
Unit tests for the hero fields in scripts/check-listing.py (D-123): heroHeadline and heroSubline may
sit in a listing record beside the six store fields, follow the same text rules, and are set in every
full record or none. Standard library only; the script is loaded by path.
"""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "check-listing.py"
spec = importlib.util.spec_from_file_location("check_listing", SCRIPT)
listing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(listing)

FAMILY = json.loads(listing.LANGUAGES.read_text(encoding="utf-8"))["languages"]
CATALOGS = sorted({entry["catalog"] for entry in FAMILY})


def record(**extra) -> dict:
    """A full record within every store limit, plus any hero fields."""
    return {
        "title": "Fixture",
        "shortDescription": "Does one thing well.",
        "fullDescription": "Does one thing well, all day.",
        "features": ["One thing"],
        "keywords": ["thing"],
        "screenshotCaptions": ["The hero."],
        **extra,
    }


def problems_for(records: dict) -> list[str]:
    """Runs the listing check on a full family listing whose catalog records are `records`."""
    locales = {}
    for entry in FAMILY:
        identity, catalog = entry["identity"], entry["catalog"]
        locales[identity] = records[identity] if identity == catalog else {"copyFrom": catalog}
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "listings.json"
        path.write_text(json.dumps({"schemaVersion": 1, "locales": locales}), encoding="utf-8")
        readme = Path(folder) / "README.md"
        readme.write_text("<h1>Fixture</h1>\n<p>Does one thing well.</p>\n", encoding="utf-8")
        checker = listing.Checker("Fixture", [])
        listing.check_listing(checker, path, CATALOGS, readme)
        return checker.problems


class HeroFields(unittest.TestCase):
    def test_hero_fields_in_every_record_pass(self):
        records = {catalog: record(heroHeadline="One thing, done well.", heroSubline="All day.") for catalog in CATALOGS}
        self.assertEqual(problems_for(records), [])

    def test_a_listing_without_hero_fields_still_passes(self):
        self.assertEqual(problems_for({catalog: record() for catalog in CATALOGS}), [])

    def test_a_hero_field_missing_from_some_records_fails(self):
        records = {catalog: record(heroHeadline="One thing.") for catalog in CATALOGS}
        records["ja-JP"] = record()
        problems = problems_for(records)
        self.assertTrue(any(p.startswith("heroHeadline: set in") and "ja-JP" in p for p in problems), problems)

    def test_hero_text_follows_the_text_rules(self):
        records = {catalog: record(heroHeadline="One thing — done.") for catalog in CATALOGS}
        records["de-DE"] = record(heroHeadline="x" * 121)
        problems = problems_for(records)
        self.assertIn("en-US.heroHeadline: contains an em dash", problems)
        self.assertIn("de-DE.heroHeadline: 121 characters, over the 120 limit", problems)

    def test_other_extra_fields_are_still_refused(self):
        records = {catalog: record() for catalog in CATALOGS}
        records["en-US"] = record(releaseNotes="New.")
        self.assertTrue(any("unexpected releaseNotes" in p for p in problems_for(records)))


if __name__ == "__main__":
    unittest.main()
