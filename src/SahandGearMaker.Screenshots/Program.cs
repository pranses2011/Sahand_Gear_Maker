using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Rendering;

namespace SahandGearMaker.Screenshots;

/// <summary>
/// ابزار تولید اسکرین‌شات واقعی برنامه — رندر سه‌بعدی CPU (رندرر نرم‌افزاری خود برنامه)
/// برای همهٔ خانواده‌ها + کپچر نمای رابط WPF. خروجی: PNG در پوشهٔ داده‌شده.
/// فقط برای مستندسازی؛ هیچ داده‌ای ارسال نمی‌شود.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "screenshots";
        Directory.CreateDirectory(outDir);
        int failed = 0;

        // ---------- ۱) رندر سه‌بعدی خانواده‌ها با رندرر خود برنامه ----------
        (string FileName, string Title, GearFamily Family, Action<ToothParameters, BodyFeatures, FamilyParameters> Setup)[] shots =
        {
            ("gear-external-spur", "چرخ‌دندهٔ خارجی مستقیم", GearFamily.ExternalSpur, (t, b, s) => { t.Module = 2.5; t.ToothCount = 24; t.FaceWidth = 16; b.BoreDiameter = 10; }),
            ("gear-external-helical", "چرخ‌دندهٔ مارپیچ", GearFamily.ExternalHelical, (t, b, s) => { t.Module = 2.5; t.ToothCount = 24; t.FaceWidth = 22; t.HelixAngle = UnitConverter.DegToRad(22); b.BoreDiameter = 10; }),
            ("gear-herringbone", "چرخ‌دندهٔ جناغی", GearFamily.ExternalHerringbone, (t, b, s) => { t.Module = 2.5; t.ToothCount = 23; t.FaceWidth = 26; t.HelixAngle = UnitConverter.DegToRad(28); b.BoreDiameter = 10; }),
            ("gear-internal", "چرخ‌دندهٔ داخلی", GearFamily.InternalSpur, (t, b, s) => { t.Module = 2.5; t.ToothCount = 40; t.FaceWidth = 14; b.BoreDiameter = 0; }),
            ("gear-bevel", "چرخ‌دندهٔ مخروطی", GearFamily.BevelSpur, (t, b, s) => { t.Module = 3; t.ToothCount = 22; t.FaceWidth = 16; b.BoreDiameter = 12; s.BevelPitchConeAngle = Math.PI / 4; }),
            ("gear-bevel-spiral", "مخروطی مارپیچ", GearFamily.BevelSpiral, (t, b, s) => { t.Module = 3; t.ToothCount = 20; t.FaceWidth = 15; b.BoreDiameter = 10; s.BevelPitchConeAngle = Math.PI / 4; s.BevelSpiralAngle = UnitConverter.DegToRad(32); }),
            ("gear-worm", "کرم حلزونی", GearFamily.Worm, (t, b, s) => { t.Module = 2.5; t.ToothCount = 3; t.FaceWidth = 45; b.BoreDiameter = 12; s.WormPitchDiameterMm = 40; }),
            ("gear-worm-wheel", "چرخ کرم", GearFamily.WormWheel, (t, b, s) => { t.Module = 2.5; t.ToothCount = 36; t.FaceWidth = 0; b.BoreDiameter = 16; b.HubDiameter = 56; b.HubLength = 8; s.WormPitchDiameterMm = 40; s.WormStartsCount = 3; }),
            ("gear-crown-face", "چرخ تاجی", GearFamily.CrownFace, (t, b, s) => { t.Module = 2; t.ToothCount = 32; t.FaceWidth = 18; b.BoreDiameter = 14; }),
            ("gear-noncircular", "چرخ بیضوی", GearFamily.NonCircular, (t, b, s) => { t.Module = 2; t.ToothCount = 28; t.FaceWidth = 10; b.BoreDiameter = 6; s.NonCircularEccentricity = 0.5; }),
            ("gear-harmonic-flexspline", "فِلکسی‌پلاین هارمونیک", GearFamily.HarmonicDrive, (t, b, s) => { t.Module = 0.8; t.ToothCount = 160; t.FaceWidth = 12; b.BoreDiameter = 24; b.LighteningHoleCount = 6; b.LighteningHoleDiameter = 5; b.LighteningHoleCircleRadius = 26; }),
            ("gear-spline", "هزارخار", GearFamily.SplineExternal, (t, b, s) => { t.Module = 1.5; t.ToothCount = 18; t.FaceWidth = 22; b.BoreDiameter = 0; s.SplineBacklashMm = 0.05; }),
            ("gear-rack", "شانهٔ مستقیم", GearFamily.Rack, (t, b, s) => { t.Module = 2.5; t.ToothCount = 20; t.FaceWidth = 14; s.RackLengthMm = 120; }),
        };

        foreach (var (fileName, title, family, setup) in shots)
        {
            try
            {
                var part = new GearPart { Name = title, Family = family };
                setup(part.Teeth, part.Body, part.Specific);
                var result = GearGeometryEngine.BuildPart(part);
                RenderMeshPng(Path.Combine(outDir, fileName + ".png"), result.Mesh, part.DisplayColor);
                Console.WriteLine($"[موفق] {fileName}.png — {result.Mesh.TriangleCount:N0} مثلث");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[ناموفق] {fileName}: {ex.Message}");
            }
        }

        // ---------- ۲) کپچر رابط کاربری (WPF) ----------
        try
        {
            CaptureUi(Path.Combine(outDir, "ui-main.png"));
            Console.WriteLine("[موفق] ui-main.png");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"[ناموفق] ui-main: {ex.Message}");
        }

        return failed == 0 ? 0 : 2;
    }

    /// <summary>رندر مش با رندرر نرم‌افزاری خود برنامه و ذخیرهٔ PNG.</summary>
    private static void RenderMeshPng(string path, TriMesh mesh, string colorHex)
    {
        const int W = 1000, H = 760;
        var renderer = new SoftwareRenderer(W, H);
        renderer.Clear(248, 250, 252);

        var (min, max) = mesh.Bounds();
        var center = new Vector3(
            (min.X + max.X) / 2,
            (min.Y + max.Y) / 2,
            (min.Z + max.Z) / 2);
        double radius = Math.Max(
            Math.Max(max.X - min.X, max.Y - min.Y),
            max.Z - min.Z) / 2;
        double distance = Math.Max(30, radius * 3.2);

        var camera = new OrbitCamera
        {
            Target = center,
            Distance = distance,
            Azimuth = UnitConverter.DegToRad(38),
            Elevation = UnitConverter.DegToRad(22),
        };

        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        renderer.Render(mesh, camera, SoftwareRenderer.DisplayMode.ShadedWithEdges, color);

        var bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
        renderer.Blit(bmp);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        enc.Save(fs);
    }

    /// <summary>کپچر پنجرهٔ اصلی برنامه با RTB (بدون نمایش روی صفحه).</summary>
    private static void CaptureUi(string path)
    {
        var app = new SahandGearMaker.App.App();
        app.InitializeComponent(); // بارگذاری منابع App.xaml (فونت وزیرمتن/پالت)
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var window = new SahandGearMaker.App.MainWindow();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 0;
        window.Top = 0;
        window.Width = 1360;
        window.Height = 860;
        window.Show();

        // پمپ دیسپچر برای چیدمان، binding و رندر
        for (int i = 0; i < 12; i++)
        {
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }
        window.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(path, FileMode.Create);
        enc.Save(fs);

        window.Close();
        app.Shutdown();
    }
}
