"""
Unit tests for localized screenshots and 200% captures in scripts/store/render-screenshots.py: an entry
whose "file" and "source" contain {locale} is cropped once per listing locale with its own record, from
that locale's capture; a 200% capture is cropped by the same logical rules as a 100% one and reduced to
the output size. A manifest without {locale} and with 100% captures behaves as before (test_crops.py and
test_heroes.py). Standard library only.
"""
import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "store" / "render-screenshots.py"
spec = importlib.util.spec_from_file_location("render_screenshots", SCRIPT)
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)

SIZE = (160, 90)
SCREEN = {"width": 400, "height": 240}
WORK_AREA = {"x": 0, "y": 0, "width": 400, "height": 220}
WINDOW = {"x": 170, "y": 100, "width": 60, "height": 30}
SHOT = {
    "file": "screenshots/{locale}/01-home-dark.png",
    "source": "raw/microsoft/{locale}/01-home-dark.png",
    "theme": "dark",
    "caption": "Home.",
    "alt": "The home page.",
}
HERO = {
    "file": "screenshots/{locale}/00-hero-dark.png",
    "source": "raw/microsoft/{locale}/01-home-dark.png",
    "theme": "dark",
    "hero": {"layout": "split"},
    "caption": "The hero.",
    "alt": "The window beside a headline.",
}
LOCALES = {
    "en-US": {"screenshotCaptions": ["The hero.", "Home."]},
    "en-CA": {"copyFrom": "en-US"},
    "fr-FR": {"screenshotCaptions": ["Le héros.", "Accueil."]},
}


def sidecar(scale: int = 100) -> dict:
    """A capture description at `scale` percent: the same logical desktop at any scale."""
    factor = scale // 100

    def scaled(rect: dict) -> dict:
        return {key: value * factor for key, value in rect.items()}

    return {"screen": scaled(SCREEN), "scale": scale, "window": scaled(WINDOW), "workArea": scaled(WORK_AREA),
            "theme": {"apps": "dark", "system": "dark"}, "wallpaper": renderer.WALLPAPER, "effects": True}


class Localized(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.assets = Path(self.folder.name)
        (self.assets / "microsoft").mkdir()
        (self.assets / "microsoft" / "listings.json").write_text(json.dumps({"schemaVersion": 1, "locales": LOCALES}))

    def tearDown(self):
        self.folder.cleanup()

    def write_capture(self, locale: str, scale: int = 100) -> None:
        description = sidecar(scale)
        path = self.assets / "raw" / "microsoft" / locale / "01-home-dark.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        width, height = description["screen"]["width"], description["screen"]["height"]
        renderer.write_rgb_png(path, bytes(width * height * 3), width, height)
        path.with_suffix(".json").write_text(json.dumps(description))

    def write_png(self, relative: str) -> None:
        path = self.assets / "microsoft" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        renderer.write_rgb_png(path, bytes(SIZE[0] * SIZE[1] * 3), *SIZE)

    def check(self, entries: list[dict]) -> tuple[int, str]:
        errors = io.StringIO()
        with redirect_stderr(errors), redirect_stdout(io.StringIO()):
            status = renderer.check(self.assets, entries, SIZE)
        return status, errors.getvalue()

    def test_each_locale_with_its_own_record_is_cropped_from_its_own_capture(self):
        self.write_capture("en-US")
        self.write_capture("fr-FR")
        jobs, problems = renderer.plan(self.assets, [SHOT], SIZE)
        self.assertEqual(problems, [])
        self.assertEqual([(job.locale, job.source, job.file) for job in jobs], [
            ("en-US", "raw/microsoft/en-US/01-home-dark.png", "screenshots/en-US/01-home-dark.png"),
            ("fr-FR", "raw/microsoft/fr-FR/01-home-dark.png", "screenshots/fr-FR/01-home-dark.png"),
        ])

    def test_a_locale_without_its_capture_fails_rather_than_showing_english(self):
        self.write_capture("en-US")
        _, problems = renderer.plan(self.assets, [SHOT], SIZE)
        self.assertEqual(problems, ["raw/microsoft/fr-FR/01-home-dark.png: missing capture"])

    def test_locale_must_be_in_both_file_and_source(self):
        half = dict(SHOT, source="raw/microsoft/01-home-dark.png")
        problems = renderer.localized_problems([half])
        self.assertEqual(len(problems), 1)
        self.assertIn("must be in both file and source", problems[0])

    def test_a_full_localized_set_passes_beside_the_heroes(self):
        for locale in ("en-US", "fr-FR"):
            self.write_capture(locale)
            self.write_png(f"screenshots/{locale}/00-hero-dark.png")
            self.write_png(f"screenshots/{locale}/01-home-dark.png")
        self.assertEqual(self.check([HERO, SHOT]), (0, ""))

    def test_images_for_a_dropped_or_copy_from_locale_are_named(self):
        for locale in ("en-US", "fr-FR"):
            self.write_capture(locale)
            self.write_png(f"screenshots/{locale}/00-hero-dark.png")
            self.write_png(f"screenshots/{locale}/01-home-dark.png")
        self.write_png("screenshots/en-CA/01-home-dark.png")
        self.write_png("screenshots/de-DE/01-home-dark.png")
        status, errors = self.check([HERO, SHOT])
        self.assertEqual(status, 1)
        self.assertIn("microsoft/screenshots/en-CA/01-home-dark.png: not in manifest.json", errors)
        self.assertIn("microsoft/screenshots/de-DE/01-home-dark.png: not in manifest.json", errors)


class TwoHundredPercent(unittest.TestCase):
    def test_a_200_percent_capture_crops_at_the_same_logical_corner(self):
        self.assertEqual(renderer.crop_box(sidecar(200), "window", SIZE), renderer.crop_box(sidecar(100), "window", SIZE))
        self.assertEqual(renderer.crop_box(sidecar(200), "tray", SIZE), renderer.crop_box(sidecar(100), "tray", SIZE))

    def test_an_odd_physical_edge_rounds_the_window_outward(self):
        odd = sidecar(200)
        odd["window"] = {"x": 339, "y": 199, "width": 122, "height": 62}
        _, window, _ = renderer.logical(odd)
        self.assertEqual(window, {"x": 169, "y": 99, "width": 62, "height": 32})

    def test_the_crop_is_doubled_and_reduced_to_the_output_size(self):
        shot = renderer.Shot({}, None, "raw/a.png", "a.png", left=12, top=7, scale=200)
        command = renderer.magick_command(shot, SIZE, ".render/0.rgb")
        self.assertIn("-crop 320x180+24+14 +repage -scale 160x90! ", command)
        normal = renderer.magick_command(renderer.Shot({}, None, "raw/a.png", "a.png", left=12, top=7), SIZE, "x.rgb")
        self.assertIn("-crop 160x90+12+7 +repage -alpha off", normal)

    def test_only_100_and_200_percent_are_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            assets = Path(folder)
            for scale in (100, 200, 150):
                description = sidecar(200 if scale == 150 else scale)
                description["scale"] = scale
                path = assets / f"{scale}.png"
                width, height = description["screen"]["width"], description["screen"]["height"]
                renderer.write_rgb_png(path, bytes(width * height * 3), width, height)
                path.with_suffix(".json").write_text(json.dumps(description))
            self.assertEqual(renderer.capture_problems(assets, "100.png"), [])
            self.assertEqual(renderer.capture_problems(assets, "200.png"), [])
            self.assertIn("captures must be at 100% or 200%", renderer.capture_problems(assets, "150.png")[0])


if __name__ == "__main__":
    unittest.main()
