using SahandGearMaker.Assembly;
using SahandGearMaker.Domain;
using SahandGearMaker.Persistence;
using Xunit;

namespace SahandGearMaker.Tests;

public class PairCalculatorTests
{
    [Fact]
    public void ExternalPair_CenterDistance_MatchesStandardFormula()
    {
        // a = m(z1+z2)/2 (سند ۰۳، بخش ۴)
        var a = new ToothParameters { ToothCount = 20, Module = 2 };
        var b = new ToothParameters { ToothCount = 30, Module = 2 };
        var r = GearPairCalculator.CheckExternalPair(a, b);
        Assert.True(r.IsCompatible);
        Assert.Equal(2 * (20 + 30) / 2.0, r.CenterDistanceMm, 9);
        Assert.Equal(20.0 / 30.0, r.Ratio, 9);
        Assert.Equal(-1, rRotationDirectionSign(r));
    }

    private static int rRotationDirectionSign(MatingCheckResult r) => r.RotationDirection;

    [Fact]
    public void ExternalPair_ModuleMismatch_IsError()
    {
        var a = new ToothParameters { ToothCount = 20, Module = 2 };
        var b = new ToothParameters { ToothCount = 30, Module = 3 };
        var r = GearPairCalculator.CheckExternalPair(a, b);
        Assert.False(r.IsCompatible);
        Assert.Contains(r.Diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void ExternalHelicalPair_SameHand_IsError()
    {
        var a = new ToothParameters { ToothCount = 20, Module = 2, HelixAngle = 0.3 };
        var b = new ToothParameters { ToothCount = 30, Module = 2, HelixAngle = 0.3 };
        var r = GearPairCalculator.CheckExternalPair(a, b);
        Assert.False(r.IsCompatible);
    }

    [Fact]
    public void InternalPair_CenterDistance_And_Direction()
    {
        // a = m(zr − zp)/2 و جهت هم‌جهت (سند ۰۳، بخش ۶)
        var pinion = new ToothParameters { ToothCount = 20, Module = 2 };
        var ring = new ToothParameters { ToothCount = 60, Module = 2 };
        var r = GearPairCalculator.CheckInternalPair(pinion, ring);
        Assert.True(r.IsCompatible);
        Assert.Equal(2 * (60 - 20) / 2.0, r.CenterDistanceMm, 9);
        Assert.Equal(+1, r.RotationDirection);
    }

    [Fact]
    public void InternalPair_SmallDifference_WarnsInterference()
    {
        var pinion = new ToothParameters { ToothCount = 40, Module = 2 };
        var ring = new ToothParameters { ToothCount = 44, Module = 2 };
        var r = GearPairCalculator.CheckInternalPair(pinion, ring);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.InterferenceRiskInternal);
    }

    [Fact]
    public void RackPinion_TravelPerRevolution_MatchesPitch()
    {
        var pinion = new ToothParameters { ToothCount = 20, Module = 2 };
        var rack = new ToothParameters { Module = 2 };
        var r = GearPairCalculator.CheckRackPinion(pinion, rack);
        Assert.True(r.IsCompatible);
        Assert.Equal(Math.PI * 2 * 20, r.Ratio, 9); // π·m·z
    }
}

public class PlanetaryTests
{
    [Fact]
    public void GeometricConstraint_RingEqualsSunPlusTwicePlanet()
    {
        // zᵣ = zₛ + 2zₚ (سند ۰۳، بخش ۶)
        var r = PlanetaryGearsetCalculator.Design(20, 15, 3, 2.0, PlanetaryFixedMember.Ring);
        Assert.Equal(50, r.RingTeeth);
        Assert.True(r.GeometryFeasible);
    }

    [Fact]
    public void WillisRatio_RingFixed_MatchesReference()
    {
        // حلقه ثابت: i = 1 + zr/zs = 1 + 50/20 = 3.5
        var r = PlanetaryGearsetCalculator.Design(20, 15, 3, 2.0, PlanetaryFixedMember.Ring);
        Assert.Equal(3.5, r.OverallRatio, 9);
    }

    [Fact]
    public void WillisRatio_SunFixed_MatchesReference()
    {
        // خورشیدی ثابت: i = 1 + zs/zr = 1 + 20/50 = 1.4
        var r = PlanetaryGearsetCalculator.Design(20, 15, 3, 2.0, PlanetaryFixedMember.Sun);
        Assert.Equal(1.4, r.OverallRatio, 9);
    }

    [Fact]
    public void AdjacentConstraint_WarnsWhenNotDivisible()
    {
        // zs+zr = 40 بر 3 بخش‌پذیر نیست → هشدار فاز
        var r = PlanetaryGearsetCalculator.Design(20, 15, 3, 2.0, PlanetaryFixedMember.Ring);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.PlanetSpacingInvalid);
    }

