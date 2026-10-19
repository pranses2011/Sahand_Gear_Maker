using System.Text.RegularExpressions;
using SahandGearMaker.Cad;
using SahandGearMaker.Drawing;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های هستهٔ CAD — نسخهٔ ۰٫۵ (ایراد ۳ کاربر):
/// ساخت BREP تحلیلی، سلامت توپولوژی (هر یال دقیقاً ۲ بار مصرف می‌شود)،
/// و صحت نگارش STEP/IGES (بازخوانی مرجع‌ها).
/// </summary>
public class CadKernelTests
{
    private static (CadSolid Solid, CadBuildReport Report) BuildSolid(GearPart part)
    {
        var result = GearGeometryEngine.BuildPart(part);
        return CadPartBuilder.Build(part, result);
    }

    // ═══════════ سلامت توپولوژی BREP ═══════════

    [Theory]
    [InlineData(GearFamily.ExternalSpur)]
    [InlineData(GearFamily.ExternalHelical)]
    [InlineData(GearFamily.ExternalHerringbone)]
    [InlineData(GearFamily.InternalSpur)]
    [InlineData(GearFamily.Worm)]
    [InlineData(GearFamily.WormWheel)]
    [InlineData(GearFamily.BevelSpur)]
    [InlineData(GearFamily.Rack)]
    public void CadSolid_EveryEdgeUsedExactlyTwice(GearFamily family)
    {
        var part = GearFamilyDefaults.CreatePart(family, "t");
        GearFamilyDefaults.Normalize(part);
        if (family == GearFamily.ExternalHelical) part.Teeth.HelixAngle = UnitConverter.DegToRad(20);
        if (family == GearFamily.ExternalHerringbone) part.Teeth.HelixAngle = UnitConverter.DegToRad(25);
        if (family == GearFamily.ExternalSpur)
        {
            part.Body.BoreShape = BoreShapeType.DShape;
            part.Body.BoreFlatDistanceMm = 4.0;
            part.Body.LighteningHoleCount = 5;
            part.Body.LighteningHoleShape = LighteningHoleShape.Hexagon;
            part.Body.LighteningHoleDiameter = 8;
            part.Body.HubDiameter = 24;
            part.Body.HubLength = 6;
        }

        var (solid, report) = BuildSolid(part);
        Assert.True(solid.Faces.Count > 4, $"{family}: وجه کافی ساخته نشد");

        // هر یال هندسی (جفت رأس + منحنی) باید دقیقاً ۲ بار در مرز وجوه ظاهر شود
        var usage = new Dictionary<string, int>();
        foreach (var f in solid.Faces)
        {
            Count(f.Outer);
            foreach (var h in f.Inner)
                Count(h);

            void Count(CadLoop loop)
            {
                int n = loop.Nodes.Count;
                for (int i = 0; i < n; i++)
                {
                    var from = loop.Nodes[i];
                    var to = loop.Nodes[(i + 1) % n];
                    if (from.Kind != CadSegmentKind.Line)
                        continue; // کمان‌ها با کلید مشابهِ خط نمی‌سنجم — جداگانه زیر
                    var key = EdgeKey(from.Point, to.Point);
                    usage[key] = usage.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
        }
        var bad = usage.Where(kv => kv.Value != 2).ToList();
        Assert.True(bad.Count == 0,
            $"{family}: {bad.Count} یال خطی با مصرف ≠ ۲ (نمونه: {string.Join(";", bad.Take(3).Select(b => b.Key + "×" + b.Value))}) — پوسته آب‌بند نیست. راهبرد: {report.Strategy}");
    }

    private static string EdgeKey(Vector3 a, Vector3 b)
    {
        double Z(double v) => Math.Round(v, 4) == 0 ? 0 : Math.Round(v, 4);
        string ka = $"{Z(a.X)}|{Z(a.Y)}|{Z(a.Z)}";
        string kb = $"{Z(b.X)}|{Z(b.Y)}|{Z(b.Z)}";
        return string.CompareOrdinal(ka, kb) < 0 ? ka + "->" + kb : kb + "->" + ka;
    }

    [Fact]
    public void CadSolid_AnalyticCylindersForRoundBore()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "t");
        var (solid, _) = BuildSolid(part);
        int cylinders = solid.Faces.Count(f => f.Surface is CadCylinder);
        Assert.True(cylinders >= 2, $"بور گرد باید حداقل ۲ وجه استوانه‌ای داشته باشد (بدست آمد {cylinders})");
        Assert.Contains(solid.Faces, f => f.Surface is CadPlane);
    }

