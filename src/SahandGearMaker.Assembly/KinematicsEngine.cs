using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;

namespace SahandGearMaker.Assembly;

/// <summary>وضعیت لحظه‌ای یک عضو در پیش‌نمایش حرکت.</summary>
public sealed class KinematicState
{
    /// <summary>شناسهٔ عضو.</summary>
    public string MemberId { get; init; } = "";

    /// <summary>نام نمایشی عضو.</summary>
    public string Name { get; init; } = "";

    /// <summary>زاویهٔ فعلی (رادیان).</summary>
    public double AngleRad { get; init; }

    /// <summary>سرعت زاویه‌ای نسبت به ورودی (علامت = جهت).</summary>
    public double SpeedRatio { get; init; }
}

/// <summary>نتیجهٔ گام پیش‌نمایش حرکت.</summary>
public sealed class KinematicsStep
{
    public IReadOnlyList<KinematicState> States { get; init; } = Array.Empty<KinematicState>();
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; init; } = Array.Empty<GearDiagnostic>();
}

/// <summary>
/// موتور سینماتیک ایدئال (سند ۰۷، بخش ۳٫۴) — حرکت صلب بدون لقی/انعطاف.
/// پشتیبانی: جفت خارجی/داخلی، شانه-پینیون، قطار مرکب، سیاره‌ای ساده و جفت‌های پیشرفته.
/// </summary>
public static class KinematicsEngine
{
    /// <summary>حالت زاویه‌ای جفت ساده (خارجی/داخلی/متقاطع/مخروطی/تاجی/هیپوئید) پس از چرخش ورودی به اندازهٔ Δθ.</summary>
    public static KinematicsStep PairStep(string idA, string idB, double ratio, int direction, double inputAngleRad)
    {
        var diags = new List<GearDiagnostic>
        {
            new("SGM-K100", Severity.Info,
                "پیش‌نمایش سینماتیک ایدئال است؛ لقی/انعطاف/ضربه مدل نمی‌شود و به هندسهٔ ذخیره‌شده آسیب نمی‌زند.", "Kinematics"),
        };
        return new KinematicsStep
        {
            States = new[]
            {
                new KinematicState { MemberId = idA, Name = idA, AngleRad = inputAngleRad, SpeedRatio = 1.0 },
                new KinematicState { MemberId = idB, Name = idB, AngleRad = direction * inputAngleRad / ratio, SpeedRatio = direction / ratio },
            },
            Diagnostics = diags,
        };
    }

