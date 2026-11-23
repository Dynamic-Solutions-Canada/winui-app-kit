namespace WinUIAppKit.Windowing;

/// <summary>
/// The arithmetic behind <see cref="OsdController"/>: where an on-screen display sits and how it fades.
/// Depends on nothing else in the kit or WinUI, so a portable test harness can link it.
/// </summary>
public static class OsdLayout
{
    /// <summary>Gap between the popup and the bottom of the display, in DIPs, like Windows' volume popup.</summary>
    public const double BottomGapDip = 64;

    /// <summary>Steps of the fade-out; with <see cref="FadeStepMs"/> it lasts about a third of a second.</summary>
    public const int FadeSteps = 12;

    /// <summary>Time between fade steps, in milliseconds.</summary>
    public const int FadeStepMs = 30;

    /// <summary>
    /// The popup's width in DIPs: its content's desired width plus a little room, within the limits.
    /// A fixed-width popup passes the same value for both limits.
    /// </summary>
    /// <param name="desiredDip">The content's desired width, measured with unlimited width.</param>
    /// <param name="minDip">The narrowest it may be.</param>
    /// <param name="maxDip">The widest it may be; longer content is trimmed by its own layout.</param>
    public static double Width(double desiredDip, double minDip, double maxDip) =>
        Math.Clamp(desiredDip + 8, minDip, Math.Max(minDip, maxDip));

    /// <summary>
    /// Bottom centre of a display, <see cref="BottomGapDip"/> above its bottom edge, in physical pixels.
    /// Uses the whole display, not its work area, so the popup sits in the same place whatever the taskbar does.
    /// </summary>
    /// <param name="left">The display's left edge, physical pixels.</param>
    /// <param name="right">The display's right edge, physical pixels.</param>
    /// <param name="bottom">The display's bottom edge, physical pixels.</param>
    /// <param name="scale">The display's scale (1.0 at 100%).</param>
    /// <param name="widthDip">The popup's width in DIPs.</param>
    /// <param name="heightDip">The popup's height in DIPs.</param>
    /// <returns>The window's position and size in physical pixels.</returns>
    public static (int X, int Y, int Width, int Height) Place(int left, int right, int bottom, double scale, double widthDip, double heightDip)
    {
        int width = (int)Math.Ceiling(widthDip * scale);
        int height = (int)Math.Ceiling(heightDip * scale);
        int x = left + (right - left - width) / 2;
        int y = bottom - height - (int)(BottomGapDip * scale);
        return (x, y, width, height);
    }

    /// <summary>The window's opacity (255 opaque) after <paramref name="step"/> of the fade's steps.</summary>
    public static byte FadeAlpha(int step) =>
        (byte)Math.Clamp(255 - 255 * step / FadeSteps, 0, 255);
}