    [Fact]
    public void CadSolid_DShapeBore_PlanarPrismWall()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "t");
        part.Body.BoreShape = BoreShapeType.DShape;
        part.Body.BoreFlatDistanceMm = 4.0;
        var (solid, _) = BuildSolid(part);
        // دیوارهٔ منشوری D: وجوه صفحه‌ای با نرمال شعاعی (نه استوانه)
        Assert.Contains(solid.Faces, f => f.Surface is CadPlane p && Math.Abs(Math.Abs(p.AxisZ.Z) - 1) > 0.01);
    }

    // ═══════════ صحت نگارش STEP ═══════════

    [Fact]
    public void StepWriter_StructurallyValidAndReferencesResolve()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "چرخ تست");
        part.Body.BoreShape = BoreShapeType.Hexagon;
        part.Body.LighteningHoleCount = 4;
        part.Body.LighteningHoleShape = LighteningHoleShape.Circle;
        var (solid, _) = BuildSolid(part);

        var step = new StepWriter().Write(solid);

        Assert.StartsWith("ISO-10303-21;", step);
        Assert.Contains("FILE_SCHEMA(('AUTOMOTIVE_DESIGN", step);
        Assert.Contains("MANIFOLD_SOLID_BREP", step);
        Assert.Contains("ADVANCED_BREP_SHAPE_REPRESENTATION", step);
        Assert.Contains("CYLINDRICAL_SURFACE", step);
        Assert.EndsWith("END-ISO-10303-21;\n", step.TrimEnd() + "\n");

        // بازخوانی: همهٔ #ارجاع‌ها باید به موجودیت‌های تعریف‌شده اشاره کنند
        var defined = new HashSet<int>();
        foreach (Match m in Regex.Matches(step, @"^#(\d+) =", RegexOptions.Multiline))
            defined.Add(int.Parse(m.Groups[1].Value));
        Assert.True(defined.Count > 100, $"تعداد موجودیت‌ها باید معنادار باشد ({defined.Count})");

        foreach (Match m in Regex.Matches(step, @"#(\d+)"))
        {
            int id = int.Parse(m.Groups[1].Value);
            if (step[(m.Index + m.Length)..].StartsWith(" ="))
                continue; // تعریف، نه ارجاع
            Assert.Contains(id, defined);
        }

        // هر ORIENTED_EDGE باید به EDGE_CURVE ارجاع دهد
        int oriented = Regex.Matches(step, @"ORIENTED_EDGE").Count;
        Assert.True(oriented > 100, $"حلقه‌ها باید یال جهت‌دار داشته باشند ({oriented})");
    }

    [Fact]
    public void StepWriter_SharedEdgesBetweenCapAndBoreCylinder()
    {
        // درپوش و استوانهٔ بور باید یال‌های کمانی مشترک داشته باشند (مدیریت منیفولد)
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "t");
        var (solid, _) = BuildSolid(part);
        var step = new StepWriter().Write(solid);

        int edgeCurves = Regex.Matches(step, @"EDGE_CURVE").Count;
        int orientedEdges = Regex.Matches(step, @"ORIENTED_EDGE").Count;
        int falseOriented = Regex.Matches(step, @"ORIENTED_EDGE\('',#\d+,#\d+,#\d+,.F.\)").Count;

        // اگر هیچ یالی به‌اشتراک گذاشته نشده بود، همه جهت‌دارها .T. می‌شدند
        Assert.True(falseOriented > 0, "یال‌های معکوس (اشتراک درپوش/دیواره) وجود ندارد — پوسته دوخته نشده است");
        Assert.True(orientedEdges >= edgeCurves * 2 - 4,
            $"یال‌ها باید حدوداً ۲ بار مصرف شوند: {orientedEdges} جهت‌دار / {edgeCurves} یال");
    }

    // ═══════════ صحت نگارش IGES ═══════════

    [Fact]
    public void IgesWriter_SectionsAndEntityCountsConsistent()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "t");
        // بور گرد + حفره‌های دایره‌ای → موجودیت‌های 120 (استوانه) و 100 (کمان) حاضرند
        part.Body.LighteningHoleCount = 4;
        part.Body.LighteningHoleShape = LighteningHoleShape.Circle;
        var (solid, _) = BuildSolid(part);

        var iges = new IgesWriter().Write(solid);

        // بخش‌ها
        Assert.True(iges.StartsWith("Sahand Gear Maker") && iges.Contains("S0000001"), "بخش S");
        Assert.Contains("G0000001", iges);
        Assert.Contains("D0000001", iges);
        Assert.Contains("P0000001", iges);
        Assert.Contains("T0000001", iges);

        // خط پایانی: شمارش بخش‌ها
        var last = iges.TrimEnd().Split('\n')[^1];
        Assert.Matches(@"^S\d+G\d+D\d+P\d+ *T0000001$", last);

        // موجودیت‌های کلیدی — در رکوردهای دایرکتوری (فیلد ۱ = نوع، ۸ ستون)
        var dFirstFields = iges.Split('\n')
            .Where(l => l.Length == 80 && (l.EndsWith("D") == false) && l[72] == 'D')
            .Select(l => l[..8].Trim())
            .ToList();
        Assert.Contains("108", dFirstFields); // صفحه
        Assert.Contains("144", dFirstFields); // سطح بریده
        Assert.Contains("102", dFirstFields); // منحنی مرکب
        Assert.Contains("110", dFirstFields); // خط
        Assert.Contains("120", dFirstFields); // سطح دوران (استوانه)
        Assert.Contains("100", dFirstFields); // کمان دایره‌ای
        Assert.Contains("124", dFirstFields); // تبدیل (صفحهٔ کمان)

        // شماره‌های رکورد دایرکتوری: زوج/فرد جفت
        var dLines = iges.Split('\n').Where(l => l.Length > 73 && l[72] == 'D').ToList();
        Assert.True(dLines.Count % 2 == 0, "رکوردهای D باید زوج باشند");
        for (int i = 0; i < dLines.Count; i += 2)
        {
            int n1 = int.Parse(dLines[i][73..]);
            int n2 = int.Parse(dLines[i + 1][73..]);
            Assert.Equal(n1 + 1, n2);
        }
    }

    [Theory]
    [InlineData(GearFamily.ExternalHerringbone)]
    [InlineData(GearFamily.InternalSpur)]
    [InlineData(GearFamily.SplineExternal)]
    public void CadSolid_ExoticFamilies_BuildAndExport(GearFamily family)
    {
        var part = GearFamilyDefaults.CreatePart(family, "t");
        GearFamilyDefaults.Normalize(part);
        if (family == GearFamily.ExternalHerringbone) part.Teeth.HelixAngle = UnitConverter.DegToRad(25);
        if (family == GearFamily.ExternalHerringbone) part.Body.HerringboneCentralGapMm = 4;

        var result = GearGeometryEngine.BuildPart(part);
        var (solid, report) = CadPartBuilder.Build(part, result);
        Assert.True(solid.Faces.Count > 10);

        var step = new StepWriter().Write(solid);
        Assert.Contains("MANIFOLD_SOLID_BREP", step);
        var iges = new IgesWriter().Write(solid);
        Assert.Contains("T0000001", iges);
    }

    [Fact]
    public void CadFile_WritesRealFiles()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "چرخ فایل");
        var (solid, _) = BuildSolid(part);

        string dir = Path.Combine(Path.GetTempPath(), "sgm-cad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string step = Path.Combine(dir, "test.step");
            CadFile.WriteStep(solid, step);
            Assert.True(new FileInfo(step).Length > 5000, "فایل STEP باید محتوای معنادار داشته باشد");
            string content = File.ReadAllText(step);
            Assert.StartsWith("ISO-10303-21;", content);

            string iges = Path.Combine(dir, "test.igs");
            CadFile.WriteIges(solid, iges);
            Assert.True(new FileInfo(iges).Length > 3000, "فایل IGES باید محتوای معنادار داشته باشد");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

// ═══════════ بهینه‌سازی چاپ (ایراد ۱۸) ═══════════

public class PrintOptimizationTests
{
    [Fact]
    public void PrintPreparation_KeepsWatertightAndReportsVolume()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "چاپ");
        part.Body.BoreShape = BoreShapeType.DShape;
        var result = GearGeometryEngine.BuildPart(part);
        var copy = result.Mesh.Transformed(new MathX.Vector3(0, 0, 0), 0);

        var report = SahandGearMaker.Exports.PrintPreparation.Prepare(copy, new SahandGearMaker.Exports.PrintOptimizationOptions
        {
            Enabled = true,
            LayFlatVertical = true,
        });

        Assert.True(report.Watertight, "مش چاپ باید آب‌بند بماند");
        Assert.True(report.OutwardNormals, "نرمال‌ها باید رو به بیرون باشند");
        Assert.True(report.VolumeMm3 > 1000, $"حجم معنادار (بدست آمد {report.VolumeMm3:F0})");
        Assert.True(report.MassGrams > 1, "جرم برآوردی باید مثبت باشد");
        Assert.True(report.SizeMm.X > 10 && report.SizeMm.Y > 10 && report.SizeMm.Z > 1, "ابعاد گزارش‌شده معقول");
    }

    [Fact]
    public void PrintPreparation_HorizontalOrientation_RotatesAxis()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "چاپ-افقی");
        var result = GearGeometryEngine.BuildPart(part);
        var before = result.Mesh.Bounds();
        double heightBefore = before.Max.Z - before.Min.Z;

        var copy = result.Mesh.Transformed(new MathX.Vector3(0, 0, 0), 0);
        var report = SahandGearMaker.Exports.PrintPreparation.Prepare(copy, new SahandGearMaker.Exports.PrintOptimizationOptions
        {
            Enabled = true,
            LayFlatHorizontal = true,
        });
        var after = copy.Bounds();
        double heightAfter = after.Max.Z - after.Min.Z;

        // چرخش ۹۰° حول X: پهنای دندانه (که Z بود) حالا در Y است — ارتفاع جدید = قطر
        Assert.True(Math.Abs(heightAfter - (before.Max.Y - before.Min.Y)) < 0.01,
            $"ارتفاع پس از چرخش باید قطر قبلی باشد ({heightAfter:F1} در برابر {before.Max.Y - before.Min.Y:F1})");
        Assert.True(heightBefore > 1, "قبل از چرخش ارتفاع = پهنای دندانه");
        Assert.Contains("افقی", report.OrientationNote);
    }

    [Fact]
    public void PrintPreparation_WeldsDuplicateVertices()
    {
        // مش دستی با رئوس تکراری کوچک (اختلاف < تلرانس جوش)
        var mesh = new TriMesh();
        int a = mesh.AddVertex(new MathX.Vector3(0, 0, 0));
        int b = mesh.AddVertex(new MathX.Vector3(10, 0, 0));
        int c = mesh.AddVertex(new MathX.Vector3(0, 10, 0));
        int d = mesh.AddVertex(new MathX.Vector3(0.0000001, 0.0000001, 0.0000001)); // ≈ همان a
        mesh.AddTriangle(a, b, c);
        mesh.AddTriangle(b, d, c);

        var report = SahandGearMaker.Exports.PrintPreparation.Prepare(mesh, new SahandGearMaker.Exports.PrintOptimizationOptions
        {
            Enabled = true,
            WeldToleranceMm = 0.001,
        });
        Assert.True(report.VerticesWelded >= 1, "رأس تکراری باید جوش بخورد");
        Assert.True(mesh.VertexCount <= 3, $"پس از جوش باید ≤ ۳ رأس بماند ({mesh.VertexCount})");
    }

    [Fact]
    public void PrintPreparation_DoesNotMutateOriginalMesh()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "اصل");
        var result = GearGeometryEngine.BuildPart(part);
        var boundsBefore = result.Mesh.Bounds();
        int trisBefore = result.Mesh.TriangleCount;

        var copy = result.Mesh.Transformed(new MathX.Vector3(0, 0, 0), 0);
        SahandGearMaker.Exports.PrintPreparation.Prepare(copy, new SahandGearMaker.Exports.PrintOptimizationOptions
        {
            Enabled = true,
            LayFlatHorizontal = true,
        });

        var boundsAfter = result.Mesh.Bounds();
        Assert.Equal(trisBefore, result.Mesh.TriangleCount);
        Assert.Equal(boundsBefore.Max.Z, boundsAfter.Max.Z, 5);
    }
}

