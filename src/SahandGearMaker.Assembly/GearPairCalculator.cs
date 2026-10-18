using SahandGearMaker.Domain;

namespace SahandGearMaker.Assembly;

/// <summary>نتیجهٔ بررسی سازگاری یک جفت/مونتاژ (سند ۰۳، بخش ۶).</summary>
public sealed class MatingCheckResult
{
    public bool IsCompatible { get; init; }
    public double CenterDistanceMm { get; init; }
    /// <summary>نسبت انتقال هندسی |ω_out/ω_in|.</summary>
    public double Ratio { get; init; }
    /// <summary>جهت چرخش خروجی نسبت به ورودی: +1 هم‌جهت، −1 مخالف.</summary>
    public int RotationDirection { get; init; }
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; init; } = Array.Empty<GearDiagnostic>();
}

/// <summary>
/// محاسبات جفت‌شدن چرخ‌دنده‌های استوانه‌ای (سند ۰۳، بخش ۶).
/// روابط مرجع بدون اصلاح پروفیل: a = m·(z1+z2)/2 (خارجی) و a = m·(zr−zp)/2 (داخلی).
/// برای جفت‌های با اصلاح پروفیل، فاصلهٔ مؤثر با مجموع جابه‌جایی‌ها گزارش می‌شود.
/// </summary>
public static class GearPairCalculator
{
    public static MatingCheckResult CheckExternalPair(ToothParameters gearA, ToothParameters gearB)
    {
        ArgumentNullException.ThrowIfNull(gearA);
        ArgumentNullException.ThrowIfNull(gearB);
        var diags = new List<GearDiagnostic>();

        bool helical = Math.Abs(gearA.HelixAngle) > 1e-9 || Math.Abs(gearB.HelixAngle) > 1e-9;
        double mtA = GearValidator.TransverseModule(gearA.Module, gearA.HelixAngle);
        double mtB = GearValidator.TransverseModule(gearB.Module, gearB.HelixAngle);
        double alphaA = GearValidator.TransversePressureAngle(gearA.PressureAngle, gearA.HelixAngle);
        double alphaB = GearValidator.TransversePressureAngle(gearB.PressureAngle, gearB.HelixAngle);

        bool ok = true;
        if (Math.Abs(mtA - mtB) > 1e-9)
        {
            ok = false;
            diags.Add(new(DiagnosticCodes.ModuleNotPositive, Severity.Error,
                $"مدول عرضی دو چرخ‌دنده برابر نیست ({mtA:F4} در برابر {mtB:F4} میلی‌متر)؛ جفت‌شدن ممکن نیست.", "Module"));
        }
        if (Math.Abs(alphaA - alphaB) > 1e-7)
        {
            ok = false;
            diags.Add(new(DiagnosticCodes.PressureAngleOutOfRange, Severity.Error,
                "زاویهٔ فشار عرضی دو چرخ‌دنده برابر نیست؛ جفت‌شدن ممکن نیست.", "PressureAngle"));
        }
        if (helical)
        {
            // جفت خارجی موازی: جهت مارپیچ باید مخالف باشد
            if (Math.Sign(gearA.HelixAngle) == Math.Sign(gearB.HelixAngle) && Math.Abs(gearA.HelixAngle) > 1e-9 && Math.Abs(gearB.HelixAngle) > 1e-9)
            {
                ok = false;
                diags.Add(new(DiagnosticCodes.HelixAngleOutOfRange, Severity.Error,
                    "برای جفت خارجی با محورهای موازی، جهت مارپیچ دو چرخ‌دنده باید مخالف هم باشد (یکی چپ، یکی راست).", "HelixAngle"));
            }
        }

        double a = mtA * (gearA.ToothCount + gearB.ToothCount) / 2.0
                 + (gearA.ProfileShiftCoefficient + gearB.ProfileShiftCoefficient) * gearA.Module;
        double ratio = (double)gearA.ToothCount / gearB.ToothCount;

        diags.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
            "بررسی تماس/تداخل دقیق اجرا نشده؛ فاصلهٔ محور اسمی گزارش شده است."));

        return new MatingCheckResult
        {
            IsCompatible = ok,
            CenterDistanceMm = a,
            Ratio = ratio,
            RotationDirection = -1, // جفت خارجی: جهت مخالف
            Diagnostics = diags,
        };
    }

    public static MatingCheckResult CheckInternalPair(ToothParameters pinion, ToothParameters ring)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        ArgumentNullException.ThrowIfNull(ring);
        var diags = new List<GearDiagnostic>();

        double mtP = GearValidator.TransverseModule(pinion.Module, pinion.HelixAngle);
        double mtR = GearValidator.TransverseModule(ring.Module, ring.HelixAngle);
        bool ok = true;
        if (Math.Abs(mtP - mtR) > 1e-9)
        {
            ok = false;
            diags.Add(new(DiagnosticCodes.ModuleNotPositive, Severity.Error,
                "مدول پینیون و حلقهٔ داخلی باید برابر باشد.", "Module"));
        }
        if (ring.ToothCount <= pinion.ToothCount)
        {
            ok = false;
            diags.Add(new(DiagnosticCodes.InternalToothCountInvalid, Severity.Error,
                $"تعداد دندانهٔ حلقه ({ring.ToothCount}) باید بیش از پینیون ({pinion.ToothCount}) باشد.", "ToothCount"));
        }

        // قید تداخل شعاعی متداول: اختلاف دندانه‌ها نباید خیلی کم باشد (هشدار مهندسی)
        if (ok && ring.ToothCount - pinion.ToothCount < 8)
            diags.Add(new(DiagnosticCodes.InterferenceRiskInternal, Severity.Warning,
                $"اختلاف دندانهٔ حلقه و پینیون فقط {ring.ToothCount - pinion.ToothCount} است؛ ریسک تداخل هنگام مونتاژ شعاعی (پیشنهاد: ≥ ۸).", "ToothCount"));

        double a = mtP * (ring.ToothCount - pinion.ToothCount) / 2.0;
        double ratio = (double)pinion.ToothCount / ring.ToothCount;

        diags.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
            "بررسی تماس/تداخل دقیق اجرا نشده؛ فاصلهٔ محور اسمی گزارش شده است."));

        return new MatingCheckResult
        {
            IsCompatible = ok,
            CenterDistanceMm = a,
            Ratio = ratio,
            RotationDirection = +1, // جفت داخلی: جهت هم‌جهت
            Diagnostics = diags,
        };
    }

    /// <summary>جفت شانه و پینیون: سازگاری گام و زاویه (سند ۰۶، بخش ۲).</summary>
    public static MatingCheckResult CheckRackPinion(ToothParameters pinion, ToothParameters rack)
    {
        ArgumentNullException.ThrowIfNull(pinion);
        ArgumentNullException.ThrowIfNull(rack);
        var diags = new List<GearDiagnostic>();

        double mtP = GearValidator.TransverseModule(pinion.Module, pinion.HelixAngle);
        bool ok = Math.Abs(mtP - rack.Module) < 1e-9;
        if (!ok)
            diags.Add(new(DiagnosticCodes.ModuleNotPositive, Severity.Error,
                "گام شانه (π·m) باید با گام پینیون برابر باشد؛ ماژول‌ها را یکسان کنید.", "Module"));

        // حرکت انتقالی شانه به ازای یک دور پینیون = π·m·z
        double travelPerRevolution = Math.PI * rack.Module * pinion.ToothCount;

        diags.Add(new(DiagnosticCodes.ContactAnalysisNotRun, Severity.Unverified,
            "بررسی تماس شانه/پینیون دقیق اجرا نشده است."));

        return new MatingCheckResult
        {
            IsCompatible = ok,
            CenterDistanceMm = double.NaN, // شانه فاصلهٔ محور ندارد؛ با ارتفاع خط گام تنظیم می‌شود
            Ratio = travelPerRevolution,   // در جفت شانه: «نسبت» = مسیر انتقالی به ازای هر دور (mm/rev)
            RotationDirection = 0,
            Diagnostics = diags,
        };
    }
}
