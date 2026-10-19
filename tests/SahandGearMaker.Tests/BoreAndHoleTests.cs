using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های بستهٔ ۰٫۵ — «همهٔ انواع سوراخ» و «حفره‌های سبک‌سازی چندشکلی»:
/// بور D/دو-D/شش‌گوش/چهارگوش/خاردار/هزارخار، شکل‌های حفره، اندازه و حالت بدون چرخش.
/// </summary>
public class BoreAndHoleTests
{
    private static GearBuildResult BuildSpurResult(BoreShapeType shape, Action<BodyFeatures>? setup = null)
    {
        var body = new BodyFeatures { BoreDiameter = 12, BoreShape = shape, BoreFlatDistanceMm = 4.5 };
        setup?.Invoke(body);
        return GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 3, ToothCount = 24, FaceWidth = 20 }, body, new GeometrySettings());
    }

    // ═══════════ ۴) همهٔ انواع سوراخ مرکزی ═══════════

    [Theory]
    [InlineData(BoreShapeType.Round)]
    [InlineData(BoreShapeType.DShape)]
    [InlineData(BoreShapeType.DoubleD)]
    [InlineData(BoreShapeType.Hexagon)]
    [InlineData(BoreShapeType.Square)]
    [InlineData(BoreShapeType.Keyed)]
    [InlineData(BoreShapeType.Spline)]
    public void AllBoreShapes_BuildWatertightAndOutward(BoreShapeType shape)
    {
        var result = BuildSpurResult(shape, b =>
        {
            if (shape == BoreShapeType.Keyed) b.KeywayWidth = 4;
            if (shape == BoreShapeType.Spline) { b.BoreSplineCount = 6; b.BoreSplineDepthMm = 1.2; }
        });
        Assert.True(result.Mesh.TriangleCount > 500);
        var stats = MeshStats.Of(result.Mesh);
        Assert.True(stats.OpenEdges == 0, $"{shape}: {stats.OpenEdges} یال باز");
        Assert.True(stats.BadWinding == 0, $"{shape}: {stats.BadWinding} یال ناسازگار");
        Assert.True(stats.SignedVolume > 0, $"{shape}: حجم منفی");
    }

    [Fact]
    public void DShape_HasFlatAtRequestedDistance()
    {
        // سطح تخت D در x = −4.5؛ کمینهٔ X رئوس بور (شعاع < 10) باید ≈ −4.5 باشد
        var result = BuildSpurResult(BoreShapeType.DShape);
        double minX = double.MaxValue;
        foreach (var v in result.Mesh.Vertices)
        {
            if (Math.Abs(v.Z) > 1e-9 && Math.Abs(v.Z - 20) > 1e-9) continue;
            double r = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (r > 10) continue; // فقط ناحیهٔ بور (نه دندانه)
            minX = Math.Min(minX, v.X);
        }
        Assert.True(minX > -5.1 && minX < -4.0, $"کمینهٔ X باید ≈ −۴٫۵ باشد (بدست آمد {minX:F2})");
        Assert.True(minX > -6.0 + 0.5, "سطح تخت باید دایره را قطع کند");
    }

    [Fact]
    public void HexagonBore_HasSixFlatVertices()
    {
        var result = BuildSpurResult(BoreShapeType.Hexagon);
        // بین-سر = ۱۲ → شعاع محیطی = 12/√3 ≈ 6.93؛ رئوس بور در نزدیکی این شعاع
        double rVertex = 12.0 / Math.Sqrt(3.0);
        int nearVertex = 0;
        foreach (var v in result.Mesh.Vertices)
        {
            if (Math.Abs(v.Z) > 1e-9) continue;
            double r = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (Math.Abs(r - rVertex) < 0.05) nearVertex++;
        }
        Assert.True(nearVertex >= 6, $"۶ رأس شش‌گوش باید در شعاع {rVertex:F2} باشند ({nearVertex} یافت شد)");
    }

    [Fact]
    public void SplineBore_TeethProtrudeBeyondBoreRadius()
    {
        var result = BuildSpurResult(BoreShapeType.Spline);
        double rTip = 6.0 + 1.2;
        double maxR = 0;
        foreach (var v in result.Mesh.Vertices)
        {
            if (Math.Abs(v.Z) > 1e-9) continue;
            double r = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (r > 15) continue; // فقط ناحیهٔ بور — دندانه‌ها نه
            maxR = Math.Max(maxR, r);
        }
        Assert.True(maxR >= rTip - 0.2, $"خارها باید تا {rTip:F1} برسند (بیشینه {maxR:F2})");
        Assert.True(maxR < rTip + 0.5, "خارها نباید از ارتفاع مجاز فراتر روند");
    }

    [Fact]
    public void BoreSection_KeyedMigration_KeywayImpliesKeyedShape()
    {
        var body = new BodyFeatures { BoreDiameter = 10, KeywayWidth = 4 };
        Assert.Equal(BoreShapeType.Keyed, BoreSection.EffectiveShape(body));

        body.BoreShape = BoreShapeType.DShape;
        Assert.Equal(BoreShapeType.DShape, BoreSection.EffectiveShape(body)); // انتخاب صریح اولویت دارد
    }

    [Fact]
    public void BoreSections_AllCCWAndClosed()
    {
        var body = new BodyFeatures { BoreDiameter = 12, KeywayWidth = 4, BoreSplineCount = 8 };
        foreach (BoreShapeType shape in Enum.GetValues<BoreShapeType>())
        {
            body.BoreShape = shape;
            var pts = BoreSection.Build(body, 6, 0.02);
            Assert.True(pts.Count >= 3, $"{shape}: مقطع خالی است");
            // CCW: مساحت امضادار مثبت
            double area2 = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                area2 += a.X * b.Y - b.X * a.Y;
                Assert.False(Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9,
                    $"{shape}: نقطهٔ متوالی تکراری در مقطع");
            }
            Assert.True(area2 > 0, $"{shape}: مقطع باید CCW باشد");
        }
    }

    [Fact]
    public void ShapedBore_ContinuesThroughHub()
    {
        // بور D + توپی: حلقهٔ بور باید در کف توپی هم باشد (سطح تخت D نباید زیر توپی مسدود شود)
        var body = new BodyFeatures
        {
            BoreDiameter = 12,
            BoreShape = BoreShapeType.DShape,
            BoreFlatDistanceMm = 4.5,
            HubDiameter = 24,
            HubLength = 8,
        };
        var result = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 3, ToothCount = 24, FaceWidth = 20 }, body, new GeometrySettings());

        // در سر توپی (z=28) حلقهٔ داخلی همان بور D است — سطح تخت باید آنجا هم باشد
        double minXAtHub = double.MaxValue;
        foreach (var v in result.Mesh.Vertices)
            if (Math.Abs(v.Z - 28.0) < 1e-6 && v.X * v.X + v.Y * v.Y < 100) // فقط ناحیهٔ بور (نه رینگ توپی)
                minXAtHub = Math.Min(minXAtHub, v.X);
        Assert.True(minXAtHub < -4.0, $"بور D باید داخل توپی ادامه یابد (کمینهٔ X در z=28: {minXAtHub:F2})");
        Assert.True(minXAtHub > -5.1, $"سطح تخت D حفظ شود (بدست آمد {minXAtHub:F2})");

        var stats = MeshStats.Of(result.Mesh);
        Assert.True(stats.OpenEdges == 0, "مش بور+توپی باید آب‌بند باشد");
    }

    // ═══════════ ۲/۵/۱۰) حفره‌های سبک‌سازی چندشکلی ═══════════

    [Theory]
    [InlineData(LighteningHoleShape.Circle)]
    [InlineData(LighteningHoleShape.Triangle)]
    [InlineData(LighteningHoleShape.Square)]
    [InlineData(LighteningHoleShape.Pentagon)]
    [InlineData(LighteningHoleShape.Hexagon)]
    [InlineData(LighteningHoleShape.Octagon)]
    [InlineData(LighteningHoleShape.Slot)]
    [InlineData(LighteningHoleShape.Star)]
    public void AllLighteningShapes_BuildWatertightAndRemoveMaterial(LighteningHoleShape shape)
    {
        var holes = new BodyFeatures
        {
            BoreDiameter = 12,
            LighteningHoleCount = 6,
            LighteningHoleShape = shape,
            LighteningHoleDiameter = 10,
        };
        var solid = new BodyFeatures { BoreDiameter = 12 };
        var teeth = new ToothParameters { Module = 3, ToothCount = 36, FaceWidth = 20 };

        var withHoles = GearGeometryEngine.Build(GearFamily.ExternalSpur, teeth, holes, new GeometrySettings());
        var without = GearGeometryEngine.Build(GearFamily.ExternalSpur, teeth, solid, new GeometrySettings());

        var s1 = MeshStats.Of(withHoles.Mesh);
        var s2 = MeshStats.Of(without.Mesh);
        Assert.True(s1.OpenEdges == 0 && s1.BadWinding == 0, $"{shape}: مش ناسالم");
        Assert.True(s2.SignedVolume - s1.SignedVolume > 500,
            $"{shape}: حفره‌ها باید مادهٔ قابل‌توجهی حذف کنند (حذف: {s2.SignedVolume - s1.SignedVolume:F0} mm³)");
    }

    [Fact]
    public void LighteningHoles_NoRotation_StraightThroughHelicalGear()
    {
        // ایراد ۵: حالت بدون چرخش — مرکز حفره در سطح بالا و پایین یکسان
        var body = new BodyFeatures
        {
            BoreDiameter = 10,
            LighteningHoleCount = 5,
            LighteningHoleShape = LighteningHoleShape.Hexagon,
            LighteningHoleDiameter = 8,
            LighteningHoleCircleRadius = 0, // خودکار
            LighteningHoleFollowTwist = false,
        };
        var teeth = new ToothParameters { Module = 3, ToothCount = 32, FaceWidth = 25, HelixAngle = UnitConverter.DegToRad(22) };
        var result = GearGeometryEngine.Build(GearFamily.ExternalHelical, teeth, body, new GeometrySettings());

        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var v in result.Mesh.Vertices) { zMin = Math.Min(zMin, v.Z); zMax = Math.Max(zMax, v.Z); }

        // رئوس یک حفرهٔ مشخص (نزدیک زاویهٔ ۷۲° = اولین حفره) در دو سطح
        bool found = false;
        foreach (var v in result.Mesh.Vertices)
        {
            if (Math.Abs(v.Z - zMin) > 1e-9) continue;
            double a = Math.Atan2(v.Y, v.X);
            if (Math.Abs(a - 2 * Math.PI / 5) > 0.15) continue;
            foreach (var w in result.Mesh.Vertices)
            {
                if (Math.Abs(w.Z - zMax) > 1e-9) continue;
                if (Math.Abs(w.X - v.X) < 0.15 && Math.Abs(w.Y - v.Y) < 0.15)
                { found = true; break; }
            }
            break;
        }
        Assert.True(found, "حفرهٔ بدون چرخش باید در دو سطح دقیقاً روی هم باشد");
        var stats = MeshStats.Of(result.Mesh);
        Assert.True(stats.OpenEdges == 0, "مش باید آب‌بند بماند");
    }

    [Fact]
    public void LighteningHoles_FollowTwist_CentersRotate()
    {
        var body = new BodyFeatures
        {
            BoreDiameter = 10,
            LighteningHoleCount = 5,
            LighteningHoleDiameter = 8,
            LighteningHoleCircleRadius = 0,
            LighteningHoleFollowTwist = true,
        };
        var teeth = new ToothParameters { Module = 3, ToothCount = 32, FaceWidth = 25, HelixAngle = UnitConverter.DegToRad(22) };
        var result = GearGeometryEngine.Build(GearFamily.ExternalHelical, teeth, body, new GeometrySettings());

        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var v in result.Mesh.Vertices) { zMin = Math.Min(zMin, v.Z); zMax = Math.Max(zMax, v.Z); }

        // مرکز حفرهٔ اول در دو سطح: زاویه باید متفاوت باشد (پیچش apply شده)
        (double x0, double y0) = ClosestTo(result.Mesh, zMin, 0);
        (double x1, double y1) = ClosestTo(result.Mesh, zMax, 0);
        double dTheta = Math.Abs(NormAngle(Math.Atan2(y1, x1) - Math.Atan2(y0, x0)));
        Assert.True(dTheta > 0.02, $"با FollowTwist مرکز حفره باید بچرخد (Δθ={UnitConverter.RadToDeg(dTheta):F2}°)");
    }

    private static (double, double) ClosestTo(TriMesh mesh, double z, double angle)
    {
        double best = double.MaxValue, bx = 0, by = 0;
        foreach (var v in mesh.Vertices)
        {
            if (Math.Abs(v.Z - z) > 1e-9) continue;
            double d = Math.Abs(NormAngle(Math.Atan2(v.Y, v.X) - angle));
            if (d < best) { best = d; bx = v.X; by = v.Y; }
        }
        return (bx, by);
    }

    private static double NormAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    [Fact]
    public void LighteningSections_SignedAreaPositive()
    {
        foreach (LighteningHoleShape shape in Enum.GetValues<LighteningHoleShape>())
        {
            var pts = LighteningSection.Build(shape, 10, 0.05);
            Assert.True(pts.Count >= 3);
            double area2 = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                area2 += a.X * b.Y - b.X * a.Y;
            }
            Assert.True(area2 > 0, $"{shape}: CCW نیست");
        }
    }

    [Fact]
    public void Clone_PreservesNewHoleParameters()
    {
        var part = new GearPart { Family = GearFamily.ExternalSpur };
        part.Body.BoreShape = BoreShapeType.Spline;
        part.Body.BoreSplineCount = 10;
        part.Body.BoreSplineDepthMm = 1.7;
        part.Body.BoreFlatDistanceMm = 4.4;
        part.Body.LighteningHoleShape = LighteningHoleShape.Star;
        part.Body.LighteningHoleFollowTwist = true;
        part.Body.LighteningHoleRotationDeg = 12;
        var copy = part.Clone("کپی");
        Assert.Equal(BoreShapeType.Spline, copy.Body.BoreShape);
        Assert.Equal(10, copy.Body.BoreSplineCount);
        Assert.Equal(1.7, copy.Body.BoreSplineDepthMm, 3);
        Assert.Equal(4.4, copy.Body.BoreFlatDistanceMm, 3);
        Assert.Equal(LighteningHoleShape.Star, copy.Body.LighteningHoleShape);
        Assert.True(copy.Body.LighteningHoleFollowTwist);
        Assert.Equal(12, copy.Body.LighteningHoleRotationDeg, 3);
    }
}

/// <summary>آمار سلامت مش برای آزمون‌ها.</summary>
internal readonly record struct MeshStats(int OpenEdges, int BadWinding, double SignedVolume)
{
    public static MeshStats Of(TriMesh mesh)
    {
        var verts = mesh.Vertices;
        var dir = new Dictionary<(int, int), int>();
        double volume = 0;
        foreach (var t in mesh.Triangles)
        {
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            volume += (a.X * (b.Y * c.Z - c.Y * b.Z) - a.Y * (b.X * c.Z - c.X * b.Z) + a.Z * (b.X * c.Y - c.X * b.Y)) / 6.0;
            Add(t.A, t.B); Add(t.B, t.C); Add(t.C, t.A);
            void Add(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                int d = u < v ? 1 : -1;
                dir[key] = dir.TryGetValue(key, out var x) ? x + d : d;
            }
        }
        int open = dir.Count(kv => Math.Abs(kv.Value) == 1);
        int bad = dir.Count(kv => Math.Abs(kv.Value) >= 2);
        return new MeshStats(open, bad, volume);
    }
}
