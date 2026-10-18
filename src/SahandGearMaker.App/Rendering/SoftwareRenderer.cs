using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Rendering;

/// <summary>
/// دوربین مداری (Orbit) برای نمای سه‌بعدی — چرخش/pan/zoom با ماوس و صفحه‌کلید (سند ۰۲، بخش ۶.۳).
/// </summary>
public sealed class OrbitCamera
{
    /// <summary>فاصله از هدف.</summary>
    public double Distance { get; set; } = 160;

    /// <summary>زاویهٔ آزیموت حول محور Z (رادیان).</summary>
    public double Azimuth { get; set; } = Math.PI / 6;

    /// <summary>زاویهٔ ارتفاع از صفحهٔ XY (رادیان).</summary>
    public double Elevation { get; set; } = Math.PI / 5;

    /// <summary>نقطهٔ هدف (مرکز نگاه).</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    public OrbitCamera Pan(double dxScreen, double dyScreen)
    {
        // پن در صفحهٔ صفحه بینندگی — تقریب خطی با گام نسبت به فاصله
        double panScale = Distance * 0.0016;
        double cosA = Math.Cos(Azimuth), sinA = Math.Sin(Azimuth);
        Target = new Vector3(
            Target.X - (dxScreen * cosA) * panScale,
            Target.Y - (dxScreen * sinA) * panScale,
            Target.Z + dyScreen * panScale);
        return this;
    }

    public OrbitCamera Rotate(double dAzimuth, double dElevation)
    {
        Azimuth += dAzimuth;
        Elevation = Math.Clamp(Elevation + dElevation, -1.45, 1.45);
        return this;
    }

    public OrbitCamera Zoom(double factor) => ZoomTo(Distance * factor);

    public OrbitCamera ZoomTo(double distance)
    {
        Distance = Math.Clamp(distance, 5, 5000);
        return this;
    }

    /// <summary>محاسبهٔ ماتریس نما (View) به‌صورت ردیف‌های راست‌به‌چپ ساده.</summary>
    public (Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        double ce = Math.Cos(Elevation), se = Math.Sin(Elevation);
        var forward = new Vector3(
            -ce * Math.Cos(Azimuth),
            -ce * Math.Sin(Azimuth),
            -se).Normalized();
        var eye = Target + forward * -Distance;
        var worldUp = Vector3.UnitZ;
        var right = Vector3.Cross(worldUp, forward).Normalized();
        var up = Vector3.Cross(forward, right).Normalized();
        return (eye, forward, right, up);
    }
}

/// <summary>
/// رندرر نرم‌افزاری سبک: رستر‌سازی مثلث‌ها با Z-Buffer روی WriteableBitmap.
/// بدون وابستگی native/گرافیکی — کاملاً آفلاین و قابل‌نگهداری (تصمیم معماری سند ARCHITECTURE).
/// نورپردازی: lambert ساده + محیط؛ نمایش‌های سایه‌دار/لبه‌دار/سیمی (سند ۰۲، بخش ۶.۳).
/// </summary>
public sealed class SoftwareRenderer
{
    private readonly float[] _zbuffer;
    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly double _halfW;
    private readonly double _halfH;

    public enum DisplayMode
    {
        Shaded,
        Wireframe,
        ShadedWithEdges,
    }

    public SoftwareRenderer(int width, int height)
    {
        _width = Math.Max(2, width);
        _height = Math.Max(2, height);
        _halfW = _width / 2.0;
        _halfH = _height / 2.0;
        _pixels = new byte[_width * _height * 4];
        _zbuffer = new float[_width * _height];
    }

