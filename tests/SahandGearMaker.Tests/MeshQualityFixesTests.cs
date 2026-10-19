using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های بستهٔ ۰٫۵٫۰ — ایرادهای ۶/۷/۸/۱۲ گزارش کاربر:
/// جناغی ناقرینه، مش ناقص/راه‌راه (جهت‌گیری مثلث)، کرم با پارامترهای پیش‌فرض.
/// </summary>
public class MeshQualityFixesTests
{
    // ═══════════ ۶) جناغی: پیوستگی در مفصل و جفت‌شدن بالا/پایین ═══════════

    [Theory]
    [InlineData(25.0, 20.0, 0.0)]
    [InlineData(20.0, 30.0, 0.0)]
    [InlineData(25.0, 24.0, 4.0)]   // با گپ مرکزی
    [InlineData(30.0, 16.0, 2.0)]
    public void Herringbone_JointContinuous_AndTopAlignedWithBottom(double helixDeg, double width, double gapMm)
    {
        var teeth = new ToothParameters
        {
            Module = 2.0,
            ToothCount = 24,
            FaceWidth = width,
            HelixAngle = UnitConverter.DegToRad(helixDeg),
        };
        var body = new BodyFeatures { HerringboneCentralGapMm = gapMm, BoreDiameter = 8 };
        var result = GearGeometryEngine.Build(GearFamily.ExternalHerringbone, teeth, body, new GeometrySettings());

        var mesh = result.Mesh;

        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var v in mesh.Vertices)
        {
            zMin = Math.Min(zMin, v.Z);
            zMax = Math.Max(zMax, v.Z);
        }
        Assert.True(zMax - zMin >= width * 0.999, "پهنای کامل ساخته شود");

        // پیوستگی مفصل: جابه‌جایی زاویه‌ای نوک دندانه‌ها بین سطح‌های متوالی z باید کوچک و پیوسته
        // باشد (جهش ناگهانی = پارگی دندانه در مفصل). نوک‌ها را با فاز دایره‌ای (واحد دندانه) می‌سنجم.
        double tipR = result.Figures.TipRadius;
        var levelsSet = new SortedSet<double>();
        var tipAnglesByLevel = new Dictionary<double, List<double>>();
        foreach (var v in mesh.Vertices)
        {
            var vlevel = Math.Round(v.Z, 4);
            levelsSet.Add(vlevel);
            if (v.X * v.X + v.Y * v.Y < Math.Pow(0.85 * tipR, 2))
                continue; // فقط رأس‌های نزدیک نوک دندانه
            (tipAnglesByLevel.TryGetValue(vlevel, out var l) ? l : tipAnglesByLevel[vlevel] = new()).Add(Math.Atan2(v.Y, v.X));
        }
        var levels = levelsSet.ToList();

        int zc = teeth.ToothCount;
        double pitch = 2.0 * Math.PI / zc; // گام زاویه‌ای دندانه
        double maxLevelShift = 0;
        for (int i = 0; i + 1 < levels.Count; i++)
        {
            if (!tipAnglesByLevel.TryGetValue(levels[i], out var lo) || !tipAnglesByLevel.TryGetValue(levels[i + 1], out var hi))
                continue;
            if (lo.Count < 5 || hi.Count < 5)
                continue;

            // اختلاف فاز دایره‌ای مجموعه‌ها در واحد گام دندانه (میانگین برداری e^{i2πφ})
            double re = 0, im = 0;
            foreach (var a in lo)
            {
                double phase = a * zc / (2.0 * Math.PI);
                re += Math.Cos(2.0 * Math.PI * phase);
                im += Math.Sin(2.0 * Math.PI * phase);
            }
            double re2 = 0, im2 = 0;
            foreach (var a in hi)
            {
                double phase = a * zc / (2.0 * Math.PI);
                re2 += Math.Cos(2.0 * Math.PI * phase);
                im2 += Math.Sin(2.0 * Math.PI * phase);
            }
            double shiftRad = (Math.Atan2(im2, re2) - Math.Atan2(im, re)) / zc; // به رادیان
            while (shiftRad > Math.PI / zc) shiftRad -= 2.0 * Math.PI / zc;
            while (shiftRad < -Math.PI / zc) shiftRad += 2.0 * Math.PI / zc;
            maxLevelShift = Math.Max(maxLevelShift, Math.Abs(shiftRad));
        }

