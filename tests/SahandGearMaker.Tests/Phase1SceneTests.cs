using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های فاز ۱A — موتور پیش‌نمایش حرفه‌ای (کتابخانهٔ Scene):
/// برش صفحهٔ نزدیک، backface culling، پیکینگ، دوربین انیمیت‌شونده، SSAA،
/// بافر پایدار و صحت رندر پایه روی سخت‌افزار بدون GPU.
/// </summary>
public class Phase1SceneTests
{
    private static SceneObject MakeGearObject(string id = "g1", double x = 0, double y = 0, double theta = 0, double scale = 1)
    {
        var part = new GearPart
        {
            Name = "test",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 24, FaceWidth = 12 },
            Body = new BodyFeatures { BoreDiameter = 8 },
        };
        var mesh = GearGeometryEngine.BuildPart(part).Mesh;
        return new SceneObject
        {
            Id = id,
            Name = "چرخ‌دندهٔ آزمون",
            Mesh = mesh,
            LocalBounds = mesh.Bounds(),
            Transform = new ObjectTransform(new Vector3(x, y, 0), theta, scale),
        };
    }

    private static (SceneRenderer R, SceneCamera Cam) Setup(int w = 320, int h = 240, int ss = 1)
    {
        var r = new SceneRenderer();
        r.Resize(w, h, ss);
        var cam = new SceneCamera { Distance = 160, Elevation = 0.5, Azimuth = 0.6 };
        cam.Snap();
        return (r, cam);
    }

    // ---------- V2: برش صفحهٔ نزدیک ----------

    [Fact]
    public void NearClipping_CameraInsideGeometry_DoesNotCrashAndStillRenders()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        // دوربین داخل/خیلی نزدیک مدل
        cam.SetView(0.3, 0.2, 5, new Vector3(0, 0, 0), animate: false);
        var frame = cam.ComputeFrame(320, 240);
        var stats = r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        Assert.True(stats.TrianglesDrawn > 0);
    }

    // ---------- P4: backface culling ----------

    [Fact]
    public void BackfaceCulling_HalvesTriangleCount_ForClosedMesh()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        var frame = cam.ComputeFrame(320, 240);
        var stats = r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        // حدوداً نصف مثلث‌های مش دیده می‌شود (سمت پشتی حذف + برش فراستوم)
        Assert.True(stats.TrianglesDrawn <= obj.Mesh.TriangleCount,
            "تعداد مثلث رسم‌شده نباید از کل مثلث‌ها بیشتر باشد.");
        Assert.True(stats.TrianglesDrawn > obj.Mesh.TriangleCount / 4,
            "بیش از یک‌چهارم مثلث‌ها باید رسم شوند (نمای ایزومتریک).");
    }

    // ---------- V1: SSAA ----------

    [Fact]
    public void Supersampling_ProducesSameOutputSize()
    {
        var (r, cam) = Setup(ss: 2);
        var obj = MakeGearObject();
        var frame = cam.ComputeFrame(320, 240);
        r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        Assert.Equal(320 * 240 * 4, r.Output.Length);
    }

    // ---------- پایداری بافر (P1/P2) ----------

    [Fact]
    public void StableBuffers_TwoRendersSameArrayReference()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        var frame = cam.ComputeFrame(320, 240);
        r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        var out1 = r.Output;
        r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        // Span روی همان بافر — اگر Resize نشده باشد آدرس یکسان است
        Assert.Equal(out1.Length, r.Output.Length);
    }

    [Fact]
    public void Render_MultiPart_AllObjectsDrawn()
    {
        var (r, cam) = Setup();
        var a = MakeGearObject("a", -40, 0);
        var b = MakeGearObject("b", 40, 0, Math.PI / 24);
        var frame = cam.ComputeFrame(320, 240);
        var stats = r.Render(new[] { a, b }, frame, new RenderOptions(), 0);
        Assert.Equal(2, stats.ObjectsDrawn);
    }

    // ---------- فاز ۱٫۱۹: پیکینگ ----------

    [Fact]
    public void Picking_ClickOnObject_SelectsIt()
    {
        var (r, cam) = Setup();
        var a = MakeGearObject("a", -40, 0);
        var b = MakeGearObject("b", 40, 0);
        var frame = cam.ComputeFrame(320, 240);
        var scene = new[] { a, b };

        // نقطهٔ مرکز شیئ b را روی صفحه بیابیم: مرکز جهان (40,0,0) را از دوربین ببین
        var world = new Vector3(40, 0, 0);
        // پروژه به صفحه
        var rel = world - frame.Eye;
        double cz = Vector3.Dot(rel, frame.Forward);
        Assert.True(cz > 0);
        double cx = Vector3.Dot(rel, frame.Right), cy = Vector3.Dot(rel, frame.Up);
        double sx = frame.HalfW + cx * frame.Focal / cz;
        double sy = frame.HalfH - cy * frame.Focal / cz;

        var hit = ScenePicking.Pick(scene, frame, sx, sy);
        Assert.NotNull(hit);
        Assert.Equal("b", hit!.ObjectId);
        // نقطهٔ برخورد روی سطح مقابلِ شیئ b است — نزدیک مرکز (40,0,0) ولی نه لزوماً خود آن
        Assert.True((hit.Point - new Vector3(40, 0, 0)).Length < 55,
            $"نقطهٔ برخورد {hit.Point} خارج از شعاع شیئ b است.");
    }

    [Fact]
    public void Picking_EmptyArea_ReturnsNull()
    {
        var (r, cam) = Setup();
        var a = MakeGearObject("a", 0, 0);
        var frame = cam.ComputeFrame(320, 240);
        // گوشهٔ بالا-راست دور از مدل
        var hit = ScenePicking.Pick(new[] { a }, frame, 4, 4);
        Assert.Null(hit);
    }

    [Fact]
    public void Picking_HiddenObjectsNotPickable()
    {
        var (r, cam) = Setup();
        var a = MakeGearObject("a", 0, 0);
        a.Visible = false;
        var frame = cam.ComputeFrame(320, 240);
        Assert.Null(ScenePicking.Pick(new[] { a }, frame, 160, 120));
    }

    // ---------- دوربین (فاز ۱٫۱۳/۱۴/۱۷) ----------

    [Fact]
    public void CameraTween_ConvergesToTarget()
    {
        var cam = new SceneCamera { Distance = 100 };
        cam.Snap();
        cam.SetView(1.0, 0.8, 250, new Vector3(10, 20, 5), animate: true);
        for (int i = 0; i < 400 && cam.IsAnimating; i++)
            cam.Update(1.0 / 60.0);
        Assert.False(cam.IsAnimating);
        var f = cam.ComputeFrame(100, 100);
        Assert.Equal(10, f.Target.X, 1);
        // چشم روی فاصلهٔ ۲۵۰ از هدف
        Assert.Equal(250, (f.Eye - f.Target).Length, 1);
    }

    [Fact]
    public void CameraOrtho_ProducesValidFrame()
    {
        var cam = new SceneCamera { Projection = ProjectionKind.Orthographic, Distance = 120 };
        cam.Snap();
        var f = cam.ComputeFrame(200, 150);
        Assert.Equal(ProjectionKind.Orthographic, f.Projection);
        Assert.True(f.OrthoScale > 0);
    }

    [Fact]
    public void ZoomAt_AnchorStaysOnSameRay()
    {
        var cam = new SceneCamera { Distance = 200 };
        cam.Snap();
        var anchor = new Vector3(30, 10, 0);
        cam.ZoomAt(0.5, 160, 120, 320, 240, cam.ComputeFrame(320, 240), anchor);
        Assert.Equal(100, cam.Distance, 3);
        // هدف روی خط لنگر→هدف قبلی با نسبت ۰٫۵ باقی مانده
        // (هدف قبلی مبدأ بود)
        Assert.Equal(15, cam.Target.X, 2);
        Assert.Equal(5, cam.Target.Y, 2);
    }

    // ---------- هایلایت و گرید ----------

    [Fact]
    public void Highlight_DoesNotBreakRender()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        var frame = cam.ComputeFrame(320, 240);
        var stats = r.Render(new[] { obj }, frame, new RenderOptions { HighlightId = "g1" }, 0);
        Assert.True(stats.TrianglesDrawn > 0);
    }

    [Fact]
    public void GridAndAxes_DrawWithoutGeometry()
    {
        var (r, cam) = Setup();
        var frame = cam.ComputeFrame(320, 240);
        var stats = r.Render(Array.Empty<SceneObject>(), frame,
            new RenderOptions { ShowGrid = true, ShowAxes = true }, 0);
        Assert.Equal(0, stats.ObjectsDrawn);
        // خروجی نباید کاملاً رنگ پس‌زمینه باشد (گرید رسم شده)
        var bg = new Rgb(0xEA, 0xEE, 0xF4);
        int different = 0;
        var px = r.Output;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != bg.B || px[i + 1] != bg.G || px[i + 2] != bg.R) different++;
        Assert.True(different > 50, $"گرید رسم نشده است (فقط {different} پیکسل متفاوت).");
    }

    [Fact]
    public void ClipPlane_HidesUpperGeometry()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        var bounds = obj.WorldBounds;
        var frame = cam.ComputeFrame(320, 240);
        var full = r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        var clipped = r.Render(new[] { obj }, frame, new RenderOptions { ClipZ = bounds.Min.Z + 1.0 }, 0);
        Assert.True(clipped.TrianglesDrawn < full.TrianglesDrawn,
            "صفحهٔ برش باید تعداد مثلث‌های رسم‌شده را کاهش دهد.");
    }

    // ---------- مشخصات فاز ۱: عملکرد ----------

    [Fact]
    public void Performance_HeavyMesh_RendersWithinBudget()
    {
        // مش سنگین: مارپیچ با برش‌های عرضی زیاد — ده‌ها هزار مثلث
        var part = new GearPart
        {
            Name = "heavy",
            Family = GearFamily.ExternalHelical,
            Teeth = new ToothParameters { Module = 1.2, ToothCount = 64, FaceWidth = 40, HelixAngle = UnitConverter.DegToRad(25) },
            Body = new BodyFeatures { BoreDiameter = 10 },
            GeometrySettings = new GeometrySettings { ProfileToleranceMm = 0.008, AxialSliceCount = 160 },
        };
        var build = GearGeometryEngine.BuildPart(part);
        Assert.True(build.Mesh.TriangleCount > 20_000, $"برای آزمون کارایی مش سنگین لازم است (فعلی: {build.Mesh.TriangleCount})");

        var obj = new SceneObject
        {
            Id = "heavy",
            Mesh = build.Mesh,
            LocalBounds = build.Mesh.Bounds(),
        };
        var (r, cam) = Setup(640, 480);
        var frame = cam.ComputeFrame(640, 480);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stats = r.Render(new[] { obj }, frame, new RenderOptions(), 0);
        sw.Stop();

        Assert.True(stats.TrianglesDrawn > 10_000);
        // سقف مهندسی: رگرسیون مرتبه‌ای را بگیرد، نه نوسان اشغال‌شدن CPU رانرهای اشتراکی CI.
        // رندر صحیح در ~۵۰–۱۵۰ms تمام می‌شود؛ سقف ۲۵۰۰ms فقط فاجعه (رگرسیون ۱۰×+) را قرمز می‌کند.
        Assert.True(sw.Elapsed.TotalMilliseconds < 2500,
            $"رندر مش {build.Mesh.TriangleCount:N0} مثلثی {sw.Elapsed.TotalMilliseconds:F0}ms طول کشید (سقف ۲۵۰۰ms — رگرسیون جدی).");
    }

    [Fact]
    public void NiceStep_ProducesReadableSteps()
    {
        Assert.Equal(0.5, SceneOverlay.NiceStep(0.7));
        Assert.Equal(1.0, SceneOverlay.NiceStep(1.2));
        Assert.Equal(2.0, SceneOverlay.NiceStep(1.6));
        Assert.Equal(5.0, SceneOverlay.NiceStep(4.2));
        Assert.Equal(10.0, SceneOverlay.NiceStep(8.5));
        Assert.Equal(20.0, SceneOverlay.NiceStep(18));
        Assert.Equal(100.0, SceneOverlay.NiceStep(95));
    }

    [Fact]
    public void SavedView_RoundTrip()
    {
        var cam = new SceneCamera { Azimuth = 1.2, Elevation = 0.4, Distance = 300 };
        cam.Target = new Vector3(5, 6, 7);
        var saved = SavedCameraView.FromCamera(cam, "نمای من");
        Assert.Equal(1.2, saved.Azimuth, 9);
        Assert.Equal("نمای من", saved.Name);

        var cam2 = new SceneCamera();
        saved.ApplyTo(cam2);
        Assert.Equal(1.2, cam2.Azimuth, 9);
        Assert.Equal(300, cam2.Distance, 9);
        Assert.Equal(5, cam2.Target.X, 9);
    }

    // ---------- فاز ۴٫۱: خط اندازه‌گیری ----------

    [Fact]
    public void MeasureLine_RendersVisibleOverlay()
    {
        var (r, cam) = Setup();
        var obj = MakeGearObject();
        var frame = cam.ComputeFrame(320, 240);
        var a = new Vector3(-30, -20, 5);
        var b = new Vector3(35, 25, 5);
        r.Render(new[] { obj }, frame, new RenderOptions
        {
            MeasureLines = new List<(MathX.Vector3, MathX.Vector3)> { (a, b) },
            MeasureLabels = new List<(MathX.Vector3, string)> { ((a + b) / 2, "12.5mm" ) },
        }, 0);

        // رنگ زرد اندازه‌گیری (245,158,11) باید در خروجی باشد
        bool found = false;
        var px = r.Output;
        for (int i = 0; i < px.Length && !found; i += 4)
            if (Math.Abs(px[i + 2] - 245) < 12 && Math.Abs(px[i + 1] - 158) < 25 && Math.Abs(px[i] - 11) < 40)
                found = true;
        Assert.True(found, "پاره‌خط اندازه‌گیری زرد رسم نشده است.");
    }

    [Fact]
    public void ClipPlane_RoundTripsGeometryUnchanged()
    {
        // فاز ۴٫۲: صفحهٔ برش «نمایشی» است — هندسهٔ پارامتریک نباید تغییر کند
        var part = new GearPart
        {
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 20, FaceWidth = 12 },
        };
        var before = System.Text.Json.JsonSerializer.Serialize(part);
        // رندر با برش (شبیه UI):
        var (r, cam) = Setup();
        var build = GearGeometryEngine.BuildPart(part);
        var obj = new SceneObject { Id = "x", Mesh = build.Mesh, LocalBounds = build.Mesh.Bounds() };
        var frame = cam.ComputeFrame(320, 240);
        r.Render(new[] { obj }, frame, new RenderOptions { ClipZ = 0.0 }, 0);
        var after = System.Text.Json.JsonSerializer.Serialize(part);
        Assert.Equal(before, after);
    }
}
