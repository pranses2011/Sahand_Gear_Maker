using SahandGearMaker.Assembly;
using SahandGearMaker.Domain;
using SahandGearMaker.Drawing;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using SahandGearMaker.Persistence;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>آزمون‌های لایهٔ مونتاژ پیشرفته (جفت‌ها، سینماتیک، دستیار، تحلیل، برخورد).</summary>
public class AssemblyAdvancedTests
{
    private static ToothParameters T(double m, int z, double w, double helixDeg = 0) => new()
    {
        Module = m, ToothCount = z, FaceWidth = w, HelixAngle = UnitConverter.DegToRad(helixDeg),
    };

    [Fact]
    public void BevelPair_ComputesConeAngles()
    {
        var r = AdvancedPairCalculator.CheckBevelPair(T(3, 20, 15), T(3, 40, 15));
        Assert.True(r.IsCompatible);
        Assert.Equal(2.0, r.Ratio, 3);
        Assert.Equal(-1, r.RotationDirection);
    }

    [Fact]
    public void WormPair_ComputesCenterAndRatio()
    {
        var r = AdvancedPairCalculator.CheckWormPair(T(2.5, 2, 40), T(2.5, 40, 12), 40);
        Assert.True(r.IsCompatible);
        Assert.Equal(20.0, r.Ratio, 3);
        Assert.Equal((40 + 100) / 2.0, r.CenterDistanceMm, 2);
    }

    [Fact]
    public void WormPair_ModuleMismatch_Rejected()
    {
        var r = AdvancedPairCalculator.CheckWormPair(T(2.5, 2, 40), T(3, 40, 12), 40);
        Assert.False(r.IsCompatible);
    }

    [Fact]
    public void HarmonicAssembly_RequiresTwoTeethDifference()
    {
        var ok = AdvancedPairCalculator.CheckHarmonicAssembly(T(0.8, 100, 10), T(0.8, 102, 10));
        Assert.True(ok.IsCompatible);
        Assert.Equal(51.0, ok.Ratio, 3);

        var bad = AdvancedPairCalculator.CheckHarmonicAssembly(T(0.8, 100, 10), T(0.8, 103, 10));
        Assert.False(bad.IsCompatible);
    }

    [Fact]
    public void NonCircularPair_MeanRatioOne()
    {
        var r = AdvancedPairCalculator.CheckNonCircularPair(T(2, 28, 10), T(2, 28, 10), 0.5);
        Assert.True(r.IsCompatible);
        Assert.Equal(1.0, r.Ratio, 6);
        Assert.True(r.CenterDistanceMm > 20);
    }

    [Fact]
    public void SplineConnection_SameCountRequired()
    {
        Assert.True(AdvancedPairCalculator.CheckSplineConnection(T(1.5, 18, 20), T(1.5, 18, 20)).IsCompatible);
        Assert.False(AdvancedPairCalculator.CheckSplineConnection(T(1.5, 18, 20), T(1.5, 20, 20)).IsCompatible);
    }

    [Fact]
    public void Kinematics_PairStep_OppositeRotation()
    {
        var s = KinematicsEngine.PairStep("a", "b", 2.0, -1, Math.PI / 2);
        Assert.Equal(Math.PI / 2, s.States[0].AngleRad, 9);
        Assert.Equal(-Math.PI / 4, s.States[1].AngleRad, 9);
    }

    [Fact]
    public void Kinematics_RackDisplacement_MatchesPitch()
    {
        var (disp, angle) = KinematicsEngine.RackStep(T(2, 20, 10), 2 * Math.PI);
        // یک دور کامل پینیون = π·m·z جابه‌جایی شانه
        Assert.Equal(Math.PI * 2 * 20, disp, 6);
        Assert.Equal(2 * Math.PI, angle, 9);
    }

    [Fact]
    public void Kinematics_Planetary_WillisRelation()
    {
        // zr = zs + 2zp: 40 = 16 + 2×12
        var s = KinematicsEngine.PlanetaryStep(16, 12, 40, 3, PlanetaryFixedMember.Ring, Math.PI);
        double expectedRatio = 1.0 + 40.0 / 16.0;
        // SpeedRatio = ω_out/ω_in = 1/i
        Assert.Equal(1.0 / expectedRatio, s.States.First(x => x.MemberId == "carrier").SpeedRatio, 6);
        // زاویهٔ حامل = ورودی / نسبت
        Assert.Equal(Math.PI / expectedRatio, s.States.First(x => x.MemberId == "carrier").AngleRad, 6);
        // عضو ثابت باید سرعت صفر داشته باشد
        Assert.Equal(0, s.States.First(x => x.MemberId == "ring").SpeedRatio, 9);
    }

