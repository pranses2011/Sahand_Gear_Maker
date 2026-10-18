namespace SahandGearMaker.Domain;

/// <summary>
/// اعتبارسنجی پارامترهای دندانه (سند ۰۶، بخش ۲ — آزمون‌های مشترک عددی: صفر، منفی، NaN، Infinity، بسیار بزرگ).
/// این کلاس مستقل از WPF است و روی لینوکس آزمون می‌شود.
/// </summary>
public static class GearValidator
{
    /// <summary>بیشینهٔ مطلقاً قابل قبول برای ورودی عددی طول/ماژول (mm) برای جلوگیری از سرریز مش.</summary>
    public const double MaxReasonableLengthMm = 5_000.0;

    public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>
    /// اعتبارسنجی کامل یک قطعهٔ چرخ‌دنده. خطاها ساخت هندسه را متوقف می‌کنند؛
    /// هشدارها با توضیح ادامه می‌یابند (سیاست سند ۰۳، بخش ۸).
    /// </summary>
    public static IReadOnlyList<GearDiagnostic> Validate(GearFamily family, ToothParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var list = new List<GearDiagnostic>();

        // ورودی‌های غیرمحدود — خطای قطعی
        if (!IsFinite(p.Module) || !IsFinite(p.PressureAngle) || !IsFinite(p.HelixAngle) ||
            !IsFinite(p.FaceWidth) || !IsFinite(p.ProfileShiftCoefficient) || !IsFinite(p.BacklashAllowance))
        {
            list.Add(new(DiagnosticCodes.NonFiniteInput, Severity.Error,
                "یکی از مقادیر عددی ورودی نامعتبر است (NaN یا بی‌نهایت). مقدار را تصحیح کنید.", "Module"));
            return list;
        }

        // ماژول
        if (p.Module <= 0)
            list.Add(new(DiagnosticCodes.ModuleNotPositive, Severity.Error,
                "ماژول باید عددی مثبت باشد؛ مثلاً بین ۰٫۱ تا ۵۰ میلی‌متر.", "Module"));
        else if (p.Module > MaxReasonableLengthMm)
            list.Add(new(DiagnosticCodes.ModuleNotPositive, Severity.Error,
                $"ماژول از حد معقول ({MaxReasonableLengthMm} mm) بزرگ‌تر است.", "Module"));

        // زاویهٔ فشار — دامنهٔ مهندسی معقول
        if (p.PressureAngle <= 0 || p.PressureAngle >= UnitConverter.DegToRad(45))
            list.Add(new(DiagnosticCodes.PressureAngleOutOfRange, Severity.Error,
                "زاویهٔ فشار باید بین ۰ و ۴۵ درجه باشد (مقادیر متداول: ۱۴٫۵، ۲۰ یا ۲۵ درجه).", "PressureAngle"));

        // زاویهٔ مارپیچ
        if (Math.Abs(p.HelixAngle) >= UnitConverter.DegToRad(60))
            list.Add(new(DiagnosticCodes.HelixAngleOutOfRange, Severity.Error,
                "زاویهٔ مارپیچ باید کوچک‌تر از ۶۰ درجه باشد؛ مقادیر متداول ۱۵ تا ۳۰ درجه‌اند.", "HelixAngle"));

        // پهنای دندانه
        if (p.FaceWidth <= 0)
            list.Add(new(DiagnosticCodes.FaceWidthNotPositive, Severity.Error,
                "پهنای دندانه باید مثبت باشد.", "FaceWidth"));
        else if (p.FaceWidth > MaxReasonableLengthMm)
            list.Add(new(DiagnosticCodes.FaceWidthNotPositive, Severity.Error,
                $"پهنای دندانه از حد معقول ({MaxReasonableLengthMm} mm) بزرگ‌تر است.", "FaceWidth"));

        if (list.Any(d => d.Severity == Severity.Error))
            return list; // خطاهای پایه مانع ادامهٔ بررسی‌های وابسته می‌شوند

        // تعداد دندانه
        if (family is GearFamily.Rack or GearFamily.HelicalRack)
        {
            // شانه دندانهٔ گسسته ندارد؛ گام از ماژول می‌آید.
        }
        else
        {
            // حداقل دندانهٔ معتبر وابسته به خانواده است (کرم: تعداد رشته‌ها ۱ تا ۱۲)
            int minTeeth = family switch
            {
                GearFamily.Worm => 1,
                GearFamily.SplineExternal or GearFamily.SplineInternal => 6,
                GearFamily.CrownFace or GearFamily.Cycloidal or GearFamily.NonCircular => 12,
                GearFamily.HarmonicDrive => 60,
                GearFamily.WormWheel => 20,
                _ => 5,
            };
            int maxTeeth = family is GearFamily.HarmonicDrive ? 720 : 600;

            if (p.ToothCount < minTeeth)
                list.Add(new(DiagnosticCodes.ToothCountTooLow, Severity.Error,
                    $"تعداد دندانه برای این خانواده باید حداقل {minTeeth} باشد.", "ToothCount"));
            else if (p.ToothCount > maxTeeth)
                list.Add(new(DiagnosticCodes.MeshTooHeavy, Severity.Warning,
                    "تعداد دندانهٔ زیاد، مش بسیار سنگینی می‌سازد؛ پیش‌نمایش کند و فایل خروجی حجیم می‌شود.", "ToothCount"));

            if (family is GearFamily.InternalSpur or GearFamily.InternalHelical or GearFamily.SplineInternal
                && p.ToothCount < 18 && family != GearFamily.SplineInternal)
                list.Add(new(DiagnosticCodes.InternalToothCountInvalid, Severity.Error,
                    "برای چرخ‌دندهٔ داخلی، حداقل تعداد دندانهٔ معقول ۱۸ است (دندانهٔ کمتر ریسک تداخل جدی دارد).", "ToothCount"));
        }

        // خطر undercut برای خارجی بدون اصلاح — فرمول مرجع تقریبی zmin = 2/sin²(α)
        if (family is GearFamily.ExternalSpur or GearFamily.ExternalHelical or GearFamily.ExternalHerringbone)
        {
            double alphaT = TransversePressureAngle(p.PressureAngle, p.HelixAngle);
            double zMin = 2.0 / Math.Pow(Math.Sin(alphaT), 2.0);
            if (p.ProfileShiftCoefficient == 0 && p.ToothCount < zMin)
                list.Add(new(DiagnosticCodes.UndercutRisk, Severity.Warning,
                    $"با زاویهٔ فشار {UnitConverter.RadToDeg(alphaT):F1} درجه، تعداد دندانهٔ {p.ToothCount} کمتر از حد بدون برش‌خوردگی (~{Math.Ceiling(zMin)}) است؛ ریشهٔ دندانه در ساخت واقعی برش‌خوردگی (undercut) پیدا می‌کند.", "ToothCount"));

            if (Math.Abs(p.ProfileShiftCoefficient) > 0)
                list.Add(new(DiagnosticCodes.ProfileShifted, Severity.Warning,
                    $"اصلاح پروفیل x={p.ProfileShiftCoefficient:F3} اعمال می‌شود؛ در محاسبهٔ فاصلهٔ محور جفت‌ها اثر آن لحاظ شده است.", "ProfileShiftCoefficient"));
        }

        // مارپیچ — تعداد برش به‌صورت تطبیقی از تلرانس وتر محاسبه می‌شود
        if (Math.Abs(p.HelixAngle) > 1e-9 && family is GearFamily.ExternalHelical or GearFamily.InternalHelical or GearFamily.HelicalRack)
            list.Add(new(DiagnosticCodes.HelicalApproximation, Severity.Info,
                "بدنهٔ مارپیچ با برش‌های موازی صفحهٔ عرضی ساخته می‌شود؛ تعداد برش به‌صورت تطبیقی از تلرانس وتر تنظیم می‌شود (دقت قابل کنترل با «تلرانس پروفیل»).", "HelixAngle"));

        // موارد بررسی‌نشده — صادقانه (سند ۰۳، بخش ۸)
        list.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
            "تحلیل تماس/تنش اجرا نشده است؛ این وضعیت با «سالم/تأییدشده» برابر نیست."));
        list.Add(new(DiagnosticCodes.StrengthNotAnalyzed, Severity.Unverified,
            "محاسبات استحکام، عمر و راندمان در دامنهٔ این نسخه نیست."));

        return list;
    }

    /// <summary>زاویهٔ فشار عرضی از نرمال: tan(αt) = tan(αn)/cos(β) (سند ۰۳، بخش ۴).</summary>
    public static double TransversePressureAngle(double alphaNormal, double helixAngle)
        => Math.Atan(Math.Tan(alphaNormal) / Math.Cos(helixAngle));

    /// <summary>مدول عرضی از نرمال: mt = mn / cos(β).</summary>
    public static double TransverseModule(double moduleNormal, double helixAngle)
        => moduleNormal / Math.Cos(helixAngle);
}
