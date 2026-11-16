using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WinUIAppKit.Tray;

/// <summary>
/// Turns tray artwork drawn in code into a crisp PNG for <see cref="TrayIcon.Update"/> (TRAY-2): the app
/// draws at four times the size, which is then shrunk once to the exact pixel size the tray uses, so
/// small sizes stay sharp instead of Windows stretching a nearby one. The artwork itself stays in the app.
/// </summary>
public static class TrayIconRasterizer
{
    /// <summary>How much larger the artwork is drawn than the final icon.</summary>
    public const int Supersample = 4;

    /// <summary>
    /// Draw tray artwork and return it as PNG bytes.
    /// </summary>
    /// <param name="size">The icon's pixel size, normally <see cref="TrayIcon.IconPixelSize"/>; clamped to 16 to 256.</param>
    /// <param name="draw">
    /// Draws the artwork on a transparent, anti-aliased canvas. The second argument is the canvas's side
    /// in pixels (<paramref name="size"/> times <see cref="Supersample"/>), so the artwork scales with it.
    /// </param>
    /// <returns>The icon as PNG bytes, <paramref name="size"/> pixels square.</returns>
    public static byte[] Render(int size, Action<Graphics, float> draw)
    {
        size = Math.Clamp(size, 16, 256);
        int large = size * Supersample;

        using var big = new Bitmap(large, large, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            draw(g, large);
        }

        // One high-quality shrink to the exact size.
        using var small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(big, 0, 0, size, size);
        }

        using var png = new MemoryStream();
        small.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    /// <summary>The line colour that reads on the taskbar: white on a dark taskbar, near-black on a light one.</summary>
    /// <param name="darkTaskbar">From <see cref="TrayIcon.TaskbarIsDark"/>.</param>
    public static Color Ink(bool darkTaskbar) => darkTaskbar ? Color.White : Color.FromArgb(32, 32, 32);

    /// <summary>The taskbar's own colour, for a ring that separates a badge from the artwork behind it.</summary>
    /// <param name="darkTaskbar">From <see cref="TrayIcon.TaskbarIsDark"/>.</param>
    public static Color Surface(bool darkTaskbar) => darkTaskbar ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);

    /// <summary>A rounded rectangle path, the shape most tray artwork is built from. The caller disposes it.</summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="radius">The corner radius, in the same pixels.</param>
    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Max(0.1f, radius * 2);
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
