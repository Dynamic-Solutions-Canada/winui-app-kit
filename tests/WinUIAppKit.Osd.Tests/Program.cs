using WinUIAppKit.Windowing;

// Portable checks for where an on-screen display sits and how it fades (OsdLayout).
// Exits non-zero when any check fails.

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        passed++;
    }
    else
    {
        failures.Add(description);
    }
}

// ── Width ───────────────────────────────────────────────────────────────

Check(OsdLayout.Width(100, 300, 300) == 300, "width: a fixed-width popup ignores short content");
Check(OsdLayout.Width(900, 300, 300) == 300, "width: a fixed-width popup ignores long content");
Check(OsdLayout.Width(400, 320, 640) == 408, "width: content between the limits gets a little room");
Check(OsdLayout.Width(100, 320, 640) == 320, "width: short content keeps the minimum");
Check(OsdLayout.Width(1000, 320, 640) == 640, "width: long content stops at the maximum");
Check(OsdLayout.Width(100, 320, 200) == 320, "width: a maximum below the minimum means the minimum");

// ── Place ───────────────────────────────────────────────────────────────

var at100 = OsdLayout.Place(0, 1920, 1080, 1.0, 300, 56);
Check(at100 == (810, 960, 300, 56), "place: bottom centre, 64 DIPs above the bottom, at 100%");
var at150 = OsdLayout.Place(0, 2880, 1620, 1.5, 300, 56);
Check(at150 == (1215, 1440, 450, 84), "place: sizes and the gap scale with the display");
var second = OsdLayout.Place(-1920, 0, 1080, 1.0, 300, 56);
Check(second.X == -1110, "place: a display left of the main one keeps its own coordinates");
var odd = OsdLayout.Place(0, 1366, 768, 1.25, 301, 57);
Check(odd.Width == 377 && odd.Height == 72, "place: fractional sizes round up, so content is never clipped");

// ── Fade ────────────────────────────────────────────────────────────────

Check(OsdLayout.FadeAlpha(0) == 255, "fade: opaque before the first step");
Check(OsdLayout.FadeAlpha(OsdLayout.FadeSteps) == 0, "fade: transparent at the last step");
Check(OsdLayout.FadeAlpha(OsdLayout.FadeSteps + 5) == 0, "fade: never wraps past transparent");
bool falling = true;
for (int step = 1; step <= OsdLayout.FadeSteps; step++)
{
    falling &= OsdLayout.FadeAlpha(step) < OsdLayout.FadeAlpha(step - 1);
}
Check(falling, "fade: every step is more transparent than the last");
int fadeMs = OsdLayout.FadeSteps * OsdLayout.FadeStepMs;
Check(fadeMs >= 250 && fadeMs <= 500, "fade: lasts between a quarter and half a second");

// ── Report ──────────────────────────────────────────────────────────────

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL {failure}");
}

Console.WriteLine($"WinUIAppKit.Osd.Tests: {passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;
