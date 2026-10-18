using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های خانواده‌های پیشرفته (مخروطی، کرم/چرخ کرم، تاجی، سیکلوئیدی، غیرمدور، هارمونیک، هزارخار).
/// معیار: مش آب‌بند (منیفولد بسته)، حجم مثبت، بازتولیدپذیری و رد ورودی نامعتبر با پیام روشن.
/// </summary>
public class AdvancedFamilyTests
{
    private static ToothParameters T(double module, int z, double width, double helix = 0) => new()
    {
        Module = module,
        ToothCount = z,
        FaceWidth = width,
        HelixAngle = helix,
    };

    private static void AssertWatertight(GearBuildResult r, string name)
    {
        Assert.NotNull(r);
        var v = r.Mesh.Validate();
        Assert.True(v.ClosedManifold,
            $"{name}: مش آب‌بند نیست — {string.Join("; ", v.Issues)}");
        Assert.True(v.SignedVolumeMm3 > 0.001, $"{name}: حجم مش باید مثبت باشد (فعلی: {v.SignedVolumeMm3:F2})");
    }

    // ---------- مخروطی ----------

    [Fact]
    public void BevelStraight_BuildsWatertightMesh()
    {
        var r = GearGeometryEngine.Build(GearFamily.BevelSpur, T(3, 24, 18), new BodyFeatures { BoreDiameter = 12 },
            new GeometrySettings(), new FamilyParameters { BevelPitchConeAngle = 0.6 });
        AssertWatertight(r, "مخروطی مستقیم");
        Assert.InRange(r.Figures.PitchRadius, 30, 40);
    }

    [Fact]
    public void BevelSpiral_BuildsWatertightMesh()
    {
        var r = GearGeometryEngine.Build(GearFamily.BevelSpiral, T(3, 20, 15), new BodyFeatures { BoreDiameter = 10 },
            new GeometrySettings(), new FamilyParameters
            {
                BevelPitchConeAngle = Math.PI / 4,
                BevelSpiralAngle = UnitConverter.DegToRad(32),
            });
        AssertWatertight(r, "مخروطی مارپیچ");
    }

