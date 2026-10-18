using SahandGearMaker.Domain;
using Xunit;

namespace SahandGearMaker.Tests;

public class UnitConverterTests
{
    [Theory]
    [InlineData(25.4, 1.0)]
    [InlineData(1.0, 1.0 / 25.4)]
    [InlineData(0.0, 0.0)]
    public void InchRoundTrip_PreservesSize(double mm, double inch)
    {
        Assert.Equal(mm, UnitConverter.ToInternalLength(inch, LengthUnit.Inch), 9);
        Assert.Equal(inch, UnitConverter.FromInternalLength(mm, LengthUnit.Inch), 12);
    }

    [Fact]
    public void MmIsIdentity()
    {
        Assert.Equal(12.34, UnitConverter.ToInternalLength(12.34, LengthUnit.Millimeter), 12);
    }

    [Fact]
    public void DegreeRadianConversion()
    {
        Assert.Equal(Math.PI, UnitConverter.DegToRad(180), 12);
        Assert.Equal(180, UnitConverter.RadToDeg(Math.PI), 12);
    }
}

public class GearValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void NonPositiveModule_IsError(double module)
    {
        var p = new ToothParameters { Module = module };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ModuleNotPositive && d.Severity == Severity.Error);
    }

    [Fact]
    public void NaNInput_IsError()
    {
        var p = new ToothParameters { Module = double.NaN };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.NonFiniteInput && d.Severity == Severity.Error);
    }

    [Fact]
    public void TooFewTeeth_IsError()
    {
        var p = new ToothParameters { ToothCount = 3, Module = 2 };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ToothCountTooLow && d.Severity == Severity.Error);
    }

    [Fact]
    public void PressureAngleOutOfRange_IsError()
    {
        var p = new ToothParameters { PressureAngle = UnitConverter.DegToRad(50) };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.PressureAngleOutOfRange);
    }

    [Fact]
    public void SmallExternalGear_WarnsUndercut()
    {
        // zmin = 2/sin²(20°) ≈ 17.1 → 10 دندانه بدون اصلاح باید هشدار بدهد
        var p = new ToothParameters { ToothCount = 10, Module = 2 };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.UndercutRisk && d.Severity == Severity.Warning);
    }

    [Fact]
    public void ValidGear_HasUnverifiedHonestyEntries()
    {
        var p = new ToothParameters { ToothCount = 24, Module = 2 };
        var diags = GearValidator.Validate(GearFamily.ExternalSpur, p);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ContactAnalysisNotRun && d.Severity == Severity.Unverified);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.StrengthNotAnalyzed && d.Severity == Severity.Unverified);
        Assert.DoesNotContain(diags, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void HelicalTransverseRelations()
    {
        // mt = mn/cos β و tan αt = tan αn / cos β (سند ۰۳، بخش ۴)
        double beta = UnitConverter.DegToRad(20);
        double mt = GearValidator.TransverseModule(2.0, beta);
        Assert.Equal(2.0 / Math.Cos(beta), mt, 12);

        double at = GearValidator.TransversePressureAngle(UnitConverter.DegToRad(20), beta);
        Assert.Equal(Math.Atan(Math.Tan(UnitConverter.DegToRad(20)) / Math.Cos(beta)), at, 12);
    }
}

public class GearFiguresTests
{
    [Fact]
    public void SpurReferenceDiameters_MatchStandardFormulas()
    {
        // مدل مرجع full-depth بدون اصلاح: d=m·z، da=m(z+2)، df=m(z−2.5) (سند ۰۳، بخش ۴)
        var p = new ToothParameters { ToothCount = 20, Module = 2 };
        var f = GearFiguresCalculator.Compute(GearFamily.ExternalSpur, p);
        Assert.Equal(40.0, f.PitchRadius * 2, 9);
        Assert.Equal(44.0, f.TipRadius * 2, 9);
        Assert.Equal(35.0, f.RootRadius * 2, 9);
        Assert.Equal(40.0 * Math.Cos(UnitConverter.DegToRad(20)), f.BaseRadius * 2, 9);
    }

    [Fact]
    public void InternalGear_TipInsidePitch_RootOutside()
    {
        var p = new ToothParameters { ToothCount = 40, Module = 2 };
        var f = GearFiguresCalculator.Compute(GearFamily.InternalSpur, p);
        Assert.True(f.TipRadius < f.PitchRadius, "سر دندانهٔ داخلی باید داخل دایرهٔ گام باشد");
        Assert.True(f.RootRadius > f.PitchRadius, "ریشهٔ دندانهٔ داخلی باید بیرون دایرهٔ گام باشد");
    }

    [Fact]
    public void DeterministicRebuild_ProducesIdenticalFigures()
    {
        var p = new ToothParameters { ToothCount = 33, Module = 1.5, HelixAngle = 0.3 };
        var a = GearFiguresCalculator.Compute(GearFamily.ExternalHelical, p);
        var b = GearFiguresCalculator.Compute(GearFamily.ExternalHelical, p);
        Assert.Equal(a.PitchRadius, b.PitchRadius, 12);
        Assert.Equal(a.ToothThicknessAtPitch, b.ToothThicknessAtPitch, 12);
    }
}
