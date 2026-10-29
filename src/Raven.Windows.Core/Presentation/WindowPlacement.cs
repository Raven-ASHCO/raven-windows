using System;

namespace Raven.Windows.Core.Presentation;

/// <summary>
/// Where the main window opens: inside the monitor's work area (E1: on a 1920x1020 work area it
/// opened at 224,224 with 1300x875, and the message box sat 79 px under the taskbar). All values
/// are physical pixels.
/// </summary>
public static class WindowPlacement
{
    public readonly record struct Rect(int X, int Y, int Width, int Height);

    /// <summary>tokens.json layout.minWindow: the smallest window, in device-independent pixels (as macOS points).</summary>
    public const int MinWidthDip = 560;
    public const int MinHeightDip = 480;

    /// <summary>
    /// The smallest window in physical pixels at <paramref name="scale"/> (1.0 at 96 DPI): 560 x 480
    /// at 100 %, 700 x 600 at 125 % (E1 measured physical pixels on a 125 % screen and found
    /// 700 x 600: the 560 x 480 minimum, scaled).
    /// </summary>
    public static (int Width, int Height) MinimumPixels(double scale) =>
        ((int)Math.Ceiling(MinWidthDip * scale), (int)Math.Ceiling(MinHeightDip * scale));

    /// <summary>
    /// <paramref name="wanted"/> made to fit <paramref name="area"/>: no larger than the area (nor
    /// smaller than the minimum, unless the area is), and moved so no edge is outside it.
    /// </summary>
    public static Rect Fit(Rect wanted, Rect area, int minWidth, int minHeight)
    {
        int width = Math.Min(Math.Max(wanted.Width, Math.Min(minWidth, area.Width)), area.Width);
        int height = Math.Min(Math.Max(wanted.Height, Math.Min(minHeight, area.Height)), area.Height);
        int x = Math.Clamp(wanted.X, area.X, area.X + area.Width - width);
        int y = Math.Clamp(wanted.Y, area.Y, area.Y + area.Height - height);
        return new Rect(x, y, width, height);
    }
}
