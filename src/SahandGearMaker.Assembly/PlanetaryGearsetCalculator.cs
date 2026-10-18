using SahandGearMaker.Domain;

namespace SahandGearMaker.Assembly;

/// <summary>حالت عضو ثابت مجموعهٔ سیاره‌ای.</summary>
public enum PlanetaryFixedMember
{
    /// <summary>حلقهٔ داخلی ثابت؛ ورودی خورشیدی، خروجی حامل.</summary>
    Ring,
    /// <summary>خورشیدی ثابت؛ ورودی حلقه، خروجی حامل.</summary>
    Sun,
    /// <summary>حامل ثابت (حالت ستاره‌ای).</summary>
    Carrier,
}

/// <summary>نتیجهٔ طراحی/بررسی مجموعهٔ سیاره‌ای ساده.</summary>
public sealed class PlanetaryResult
{
    public bool GeometryFeasible { get; init; }
    public int SunTeeth { get; init; }
    public int PlanetTeeth { get; init; }
    public int RingTeeth { get; init; }
    public int PlanetCount { get; init; }
    /// <summary>نسبت انتقال کل (همیشه ≥ 1 برای حالت‌های کاهشی متداول).</summary>
    public double OverallRatio { get; init; }
    public double RotationDirection { get; init; }
    /// <summary>فاصلهٔ محور خورشیدی-سیاره (همچنین شعاع قرارگیری سیاره‌ها).</summary>
    public double PlanetOrbitRadiusMm { get; init; }
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; init; } = Array.Empty<GearDiagnostic>();
    /// <summary>زاویهٔ فاز اولین سیاره برای مونتاژ هم‌مرکز (رادیان).</summary>
    public double[] PlanetAngles { get; init; } = Array.Empty<double>();
}

