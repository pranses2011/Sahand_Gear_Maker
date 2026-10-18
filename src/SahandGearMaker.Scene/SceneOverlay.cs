using SahandGearMaker.MathX;

namespace SahandGearMaker.Scene;

/// <summary>
/// روکش‌های بوم (فاز ۱٫۷ و ۱٫۸):
///  - گرید مرجع صفحهٔ Z=0 با گام خوانا (1-2-5) و خطوط اصلی هر ۵ گام.
///  - محورهای XYZ رنگی از مبدأ + نشانگر مبدأ.
///  - مقیاس گرافیکی پایین بوم (نوار طول معلوم بر حسب mm).
/// همهٔ خطوط جهان با تست عمق رسم می‌شوند تا زیر هندسه پنهان شوند.
/// </summary>
public static class SceneOverlay
{
    private const double NearPlane = 0.5;

    /// <summary>گام «خوانا» بعدی از خانوادهٔ 1-2-5 (فاز ۱٫۷).</summary>
    public static double NiceStep(double rough)
    {
        if (rough <= 0) return 1;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double norm = rough / pow;
        double nice = norm switch
        {
            < 1.5 => 1,
            < 3.5 => 2,
            < 7.5 => 5,
            _ => 10,
        };
        return nice * pow;
    }

    public static void DrawWorldOverlay(byte[] pixels, float[] zbuf, int pw, int ph,
        in CameraFrame cam, (Vector3 Min, Vector3 Max) bounds, RenderOptions opt)
    {
        double step = opt.GridStepMm > 0 ? opt.GridStepMm : NiceStep(Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y) / 10);
        double extent = Math.Ceiling(Math.Max(
            Math.Max(Math.Abs(bounds.Min.X), Math.Abs(bounds.Max.X)),
            Math.Max(Math.Abs(bounds.Min.Y), Math.Abs(bounds.Max.Y))) / step) * step + step * 2;
        extent = Math.Min(extent, step * 60);

        if (opt.ShowGrid)
        {
            var minor = new Rgb(196, 202, 214);
            var major = new Rgb(168, 176, 194);
            int lines = (int)Math.Round(extent / step);
            for (int i = -lines; i <= lines; i++)
            {
                double p = i * step;
                bool isMajor = i % 5 == 0;
                var color = isMajor ? major : minor;
                // خط موازی X
                DrawWorldLine(pixels, zbuf, pw, ph, cam,
                    new Vector3(-extent, p, 0), new Vector3(extent, p, 0), color);
                // خط موازی Y
                DrawWorldLine(pixels, zbuf, pw, ph, cam,
                    new Vector3(p, -extent, 0), new Vector3(p, extent, 0), color);
            }
        }

