using SahandGearMaker.Domain;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using Xunit;

namespace SahandGearMaker.Tests;

public class ExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sgm-export-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly GearBuildResult _build;

    public ExportTests()
    {
        Directory.CreateDirectory(_dir);
        _build = GearGeometryEngine.Build(
            GearFamily.ExternalSpur,
            new ToothParameters { ToothCount = 16, Module = 2, FaceWidth = 12 },
            new BodyFeatures { BoreDiameter = 8 },
            new GeometrySettings());
    }

    private string Dir => _dir;

    [Fact]
    public void StlBinary_PassesPostWriteValidation()
    {
        var r = MeshExporter.ExportStlBinary(_build.Mesh, Path.Combine(Dir, "a.stl"), new MeshExportSettings());
        Assert.True(r.Success);
        Assert.True(r.PostWriteValidated);
        Assert.Equal(_build.Mesh.TriangleCount, r.TriangleCount);
        Assert.True(new FileInfo(r.FilePath!).Length == 84 + 50L * _build.Mesh.TriangleCount);
    }

    [Fact]
    public void StlAscii_PassesPostWriteValidation()
    {
        var r = MeshExporter.ExportStlAscii(_build.Mesh, Path.Combine(Dir, "a.stl"), new MeshExportSettings());
        Assert.True(r.Success);
        Assert.Contains("endsolid", File.ReadAllText(r.FilePath!), StringComparison.Ordinal);
    }

    [Fact]
    public void Obj_WritesFacesAndMtl()
    {
        var path = Path.Combine(Dir, "a.obj");
        var r = MeshExporter.ExportObj(_build.Mesh, path, new MeshExportSettings(), "نام فارسی");
        Assert.True(r.Success);
        Assert.True(File.Exists(Path.ChangeExtension(path, ".mtl")));
        Assert.Contains("mtllib", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Ply_HasValidHeader()
    {
        var path = Path.Combine(Dir, "a.ply");
        var r = MeshExporter.ExportPlyBinary(_build.Mesh, path, new MeshExportSettings());
        Assert.True(r.Success);
        var head = File.ReadAllText(path)[..200];
        Assert.Contains("format binary_little_endian", head, StringComparison.Ordinal);
    }

    [Fact]
    public void Glb_HasMagicAndVersion()
    {
        var path = Path.Combine(Dir, "a.glb");
        var r = MeshExporter.ExportGlb(_build.Mesh, path, new MeshExportSettings(), "test");
        Assert.True(r.Success);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(bytes, 0)); // "glTF"
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
    }

    [Fact]
    public void Glb_ScalesMmToMeters()
    {
        var path = Path.Combine(Dir, "unit.glb");
        var r = MeshExporter.ExportGlb(_build.Mesh, path, new MeshExportSettings());
        Assert.Equal(0.001, r.UnitScale, 12);
    }

    [Fact]
    public void InchExport_ScalesCoordinates()
    {
        var path = Path.Combine(Dir, "inch.stl");
        var mmPath = Path.Combine(Dir, "mm.stl");
        var sInch = new MeshExportSettings { Unit = LengthUnit.Inch };
        var sMm = new MeshExportSettings { Unit = LengthUnit.Millimeter };
        MeshExporter.ExportStlBinary(_build.Mesh, path, sInch);
        MeshExporter.ExportStlBinary(_build.Mesh, mmPath, sMm);

        // اولین vertex بعد از هدر ۸۴ بایتی + نرمال ۱۲ بایتی
        float inchX = BitConverter.ToSingle(File.ReadAllBytes(path), 84 + 12);
        float mmX = BitConverter.ToSingle(File.ReadAllBytes(mmPath), 84 + 12);
        Assert.Equal(mmX / 25.4f, inchX, 4);
    }

    [Fact]
    public void PresetTolerance_ScalesWithModel()
    {
        var s = new MeshExportSettings();
        double small = s.EffectiveToleranceMm(20);
        double big = s.EffectiveToleranceMm(500);
        Assert.True(big > small, "مدل بزرگ‌تر باید تلرانس بزرگ‌تری داشته باشد");
        Assert.True(small >= 0.02);
    }

    [Fact]
    public void SanitizeName_RemovesInvalidChars()
    {
        Assert.Equal("abc", MeshExporter.SanitizeName("a<b>|c?*"));
        Assert.Equal("part", MeshExporter.SanitizeName("///"));
    }

    [Fact]
    public void QualityPresets_ChangeTriangleCounts()
    {
        var coarse = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { ToothCount = 30, Module = 2 },
            new BodyFeatures { BoreDiameter = 10 },
            new GeometrySettings { ProfileToleranceMm = 0.5 });
        var fine = GearGeometryEngine.Build(GearFamily.ExternalSpur,
            new ToothParameters { ToothCount = 30, Module = 2 },
            new BodyFeatures { BoreDiameter = 10 },
            new GeometrySettings { ProfileToleranceMm = 0.005 });
        Assert.True(fine.Mesh.TriangleCount > coarse.Mesh.TriangleCount,
            $"دقت‌تر باید مثلث بیشتری بسازد: fine={fine.Mesh.TriangleCount} coarse={coarse.Mesh.TriangleCount}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