/// <summary>
/// مجموعهٔ سیاره‌ای ساده (سند ۰۳، بخش ۶):
///   قید هندسی مرجع: zᵣ = zₛ + 2zₚ (خانوادهٔ هم‌مدول بدون اصلاح)
///   رابطهٔ ویلیس برای حلقهٔ داخلی: (ωₛ−ω꜀)/(ωᵣ−ω꜀) = −zᵣ/zₛ
/// قید تقسیم‌پذیری مونتاژ: (zₛ+zᵣ) بر تعداد سیاره‌ها بخش‌پذیر باشد (شرط کلاسیک فاز).
/// </summary>
public static class PlanetaryGearsetCalculator
{
    public static PlanetaryResult Design(int sunTeeth, int planetTeeth, int planetCount,
        double module, PlanetaryFixedMember fixedMember)
    {
        var diags = new List<GearDiagnostic>();
        int ringTeeth = sunTeeth + 2 * planetTeeth;

        bool feasible = sunTeeth >= 5 && planetTeeth >= 5 && module > 0 && planetCount >= 1;
        if (!feasible)
        {
            diags.Add(new(DiagnosticCodes.PlanetCountInvalid, Severity.Error,
                "تعداد دندانهٔ خورشیدی/سیاره باید حداقل ۵، ماژول مثبت و تعداد سیاره حداقل ۱ باشد."));
            return new PlanetaryResult
            {
                GeometryFeasible = false,
                SunTeeth = sunTeeth,
                PlanetTeeth = planetTeeth,
                RingTeeth = ringTeeth,
                PlanetCount = planetCount,
                OverallRatio = double.NaN,
                PlanetOrbitRadiusMm = module * (sunTeeth + planetTeeth) / 2.0,
                Diagnostics = diags,
            };
        }

        // قید تقسیم‌پذیری فاز: (zₛ + zᵣ) mod n == 0 → مونتاژ هم‌فاز ممکن است
        if ((sunTeeth + ringTeeth) % planetCount != 0)
        {
            diags.Add(new(DiagnosticCodes.PlanetSpacingInvalid, Severity.Warning,
                $"مجموع دندانه‌های خورشیدی و حلقه ({sunTeeth}+{ringTeeth}={sunTeeth + ringTeeth}) بر تعداد سیاره‌ها ({planetCount}) بخش‌پذیر نیست؛ " +
                "چیدمان هم‌فاز دقیق ممکن نیست و در ساخت واقعی نیازمند تنظیم فاز یا تعداد سیارهٔ متفاوت است."));
        }

        if (planetCount > 1 && AdjacentPlanetsInterfere(sunTeeth, planetTeeth, planetCount))
        {
            diags.Add(new(DiagnosticCodes.PlanetSpacingInvalid, Severity.Warning,
                "سیاره‌های مجاور ممکن است با هم تماس پیدا کنند (فاصلهٔ زاویه‌ای کم)؛ تعداد سیاره را کم کنید."));
        }

        // نسبت ویلیس برای حالت‌های متداول
        double ratio = fixedMember switch
        {
            PlanetaryFixedMember.Ring => 1.0 + (double)ringTeeth / sunTeeth,   // خورشیدی ورودی، حامل خروجی
            PlanetaryFixedMember.Sun => 1.0 + (double)sunTeeth / ringTeeth,    // حلقه ورودی، حامل خروجی
            _ => -(double)ringTeeth / sunTeeth,                                 // حامل ثابت (ستاره‌ای): خروجی معکوس
        };

        double orbit = module * (sunTeeth + planetTeeth) / 2.0;
        var angles = new double[planetCount];
        for (int i = 0; i < planetCount; i++)
            angles[i] = 2.0 * Math.PI * i / planetCount;

        diags.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
            "فاز دندانهٔ سیاره‌ها و برخورد دقیق فقط به‌صورت قیدهای مرجع بررسی شده است."));
        diags.Add(new(DiagnosticCodes.StandardComplianceNotChecked, Severity.Unverified,
            "قید zᵣ = zₛ + 2zₚ بدون اصلاح پروفیل اعمال شده؛ برای ساخت واقعی، اصلاح پروفیل بررسی شود."));

        return new PlanetaryResult
        {
            GeometryFeasible = true,
            SunTeeth = sunTeeth,
            PlanetTeeth = planetTeeth,
            RingTeeth = ringTeeth,
            PlanetCount = planetCount,
            OverallRatio = Math.Abs(ratio),
            RotationDirection = Math.Sign(ratio),
            PlanetOrbitRadiusMm = orbit,
            PlanetAngles = angles,
            Diagnostics = diags,
        };
    }

    /// <summary>بررسی تماس سیاره‌های مجاور: زاویهٔ بین مراکز در برابر مجموع شعاع سر.</summary>
    private static bool AdjacentPlanetsInterfere(int sun, int planet, int count)
    {
        double angleBetween = 2.0 * Math.PI / count;
        double required = 2.0 * (planet + 2.0) / (sun + planet); // زاویهٔ حداقلی بر حسب نسبت (تقریب مرجع)
        return angleBetween < required;
    }
}

/// <summary>
/// قطار مرکب چندمرحله‌ای (سند ۰۳، بخش ۶): نسبت کل = حاصل‌ضرب نسبت‌های مراحل؛
/// جهت چرخش با تعداد تماس‌های خارجی تعیین می‌شود.
/// </summary>
public static class CompoundTrainCalculator
{
    public sealed class Stage
    {
        public int DriverTeeth { get; init; }
        public int DrivenTeeth { get; init; }
    }

    public static (double Ratio, int Direction, IReadOnlyList<GearDiagnostic> Diagnostics) Evaluate(IEnumerable<Stage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var diags = new List<GearDiagnostic>();
        double ratio = 1.0;
        int externalContacts = 0;

        foreach (var stage in stages)
        {
            if (stage.DriverTeeth < 5 || stage.DrivenTeeth < 5)
            {
                diags.Add(new(DiagnosticCodes.ToothCountTooLow, Severity.Error,
                    $"مرحله با دندانه‌های {stage.DriverTeeth}/{stage.DrivenTeeth} نامعتبر است (حداقل ۵)."));
                return (double.NaN, 0, diags);
            }
            ratio *= (double)stage.DriverTeeth / stage.DrivenTeeth;
            externalContacts++;
        }

        if (diags.Count == 0)
            diags.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
                "فاصله‌های محور مراحل باید جداگانه با GearPairCalculator بررسی شود."));

        return (ratio, externalContacts % 2 == 0 ? +1 : -1, diags);
    }
}
