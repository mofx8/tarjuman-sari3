using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using QuickTranslate.Platform.Native;

namespace QuickTranslate.Platform;

/// <summary>A monitor in physical pixels (virtual-screen coordinates, may be negative).</summary>
public sealed record MonitorInfo(IntPtr Handle, int X, int Y, int Width, int Height, double Scale, string Device, bool IsPrimary);

public static class Screens
{
    public static (int X, int Y) CursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref NativeMethods.RECT r, IntPtr _) =>
        {
            list.Add(Describe(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The monitor currently under the mouse pointer.</summary>
    public static MonitorInfo UnderCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        return Describe(NativeMethods.MonitorFromPoint(p, NativeMethods.MONITOR_DEFAULTTONEAREST));
    }

    private static MonitorInfo Describe(IntPtr h)
    {
        var mi = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        NativeMethods.GetMonitorInfoW(h, ref mi);
        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(h, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0;
        var r = mi.rcMonitor;
        return new(h, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, scale, mi.szDevice, (mi.dwFlags & 1) != 0);
    }

    /// <summary>Makes a window click-friendly but never focus-stealing (for overlays above games).</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20; const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        var cur = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(cur | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    /// <summary>Captures one monitor at full physical resolution.</summary>
    public static Bitmap Capture(MonitorInfo m) => Capture(m.X, m.Y, m.Width, m.Height);

    public static Bitmap Capture(int x, int y, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Crops a region and upscales small crops so the OCR engine reads them reliably.</summary>
    public static Bitmap CropForOcr(Bitmap source, Rectangle region, bool upscale, out double factor)
    {
        region.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        factor = 1;
        if (upscale)
        {
            // Windows OCR works best with glyphs ~20-40 px tall; small UI text gets enlarged.
            if (region.Height < 120) factor = 3;
            else if (region.Height < 300) factor = 2;
        }
        var bmp = new Bitmap((int)(region.Width * factor), (int)(region.Height * factor), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, bmp.Width, bmp.Height), region, GraphicsUnit.Pixel);
        return bmp;
    }
}