    [Fact]
    public void Kinematics_Planetary_BadConstraint_Rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            KinematicsEngine.PlanetaryStep(16, 12, 41, 3, PlanetaryFixedMember.Ring, 1));
    }

    [Fact]
    public void Kinematics_Harmonic_RatioAndDirection()
    {
        var s = KinematicsEngine.HarmonicStep(100, Math.PI);
        Assert.Equal(-1.0 / 51.0, s.States[1].SpeedRatio, 9);
    }

    [Fact]
    public void SizingAssistant_FindsExactRatioCombinations()
    {
        var suggestions = SizingAssistant.SuggestExternalPair(2.0, maxCenterDistanceMm: 120);
        Assert.NotEmpty(suggestions);
        foreach (var s in suggestions)
        {
            Assert.Equal(2.0, s.AchievedRatio, 2);
            Assert.True(s.CenterDistanceMm <= 120 + 1e-9);
        }
    }

    [Fact]
    public void SizingAssistant_NoSolution_ReturnsEmpty()
    {
        var suggestions = SizingAssistant.SuggestExternalPair(2.0, maxCenterDistanceMm: 5);
        Assert.Empty(suggestions);
    }

    [Fact]
    public void GearAnalysis_ContactRatioPositiveForStandardGear()
    {
        var r = GearAnalysisCalculator.Analyze(T(2, 25, 10));
        Assert.InRange(r.ContactRatio, 1.0, 2.2);
        Assert.False(r.UndercutLikely);
    }

    [Fact]
    public void GearAnalysis_UndercutDetected()
    {
        var r = GearAnalysisCalculator.Analyze(T(2, 12, 10));
        Assert.True(r.UndercutLikely);
    }

    [Fact]
    public void Interchecker_DistantMeshes_NoInterference()
    {
        var a = GearGeometryEngine.Build(GearFamily.ExternalSpur, T(2, 20, 8), new BodyFeatures(), new GeometrySettings());
        var b = GearGeometryEngine.Build(GearFamily.ExternalSpur, T(2, 20, 8), new BodyFeatures(), new GeometrySettings());
        var moved = b.Mesh.Transformed(new MathX.Vector3(100, 0, 0), 0);
        var result = InterferenceChecker.Check(a.Mesh, moved, cutoffMm: 2);
        Assert.False(result.HasInterference);
    }

    [Fact]
    public void Interchecker_IdenticalOverlappingMeshes_Interference()
    {
        var a = GearGeometryEngine.Build(GearFamily.ExternalSpur, T(2, 20, 8), new BodyFeatures(), new GeometrySettings());
        var result = InterferenceChecker.Check(a.Mesh, a.Mesh);
        Assert.True(result.HasInterference);
    }
}

/// <summary>آزمون‌های نقشهٔ فنی (SVG/PDF) و شکل‌دهی فارسی.</summary>
public class DrawingTests
{
    [Fact]
    public void ArabicShaper_JoiningForms()
    {
        // «بس» — ب میانی؟ خیر: ب (دوچسب) + س: ب آغازین، س پایانی
        var shaped = ArabicShaper.Shape("بس");
        Assert.Equal(0xFE91, shaped[0]); // ب initial
        Assert.Equal(0xFEB2, shaped[1]); // س final
    }

    [Fact]
    public void ArabicShaper_NonJoiningLetter()
    {
        // «اب» — الف (راست‌چسب، اول کلمه) → ایزوله؛ ب (الف به چپ وصل نمی‌شود) → ایزوله
        var shaped = ArabicShaper.Shape("اب");
        Assert.Equal(0xFE8D, shaped[0]); // ا isolated
        Assert.Equal(0xFE8F, shaped[1]); // ب isolated
    }

    [Fact]
    public void ArabicShaper_LamAlefLigature()
    {
        var shaped = ArabicShaper.Shape("لا");
        Assert.Single(shaped);
        Assert.Equal(0xFEFB, shaped[0]); // لا isolated
    }

    [Fact]
    public void ArabicShaper_Zwnj_BreaksJoining()
    {
        // «می‌کنم» — ZWNJ بین می و کنم
        var shaped = ArabicShaper.Shape("می‌کنم");
        Assert.Contains(0x200C, shaped);
        // ی قبل از ZWNJ باید پایانی باشد نه میانی
        Assert.Equal(0xFBFF, shaped[1]); // ی final
    }

