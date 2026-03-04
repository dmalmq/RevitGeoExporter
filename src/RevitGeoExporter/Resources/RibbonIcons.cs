using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RevitGeoExporter.Resources;

/// <summary>
/// Generates ribbon button icons at runtime using System.Drawing.
/// </summary>
internal static class RibbonIcons
{
    public static BitmapSource CreateExportIcon(int size)
    {
        using Bitmap bmp = new(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float pad = size * 0.15f;
        float stroke = Math.Max(size / 16f, 1.5f);
        using Pen pen = new(Color.FromArgb(60, 60, 60), stroke)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Box (open top)
        float boxTop = size * 0.45f;
        float boxBottom = size - pad;
        float boxLeft = pad;
        float boxRight = size - pad;
        g.DrawLine(pen, boxLeft, boxTop, boxLeft, boxBottom);
        g.DrawLine(pen, boxLeft, boxBottom, boxRight, boxBottom);
        g.DrawLine(pen, boxRight, boxBottom, boxRight, boxTop);

        // Arrow shaft (vertical, centre)
        float cx = size / 2f;
        float arrowTop = pad;
        float arrowBottom = size * 0.55f;
        g.DrawLine(pen, cx, arrowTop, cx, arrowBottom);

        // Arrow head
        float headSize = size * 0.15f;
        g.DrawLine(pen, cx, arrowTop, cx - headSize, arrowTop + headSize);
        g.DrawLine(pen, cx, arrowTop, cx + headSize, arrowTop + headSize);

        return ConvertToBitmapSource(bmp);
    }

    public static BitmapSource CreateSettingsIcon(int size)
    {
        using Bitmap bmp = new(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float cx = size / 2f;
        float cy = size / 2f;
        float stroke = Math.Max(size / 16f, 1.5f);
        using Pen pen = new(Color.FromArgb(60, 60, 60), stroke)
        {
            LineJoin = LineJoin.Round,
        };

        // Gear: outer teeth as a polygon
        int teeth = 8;
        float outerRadius = size * 0.42f;
        float innerRadius = size * 0.30f;
        int points = teeth * 2;
        PointF[] gear = new PointF[points];
        for (int i = 0; i < points; i++)
        {
            float angle = (float)(i * Math.PI / teeth) - (float)(Math.PI / 2);
            float r = i % 2 == 0 ? outerRadius : innerRadius;
            gear[i] = new PointF(cx + r * (float)Math.Cos(angle), cy + r * (float)Math.Sin(angle));
        }

        g.DrawPolygon(pen, gear);

        // Centre circle
        float holeRadius = size * 0.12f;
        g.DrawEllipse(pen, cx - holeRadius, cy - holeRadius, holeRadius * 2, holeRadius * 2);

        return ConvertToBitmapSource(bmp);
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
