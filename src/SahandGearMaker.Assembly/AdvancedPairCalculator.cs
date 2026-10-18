using SahandGearMaker.Domain;

namespace SahandGearMaker.Assembly;

/// <summary>
/// محاسبات جفت‌شدن خانواده‌های پیشرفته (مخروطی، کرم، متقاطع، تاجی، غیرمدور، هارمونیک، هزارخار، هیپوئید).
/// همهٔ روابط با فرمول‌های مرجع مهندسی و برچسب صادقانهٔ تقریب اعمال می‌شوند.
/// </summary>
public static class AdvancedPairCalculator
{
    // ---------- مخروطی ----------

    /// <summary>
    /// جفت مخروطی با زاویهٔ محور Σ (پیش‌فرض ۹۰°): زاویه‌های گام δ۱ = atan(sin Σ/(u + cos Σ))،
    /// δ₂ = Σ − δ₁؛ فاصلهٔ رأس مشترک و نسبت u = z₂/z₁.
    /// </summary>
    public static MatingCheckResult CheckBevelPair(ToothParameters pinion, ToothParameters gear,
        double shaftAngleRad = Math.PI / 2.0)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        ArgumentNullException.ThrowIfNull(gear);
        var diags = new List<GearDiagnostic>();
        double sigma = shaftAngleRad > 1e-6 ? shaftAngleRad : Math.PI / 2.0;

        if (Math.Abs(pinion.Module - gear.Module) > 1e-9)
            diags.Add(new("SGM-A200", Severity.Error, "ماژول دو چرخ مخروطی باید برابر باشد.", "Module"));
        if (Math.Abs(pinion.PressureAngle - gear.PressureAngle) > 1e-9)
            diags.Add(new("SGM-A201", Severity.Warning, "زاویهٔ فشار دو چرخ متفاوت است؛ برای جفت واقعی باید برابر باشند.", "PressureAngle"));

        double u = (double)gear.ToothCount / pinion.ToothCount;
        double delta1 = Math.Atan(Math.Sin(sigma) / (u + Math.Cos(sigma)));
        double delta2 = sigma - delta1;
        double ratio = u;
        int dir = -1; // جفت مخروطی خارجی: جهت چرخش معکوس

        bool ok = diags.All(d => d.Severity != Severity.Error);
        diags.Add(new("SGM-A202", Severity.Info,
            $"زوایای گام: δ₁={UnitConverter.RadToDeg(delta1):F2}°، δ₂={UnitConverter.RadToDeg(delta2):F2}°؛ فاصلهٔ رأس مشترک (cone distance) R={pinion.Module * pinion.ToothCount / 2.0 / Math.Sin(delta1):F2} mm.", "BevelPair"));