    [Fact]
    public void ArabicShaper_LatinUnchanged()
    {
        var shaped = ArabicShaper.Shape("Gear 20");
        Assert.Equal("Gear 20".EnumerateRunes().Select(r => r.Value), shaped);
    }

    [Fact]
    public void SvgWriter_ProducesValidDocument()
    {
        var part = new GearPart
        {
            Name = "آزمون",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 18, FaceWidth = 8 },
        };
        var geo = GearGeometryEngine.BuildPart(part);
        var doc = TechnicalDrawingBuilder.Build(part, geo, "پروژهٔ آزمون");
        string svg = SvgDrawingWriter.Write(doc);
        Assert.Contains("<svg", svg);
        Assert.Contains("نقشهٔ فنی", svg);
        Assert.Contains("<polygon", svg); // مرز بیرونی دندانه‌دار
        Assert.Contains("جدول پارامترهای هندسی", svg);
    }

    [Fact]
    public void PdfWriter_ProducesValidPdf()
    {
        var part = new GearPart
        {
            Name = "آزمون",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 18, FaceWidth = 8 },
        };
        var geo = GearGeometryEngine.BuildPart(part);
        var doc = TechnicalDrawingBuilder.Build(part, geo, "پروژهٔ آزمون");

        using var ms = new MemoryStream();
        PdfDrawingWriter.Write(ms, doc);
        byte[] pdf = ms.ToArray();

        Assert.True(pdf.Length > 10000);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Contains((byte)'P', pdf[1..5]);
        string tail = System.Text.Encoding.ASCII.GetString(pdf[^64..]);
        Assert.Contains("%%EOF", tail);
        Assert.Contains("startxref", tail);
        // فونت تعبیه‌شده
        string s = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("/FontFile2", s);
        Assert.Contains("/Vazirmatn", s);
    }
}