    [Fact]
    public void DivisibleAssembly_NoPhaseWarning()
    {
        // zs+zr = 44+116 = 160 بر 4 بخش‌پذیر → بدون هشدار فاز
        var r = PlanetaryGearsetCalculator.Design(24, 16, 4, 2.0, PlanetaryFixedMember.Ring);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.PlanetSpacingInvalid && d.Severity == Severity.Warning);
    }

    [Fact]
    public void PlanetAngles_AreEquallySpaced()
    {
        var r = PlanetaryGearsetCalculator.Design(20, 15, 4, 2.0, PlanetaryFixedMember.Ring);
        Assert.Equal(4, r.PlanetAngles.Length);
        Assert.Equal(2 * Math.PI / 4, r.PlanetAngles[1], 9);
    }

    [Fact]
    public void OrbitRadius_MatchesCenterDistance()
    {
        var r = PlanetaryGearsetCalculator.Design(20, 15, 3, 2.0, PlanetaryFixedMember.Ring);
        Assert.Equal(2 * (20 + 15) / 2.0, r.PlanetOrbitRadiusMm, 9);
    }
}

public class CompoundTrainTests
{
    [Fact]
    public void TwoStage_RatioIsProduct_DirectionFlipsTwice()
    {
        var stages = new[]
        {
            new CompoundTrainCalculator.Stage { DriverTeeth = 20, DrivenTeeth = 40 },
            new CompoundTrainCalculator.Stage { DriverTeeth = 10, DrivenTeeth = 30 },
        };
        var (ratio, dir, diags) = CompoundTrainCalculator.Evaluate(stages);
        Assert.Equal(0.5 * (10.0 / 30.0), ratio, 12);
        Assert.Equal(+1, dir); // دو تماس خارجی
    }

    [Fact]
    public void SingleStage_ReversesDirection()
    {
        var stages = new[] { new CompoundTrainCalculator.Stage { DriverTeeth = 20, DrivenTeeth = 40 } };
        var (ratio, dir, _) = CompoundTrainCalculator.Evaluate(stages);
        Assert.Equal(0.5, ratio, 12);
        Assert.Equal(-1, dir);
    }
}

public class ProjectFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sgm-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private string Dir
    {
        get { Directory.CreateDirectory(_dir); return _dir; }
    }

    [Fact]
    public void SaveLoadRoundTrip_PreservesParametersAndStructure()
    {
        var project = new GearProject { Name = "پروژهٔ آزمون" };
        var part = new GearPart
        {
            Name = "چرخ‌دندهٔ اصلی",
            Family = GearFamily.ExternalHelical,
            Teeth = new ToothParameters { ToothCount = 25, Module = 2.5, HelixAngle = 0.28, FaceWidth = 18 },
            Body = new BodyFeatures { BoreDiameter = 12, HubDiameter = 30, HubLength = 7 },
            Placement = new Placement { X = 10, Y = -3, Theta = 0.5 },
        };
        project.Parts.Add(part);
        project.Matings.Add(new MatingRelation { Kind = MatingKind.ExternalPair, PartAId = part.Id, PartBId = "other" });

        string path = Path.Combine(Dir, "test.sgdproj");
        ProjectFile.Save(project, path);
        var loaded = ProjectFile.Load(path);

        Assert.Equal(project.Name, loaded.Name);
        Assert.Single(loaded.Parts);
        var p = loaded.Parts[0];
        Assert.Equal(part.Id, p.Id);
        Assert.Equal("چرخ‌دندهٔ اصلی", p.Name);
        Assert.Equal(GearFamily.ExternalHelical, p.Family);
        Assert.Equal(25, p.Teeth.ToothCount);
        Assert.Equal(2.5, p.Teeth.Module, 12);
        Assert.Equal(0.28, p.Teeth.HelixAngle, 12);
        Assert.Equal(12, p.Body.BoreDiameter);
        Assert.Equal(30, p.Body.HubDiameter);
        Assert.Equal(10, p.Placement.X, 12);
        Assert.Equal(0.5, p.Placement.Theta, 12);
        Assert.Single(loaded.Matings);
        Assert.Equal(part.Id, loaded.Matings[0].PartAId);
    }

    [Fact]
    public void Save_IsAtomic_OriginalSurvivesWriteFailure()
    {
        string path = Path.Combine(Dir, "atomic.sgdproj");
        ProjectFile.Save(new GearProject { Name = "نسخهٔ اول" }, path);
        string before = File.ReadAllText(path);

        // مسیر نامعتبر برای فایل موقت → ذخیره شکست می‌خورد بدون لمس فایل اصلی
        var bad = new GearProject { Name = "نسخهٔ دوم" };
        Assert.ThrowsAny<Exception>(() => ProjectFile.Save(bad, Path.Combine(Dir, "nonexistent-dir", "x.sgdproj")));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => ProjectFile.Load(Path.Combine(Dir, "ghost.sgdproj")));
    }

    [Fact]
    public void UnknownData_FromNewerVersion_Preserved()
    {
        // شبیه‌سازی فایل از نسخهٔ آینده با فیلد ناشناخته (camelCase مطابق حالت Deserialize معمول)
        string path = Path.Combine(Dir, "future.sgdproj");
        File.WriteAllText(path,
            "{ \"schemaVersion\": 99, \"name\": \"آینده\", \"parts\": [], \"matings\": [], \"displayUnit\": 0, \"createdUtc\": \"2025-01-01T00:00:00Z\", \"modifiedUtc\": \"2025-01-01T00:00:00Z\", \"futureFeature\": { \"x\": 1 } }");
        var loaded = ProjectFile.Load(path);
        Assert.Equal("آینده", loaded.Name);
        Assert.Equal(99, loaded.SchemaVersion);
        Assert.NotNull(loaded.UnknownData);
        Assert.True(loaded.UnknownData.ContainsKey("futureFeature"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