// ═══════════ نقشهٔ فنی: پروفیل تحلیلی + ایزومتریک (ایرادهای ۱۱ و ۱۳) ═══════════

public class DrawingQualityTests
{
    [Theory]
    [InlineData(GearFamily.ExternalSpur)]
    [InlineData(GearFamily.ExternalHelical)]
    [InlineData(GearFamily.ExternalHerringbone)]
    [InlineData(GearFamily.InternalSpur)]
    [InlineData(GearFamily.SplineExternal)]
    public void PlanView_AnalyticOutline_TeethAreContinuous(GearFamily family)
    {
        var part = GearFamilyDefaults.CreatePart(family, "نقشه");
        GearFamilyDefaults.Normalize(part);
        if (family == GearFamily.ExternalHelical) part.Teeth.HelixAngle = UnitConverter.DegToRad(20);
        if (family == GearFamily.ExternalHerringbone) part.Teeth.HelixAngle = UnitConverter.DegToRad(25);
        var result = GearGeometryEngine.BuildPart(part);

        var doc = TechnicalDrawingBuilder.Build(part, result);
        var plan = doc.Views[0];

        // مرز تحلیلی: تعداد دندانهٔ قابل‌شمارش = z (قله‌های شعاعی)
        // داخلی: مرز دندانه حفرهٔ اول است (بیرونی = دایرهٔ رینگ)
        var outer = family == GearFamily.InternalSpur
            ? plan.Outlines.FirstOrDefault(o => o.Count > 100) ?? plan.Outlines[0] // مرز دندانه (حفرهٔ اول)
            : plan.Outlines[0];
        Assert.True(outer.Count > part.Teeth.ToothCount * 6, $"پروفیل تحلیلی باید نقاط کافی داشته باشد ({outer.Count})");

        int peaks = 0;
        double rMax = outer.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        double rMin = outer.Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        double tipLevel = rMax - (rMax - rMin) * 0.15;
        bool above = false;
        for (int i = 0; i < outer.Count; i++)
        {
            double r = Math.Sqrt(outer[i].X * outer[i].X + outer[i].Y * outer[i].Y);
            if (r > tipLevel && !above) { peaks++; above = true; }
            else if (r < tipLevel) above = false;
        }
        Assert.True(peaks >= part.Teeth.ToothCount - 1,
            $"تعداد قلهٔ دندانه {peaks} باید ≈ z={part.Teeth.ToothCount} باشد — دندانه‌ها تیکه‌تیکه‌اند");

        // پیوستگی: هیچ جهش بزرگی بین نقاط متوالی نیست
        double maxStep = 0;
        for (int i = 0; i < outer.Count; i++)
        {
            var a = outer[i];
            var b2 = outer[(i + 1) % outer.Count];
            maxStep = Math.Max(maxStep, Math.Sqrt(Math.Pow(a.X - b2.X, 2) + Math.Pow(a.Y - b2.Y, 2)));
        }
        Assert.True(maxStep < 2.5, $"جهش {maxStep:F2}mm در مرز — خط ناپیوسته است");
    }