    public void Clear(byte r, byte g, byte b)
    {
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = b;
            _pixels[i + 1] = g;
            _pixels[i + 2] = r;
            _pixels[i + 3] = 255;
        }
        Array.Fill(_zbuffer, float.PositiveInfinity);
    }

    /// <summary>رندر مش با دوربین داده‌شده.</summary>
    public void Render(TriMesh mesh, OrbitCamera camera, DisplayMode mode, Color baseColor)
    {
        var (eye, forward, right, up) = camera.Basis();
        var verts = mesh.Vertices;

        // تبدیل به فضای دوربین و سپس صفحه
        var projected = new (double X, double Y, double Z, double Depth)[mesh.VertexCount];
        double focal = _halfH * 1.8; // فاصلهٔ کانونی مجازی
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = verts[i] - eye;
            double camX = Vector3.Dot(p, right);
            double camY = Vector3.Dot(p, up);
            double camZ = -Vector3.Dot(p, forward); // به سمت جلو مثبت
            double invZ = camZ > 1e-6 ? 1.0 / camZ : double.NaN;
            projected[i] = (
                _halfW + camX * focal * invZ,
                _halfH - camY * focal * invZ,
                camZ,
                camZ);
        }

        foreach (var t in mesh.Triangles)
        {
            var (a, b, c) = (projected[t.A], projected[t.B], projected[t.C]);
            if (double.IsNaN(a.X) || double.IsNaN(b.X) || double.IsNaN(c.X))
                continue; // رأس پشت دوربین

            if (mode == DisplayMode.Wireframe)
            {
                DrawLine(a, b, baseColor);
                DrawLine(b, c, baseColor);
                DrawLine(c, a, baseColor);
                continue;
            }

            // نور ساده با نرمال مثلث
            var n = t.Normal(verts);
            double lambert = Math.Abs(Vector3.Dot(n, forward)) * 0.75 + 0.25;
            var shade = Color.FromRgb(
                (byte)Math.Clamp(baseColor.R * lambert, 0, 255),
                (byte)Math.Clamp(baseColor.G * lambert, 0, 255),
                (byte)Math.Clamp(baseColor.B * lambert, 0, 255));
            FillTriangle(a, b, c, shade);

            if (mode == DisplayMode.ShadedWithEdges)
            {
                var edgeColor = Color.FromRgb(30, 41, 59);
                DrawLine(a, b, edgeColor);
                DrawLine(b, c, edgeColor);
                DrawLine(c, a, edgeColor);
            }
        }
    }

    /// <summary>کپی نهایی به WriteableBitmap.</summary>
    public void Blit(WriteableBitmap bmp)
    {
        if (bmp.PixelWidth != _width || bmp.PixelHeight != _height)
            throw new InvalidOperationException("اندازهٔ بوم با رندرر هم‌خوان نیست.");
        bmp.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
    }

    private void PutPixel(int x, int y, double z, Color color)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) return;
        int idx = y * _width + x;
        if (z < _zbuffer[idx])
        {
            _zbuffer[idx] = (float)z;
            int o = idx * 4;
            _pixels[o] = color.B;
            _pixels[o + 1] = color.G;
            _pixels[o + 2] = color.R;
            _pixels[o + 3] = 255;
        }
    }

    private void FillTriangle((double X, double Y, double Z, double Depth) a,
                              (double X, double Y, double Z, double Depth) b,
                              (double X, double Y, double Z, double Depth) c, Color color)
    {
        int minX = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)));
        int maxX = (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)));
        int minY = (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)));
        int maxY = (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)));

        double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-9) return;
        double invArea = 1.0 / area;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double w0 = ((b.X - a.X) * (py - a.Y) - (px - a.X) * (b.Y - a.Y)) * invArea;
                double w1 = ((px - c.X) * (b.Y - c.Y) - (b.X - c.X) * (py - c.Y)) * invArea;
                double w2 = 1.0 - w0 - w1;
                if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                {
                    double z = w0 * c.Depth + w1 * a.Depth + w2 * b.Depth;
                    PutPixel(x, y, z, color);
                }
            }
        }
    }

    private void DrawLine((double X, double Y, double Z, double Depth) a,
                          (double X, double Y, double Z, double Depth) b, Color color)
    {
        int x0 = (int)Math.Round(a.X), y0 = (int)Math.Round(a.Y);
        int x1 = (int)Math.Round(b.X), y1 = (int)Math.Round(b.Y);
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int steps = dx + dy;
        if (steps == 0) { PutPixel(x0, y0, Math.Min(a.Depth, b.Depth), color); return; }

        int guard = 0;
        while (guard++ < 100000)
        {
            double t = steps == 0 ? 0 : Math.Clamp((double)(guard - 1) / steps, 0, 1);
            double z = a.Depth + (b.Depth - a.Depth) * t;
            PutPixel(x0, y0, z, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }
}
