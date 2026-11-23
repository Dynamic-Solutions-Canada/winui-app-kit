"""
Unit tests for how scripts/store/render-screenshots.py treats a generated hero (D-123): an entry with a
"hero" block that devkit-store-hero draws, one PNG per listing locale. The script must never crop it,
must check that each locale's PNG exists in the listing's format, and must refuse a hero that is not
first or has no {locale} in its path. Standard library only.
"""
import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "store" / "render-screenshots.py"
spec = importlib.util.spec_from_file_location("render_screenshots", SCRIPT)
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)

SIZE = (8, 4)
HERO = {
    "file": "screenshots/{locale}/00-hero-dark.png",
    "source": "raw/microsoft/00-flyout-dark.png",
    "theme": "dark",
    "hero": {"layout": "split"},
    "caption": "The hero.",
    "alt": "The flyout beside a headline.",
}
LOCALES = {
    "en-US": {"screenshotCaptions": ["The hero."]},
    "en-CA": {"copyFrom": "en-US"},
    "ja-JP": {"screenshotCaptions": ["Hero."]},
}


class HeroEntries(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.assets = Path(self.folder.name)
        (self.assets / "microsoft").mkdir()
        (self.assets / "microsoft" / "listings.json").write_text(json.dumps({"schemaVersion": 1, "locales": LOCALES}))

    def tearDown(self):
        self.folder.cleanup()

    def write_hero(self, locale: str) -> None:
        path = self.assets / "microsoft" / "screenshots" / locale / "00-hero-dark.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        renderer.write_rgb_png(path, bytes(SIZE[0] * SIZE[1] * 3), *SIZE)

    def check(self, entries: list[dict]) -> tuple[int, str]:
        errors = io.StringIO()
        with redirect_stderr(errors), redirect_stdout(io.StringIO()):
            status = renderer.check(self.assets, entries, SIZE)
        return status, errors.getvalue()

    def test_a_hero_for_every_locale_with_its_own_record_passes(self):
        self.write_hero("en-US")
        self.write_hero("ja-JP")
        self.assertEqual(self.check([HERO]), (0, ""))

    def test_a_missing_locale_fails_and_a_copy_from_locale_needs_none(self):
        self.write_hero("en-US")
        status, errors = self.check([HERO])
        self.assertEqual(status, 1)
        self.assertIn("screenshots/ja-JP/00-hero-dark.png: missing; run devkit-store-hero", errors)
        self.assertNotIn("en-CA", errors)

    def test_a_hero_in_another_format_fails(self):
        self.write_hero("en-US")
        path = self.assets / "microsoft" / "screenshots" / "ja-JP" / "00-hero-dark.png"
        path.parent.mkdir(parents=True)
        renderer.write_rgb_png(path, bytes(4 * 2 * 3), 4, 2)
        status, errors = self.check([HERO])
        self.assertEqual(status, 1)
        self.assertIn("4 x 2, not 8 x 4", errors)

    def test_the_hero_must_be_first_and_per_locale(self):
        other = {"file": "screenshots/01-home-dark.png", "theme": "dark"}
        problems = renderer.hero_problems([other, HERO])
        self.assertTrue(any("must be the first screenshot" in problem for problem in problems))
        problems = renderer.hero_problems([dict(HERO, file="screenshots/00-hero-dark.png")])
        self.assertTrue(any("must contain {locale}" in problem for problem in problems))

    def test_the_hero_is_never_cropped_or_held_to_the_capture_rules(self):
        # Its source is missing here; a captured entry would report that, a hero must not.
        jobs, problems = renderer.plan(self.assets, [HERO], SIZE)
        self.assertEqual((jobs, problems), ([], []))
        with mock.patch.object(renderer.subprocess, "run", side_effect=AssertionError("Docker ran")), redirect_stdout(io.StringIO()):
            renderer.render(self.assets, [HERO], SIZE)


if __name__ == "__main__":
    unittest.main()
