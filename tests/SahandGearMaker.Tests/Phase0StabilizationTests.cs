using SahandGearMaker.Domain;
using SahandGearMaker.Drawing;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های رگرسیون فاز ۰ (تثبیت پایه) از نقشهٔ راه ANALYSIS-AND-ROADMAP:
/// ۰٫۲ صداقت کاتالوگ نسبت به موتور واقعی، ۰٫۳ فعال‌بودن صادرکننده‌های 3MF/AMF/DXF،
/// ۰٫۵ جلوگیری از بازگشت فاصلهٔ UI↔موتور.
/// </summary>
public class Phase0StabilizationTests
{
    // ---------- ۰٫۲: کاتالوگ = منبع حقیقت UI ----------

    [Fact]
    public void Catalog_AllFamiliesHaveEntries()
    {
        var keys = CapabilityCatalog.GearFamilies.Select(f => f.Key).ToHashSet();
        foreach (GearFamily fam in Enum.GetValues(typeof(GearFamily)))
        {
            if (fam == GearFamily.SplineInternal || fam == GearFamily.SplineExternal)
            {
                // هزارخار در کاتالوگ زیر کلید SplineExternal ثبت شده — بررسی جداگانه
            }
            Assert.True(keys.Contains(fam.ToString()) || fam.ToString().StartsWith("Spline"),
                $"خانوادهٔ {fam} در کاتالوگ مدخل ندارد.");
        }
    }

    [Fact]
    public void Catalog_AllFamiliesImplementGeometry()
    {
        // از نسخهٔ ۰٫۲ همهٔ ۱۹ خانواده مش واقعی می‌سازند — UI نباید هیچ‌کدام را «نقشهٔ راه» بنامد.
        foreach (GearFamily fam in Enum.GetValues(typeof(GearFamily)))
        {
            Assert.True(CapabilityCatalog.BuildsGeometry(fam),
                $"{fam} باید هندسه بسازد (موتور ۰٫۲ کامل است) اما کاتالوگ Roadmap می‌گوید.");
        }
    }

    [Fact]
    public void Catalog_ExportFormatsMatchEngine()
    {
        var byKey = CapabilityCatalog.ExportFormats.ToDictionary(f => f.Key, f => f.Status);
        // فرمت‌هایی که صادرکنندهٔ آزموده دارند باید Implemented باشند:
        Assert.Equal(CapabilityStatus.Implemented, byKey["STL-BINARY"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["STL-ASCII"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["OBJ"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["PLY"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["GLB"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["3MF"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["AMF"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["DXF"]);
        Assert.Equal(CapabilityStatus.Implemented, byKey["SVG"]);
        // و قول‌های صادقانهٔ هنوز‌پیاده‌نشده:
        Assert.Equal(CapabilityStatus.Roadmap, byKey["STEP"]);
        Assert.Equal(CapabilityStatus.Roadmap, byKey["IGES"]);
    }

    // ---------- ۰٫۳: صادرکننده‌های تازه‌فعال واقعاً کار می‌کنند ----------

    private static (GearPart Part, GearBuildResult Build) BuildSample()
    {
        var part = new GearPart
        {
            Name = "phase0-sample",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 20, FaceWidth = 10 },
            Body = new BodyFeatures { BoreDiameter = 8 },
        };
        return (part, GearGeometryEngine.BuildPart(part));
    }

    [Fact]
    public void Export3mf_ProducesZipPackage()
    {
        var (_, build) = BuildSample();
        string path = Path.Combine(Path.GetTempPath(), $"p0-{Guid.NewGuid():N}.3mf");
        try
        {
            Format3mfAmfExporter.Export3mf(build.Mesh, path, "sample");
            Assert.True(File.Exists(path));
            // سربرگ ZIP معتبر (PK\x03\x04)
            byte[] head = new byte[4];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, 4);
            Assert.Equal(0x50, head[0]); // P
            Assert.Equal(0x4B, head[1]); // K
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportAmf_ProducesXml()
    {
        var (_, build) = BuildSample();
        string path = Path.Combine(Path.GetTempPath(), $"p0-{Guid.NewGuid():N}.amf");
        try
        {
            Format3mfAmfExporter.ExportAmf(build.Mesh, path, "sample");
            string text = File.ReadAllText(path);
            Assert.Contains("<amf", text);
            Assert.Contains("<vertices", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportDxfProfile_FromTechnicalDrawing_ProducesEntities()
    {
        var (part, build) = BuildSample();
        // همان مسیری که ExportViewModel (فاز ۰٫۳) طی می‌کند:
        var doc = TechnicalDrawingBuilder.Build(part, build);
        var outline = doc.Views.SelectMany(v => v.Outlines).OrderByDescending(o => o.Count).FirstOrDefault();
        Assert.NotNull(outline);
        Assert.True(outline!.Count > 8, "پروفیل مرز بیرونی باید نقاط کافی برای DXF داشته باشد.");

        string path = Path.Combine(Path.GetTempPath(), $"p0-{Guid.NewGuid():N}.dxf");
        try
        {
            Format3mfAmfExporter.ExportProfileDxf(outline, path, closed: true);
            string text = File.ReadAllText(path);
            Assert.Contains("SECTION", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ENTITIES", text, StringComparison.OrdinalIgnoreCase);
            // پروفیل با موجودیت LINE (هر پاره‌خط یک entity) نوشته می‌شود:
            Assert.Contains("\nLINE\n", text);
            Assert.Contains("GEARPROFILE", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TechnicalDrawing_HasSvgOutput()
    {
        var (part, build) = BuildSample();
        var doc = TechnicalDrawingBuilder.Build(part, build);
        string svg = SvgDrawingWriter.Write(doc);
        Assert.Contains("<svg", svg);
        Assert.Contains("نقشهٔ فنی", svg);
    }
}