        return new MatingCheckResult
        {
            IsCompatible = ok,
            CenterDistanceMm = 0, // مخروطی: محورها متقاطع‌اند؛ «فاصلهٔ مرکز» معنا ندارد
            Ratio = ratio,
            RotationDirection = dir,
            Diagnostics = diags,
        };
    }

    // ---------- کرم و چرخ کرم ----------

    /// <summary>
    /// جفت کرم/چرخ کرم با محورهای متقاطع ۹۰ درجه:
    /// γ = atan(z₁·m/d₁)؛ a = (d₁ + z₂·m)/2؛ نسبت i = z₂/z₁؛ جهت: کرم راست‌گرد → چرخ همان جهت ظاهری.
    /// </summary>
    public static MatingCheckResult CheckWormPair(ToothParameters worm, ToothParameters wheel,
        double wormPitchDiameterMm, bool wormRightHanded = true)
    {
        ArgumentNullException.ThrowIfNull(worm);
        ArgumentNullException.ThrowIfNull(wheel);
        var diags = new List<GearDiagnostic>();

        if (Math.Abs(worm.Module - wheel.Module) > 1e-9)
            diags.Add(new("SGM-A210", Severity.Error, "ماژول نرمال چرخ باید با ماژول محوری کرم برابر باشد.", "Module"));

        double d1 = wormPitchDiameterMm > 1e-9 ? wormPitchDiameterMm : 10.0 * worm.Module;
        double gamma = Math.Atan(worm.ToothCount * worm.Module / d1);
        double a = (d1 + wheel.ToothCount * worm.Module) / 2.0;
        double ratio = (double)wheel.ToothCount / worm.ToothCount;

        if (worm.ToothCount is < 1 or > 12)
            diags.Add(new("SGM-A211", Severity.Error, "تعداد رشته‌های کرم باید ۱ تا ۱۲ باشد.", "ToothCount"));
        if (wheel.ToothCount < 20)
            diags.Add(new("SGM-A212", Severity.Error, "چرخ کرم حداقل ۲۰ دندانه لازم دارد.", "ToothCount"));

        bool ok = diags.All(d => d.Severity != Severity.Error);
        diags.Add(new("SGM-A213", Severity.Info,
            $"زاویهٔ پیشروی γ={UnitConverter.RadToDeg(gamma):F2}°؛ فاصلهٔ محور a={a:F2} mm؛ نسبت i={ratio:F2}؛ خودقفل‌شدگی تقریبی برای γ < برابری اصطکاک (≈۶°) محتمل است (به ضریب اصطکاک واقعی وابسته است — تضمین نمی‌شود).", "WormPair"));

        return new MatingCheckResult
        {
            IsCompatible = ok,
            CenterDistanceMm = a,
            Ratio = ratio,
            RotationDirection = wormRightHanded ? -1 : 1,
            Diagnostics = diags,
        };
    }

    // ---------- مارپیچ متقاطع ----------

    /// <summary>جفت مارپیچ متقاطع: Σ = β₁ + β₂ (هم‌دست)؛ a = (d₁ + d₂)/2 با dᵢ = mₙ·zᵢ/cos βᵢ.</summary>
    public static MatingCheckResult CheckCrossedHelicalPair(ToothParameters gearA, ToothParameters gearB)
    {
        ArgumentNullException.ThrowIfNull(gearA);
        ArgumentNullException.ThrowIfNull(gearB);
        var diags = new List<GearDiagnostic>();

        if (Math.Abs(gearA.Module - gearB.Module) > 1e-9)
            diags.Add(new("SGM-A220", Severity.Error, "ماژول نرمال دو چرخ متقاطع باید برابر باشد.", "Module"));

        double d1 = gearA.Module * gearA.ToothCount / Math.Cos(gearA.HelixAngle);
        double d2 = gearB.Module * gearB.ToothCount / Math.Cos(gearB.HelixAngle);
        double shaft = Math.Abs(gearA.HelixAngle) + Math.Abs(gearB.HelixAngle); // هم‌دست
        double a = (d1 + d2) / 2.0;

        diags.Add(new("SGM-A221", Severity.Info,
            $"زاویهٔ محور Σ={UnitConverter.RadToDeg(shaft):F2}° (فرض هم‌دستی)؛ a={a:F2} mm؛ نقطه‌تماس نقطه‌ای است — ظرفیت بار جفت متقاطع پایین است.", "CrossedHelical"));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = a,
            Ratio = (double)gearB.ToothCount / gearA.ToothCount,
            RotationDirection = -1,
            Diagnostics = diags,
        };
    }

    // ---------- تاجی ----------

    /// <summary>جفت تاجی/پینیون: محور عمود؛ شعاع گام چرخ تاجی = r_p پینیون در صفحهٔ چرخ.</summary>
    public static MatingCheckResult CheckCrownPair(ToothParameters pinion, ToothParameters crownGear)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        ArgumentNullException.ThrowIfNull(crownGear);
        var diags = new List<GearDiagnostic>();

        if (Math.Abs(pinion.Module - crownGear.Module) > 1e-9)
            diags.Add(new("SGM-A230", Severity.Error, "ماژول پینیون و چرخ تاجی باید برابر باشد.", "Module"));

        double rp = crownGear.Module * crownGear.ToothCount / 2.0;
        diags.Add(new("SGM-A231", Severity.Info,
            $"شعاع گام چرخ تاجی {rp:F2} mm؛ محور پینیون عمود بر صفحهٔ چرخ و مماس دایرهٔ گام پینیون بر دایرهٔ گام چرخ؛ نسبت i={crownGear.ToothCount / (double)pinion.ToothCount:F2}.", "CrownPair"));
        diags.Add(new("SGM-A232", Severity.Unverified,
            "هم‌تداومی دندانهٔ تاجی با پینیون (پوشش کامل) در این نسخه حل عددی نشده — هندسه طرح‌ریزی است."));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = pinion.Module * pinion.ToothCount / 2.0, // فاصلهٔ محور پینیون از محور چرخ
            Ratio = crownGear.ToothCount / (double)pinion.ToothCount,
            RotationDirection = -1,
            Diagnostics = diags,
        };
    }

    // ---------- غیرمدور بیضوی ----------

    /// <summary>
    /// جفت بیضوی یکسان حول کانون‌ها: فاصلهٔ محور L = 2a؛ نسبت لحظه‌ای i(θ₁) = (1+e²+2e·cos θ₁)/(1−e²).
    /// میانگین نسبت = ۱ (دور کامل).
    /// </summary>
    public static MatingCheckResult CheckNonCircularPair(ToothParameters gearA, ToothParameters gearB,
        double eccentricity = 0.5)
    {
        ArgumentNullException.ThrowIfNull(gearA);
        ArgumentNullException.ThrowIfNull(gearB);
        var diags = new List<GearDiagnostic>();

        if (gearA.ToothCount != gearB.ToothCount)
            diags.Add(new("SGM-A240", Severity.Error,
                "دو بیضوی یکسان باید تعداد دندانهٔ برابر داشته باشند (خانوادهٔ قطعه‌های یکسان).", "ToothCount"));
        if (Math.Abs(gearA.Module - gearB.Module) > 1e-9)
            diags.Add(new("SGM-A241", Severity.Error, "ماژول دو بیضوی باید برابر باشد.", "Module"));

        double e = Math.Clamp(eccentricity, 0.05, 0.9);
        // a از شرط محیط (با E(e) تقریبی)
        double E = ApproxE(e);
        double a = gearA.ToothCount * Math.PI * gearA.Module / (4.0 * E);
        double iMax = (1 + e * e + 2 * e) / (1 - e * e);
        double iMin = (1 + e * e - 2 * e) / (1 - e * e);

        diags.Add(new("SGM-A242", Severity.Info,
            $"L=2a={2 * a:F2} mm؛ نسبت لحظه‌ای در بازه [{iMin:F2}, {iMax:F2}] با میانگین ۱ در هر دور.", "NonCircular"));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = 2 * a,
            Ratio = 1.0,
            RotationDirection = -1,
            Diagnostics = diags,
        };
    }

    // ---------- هارمونیک ----------

    /// <summary>
    /// مجموعهٔ درایو هارمونیک: موج‌ساز بیضوی (۲ برآمدگی) + فِلکسی‌پلاین (z_f) + اسپلاین دایره‌ای (z_c = z_f + 2).
    /// نسبت (موج‌ساز ورودی، اسپلاین ثابت، خروجی فِلکسی‌پلاین): i = −z_c/2 (علامت: جهت معکوس).
    /// </summary>
    public static MatingCheckResult CheckHarmonicAssembly(ToothParameters flexSpline, ToothParameters circularSpline)
    {
        ArgumentNullException.ThrowIfNull(flexSpline);
        ArgumentNullException.ThrowIfNull(circularSpline);
        var diags = new List<GearDiagnostic>();

        if (Math.Abs(flexSpline.Module - circularSpline.Module) > 1e-9)
            diags.Add(new("SGM-A250", Severity.Error, "ماژول اجزای درایو هارمونیک باید برابر باشد.", "Module"));
        if (circularSpline.ToothCount != flexSpline.ToothCount + 2)
            diags.Add(new("SGM-A251", Severity.Error,
                $"اسپلاین دایره‌ای باید دقیقاً ۲ دندانه بیشتر از فِلکسی‌پلاین باشد (فعلی: {circularSpline.ToothCount} در برابر {flexSpline.ToothCount}).", "ToothCount"));
        if (flexSpline.ToothCount % 2 != 0)
            diags.Add(new("SGM-A252", Severity.Warning, "تعداد دندانهٔ فِلکسی‌پلاین معمولاً زوج است (تقارن دو برآمدگی موج‌ساز).", "ToothCount"));

        double ratio = circularSpline.ToothCount / 2.0;
        diags.Add(new("SGM-A253", Severity.Info,
            $"نسبت (موج‌ساز ورودی / فلکسی خروجی، اسپلاین ثابت): i = z_c/2 = {ratio:F1} با جهت معکوس؛ برای z_f={flexSpline.ToothCount}، z_c={circularSpline.ToothCount}.", "Harmonic"));
        diags.Add(new("SGM-A254", Severity.Unverified,
            "تحلیل تنش فِلکسی‌پلاین و عمر چرخه‌ای در دامنهٔ این نسخه نیست."));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = 0,
            Ratio = ratio,
            RotationDirection = -1,
            Diagnostics = diags,
        };
    }

    // ---------- هزارخار ----------

    /// <summary>اتصال هزارخار: شفت خارجی + توپی داخلی با تعداد خار برابر و لقی جانبی سازگار.</summary>
    public static MatingCheckResult CheckSplineConnection(ToothParameters shaft, ToothParameters hub,
        double shaftBacklashMm = 0, double hubBacklashMm = 0)
    {
        ArgumentNullException.ThrowIfNull(shaft);
        ArgumentNullException.ThrowIfNull(hub);
        var diags = new List<GearDiagnostic>();

        if (shaft.ToothCount != hub.ToothCount)
            diags.Add(new("SGM-A260", Severity.Error, "تعداد خار شفت و توپی باید برابر باشد.", "ToothCount"));
        if (Math.Abs(shaft.Module - hub.Module) > 1e-9)
            diags.Add(new("SGM-A261", Severity.Error, "ماژول خار شفت و توپی باید برابر باشد.", "Module"));

        double totalBacklash = Math.Max(0, shaftBacklashMm) + Math.Max(0, hubBacklashMm);
        diags.Add(new("SGM-A262", Severity.Info,
            $"اتصال {shaft.ToothCount}خاره؛ لقی جانگی مجموع ≈ {totalBacklash:F3} mm؛ ظرفیت انتقال گشتاور به تلرانس/طول تماس واقعی وابسته است.", "SplineConnection"));
        diags.Add(new("SGM-A263", Severity.Unverified,
            "تلرانس‌های جداول استاندارد (DIN/ISO) در این نسخه اعمال نمی‌شود؛ ابعاد نمایشی «تعیین‌نشده»."));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = 0, // هم‌محور
            Ratio = 1.0,
            RotationDirection = 1,
            Diagnostics = diags,
        };
    }

    // ---------- هیپوئید ----------

    /// <summary>
    /// جفت هیپوئید: مخروطی مارپیچ با آفست محور E؛ نسبت زاویه‌های مارپیچ از رابطهٔ مرجع
    /// sin β₁/sin β₂ تقریبی و گزارش صادقانه.
    /// </summary>
    public static MatingCheckResult CheckHypoidPair(ToothParameters pinion, ToothParameters gear,
        double offsetMm, double pinionSpiralRad, double gearSpiralRad)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        ArgumentNullException.ThrowIfNull(gear);
        var diags = new List<GearDiagnostic>();

        if (offsetMm <= 1e-9)
            diags.Add(new("SGM-A270", Severity.Warning, "آفست صفر یعنی جفت مخروطی مارپیچ معمولی است.", "HypoidOffsetMm"));
        if (Math.Abs(pinion.Module - gear.Module) > 1e-9)
            diags.Add(new("SGM-A271", Severity.Error, "ماژول مرجع دو چرخ باید برابر باشد.", "Module"));

        double u = gear.ToothCount / (double)pinion.ToothCount;
        diags.Add(new("SGM-A272", Severity.Info,
            $"آفست E={offsetMm:F2} mm؛ β₁={UnitConverter.RadToDeg(pinionSpiralRad):F1}°، β₂={UnitConverter.RadToDeg(gearSpiralRad):F1}°؛ نسبت u={u:F2}.", "Hypoid"));
        diags.Add(new("SGM-A273", Severity.Unverified,
            "پوشش تماس دقیق هیپوئید (مسیر تماس و نسبت تمک موضعی) نیازمند حل‌گر تخصصی است؛ اینجا روابط طرح‌ریزی گزارش می‌شود."));

        return new MatingCheckResult
        {
            IsCompatible = diags.All(d => d.Severity != Severity.Error),
            CenterDistanceMm = offsetMm,
            Ratio = u,
            RotationDirection = -1,
            Diagnostics = diags,
        };
    }

    /// <summary>انتگرال بیضوی کامل مرتبهٔ دوم E(e) با سیمپسون.</summary>
    private static double ApproxE(double e)
    {
        const int N = 512;
        double k = e * e;
        double h = Math.PI / 2.0 / N, s = 0;
        for (int i = 0; i <= N; i++)
        {
            double th = h * i;
            double f = Math.Sqrt(Math.Max(0, 1 - k * Math.Sin(th) * Math.Sin(th)));
            s += (i == 0 || i == N ? 1 : (i % 2 == 1 ? 4 : 2)) * f;
        }
        return s * h / 3.0;
    }
}