        Assert.True(maxLevelShift < pitch / 3.0,
            $"جهش زاویه‌ای {UnitConverter.RadToDeg(maxLevelShift):F2}° بین سطح‌های متوالی — دندانه در مفصل می‌پارد (آستانه {UnitConverter.RadToDeg(pitch / 3.0):F2}°).");

        // آب‌بندی حفظ شود
        var report = mesh.Validate();
        Assert.True(report.ClosedManifold, "مش جناغی باید آب‌بند بماند");
    }

    [Fact]
    public void Herringbone_TopFace_RotatedExactlyLikeBottom()
    {
        // نقاط پروفیل در zMin و zMax باید زاویهٔ قطبی یکسان داشته باشند (پیچش صفر در دو سر)
        var teeth = new ToothParameters
        {
            Module = 2.0,
            ToothCount = 18,
            FaceWidth = 20.0,
            HelixAngle = UnitConverter.DegToRad(25.0),
        };
        var result = GearGeometryEngine.Build(GearFamily.ExternalHerringbone, teeth,
            new BodyFeatures { BoreDiameter = 8 }, new GeometrySettings());
        var verts = result.Mesh.Vertices;

        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var v in verts) { zMin = Math.Min(zMin, v.Z); zMax = Math.Max(zMax, v.Z); }

        var bottomAngles = new List<double>();
        var topAngles = new List<double>();
        foreach (var v in verts)
        {
            if (Math.Abs(v.Z - zMin) < 1e-9 && v.X * v.X + v.Y * v.Y > 100)
                bottomAngles.Add(Math.Atan2(v.Y, v.X));
            if (Math.Abs(v.Z - zMax) < 1e-9 && v.X * v.X + v.Y * v.Y > 100)
                topAngles.Add(Math.Atan2(v.Y, v.X));
        }
        Assert.True(bottomAngles.Count > 10 && topAngles.Count > 10, "دو سطح باید نقاط مرزی داشته باشند");

        bottomAngles.Sort();
        topAngles.Sort();
        double maxDiff = 0;
        for (int i = 0; i < Math.Min(bottomAngles.Count, topAngles.Count); i++)
        {
            double d = Math.Abs(NormalizeAngle(bottomAngles[i] - topAngles[i]));
            maxDiff = Math.Max(maxDiff, d);
        }
        Assert.True(maxDiff < UnitConverter.DegToRad(2.0),
            $"دندانه‌های سطح بالا باید روی سطح زیرین بنشینند (بیشترین انحراف {UnitConverter.RadToDeg(maxDiff):F2}° > 2°).");
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    // ═══════════ ۷/۱۲) جهت‌گیری یکنواخت مثلث‌ها — همهٔ خانواده‌ها ═══════════

    [Theory]
    [InlineData(GearFamily.BevelSpur)]
    [InlineData(GearFamily.BevelSpiral)]
    [InlineData(GearFamily.Hypoid)]
    [InlineData(GearFamily.Rack)]
    [InlineData(GearFamily.HelicalRack)]
    [InlineData(GearFamily.CrownFace)]
    [InlineData(GearFamily.HarmonicDrive)]
    [InlineData(GearFamily.Worm)]
    [InlineData(GearFamily.WormWheel)]
    public void AllFamilies_ConsistentOutwardOrientation(GearFamily family)
    {
        var part = GearFamilyDefaults.CreatePart(family, "test");
        GearFamilyDefaults.Normalize(part);
        var result = GearGeometryEngine.BuildPart(part);
        Assert.True(result.Mesh.TriangleCount > 0);

        // یال‌های ناسازگار: صفر (هر یال مشترک باید در دو جهت پیموده شود)
        var dirCount = new Dictionary<(int, int), int>();
        var verts = result.Mesh.Vertices;
        foreach (var t in result.Mesh.Triangles)
        {
            Add(t.A, t.B); Add(t.B, t.C); Add(t.C, t.A);
            void Add(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                int dir = u < v ? 1 : -1;
                dirCount[key] = dirCount.TryGetValue(key, out var d) ? d + dir : dir;
            }
        }
        int bad = dirCount.Count(kv => Math.Abs(kv.Value) >= 2);
        Assert.True(bad == 0, $"{family}: {bad} یال ناسازگار — مش راه‌راه/ناقص می‌شود");

        // حجم امضادار: مثبت (نرمال‌ها رو به بیرون)
        double volume = 0;
        foreach (var t in result.Mesh.Triangles)
        {
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            volume += (a.X * (b.Y * c.Z - c.Y * b.Z) - a.Y * (b.X * c.Z - c.X * b.Z) + a.Z * (b.X * c.Y - c.X * b.Y)) / 6.0;
        }
        Assert.True(volume > 0, $"{family}: حجم امضادار منفی — نرمال‌ها رو به داخل‌اند");
    }

    [Fact]
    public void OrientationFixer_FlipsAlreadyBrokenMesh()
    {
        // مش دستی: مکعب با یک وجه معکوس — اصلاح‌گر باید یکنواختش کند
        var mesh = new TriMesh();
        int p0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int p1 = mesh.AddVertex(new Vector3(10, 0, 0));
        int p2 = mesh.AddVertex(new Vector3(10, 10, 0));
        int p3 = mesh.AddVertex(new Vector3(0, 10, 0));
        int p4 = mesh.AddVertex(new Vector3(0, 0, 10));
        int p5 = mesh.AddVertex(new Vector3(10, 0, 10));
        int p6 = mesh.AddVertex(new Vector3(10, 10, 10));
        int p7 = mesh.AddVertex(new Vector3(0, 10, 10));

        mesh.AddTriangle(p0, p2, p1); mesh.AddTriangle(p0, p3, p2); // پایین (رو به −Z)
        mesh.AddTriangle(p4, p6, p5); mesh.AddTriangle(p4, p6, p7); // بالا — مثلث اول عمداً معکوس!
        mesh.AddTriangle(p0, p1, p5); mesh.AddTriangle(p0, p5, p4); // جلو
        mesh.AddTriangle(p1, p2, p6); mesh.AddTriangle(p1, p6, p5); // راست
        mesh.AddTriangle(p2, p3, p7); mesh.AddTriangle(p2, p7, p6); // پشت
        mesh.AddTriangle(p3, p0, p4); mesh.AddTriangle(p3, p4, p7); // چپ

        var report = mesh.NormalizeOrientation();
        Assert.False(report.WasAlreadyConsistent);
        Assert.True(report.RemainingInconsistentEdges == 0, "مکعب باید کاملاً یکنواخت شود");

        double volume = 0;
        var verts = mesh.Vertices;
        foreach (var t in mesh.Triangles)
        {
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            volume += (a.X * (b.Y * c.Z - c.Y * b.Z) - a.Y * (b.X * c.Z - c.X * b.Z) + a.Z * (b.X * c.Y - c.X * b.Y)) / 6.0;
        }
        Assert.True(volume > 999, $"حجم مکعب باید ~۱۰۰۰ باشد (بدست آمد {volume:F1})");
    }

    // ═══════════ ۸) کرم: پیش‌فرض‌های خانواده ═══════════

    [Fact]
    public void WormFamilyDefaults_CreatePart_BuildsSuccessfully()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.Worm, "کرم");
        var result = GearGeometryEngine.BuildPart(part); // نباید استثنا دهد
        Assert.True(result.Mesh.TriangleCount > 1000);
        Assert.Contains(result.Diagnostics, d => d.Code == "SGM-I250");
    }

    [Fact]
    public void WormFamilyDefaults_Normalize_ClampsStarts()
    {
        // قطعهٔ معمولی (۲۰ دندانه) که خانواده‌اش به کرم تغییر می‌کند
        var part = new GearPart { Family = GearFamily.Worm };
        part.Teeth.ToothCount = 20;
        var changes = GearFamilyDefaults.Normalize(part);
        Assert.InRange(part.Teeth.ToothCount, 1, 12);
        Assert.NotEmpty(changes);
        Assert.Equal(part.Teeth.ToothCount, part.Specific.WormStartsCount);

        // پس از نرمال‌سازی، ساخت موفق است
        var result = GearGeometryEngine.BuildPart(part);
        Assert.True(result.Mesh.TriangleCount > 0);
    }

    [Fact]
    public void Normalize_DoesNotTouchHealthyParameters()
    {
        var part = new GearPart { Family = GearFamily.ExternalSpur };
        part.Teeth.ToothCount = 33;
        part.Teeth.Module = 2.5;
        var changes = GearFamilyDefaults.Normalize(part);
        Assert.Empty(changes);
        Assert.Equal(33, part.Teeth.ToothCount);
    }
}