/// <summary>آزمون‌های گونه‌ها، ذخیرهٔ خودکار، ورودی مش و خروجی‌های جدید.</summary>
public class VariantsImportExportTests
{
    private static GearProject SampleProject() => new()
    {
        Name = "پروژهٔ آزمون",
        Parts =
        {
            new GearPart
            {
                Name = "پینیون",
                Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = 2, ToothCount = 17, FaceWidth = 10 },
            },
        },
    };

    [Fact]
    public void Variant_CaptureApply_RoundTrip()
    {
        var project = SampleProject();
        var v1 = VariantManager.Capture(project, "نسخهٔ پایه");

        project.Parts[0].Teeth.ToothCount = 40;
        project.Parts[0].Teeth.Module = 3;
        Assert.Equal(40, project.Parts[0].Teeth.ToothCount);

        VariantManager.Apply(project, v1);
        Assert.Equal(17, project.Parts[0].Teeth.ToothCount);
        Assert.Equal(2.0, project.Parts[0].Teeth.Module, 9);
    }

    [Fact]
    public void Variant_Compare_FindsDiffs()
    {
        var project = SampleProject();
        var v1 = VariantManager.Capture(project, "پایه");
        project.Parts[0].Teeth.ToothCount = 30;
        project.Parts[0].Teeth.FaceWidth = 22;
        var v2 = VariantManager.Capture(project, "اصلاح‌شده");

        var diffs = VariantManager.Compare(v1, v2);
        Assert.Contains(diffs, d => d.Field == "تعداد دندانه" && d.OldValue == "17" && d.NewValue == "30");
        Assert.Contains(diffs, d => d.Field == "پهنای دندانه (mm)");
    }

    [Fact]
    public void VariantFile_SaveLoadRoundTrip()
    {
        var project = SampleProject();
        var variants = new List<GearVariant>
        {
            VariantManager.Capture(project, "الف"),
            VariantManager.Capture(project, "ب"),
        };
        string path = Path.Combine(Path.GetTempPath(), $"vars_{Guid.NewGuid():N}{VariantFile.Extension}");
        try
        {
            VariantFile.Save(variants, path);
            var loaded = VariantFile.Load(path);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("الف", loaded[0].Name);
            Assert.Equal(17, loaded[0].Parts[0].Teeth.ToothCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Autosave_RecoverAfterSimulatedCrash()
    {
        string projPath = Path.Combine(Path.GetTempPath(), $"proj_{Guid.NewGuid():N}.sgdproj");
        var autosave = new AutosaveService(projPath, TimeSpan.Zero);
        try
        {
            var project = SampleProject();
            Assert.True(autosave.TryAutosave(project, DateTime.UtcNow));
            Assert.True(autosave.HasRecovery);

            var recovered = autosave.TryRecover();
            Assert.NotNull(recovered);
            Assert.Single(recovered.Parts);
            Assert.Equal("پینیون", recovered.Parts[0].Name);

            autosave.DiscardRecovery();
            Assert.False(autosave.HasRecovery);
        }
        finally
        {
            File.Delete(projPath);
            File.Delete(projPath + ".autosave");
        }
    }

    [Fact]
    public void MeshImporter_ObjRoundTrip()
    {
        var geo = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 4, ToothCount = 12, FaceWidth = 6 },
            new BodyFeatures(), new GeometrySettings { ProfileToleranceMm = 0.2 });
        string dir = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string objPath = Path.Combine(dir, "test.obj");
        try
        {
            MeshExporter.ExportObj(geo.Mesh, objPath, new MeshExportSettings());
            var imported = MeshImporter.Import(objPath);
            Assert.Equal("OBJ", imported.Format);
            Assert.True(imported.Mesh.TriangleCount > 100);
            Assert.True(imported.Mesh.VertexCount > 50);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MeshImporter_StlAsciiRoundTrip()
    {
        var geo = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 4, ToothCount = 12, FaceWidth = 6 },
            new BodyFeatures(), new GeometrySettings { ProfileToleranceMm = 0.3 });
        string dir = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string stlPath = Path.Combine(dir, "test.stl");
        try
        {
            MeshExporter.ExportStlAscii(geo.Mesh, stlPath, new MeshExportSettings());
            var imported = MeshImporter.Import(stlPath);
            Assert.True(imported.Mesh.TriangleCount > 100);
            // رأس‌ها ادغام شده‌اند (کمتر از ۳× مثلث)
            Assert.True(imported.Mesh.VertexCount < imported.Mesh.TriangleCount * 3);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MeshImporter_UnsupportedFormat_Rejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"x_{Guid.NewGuid():N}.step");
        File.WriteAllText(path, "dummy");
        try
        {
            Assert.Throws<InvalidOperationException>(() => MeshImporter.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Exporter_3mf_WritesValidZip()
    {
        var geo = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 3, ToothCount = 15, FaceWidth = 8 },
            new BodyFeatures(), new GeometrySettings { ProfileToleranceMm = 0.1 });
        string path = Path.Combine(Path.GetTempPath(), $"t_{Guid.NewGuid():N}.3mf");
        try
        {
            Format3mfAmfExporter.Export3mf(geo.Mesh, path, "test-gear");
            Assert.True(File.Exists(path));
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("3D/3dmodel.model"));
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("_rels/.rels"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Exporter_Amf_WritesValidXml()
    {
        var geo = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 3, ToothCount = 15, FaceWidth = 8 },
            new BodyFeatures(), new GeometrySettings { ProfileToleranceMm = 0.1 });
        string path = Path.Combine(Path.GetTempPath(), $"t_{Guid.NewGuid():N}.amf");
        try
        {
            Format3mfAmfExporter.ExportAmf(geo.Mesh, path, "test-gear");
            string xml = File.ReadAllText(path);
            Assert.Contains("<amf", xml);
            Assert.Contains("<triangle>", xml);
            Assert.Contains("unit=\"millimeter\"", xml);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BatchExporter_PartialFailure_Continues()
    {
        var geo = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { Module = 3, ToothCount = 15, FaceWidth = 8 },
            new BodyFeatures(), new GeometrySettings { ProfileToleranceMm = 0.2 });
        string dir = Path.Combine(Path.GetTempPath(), $"batch_{Guid.NewGuid():N}");
        try
        {
            var result = BatchExporter.ExportAll(new (string, TriMesh)[]
            {
                ("part-ok", geo.Mesh),
                ("bad/name", geo.Mesh), // نام نامعتبر → پاک‌سازی می‌شود ولی موفق
            }, dir, ExportFileFormat.StlBinary);
            Assert.True(result.Succeeded >= 1);
            Assert.True(File.Exists(Path.Combine(dir, "part-ok.stl")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DxfExporter_WritesR12Structure()
    {
        var polyline = new List<(double, double)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        string path = Path.Combine(Path.GetTempPath(), $"t_{Guid.NewGuid():N}.dxf");
        try
        {
            Format3mfAmfExporter.ExportProfileDxf(polyline, path);
            string dxf = File.ReadAllText(path);
            Assert.Contains("SECTION", dxf);
            Assert.Contains("ENTITIES", dxf);
            Assert.Contains("EOF", dxf);
            Assert.Contains("LINE", dxf);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