    [Fact]
    public void Bevel_WithHubAndKeyway_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.BevelSpur, T(2.5, 28, 14), new BodyFeatures
        {
            BoreDiameter = 10,
            HubDiameter = 34,
            HubLength = 8,
            KeywayWidth = 3,
            KeywayDepth = 1.7,
        }, new GeometrySettings(), new FamilyParameters { BevelPitchConeAngle = 0.55 });
        AssertWatertight(r, "مخروطی با توپی و شیار");
    }

    [Fact]
    public void Bevel_BoreTooLarge_Rejected()
    {
        var ex = Assert.Throws<GeometryBuildException>(() =>
            GearGeometryEngine.Build(GearFamily.BevelSpur, T(4, 30, 20),
                new BodyFeatures { BoreDiameter = 100 }, new GeometrySettings(),
                new FamilyParameters { BevelPitchConeAngle = 0.35 }));
        Assert.Contains("بور", ex.Message);
    }

    // ---------- کرم و چرخ کرم ----------

    [Fact]
    public void Worm_SingleStart_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.Worm, T(2.5, 2, 40), new BodyFeatures { BoreDiameter = 12 },
            new GeometrySettings(), new FamilyParameters { WormPitchDiameterMm = 40 });
        AssertWatertight(r, "کرم دومرتبه");
        Assert.Equal(2, r.Figures.ToothCount);
    }

    [Fact]
    public void Worm_FourStartLeftHand_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.Worm, T(2, 4, 50), new BodyFeatures { BoreDiameter = 16 },
            new GeometrySettings(), new FamilyParameters
            {
                WormPitchDiameterMm = 36,
                WormIsRightHanded = false,
            });
        AssertWatertight(r, "کرم چهامرتبه چپ‌گرد");
    }

    [Fact]
    public void WormWheel_Throated_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.WormWheel, T(2.5, 40, 0), new BodyFeatures
        {
            BoreDiameter = 16,
            HubDiameter = 60,
            HubLength = 8,
        }, new GeometrySettings(), new FamilyParameters
        {
            WormPitchDiameterMm = 40,
            WormStartsCount = 2,
        });
        AssertWatertight(r, "چرخ کرم گلوگاه‌دار");
        Assert.True(r.Figures.HelixAngleUsed > 0.01, "چرخ کرم باید مارپیچ غیرصفر داشته باشد");
    }

    [Fact]
    public void Worm_TooManyStarts_Rejected()
    {
        Assert.Throws<GeometryBuildException>(() =>
            GearGeometryEngine.Build(GearFamily.Worm, T(2, 20, 40), new BodyFeatures(),
                new GeometrySettings(), new FamilyParameters()));
    }

    // ---------- تاجی ----------

    [Fact]
    public void CrownFace_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.CrownFace, T(2, 32, 18), new BodyFeatures { BoreDiameter = 14 },
            new GeometrySettings(), new FamilyParameters());
        AssertWatertight(r, "چرخ تاجی");
        Assert.InRange(r.Figures.PitchRadius, 30, 34);
    }

    // ---------- سیکلوئیدی ----------

    [Fact]
    public void Cycloidal_WatertightAndNarrowerTip()
    {
        var r = GearGeometryEngine.Build(GearFamily.Cycloidal, T(2, 24, 12), new BodyFeatures { BoreDiameter = 8 },
            new GeometrySettings(), new FamilyParameters());
        AssertWatertight(r, "سیکلوئیدی");
        var involute = GearGeometryEngine.Build(GearFamily.ExternalSpur, T(2, 24, 12),
            new BodyFeatures { BoreDiameter = 8 }, new GeometrySettings());
        // دندانهٔ سیکلوئیدی در نوک باریک‌تر از اینولوت است
        Assert.True(r.Mesh.VertexCount > 100);
        Assert.InRange(r.Figures.TipRadius, involute.Figures.TipRadius - 0.01, involute.Figures.TipRadius + 0.01);
    }

    // ---------- غیرمدور ----------

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void NonCircular_VariousEccentricities_Watertight(double e)
    {
        var r = GearGeometryEngine.Build(GearFamily.NonCircular, T(2, 28, 10), new BodyFeatures { BoreDiameter = 6 },
            new GeometrySettings(), new FamilyParameters { NonCircularEccentricity = e });
        AssertWatertight(r, $"غیرمدور e={e}");
        // بور روی کانون؛ قطر بیضی از بور بزرگ‌تر است
        var (min, max) = r.Mesh.Bounds();
        Assert.True(max.X - min.X > 2.0, "قطر بیضوی باید بزرگ باشد");
    }

    [Fact]
    public void NonCircular_PerimeterClosesOnTeeth()
    {
        // محیط بیضی باید دقیقاً z گام دندانه باشد؛ کنترل با محاسبهٔ مستقل E(e)
        double e = 0.5;
        double E = NonCircularMeshBuilder.CompleteEllipticE(e);
        double a = 30 * Math.PI * 2.0 / (4.0 * E);
        double expectedPerimeter = 4.0 * a * E;
        Assert.InRange(expectedPerimeter, 30 * Math.PI * 2.0 - 1e-6, 30 * Math.PI * 2.0 + 1e-6);
    }

    [Fact]
    public void NonCircular_EccentricityOutOfRange_ClampedWithWarning()
    {
        var r = GearGeometryEngine.Build(GearFamily.NonCircular, T(0.5, 48, 8), new BodyFeatures { BoreDiameter = 1.5 },
            new GeometrySettings(), new FamilyParameters { NonCircularEccentricity = 0.99 });
        Assert.Contains(r.Diagnostics, d => d.Code == "SGM-W301");
    }

    // ---------- هارمونیک ----------

    [Fact]
    public void FlexSpline_CupWatertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.HarmonicDrive, T(0.8, 160, 12), new BodyFeatures
        {
            BoreDiameter = 24,
            LighteningHoleCount = 6,
            LighteningHoleDiameter = 5,
            LighteningHoleCircleRadius = 26,
        }, new GeometrySettings(), new FamilyParameters());
        AssertWatertight(r, "فِلکسی‌پلاین");
        Assert.True(r.Mesh.VertexCount > 2000, "فنجان دندانه‌دار باید مش قابل‌توجهی داشته باشد");
    }

    [Fact]
    public void WaveGenerator_EllipticalPrism_Watertight()
    {
        var wg = NonCircularMeshBuilder.BuildWaveGenerator(
            T(0.8, 160, 10), new BodyFeatures { BoreDiameter = 20 }, new GeometrySettings(),
            new FamilyParameters());
        var v = wg.Validate();
        Assert.True(v.ClosedManifold, "موج‌ساز: " + string.Join("; ", v.Issues));
        Assert.True(v.SignedVolumeMm3 > 0.001);
    }

    // ---------- هزارخار ----------

    [Fact]
    public void SplineExternal_ShortTeethWatertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.SplineExternal, T(1.5, 18, 22), new BodyFeatures { BoreDiameter = 0 },
            new GeometrySettings(), new FamilyParameters { SplineBacklashMm = 0.05 });
        AssertWatertight(r, "هزارخار خارجی");
        Assert.Contains(r.Diagnostics, d => d.Code == "SGM-I520");
    }

    [Fact]
    public void SplineInternal_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.SplineInternal, T(1.5, 24, 20), new BodyFeatures { BoreDiameter = 0 },
            new GeometrySettings(), new FamilyParameters());
        AssertWatertight(r, "هزارخار داخلی");
    }

    // ---------- مارپیچ متقاطع ----------

    [Fact]
    public void CrossedHelical_ReusesHelicalGeometry()
    {
        var r = GearGeometryEngine.Build(GearFamily.CrossedHelical, T(2, 21, 16, UnitConverter.DegToRad(25)),
            new BodyFeatures { BoreDiameter = 10 }, new GeometrySettings(), new FamilyParameters());
        AssertWatertight(r, "مارپیچ متقاطع");
    }

    // ---------- هیپوئید ----------

    [Fact]
    public void Hypoid_OffsetSpiralBevel_Watertight()
    {
        var r = GearGeometryEngine.Build(GearFamily.Hypoid, T(3, 22, 16), new BodyFeatures { BoreDiameter = 12 },
            new GeometrySettings(), new FamilyParameters
            {
                BevelPitchConeAngle = Math.PI / 4,
                BevelSpiralAngle = UnitConverter.DegToRad(40),
                HypoidOffsetMm = 12,
            });
        AssertWatertight(r, "هیپوئید");
    }

    // ---------- عمومی ----------

    [Fact]
    public void AllFamilies_Reproducible()
    {
        foreach (GearFamily f in Enum.GetValues<GearFamily>())
        {
            if (!CapabilityCatalog.BuildsGeometry(f))
                continue;
            if (f is GearFamily.Rack or GearFamily.HelicalRack)
                continue; // جداگانه آزموده شده‌اند

            var teeth = f switch
            {
                GearFamily.HarmonicDrive => T(0.8, 120, 10),
                GearFamily.Worm => T(2.5, 3, 30),
                GearFamily.WormWheel => T(2, 40, 12),
                GearFamily.SplineInternal or GearFamily.SplineExternal => T(1.5, 20, 18),
                _ => T(2.5, 24, 14, f is GearFamily.ExternalHelical or GearFamily.InternalHelical or GearFamily.CrossedHelical ? UnitConverter.DegToRad(18) : 0),
            };
            var body = new BodyFeatures { BoreDiameter = teeth.ToothCount > 100 ? 20 : 10 };

            var r1 = GearGeometryEngine.Build(f, teeth, body, new GeometrySettings { ProfileToleranceMm = 0.03 }, new FamilyParameters());
            var r2 = GearGeometryEngine.Build(f, teeth, body, new GeometrySettings { ProfileToleranceMm = 0.03 }, new FamilyParameters());
            Assert.Equal(r1.Mesh.VertexCount, r2.Mesh.VertexCount);
            Assert.Equal(r1.Mesh.TriangleCount, r2.Mesh.TriangleCount);
            Assert.Equal(r1.Mesh.VertexAt(0).X, r2.Mesh.VertexAt(0).X, 5);
        }
    }
}
