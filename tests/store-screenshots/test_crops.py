"""
Unit tests for the crop geometry in scripts/store/render-screenshots.py: where each crop is taken, and
when it refuses a capture whose window and shadow margin it would clip. Standard library only; the
script is loaded by path, since its file name is not an importable module name.
"""
import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "store" / "render-screenshots.py"
spec = importlib.util.spec_from_file_location("render_screenshots", SCRIPT)
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)

SCREEN = {"width": 1920, "height": 1080}
WORK_AREA = {"x": 0, "y": 0, "width": 1920, "height": 1032}
SMALL = (1366, 768)
LARGE = (1920, 1080)


def sidecar(x: int, y: int, width: int, height: int) -> dict:
    """A 1920 x 1080 capture's sidecar with the window's frame at the given place and size."""
    return {"screen": SCREEN, "workArea": WORK_AREA, "window": {"x": x, "y": y, "width": width, "height": height}}


class TrayCrop(unittest.TestCase):
    def test_tall_flyout_fails_at_the_default_size_and_names_the_fix(self):
        # The persistent report: a 420 x 960 flyout lost its header in a 1366 x 768 crop and passed.
        result = renderer.crop_box(sidecar(1488, 60, 420, 960), "tray", SMALL)
        self.assertIsInstance(result, str)
        self.assertIn("1920x1080", result)
        self.assertIn('"window" crops', result)

    def test_tall_flyout_fits_a_screen_sized_output(self):
        self.assertEqual(renderer.crop_box(sidecar(1488, 60, 420, 960), "tray", LARGE), (0, 0))

    def test_flyout_against_the_screen_edge_passes(self):
        # Flyouts sit a few pixels from the screen's right edge, where no margin can exist.
        self.assertEqual(renderer.crop_box(sidecar(1536, 797, 380, 227), "tray", SMALL), (554, 312))

    def test_flyout_flush_with_the_screen_edge_passes(self):
        self.assertEqual(renderer.crop_box(sidecar(960, 464, 960, 560), "tray", SMALL), (554, 312))

    def test_flyout_too_close_to_the_crop_left_edge_fails(self):
        self.assertIsInstance(renderer.crop_box(sidecar(560, 700, 400, 300), "tray", SMALL), str)

    def test_window_off_the_screen_fails(self):
        result = renderer.crop_box(sidecar(1700, 797, 380, 227), "tray", SMALL)
        self.assertIsInstance(result, str)
        self.assertIn("not entirely on the screen", result)

    def test_screen_smaller_than_the_output_fails(self):
        small_screen = dict(sidecar(900, 500, 300, 200), screen={"width": 1280, "height": 800})
        self.assertIn("smaller than", renderer.crop_box(small_screen, "tray", SMALL))


class WindowAndBottomCropsUnchanged(unittest.TestCase):
    def test_window_crop_centres_on_the_window(self):
        self.assertEqual(renderer.crop_box(sidecar(410, 166, 1100, 700), "window", SMALL), (277, 132))

    def test_window_crop_refuses_a_window_without_room_for_its_margin(self):
        self.assertIsInstance(renderer.crop_box(sidecar(260, 150, 1400, 700), "window", SMALL), str)

    def test_bottom_crop_centres_on_the_popup(self):
        # Hub's popup: 320 x 68 at 800, 948.
        self.assertEqual(renderer.crop_box(sidecar(800, 948, 320, 68), "bottom", SMALL), (277, 312))

    def test_bottom_crop_still_needs_the_margin_on_the_screen_sides(self):
        self.assertIsInstance(renderer.crop_box(sidecar(1590, 948, 320, 68), "bottom", SMALL), str)


class MarginFits(unittest.TestCase):
    def test_only_the_named_sides_are_checked(self):
        window = {"x": 1550, "y": 800, "width": 360, "height": 200}
        self.assertFalse(renderer.margin_fits(window, (554, 312), SMALL, ("right",)))
        self.assertTrue(renderer.margin_fits(window, (554, 312), SMALL, ("left", "top", "bottom")))


if __name__ == "__main__":
    unittest.main()
