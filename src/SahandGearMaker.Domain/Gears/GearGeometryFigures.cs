namespace SahandGearMaker.Domain;

/// <summary>
/// ارقام مشتق‌شدهٔ هندسی چرخ‌دندهٔ استوانه‌ای (خارجی/داخلی).
/// همهٔ مقادیر از ورودی‌ها قابل بازتولیدند و هنگام ذخیره‌سازی به‌عنوان «مشتق» علامت می‌خورند (سند ۰۳، بخش ۷).
/// قرارداد فرمول‌ها: مدل مرجع full-depth بدون اصلاح پروفیل با ضرایب متداول متریک.
/// </summary>
public sealed class GearGeometryFigures
{
    /// <summary>خانوادهٔ مبنا (برای جهت دندانه).</summary>
    public GearFamily Family { get; init; }

    /// <summary>مدول استفاده‌شده در صفحهٔ عرضی (برای مارپیچ: mt = mn/cos β).</summary>
    public double TransverseModule { get; init; }

    /// <summary>زاویهٔ فشار استفاده‌شده در صفحهٔ عرضی (رادیان).</summary>
    public double TransversePressureAngle { get; init; }

    /// <summary>شعاع دایرهٔ گام: r = mt·z/2 (برای شانه: خط گام).</summary>
    public double PitchRadius { get; init; }

    /// <summary>شعاع دایرهٔ پایهٔ اینولوت: rb = r·cos(αt).</summary>
    public double BaseRadius { get; init; }

    /// <summary>شعاع دایرهٔ سر (بیرونی‌ترین/داخلی‌ترین دندانه).</summary>
    public double TipRadius { get; init; }

    /// <summary>شعاع دایرهٔ ریشه.</summary>
    public double RootRadius { get; init; }

    /// <summary>زاویهٔ گام دندانه روی دایرهٔ گام (رادیان): 2π/z.</summary>
    public double AngularPitch { get; init; }

    /// <summary>ضخامت دندانه روی دایرهٔ گام (mm) پس از اعمال لقی و اصلاح پروفیل.</summary>
    public double ToothThicknessAtPitch { get; init; }

    /// <summary>زاویهٔ چرخش پروفیل دندانه‌های بعدی.</summary>
    public double HelixLeadAngle => Math.Abs(Math.Tan(HelixAngleUsed));

    /// <summary>زاویهٔ مارپیچ مؤثر (صفر برای مستقیم).</summary>
    public double HelixAngleUsed { get; init; }

    /// <summary>گردی (شعاع) فیلهٔ ریشهٔ به‌کاررفته — سقف‌گذاری‌شده با فضای موجود.</summary>
    public double RootFilletRadius { get; init; }

    /// <summary>حداقل تعداد دندانهٔ بدون برش‌خوردگی در همین زاویهٔ فشار (تقریبی مرجع).</summary>
    public double MinTeethNoUndercut => 2.0 / Math.Pow(Math.Sin(TransversePressureAngle), 2.0);

    /// <summary>برآورد حجم ماده (mm³) — مقدماتی، برای گزارش خروجی.</summary>
    public double EstimatedVolumeMm3 { get; init; }

    /// <summary>نسبت دندانهٔ چرخ‌دنده (z).</summary>
    public int ToothCount { get; init; }
}

/// <summary>
/// محاسبهٔ ارقام هندسی مطابق قراردادهای مستندشده در سند ۰۳، بخش ۴.
/// این کلاس مستقل از UI است و آزمون واحد مستقل دارد.
/// </summary>
public static class GearFiguresCalculator
{
    /// <summary>
    /// محاسبهٔ ارقام برای چرخ‌دندهٔ خارجی (ساده/مارپیچ/جناغی) یا داخلی.
    /// فرمول‌ها: r=mt·z/2، rb=r·cos(αt)، دایرهٔ سر/ریشه با ضرایب addendum/dedendum؛
    /// برای داخلی، addendum پیش‌فرض 0.8 و جهت دندانه معکوس است.
    /// </summary>
    public static GearGeometryFigures Compute(GearFamily family, ToothParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);

        bool internalGear = family is GearFamily.InternalSpur or GearFamily.InternalHelical;
        double helix = family switch
        {
            GearFamily.ExternalHerringbone => Math.Abs(p.HelixAngle), // نیمهٔ اول؛ نیمهٔ دوم قرینه است
            _ => p.HelixAngle,
        };

        double mt = GearValidator.TransverseModule(p.Module, helix);
        double alphaT = GearValidator.TransversePressureAngle(p.PressureAngle, helix);
        double r = mt * p.ToothCount / 2.0;
        double rb = r * Math.Cos(alphaT);

        double ha = p.AddendumCoefficient * p.Module + p.ProfileShiftCoefficient * p.Module;
        double hf = (p.DedendumCoefficient - p.ProfileShiftCoefficient) * p.Module;

        // اصلاح متداول برای داخلی: addendum کوتاه 0.8m اگر کاربر ضریب پیش‌فرض 1.0 گذاشته
        if (internalGear && Math.Abs(p.AddendumCoefficient - 1.0) < 1e-12)
            ha = 0.8 * p.Module + p.ProfileShiftCoefficient * p.Module;

        double ra = internalGear ? r - ha : r + ha;   // داخلی: سر دندانه به سمت مرکز
        double rf = internalGear ? r + hf : r - hf;   // داخلی: ریشه به سمت بیرون

        double pitchThickness = Math.PI * mt / 2.0
                              + 2.0 * p.ProfileShiftCoefficient * p.Module * Math.Tan(alphaT)
                              - p.BacklashAllowance;

        // فیلهٔ ریشه: حداقلِ ضریب درخواستی و نیم فضای رادیال موجود (محافظه‌کارانه، بدون خودتقاطع)
        double filletSpace = Math.Abs(r - Math.Max(rb, rf));
        double fillet = Math.Max(0, Math.Min(p.RootFilletCoefficient * p.Module, filletSpace / 2.0));

        // برآورد حجم: حلقهٔ دندانه + سهم دندانه‌ها (تقریب گزارشی؛ بور/حفره‌ها در مش دقیق لحاظ می‌شوند)
        double ringOuter = internalGear ? rf : ra;
        double ringInner = internalGear ? ra : rf;
        double ringArea = Math.PI * (ringOuter * ringOuter - ringInner * ringInner);
        double toothVolumeFactor = internalGear ? 0.35 : 0.45; // کسر حجمی دندانه‌ها (تقریب گزارشی)

        return new GearGeometryFigures
        {
            Family = family,
            TransverseModule = mt,
            TransversePressureAngle = alphaT,
            PitchRadius = r,
            BaseRadius = rb,
            TipRadius = ra,
            RootRadius = rf,
            AngularPitch = 2.0 * Math.PI / Math.Max(1, p.ToothCount),
            ToothThicknessAtPitch = pitchThickness,
            HelixAngleUsed = helix,
            RootFilletRadius = fillet,
            EstimatedVolumeMm3 = Math.Max(0, ringArea * p.FaceWidth * (1.0 + toothVolumeFactor)),
            ToothCount = p.ToothCount,
        };
    }
}
