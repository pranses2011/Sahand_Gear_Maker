using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using Xunit;

namespace SahandGearMaker.Tests;

public class MeshBuildingTests
{
    private static GearBuildResult Build(GearFamily family, ToothParameters teeth, BodyFeatures? body = null)
        => GearGeometryEngine.Build(family, teeth, body ?? new BodyFeatures { BoreDiameter = 10 }, new GeometrySettings());

    [Fact]
    public void ExternalSpur_ProducesClosedManifoldPositiveVolume()
    {
        var result = Build(GearFamily.ExternalSpur, new ToothParameters { ToothCount = 20, Module = 2, FaceWidth = 20 });
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
        Assert.True(v.SignedVolumeMm3 > 1000);
        Assert.True(result.Mesh.TriangleCount > 100);
    }

    [Fact]
    public void ExternalSpur_NoBore_AlsoClosed()
    {
        var result = Build(GearFamily.ExternalSpur, new ToothParameters { ToothCount = 24, Module = 2 }, new BodyFeatures { BoreDiameter = 0 });
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
    }

    [Fact]
    public void ExternalSpur_WithKeywayHubLightening_BuildsWithoutError()
    {
        var body = new BodyFeatures
        {
            BoreDiameter = 10,
            KeywayWidth = 3,
            KeywayDepth = 1.5,
            HubDiameter = 26,
            HubLength = 6,
        };
        var result = Build(GearFamily.ExternalSpur, new ToothParameters { ToothCount = 30, Module = 2, FaceWidth = 16 }, body);
        Assert.True(result.Mesh.TriangleCount > 500);
    }

    [Fact]
    public void ExternalHelical_ProducesClosedManifold()
    {
        var result = Build(GearFamily.ExternalHelical, new ToothParameters { ToothCount = 20, Module = 2, HelixAngle = 0.3, FaceWidth = 20 });
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
        Assert.True(result.AxialSlicesUsed > 10, "مارپیچ باید چندبرشی ساخته شود");
    }

    [Fact]
    public void ExternalHerringbone_ProducesClosedManifold()
    {
        var result = Build(GearFamily.ExternalHerringbone, new ToothParameters { ToothCount = 22, Module = 2, HelixAngle = 0.3, FaceWidth = 24 });
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
    }

    [Fact]
    public void InternalSpur_ProducesClosedManifold()
    {
        var result = Build(GearFamily.InternalSpur, new ToothParameters { ToothCount = 40, Module = 2, FaceWidth = 15 });
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
    }

    [Fact]
    public void Rack_ProducesClosedManifold()
    {
        var result = Build(GearFamily.Rack,
            new ToothParameters { Module = 2, FaceWidth = 20 },
            new BodyFeatures { LighteningHoleCircleRadius = 60 }); // طول شانه = 60mm
        var v = result.Mesh.Validate();
        Assert.True(v.ClosedManifold, string.Join("; ", v.Issues));
        Assert.True(v.SignedVolumeMm3 > 100);
    }

    [Fact]
    public void Helical_VolumeCloseToSpur_WithSameTransversePitch()
    {
        // برای mn و β یکسان، مدول عرضی بزرگ‌تر می‌شود؛ حجم باید همان مرتبه بماند (کنترل بازگشتی)
        var spur = Build(GearFamily.ExternalSpur, new ToothParameters { ToothCount = 20, Module = 2, FaceWidth = 20 });
        var hel = Build(GearFamily.ExternalHelical, new ToothParameters { ToothCount = 20, Module = 2, HelixAngle = 0.25, FaceWidth = 20 });
        double vs = spur.Mesh.Validate().SignedVolumeMm3;
        double vh = hel.Mesh.Validate().SignedVolumeMm3;
        Assert.InRange(vh / vs, 0.6, 1.6);
    }