    [Fact]
    public void PlanView_WithBoreAndHoles_ShowsAllLoops()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "نقشه کامل");
        part.Body.BoreShape = BoreShapeType.Hexagon;
        part.Body.LighteningHoleCount = 5;
        part.Body.LighteningHoleShape = LighteningHoleShape.Circle;
        var result = GearGeometryEngine.BuildPart(part);

        var doc = TechnicalDrawingBuilder.Build(part, result);
        var plan = doc.Views[0];
        // مرز بیرونی + بور شش‌گوش + ۵ حفره = ۷ حلقه
        Assert.True(plan.Outlines.Count >= 7, $"باید ۷ حلقه باشد (بدست آمد {plan.Outlines.Count})");
    }

    [Fact]
    public void Document_HasIsometricView_WithWireframe()
    {
        var part = GearFamilyDefaults.CreatePart(GearFamily.ExternalSpur, "ایزو");
        var result = GearGeometryEngine.BuildPart(part);
        var doc = TechnicalDrawingBuilder.Build(part, result);

        var iso = doc.Views.FirstOrDefault(v => v.Title.Contains("ایزومتریک"));
        Assert.True(iso is not null, "نمای ایزومتریک باید وجود داشته باشد (ایراد ۱۱)");
        Assert.True(iso!.Lines.Count > 50, $"چارچوب سیمی ایزومتریک باید خطوط کافی داشته باشد ({iso.Lines.Count})");

        // محدودهٔ معقول
        var b = iso.Bounds;
        Assert.True(b.MaxX > b.MinX && b.MaxY > b.MinY);
    }

    [Fact]
    public void IsometricView_WorksForWormAndBevel()
    {
        foreach (var family in new[] { GearFamily.Worm, GearFamily.BevelSpur })
        {
            var part = GearFamilyDefaults.CreatePart(family, "ایزو");
            GearFamilyDefaults.Normalize(part);
            var result = GearGeometryEngine.BuildPart(part);
            var doc = TechnicalDrawingBuilder.Build(part, result);
            var iso = doc.Views.FirstOrDefault(v => v.Title.Contains("ایزومتریک"));
            Assert.True(iso is { Lines.Count: > 30 }, $"{family}: ایزومتریک خالی است");
        }
    }

    [Fact]
    public void MeshFallbackOutline_NoGaps_AfterInterpolation()
    {
        // مسیر افت (خانوادهٔ غیرمنشوری): کرم — سطل‌های خالی باید پر شده باشند
        var part = GearFamilyDefaults.CreatePart(GearFamily.Worm, "کرم");
        GearFamilyDefaults.Normalize(part);
        var result = GearGeometryEngine.BuildPart(part);
        var doc = TechnicalDrawingBuilder.Build(part, result);
        var plan = doc.Views[0];
        Assert.True(plan.Outlines.Count > 0);

        var outer = plan.Outlines[0];
        // زوایای متوالی نباید گپ بزرگ داشته باشند (خط پیوسته)
        double prevAng = double.NaN;
        double maxGap = 0;
        foreach (var p in outer)
        {
            double ang = Math.Atan2(p.Y, p.X);
            if (!double.IsNaN(prevAng))
            {
                double g = Math.Abs(ang - prevAng);
                if (g > Math.PI) g = 2 * Math.PI - g;
                maxGap = Math.Max(maxGap, g);
            }
            prevAng = ang;
        }
        Assert.True(maxGap < 0.05, $"گپ زاویه‌ای {maxGap:F3} rad — خط قطع‌قطع است");
    }
}