        if (opt.ShowAxes)
        {
            double axisLen = extent * 0.55;
            DrawWorldLine(pixels, zbuf, pw, ph, cam, Vector3.Zero, new Vector3(axisLen, 0, 0), new Rgb(225, 92, 92), 2);
            DrawWorldLine(pixels, zbuf, pw, ph, cam, Vector3.Zero, new Vector3(0, axisLen, 0), new Rgb(84, 190, 104), 2);
            DrawWorldLine(pixels, zbuf, pw, ph, cam, Vector3.Zero, new Vector3(0, 0, axisLen), new Rgb(96, 130, 235), 2);
            // نشانگر مبدأ
            double m = step * 0.35;
            DrawWorldLine(pixels, zbuf, pw, ph, cam, new Vector3(-m, 0, 0), new Vector3(m, 0, 0), new Rgb(120, 128, 148), 2);
            DrawWorldLine(pixels, zbuf, pw, ph, cam, new Vector3(0, -m, 0), new Vector3(0, m, 0), new Rgb(120, 128, 148), 2);
            DrawWorldLine(pixels, zbuf, pw, ph, cam, new Vector3(0, 0, -m), new Vector3(0, 0, m), new Rgb(120, 128, 148), 2);
        }
    }

    /// <summary>رسم یک پاره‌خط جهانی با تست عمق (occlusion) — برش صفحهٔ نزدیک داخلی.</summary>
    public static void DrawWorldLine(byte[] pixels, float[] zbuf, int pw, int ph,
        in CameraFrame cam, Vector3 a, Vector3 b, Rgb color, int thickness = 1)
    {
        // تبدیل به دوربین
        var ra = a - cam.Eye; var rb = b - cam.Eye;
        double ax = Vector3.Dot(ra, cam.Right), ay = Vector3.Dot(ra, cam.Up), az = Vector3.Dot(ra, cam.Forward);
        double bx = Vector3.Dot(rb, cam.Right), by = Vector3.Dot(rb, cam.Up), bz = Vector3.Dot(rb, cam.Forward);

        // برش صفحهٔ نزدیک
        if (az < NearPlane && bz < NearPlane) return;
        if (az < NearPlane)
        {
            double t = (NearPlane - az) / (bz - az);
            ax += (bx - ax) * t; ay += (by - ay) * t; az = NearPlane;
        }
        else if (bz < NearPlane)
        {
            double t = (NearPlane - bz) / (az - bz);
            bx += (ax - bx) * t; by += (ay - by) * t; bz = NearPlane;
        }

        double sx0, sy0, dk0, sx1, sy1, dk1;
        if (cam.Projection == ProjectionKind.Perspective)
        {
            double i0 = 1.0 / az, i1 = 1.0 / bz;
            sx0 = cam.HalfW + ax * cam.Focal * i0; sy0 = cam.HalfH - ay * cam.Focal * i0; dk0 = i0;
            sx1 = cam.HalfW + bx * cam.Focal * i1; sy1 = cam.HalfH - by * cam.Focal * i1; dk1 = i1;
        }
        else
        {
            sx0 = cam.HalfW + ax * cam.OrthoScale; sy0 = cam.HalfH - ay * cam.OrthoScale; dk0 = -az;
            sx1 = cam.HalfW + bx * cam.OrthoScale; sy1 = cam.HalfH - by * cam.OrthoScale; dk1 = -bz;
        }

        DrawScreenLine(pixels, zbuf, pw, ph, sx0, sy0, dk0, sx1, sy1, dk1, color, thickness);
    }

    /// <summary>رسم پاره‌خط صفحه‌ای با تست عمق و ضخامت (برای گرید/محور/اندازه‌گیری).</summary>
    public static void DrawScreenLine(byte[] pixels, float[] zbuf, int pw, int ph,
        double ax, double ay, double ad, double bx, double by, double bd, Rgb color, int thickness = 1)
    {
        if (thickness <= 1)
        {
            BresenhamZ(pixels, zbuf, pw, ph, ax, ay, ad, bx, by, bd, color);
            return;
        }
        for (int o = 1; o < thickness; o++)
        {
            double off = o * 0.75;
            BresenhamZ(pixels, zbuf, pw, ph, ax + off, ay, ad, bx + off, by, bd, color);
            BresenhamZ(pixels, zbuf, pw, ph, ax, ay + off, ad, bx, by + off, bd, color);
        }
    }

    private static void BresenhamZ(byte[] pixels, float[] zbuf, int pw, int ph,
        double ax, double ay, double ad, double bx, double by, double bd, Rgb color)
    {
        int x0 = (int)Math.Round(ax), y0 = (int)Math.Round(ay);
        int x1 = (int)Math.Round(bx), y1 = (int)Math.Round(by);
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        if (dx + dy == 0)
        {
            Put(pixels, zbuf, pw, ph, x0, y0, (ad + bd) / 2, color);
            return;
        }
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy, steps = dx + dy, guard = 0;
        while (guard++ < 100000)
        {
            double t = (double)(guard - 1) / steps;
            double dk = ad + (bd - ad) * t;
            Put(pixels, zbuf, pw, ph, x0, y0, dk, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private static void Put(byte[] pixels, float[] zbuf, int pw, int ph, int x, int y, double dk, Rgb color)
    {
        if ((uint)x >= (uint)pw || (uint)y >= (uint)ph) return;
        int idx = y * pw + x;
        if (dk <= zbuf[idx]) return; // زیر هندسه پنهان
        int o = idx * 4;
        pixels[o] = color.B; pixels[o + 1] = color.G; pixels[o + 2] = color.R;
    }

    // ---------- مقیاس گرافیکی (فاز ۱٫۷) ----------

    /// <summary>نوار مقیاس پایین-چپ بوم: طولی معلوم بر حسب mm در مقیاس فعلی دوربین.</summary>
    public static void DrawScaleBar(byte[] pixels, int pw, int ph, in CameraFrame cam, (Vector3 Min, Vector3 Max) bounds)
    {
        // پیکسل بر میلی‌متر در عمق هدف (صفحهٔ هدف دوربین)
        double pxPerMm = cam.Projection == ProjectionKind.Perspective
            ? cam.Focal / Math.Max(1.0, Math.Abs(Vector3.Dot(cam.Target - cam.Eye, cam.Forward)))
            : cam.OrthoScale;

        // طول خوانا که حدوداً ۱۲۰ پیکسل شود
        double rough = 120 / Math.Max(1e-9, pxPerMm);
        double step = NiceStep(rough);
        int barPx = (int)Math.Round(step * pxPerMm);
        if (barPx < 24 || barPx > pw / 2) return;

        int x0 = 18, y0 = ph - 22;
        var bar = new Rgb(70, 80, 105);
        var frame = new Rgb(250, 252, 255);

        // پس‌زمینهٔ نیم‌شفاف
        for (int y = y0 - 9; y <= y0 + 7; y++)
            for (int x = x0 - 8; x <= x0 + barPx + 8; x++)
                if ((uint)x < (uint)pw && (uint)y < (uint)ph)
                    BlendPixel(pixels, pw, x, y, frame, 0.72);

        // خط + سرچنگولی‌ها
        HLine(pixels, pw, ph, x0, x0 + barPx, y0, bar);
        VLine(pixels, pw, ph, x0, y0 - 5, y0 + 5, bar, 2);
        VLine(pixels, pw, ph, x0 + barPx, y0 - 5, y0 + 5, bar, 2);

        // برچسب متن با فونت پیکسلی ۳×۵ کوچک (بدون وابستگی به WPF)
        string label = FormatMm(step);
        DrawTinyText(pixels, pw, ph, x0, y0 - 20, label, bar);
    }

    private static string FormatMm(double mm)
    {
        if (mm >= 1000) return $"{mm / 1000:0.#} m";
        if (mm >= 10 || Math.Abs(mm - Math.Round(mm)) < 1e-9) return $"{mm:0} mm";
        return $"{mm:0.#} mm";
    }

    private static void HLine(byte[] pixels, int pw, int ph, int x0, int x1, int y, Rgb c)
    {
        for (int x = x0; x <= x1; x++)
            if ((uint)x < (uint)pw && (uint)y < (uint)ph)
            {
                int o = (y * pw + x) * 4;
                pixels[o] = c.B; pixels[o + 1] = c.G; pixels[o + 2] = c.R;
            }
    }

    private static void VLine(byte[] pixels, int pw, int ph, int x, int y0, int y1, Rgb c, int w)
    {
        for (int y = y0; y <= y1; y++)
            for (int k = 0; k < w; k++)
                if ((uint)(x + k) < (uint)pw && (uint)y < (uint)ph)
                {
                    int o = (y * pw + x + k) * 4;
                    pixels[o] = c.B; pixels[o + 1] = c.G; pixels[o + 2] = c.R;
                }
    }

    private static void BlendPixel(byte[] pixels, int pw, int x, int y, Rgb c, double alpha)
    {
        int o = (y * pw + x) * 4;
        double ia = 1 - alpha;
        pixels[o] = (byte)Math.Clamp(c.B * alpha + pixels[o] * ia, 0, 255);
        pixels[o + 1] = (byte)Math.Clamp(c.G * alpha + pixels[o + 1] * ia, 0, 255);
        pixels[o + 2] = (byte)Math.Clamp(c.R * alpha + pixels[o + 2] * ia, 0, 255);
    }

    /// <summary>متن ریز ۳×۵ پیکسلی برای HUD بوم — فقط ارقام، نقطه، و حروف m.</summary>
    private static readonly Dictionary<char, string[]> TinyFont = new()
    {
        ['0'] = new[] { "111", "101", "101", "101", "111" },
        ['1'] = new[] { "010", "110", "010", "010", "111" },
        ['2'] = new[] { "111", "001", "111", "100", "111" },
        ['3'] = new[] { "111", "001", "111", "001", "111" },
        ['4'] = new[] { "101", "101", "111", "001", "001" },
        ['5'] = new[] { "111", "100", "111", "001", "111" },
        ['6'] = new[] { "111", "100", "111", "101", "111" },
        ['7'] = new[] { "111", "001", "010", "010", "010" },
        ['8'] = new[] { "111", "101", "111", "101", "111" },
        ['9'] = new[] { "111", "101", "111", "001", "111" },
        ['.'] = new[] { "000", "000", "000", "000", "010" },
        [' '] = new[] { "000", "000", "000", "000", "000" },
        ['m'] = new[] { "000", "000", "111", "111", "101" },
        ['#'] = new[] { "101", "111", "101", "111", "101" },
    };

    private static void DrawTinyText(byte[] pixels, int pw, int ph, int x0, int y0, string text, Rgb c)
    {
        int x = x0;
        foreach (var ch in text)
        {
            if (!TinyFont.TryGetValue(ch, out var glyph)) { x += 2; continue; }
            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if (glyph[gy][gx] == '1')
                    {
                        int px = x + gx, py = y0 + gy;
                        if ((uint)px < (uint)pw && (uint)py < (uint)ph)
                        {
                            int o = (py * pw + px) * 4;
                            pixels[o] = c.B; pixels[o + 1] = c.G; pixels[o + 2] = c.R;
                        }
                    }
            x += 4;
        }
    }
}
