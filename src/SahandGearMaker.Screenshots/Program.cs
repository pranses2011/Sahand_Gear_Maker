using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;

namespace SahandGearMaker.Screenshots;

/// <summary>
/// ابزار تولید اسکرین‌شات واقعی برنامه — فاز ۱: رندر صحنه با موتور جدید (کتابخانهٔ Scene)
/// برای همهٔ خانواده‌ها + صحنه‌های چندقطعه‌ای/گرید/حالت‌ها/هایلایت + کپچر نمای رابط WPF.
/// خروجی: PNG در پوشهٔ داده‌شده. فقط برای مستندسازی؛ هیچ داده‌ای ارسال نمی‌شود.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "screenshots";
        Directory.CreateDirectory(outDir);
        int failed = 0;

        // ---------- ۱) رندر سه‌بعدی خانواده‌ها با موتور جدید (فاز ۱) ----------
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
                RenderScenePng(Path.Combine(outDir, fileName + ".png"),
                    new[] { MakeObject("solo", result.Mesh, part.DisplayColor) },
                    grid: false, axes: false);
                Console.WriteLine($"[موفق] {fileName}.png — {result.Mesh.TriangleCount:N0} مثلث");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[ناموفق] {fileName}: {FullChain(ex)}");
            }
        }

        // ---------- ۲) صحنه‌های فاز ۱ (پذیرش فاز ۱: ۵ اسکرین‌شات جدید) ----------
        try
        {
            // الف) صحنهٔ چندقطعه‌ای: جفت خارجی درگیر + قطر گام درست
            var pinion = new GearPart { Name = "پینیون", Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = 2.5, ToothCount = 20, FaceWidth = 16 }, Body = new BodyFeatures { BoreDiameter = 10 } };
            var wheel = new GearPart { Name = "چرخ بزرگ", Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = 2.5, ToothCount = 36, FaceWidth = 16 }, Body = new BodyFeatures { BoreDiameter = 16 } };
            var bp = GearGeometryEngine.BuildPart(pinion);
            var bw = GearGeometryEngine.BuildPart(wheel);
            double rp = 2.5 * 20 / 2.0, rw = 2.5 * 36 / 2.0;
            var objs = new[]
            {
                MakeObject("pinion", bp.Mesh, "#2563EB", x: -rp, theta: 0),
                MakeObject("wheel", bw.Mesh, "#0D9488", x: rp + 0.5, theta: Math.PI / 20 + Math.PI / 36 * 0), // چرخش فاز تماس
            };
            objs[1].Transform = new ObjectTransform(new Vector3(rp + 0.2, 0, 0), Math.PI / 20, 1.0);
            RenderScenePng(Path.Combine(outDir, "scene-multipart.png"), objs, grid: true, axes: true);
            Console.WriteLine("[موفق] scene-multipart.png");
        }
        catch (Exception ex) { failed++; Console.WriteLine($"[ناموفق] scene-multipart: {ex.Message}"); }

        try
        {
            // ب) گرید + محورها (فاز ۱٫۷)
            var part = new GearPart { Name = "grid", Family = GearFamily.ExternalHelical,
                Teeth = new ToothParameters { Module = 2.5, ToothCount = 28, FaceWidth = 22, HelixAngle = UnitConverter.DegToRad(24) },
                Body = new BodyFeatures { BoreDiameter = 10, LighteningHoleCount = 5, LighteningHoleDiameter = 7, LighteningHoleCircleRadius = 22 } };
            var build = GearGeometryEngine.BuildPart(part);
            RenderScenePng(Path.Combine(outDir, "scene-grid-axes.png"),
                new[] { MakeObject("g", build.Mesh, "#2563EB") }, grid: true, axes: true);
            Console.WriteLine("[موفق] scene-grid-axes.png");
        }
        catch (Exception ex) { failed++; Console.WriteLine($"[ناموفق] scene-grid-axes: {ex.Message}"); }

        try
        {
            // ج) حالت‌های نمایش: سیمی و سایه خالص
            var part = new GearPart { Name = "modes", Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = 2, ToothCount = 32, FaceWidth = 14 }, Body = new BodyFeatures { BoreDiameter = 12 } };
            var build = GearGeometryEngine.BuildPart(part);
            RenderScenePng(Path.Combine(outDir, "scene-wireframe.png"),
                new[] { MakeObject("w", build.Mesh, "#334155") }, grid: false, axes: false, mode: DisplayMode.Wireframe);
            RenderScenePng(Path.Combine(outDir, "scene-shaded.png"),
                new[] { MakeObject("s", build.Mesh, "#2563EB") }, grid: false, axes: false, mode: DisplayMode.Shaded);
            Console.WriteLine("[موفق] scene-wireframe.png + scene-shaded.png");
        }
        catch (Exception ex) { failed++; Console.WriteLine($"[ناموفق] scene-modes: {ex.Message}"); }

        try
        {
            // د) هایلایت انتخاب + شفافیت (فاز ۱٫۱۹/۱٫۲۲)
            var a = new GearPart { Name = "a", Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = 2, ToothCount = 24, FaceWidth = 14 }, Body = new BodyFeatures { BoreDiameter = 8 } };
            var b2 = new GearPart { Name = "b", Family = GearFamily.InternalSpur,
                Teeth = new ToothParameters { Module = 2, ToothCount = 60, FaceWidth = 12 }, Body = new BodyFeatures() };
            var ba = GearGeometryEngine.BuildPart(a);
            var bb = GearGeometryEngine.BuildPart(b2);
            var hl = MakeObject("a", ba.Mesh, "#2563EB");
            var ghost = MakeObject("b", bb.Mesh, "#94A3B8");
            ghost.Opacity = 0.45;
            RenderScenePng(Path.Combine(outDir, "scene-highlight.png"),
                new[] { ghost, hl }, grid: true, axes: true, highlightId: "a");
            Console.WriteLine("[موفق] scene-highlight.png");
        }
        catch (Exception ex) { failed++; Console.WriteLine($"[ناموفق] scene-highlight: {ex.Message}"); }

        // ---------- ۳) کپچر رابط کاربری (WPF) ----------
        // کپچر UI از این پس توسط خود برنامهٔ واقعی انجام می‌شود:
        //   SahandGearDesigner.exe --screenshot <پوشه>
        // اجرا داخل اسمبلی درست خود برنامه، خطاهای بارگذاری BAML/منابع نسبی
        // (DeferredBinaryDeserializerExtension) را از ریشه حذف می‌کند — فاز تثبیت رلیز.

        if (failed == 0) Console.WriteLine("[تمام] همهٔ اسکرین‌شات‌های موتور ساخته شد.");
        else Console.WriteLine($"[هشدار] {failed} اسکرین‌شات ناموفق بود.");
        return failed == 0 ? 0 : 2;
    }

    /// <summary>زنجیرهٔ کامل پیام‌های خطا — برای تشخیص دقیق در CI (InnerException ها دیگری پنهان می‌کنند).</summary>
    private static string FullChain(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (sb.Length > 0) sb.Append(" ← ");
            sb.Append($"[{cur.GetType().Name}] {cur.Message}");
        }
        return sb.ToString();
    }

    private static SceneObject MakeObject(string id, TriMesh mesh, string colorHex,
        double x = 0, double y = 0, double theta = 0)
    {
        var obj = new SceneObject
        {
            Id = id,
            Name = id,
            Mesh = mesh,
            LocalBounds = mesh.Bounds(),
            Color = Rgb.FromHex(colorHex),
            Transform = new ObjectTransform(new Vector3(x, y, 0), theta, 1.0),
        };
        obj.EnsureVertexNormals();
        return obj;
    }

    /// <summary>رندر صحنه با موتور جدید (SSAA×2 + گورا) و ذخیرهٔ PNG — فاز ۱.</summary>
    private static void RenderScenePng(string path, SceneObject[] objects, bool grid, bool axes,
        DisplayMode mode = DisplayMode.ShadedWithEdges, string? highlightId = null)
    {
        const int W = 1000, H = 760;
        var renderer = new SceneRenderer();
        renderer.Resize(W, H, 2); // SSAA×2 — مثل CI در سکون

        var camera = new SceneCamera();
        var bounds = SceneRenderer.CombinedBounds(objects);
        double size = Math.Max(1, Math.Max(
            bounds.Max.X - bounds.Min.X,
            Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z)));
        camera.SetView(UnitConverter.DegToRad(38), UnitConverter.DegToRad(24), size * 2.6,
            new Vector3(
                (bounds.Min.X + bounds.Max.X) / 2,
                (bounds.Min.Y + bounds.Max.Y) / 2,
                (bounds.Min.Z + bounds.Max.Z) / 2), animate: false);

        var frame = camera.ComputeFrame(W, H);
        var stats = renderer.Render(objects, frame, new SahandGearMaker.Scene.RenderOptions
        {
            Mode = mode,
            ShowGrid = grid,
            ShowAxes = axes,
            HighlightId = highlightId,
        }, 0);

        var bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, W, H), renderer.Output.ToArray(), W * 4, 0);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        enc.Save(fs);
        _ = stats;
    }
}