    [Fact]
    public void InvalidParameters_ThrowGeometryBuildException_WithPersianMessage()
    {
        var ex = Assert.Throws<GeometryBuildException>(() =>
            Build(GearFamily.ExternalSpur, new ToothParameters { ToothCount = 20, Module = 0 }));
        Assert.NotEmpty(ex.Message);
        Assert.Contains(ex.Diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void EveryEnumFamily_IsSupported()
    {
        // پس از تکمیل نقشهٔ راه، هر عضو enum باید هندسهٔ واقعی بسازد یا خطای پارامتر بدهد (نه «نقشهٔ راه»)
        foreach (GearFamily fam in Enum.GetValues(typeof(GearFamily)))
        {
            Assert.True(GearGeometryEngine.IsSupported(fam), $"خانوادهٔ {fam} باید پشتیبانی شود.");
        }
    }

    [Fact]
    public void InvalidInput_RejectedWithClearMessage()
    {
        var ex = Assert.Throws<GeometryBuildException>(() =>
            Build(GearFamily.Worm, new ToothParameters { ToothCount = 99, Module = 2, FaceWidth = 30 }));
        Assert.Contains("رشته", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicRebuild_SameInputSameMesh()
    {
        var p = new ToothParameters { ToothCount = 18, Module = 2.5, FaceWidth = 12 };
        var a = Build(GearFamily.ExternalSpur, p);
        var b = Build(GearFamily.ExternalSpur, p);
        Assert.Equal(a.Mesh.TriangleCount, b.Mesh.TriangleCount);
        Assert.Equal(a.Mesh.VertexCount, b.Mesh.VertexCount);
        var (amin, amax) = a.Mesh.Bounds();
        var (bmin, bmax) = b.Mesh.Bounds();
        Assert.Equal(amin.X, bmin.X, 9);
        Assert.Equal(amax.Z, bmax.Z, 9);
    }

    [Fact]
    public void Bounds_ContainExpectedGearEnvelope()
    {
        var p = new ToothParameters { ToothCount = 20, Module = 2, FaceWidth = 20 };
        var result = Build(GearFamily.ExternalSpur, p);
        var (min, max) = result.Mesh.Bounds();
        Assert.Equal(0, min.Z, 6);
        Assert.Equal(p.FaceWidth, max.Z, 6);
        // شعاع سر m(z+2)/2 — با تلرانس نمونه‌برداری مرز
        Assert.InRange(max.X, result.Figures.TipRadius - 0.05, result.Figures.TipRadius + 0.05);
        Assert.InRange(max.Y, result.Figures.TipRadius - 0.05, result.Figures.TipRadius + 0.05);
    }

    [Fact]
    public void Fuzz_RandomValidInputs_NeverCrashOrInfinite()
    {
        var rng = new Random(42);
        for (int i = 0; i < 30; i++)
        {
            var family = (GearFamily[])Enum.GetValues(typeof(GearFamily));
            var fam = family[rng.Next(family.Length)];
            if (!GearGeometryEngine.IsSupported(fam)) continue;

            // دامنهٔ دندانهٔ معتبر هر خانواده (کرم: رشته؛ هارمونیک: ریز‌دندانه)
            int zMin = fam switch
            {
                GearFamily.Worm => 1,
                GearFamily.HarmonicDrive => 80,
                GearFamily.WormWheel => 25,
                GearFamily.CrownFace or GearFamily.Cycloidal or GearFamily.NonCircular => 14,
                GearFamily.SplineExternal or GearFamily.SplineInternal => 8,
                _ => 8,
            };
            int zMax = fam switch
            {
                GearFamily.Worm => 8,
                GearFamily.HarmonicDrive => 140,
                _ => 60,
            };
            var p = new ToothParameters
            {
                ToothCount = rng.Next(zMin, zMax),
                Module = fam == GearFamily.HarmonicDrive ? 0.5 + rng.NextDouble() * 0.6 : 0.5 + rng.NextDouble() * 4,
                FaceWidth = 4 + rng.NextDouble() * 30,
                HelixAngle = fam is GearFamily.Worm or GearFamily.WormWheel or GearFamily.HarmonicDrive
                    or GearFamily.NonCircular or GearFamily.CrownFace or GearFamily.Cycloidal
                    or GearFamily.SplineExternal or GearFamily.SplineInternal or GearFamily.BevelSpur
                    or GearFamily.BevelSpiral or GearFamily.Hypoid
                    ? 0 : rng.NextDouble() * 0.4,
                PressureAngle = 0.35,
            };

            GearBuildResult? result = null;
            try
            {
                result = GearGeometryEngine.Build(fam, p, new BodyFeatures { BoreDiameter = rng.Next(0, 10) }, new GeometrySettings());
            }
            catch (GeometryBuildException)
            {
                continue; // رد صادقانهٔ ورودی نامعتبر با پیام روشن — crash نیست
            }
            foreach (var v in result.Mesh.Vertices)
            {
                Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z));
            }
        }
    }
}