    /// <summary>شانه و پینیون: دوران پینیون ↔ جابه‌جایی خطی شانه.</summary>
    public static (double RackDisplacementMm, double PinionAngleRad) RackStep(ToothParameters pinion, double pinionAngleRad)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        double pitch = Math.PI * pinion.Module;
        return (pinionAngleRad * pinion.ToothCount * pitch / (2.0 * Math.PI), pinionAngleRad);
    }

    /// <summary>قطار مرکب: زاویهٔ خروجی از حاصل‌ضرب معکوس نسبت‌ها با علامت جمعی.</summary>
    public static KinematicsStep CompoundTrainStep(
        IReadOnlyList<(string Id, double Ratio, int Direction)> stages, double inputAngleRad)
    {
        if (stages is not { Count: > 0 })
            throw new ArgumentException("قطار خالی است.", nameof(stages));

        var states = new List<KinematicState> { new() { MemberId = stages[0].Id, Name = stages[0].Id, AngleRad = inputAngleRad, SpeedRatio = 1.0 } };
        double angle = inputAngleRad, speed = 1.0;
        foreach (var (id, ratio, dir) in stages)
        {
            angle = dir * angle / ratio;
            speed = dir * speed / ratio;
            states.Add(new KinematicState { MemberId = id, Name = id, AngleRad = angle, SpeedRatio = speed });
        }
        return new KinematicsStep { States = states };
    }

    /// <summary>
    /// سیاره‌ای ساده (ویلیس): یک عضو ثابت، یک ورودی، یک خروجی.
    /// نسبتها: خروجش حامل با رینگ ثابت i = 1 + zr/zs؛ خروجی رینگ با حامل ثابت i = −zr/zs و …
    /// </summary>
    public static KinematicsStep PlanetaryStep(
        int sunTeeth, int planetTeeth, int ringTeeth, int planetCount,
        PlanetaryFixedMember fixedMember, double inputAngleRad)
    {
        if (sunTeeth + 2 * planetTeeth != ringTeeth)
            throw new ArgumentException("قید هم‌مرکزی رعایت نشده: zr = zs + 2·zp لازم است.");
        if (planetCount is < 2 or > 10)
            throw new ArgumentException("تعداد سیاره باید ۲ تا ۱۰ باشد.");

        double zr = ringTeeth, zs = sunTeeth;
        double carrierRatio, outputRatio, dirOut;
        string outputName;
        switch (fixedMember)
        {
            case PlanetaryFixedMember.Ring: // ورودی خورشید، خروجی حامل
                carrierRatio = 1.0 + zr / zs;
                outputRatio = carrierRatio;
                dirOut = 1;
                outputName = "حامل";
                break;
            case PlanetaryFixedMember.Sun: // ورودی رینگ، خروجی حامل
                carrierRatio = 1.0 + zs / zr;
                outputRatio = carrierRatio;
                dirOut = 1;
                outputName = "حامل";
                break;
            case PlanetaryFixedMember.Carrier: // ورودی خورشید، خروجی رینگ
                outputRatio = zr / zs;
                dirOut = -1;
                outputName = "رینگ";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixedMember));
        }

        var states = new List<KinematicState>
        {
            new() { MemberId = "sun", Name = "خورشید", AngleRad = fixedMember == PlanetaryFixedMember.Sun ? 0 : inputAngleRad, SpeedRatio = fixedMember == PlanetaryFixedMember.Sun ? 0 : 1 },
            new() { MemberId = "carrier", Name = "حامل", AngleRad = fixedMember == PlanetaryFixedMember.Carrier ? 0 : dirOut * inputAngleRad / outputRatio, SpeedRatio = fixedMember == PlanetaryFixedMember.Carrier ? 0 : dirOut / outputRatio },
            new() { MemberId = "ring", Name = "رینگ", AngleRad = fixedMember == PlanetaryFixedMember.Ring ? 0 : -inputAngleRad * zs / zr, SpeedRatio = fixedMember == PlanetaryFixedMember.Ring ? 0 : -zs / zr },
        };

        return new KinematicsStep
        {
            States = states,
            Diagnostics = new[]
            {
                new GearDiagnostic("SGM-K110", Severity.Info,
                    $"عضو ثابت: {fixedMember}؛ خروجی: {outputName} با نسبت {outputRatio:F3}؛ جهت {dirOut:+0;-0}؛ فازگذاری {planetCount} سیارهٔ هم‌فاصله.", "Planetary"),
            },
        };
    }

    /// <summary>درایو هارمونیک: زاویهٔ خروجی فِلکسی‌پلاین از زاویهٔ موج‌ساز.</summary>
    public static KinematicsStep HarmonicStep(int flexTeeth, double waveGeneratorAngleRad)
    {
        double ratio = (flexTeeth + 2) / 2.0;
        return new KinematicsStep
        {
            States = new[]
            {
                new KinematicState { MemberId = "wavegen", Name = "موج‌ساز", AngleRad = waveGeneratorAngleRad, SpeedRatio = 1 },
                new KinematicState { MemberId = "flex", Name = "فِلکسی‌پلاین", AngleRad = -waveGeneratorAngleRad / ratio, SpeedRatio = -1 / ratio },
            },
            Diagnostics = new[]
            {
                new GearDiagnostic("SGM-K120", Severity.Info,
                    $"درایو هارمونیک: i = z_c/2 = {ratio:F1} با جهت معکوس؛ اسپلاین دایره‌ای ثابت.", "Harmonic"),
            },
        };
    }
}
