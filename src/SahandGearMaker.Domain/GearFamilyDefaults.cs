namespace SahandGearMaker.Domain;

/// <summary>
/// نرمال‌ساز پارامترهای خانواده (نسخهٔ ۰٫۵ — رفع «کرم ساخته نمی‌شود» و پارامترهای ناسازگار خانواده‌ها).
///
/// وقتی کاربر خانوادهٔ یک قطعه را عوض می‌کند یا قطعهٔ جدید می‌سازد، پارامترهای قبلی
/// ممکن است با دامنهٔ معتبر خانوادهٔ جدید نخوانند (مثلاً «تعداد دندانه» پیش‌فرض ۲۰ برای کرم
/// یعنی ۲۰ رشته و خطای SGM-E240 می‌شود). این کلاس پارامترها را با قواعد شفاف و مستند
/// به دامنهٔ معتبر می‌آورد و فهرست تغییرات را برای نمایش به کاربر برمی‌گرداند.
/// </summary>
public static class GearFamilyDefaults
{
    /// <summary>
    /// نرمال‌سازی پارامترهای قطعه برای خانوادهٔ فعلی‌اش. مقادیر سالم دست نمی‌خورد؛
    /// فقط موارد خارج از دامنهٔ مهندسی به مقدار متداول خانواده برمی‌گردند.
    /// </summary>
    /// <returns>فهرست تغییرات اعمال‌شده (پیام فارسی) — خالی یعنی بدون تغییر.</returns>
    public static List<string> Normalize(GearPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var changes = new List<string>();
        var t = part.Teeth;
        var s = part.Specific;

        switch (part.Family)
        {
            case GearFamily.Worm:
                // «تعداد دندانه» در کرم = تعداد رشته‌ها (استارت) و باید ۱..۱۲ باشد
                if (t.ToothCount is < 1 or > 12)
                {
                    changes.Add($"تعداد رشته‌های کرم از {t.ToothCount} به ۲ اصلاح شد (دامنهٔ معتبر ۱ تا ۱۲).");
                    t.ToothCount = 2;
                }
                if (t.FaceWidth < 3.0 * Math.PI * t.Module)
                {
                    double len = 3.0 * Math.PI * t.Module;
                    changes.Add($"طول کرم به {len:F1} mm افزایش یافت (حداقل ۳ گام محوری برای تشکیل دندانه).");
                    t.FaceWidth = len;
                }
                // هم‌زمان‌سازی پارامتر جفت (برای ویزارد چرخ کرم)
                s.WormStartsCount = t.ToothCount;
                break;

            case GearFamily.WormWheel:
                if (t.ToothCount < 20)
                {
                    changes.Add($"تعداد دندانهٔ چرخ کرم از {t.ToothCount} به ۳۰ اصلاح شد (حداقل مهندسی ۲۰).");
                    t.ToothCount = 30;
                }
                if (s.WormStartsCount is < 1 or > 12)
                    s.WormStartsCount = 2;
                break;

            case GearFamily.HarmonicDrive:
                if (t.ToothCount < 60)
                {
                    changes.Add($"تعداد دندانهٔ فلکسی‌پلاین از {t.ToothCount} به ۱۰۰ اصلاح شد (حداقل ۶۰).");
                    t.ToothCount = 100;
                }
                break;

            case GearFamily.InternalSpur or GearFamily.InternalHelical:
                if (t.ToothCount < 18)
                {
                    changes.Add($"تعداد دندانهٔ چرخ داخلی از {t.ToothCount} به ۲۴ اصلاح شد (حداقل ۱۸).");
                    t.ToothCount = 24;
                }
                break;

            case GearFamily.SplineExternal:
                if (t.ToothCount < 6)
                {
                    changes.Add($"تعداد خار از {t.ToothCount} به ۱۰ اصلاح شد (دامنهٔ متداول ۶ تا ۲۵).");
                    t.ToothCount = 10;
                }
                break;

            case GearFamily.SplineInternal:
                if (t.ToothCount < 6)
                {
                    changes.Add($"تعداد شیار از {t.ToothCount} به ۱۰ اصلاح شد (دامنهٔ متداول ۶ تا ۲۵).");
                    t.ToothCount = 10;
                }
                break;

            case GearFamily.CrownFace or GearFamily.Cycloidal or GearFamily.NonCircular:
                if (t.ToothCount < 12)
                {
                    changes.Add($"تعداد دندانه از {t.ToothCount} به ۱۶ اصلاح شد (حداقل این خانواده ۱۲ است).");
                    t.ToothCount = 16;
                }
                break;

            case GearFamily.Rack or GearFamily.HelicalRack:
                if (s.RackLengthMm <= 0)
                    s.RackLengthMm = 120;
                break;

            case GearFamily.CrossedHelical:
                // زوج مارپیچ متقاطع بدون زاویهٔ مارپیچ معنا ندارد؛ مقدار متداول
                if (Math.Abs(t.HelixAngle) < 1e-9)
                {
                    t.HelixAngle = UnitConverter.DegToRad(30);
                    changes.Add("زاویهٔ مارپیچ به ۳۰° تنظیم شد (زوج متقاطع به مارپیچ غیرصفر نیاز دارد).");
                }
                break;
        }

        return changes;
    }

    /// <summary>پیش‌فرض‌های ساخت قطعهٔ جدید برای یک خانواده — برای دکمهٔ «قطعهٔ جدید».</summary>
    public static GearPart CreatePart(GearFamily family, string name)
    {
        var part = new GearPart { Name = name, Family = family };
        switch (family)
        {
            case GearFamily.Worm:
                part.Teeth.ToothCount = 2;          // ۲ رشته — متداول‌ترین کرم
                part.Teeth.FaceWidth = 40;
                part.Body.BoreDiameter = 8;
                part.Specific.WormPitchDiameterMm = 0; // q=10 خودکار
                part.Specific.WormStartsCount = 2;
                break;
            case GearFamily.WormWheel:
                part.Teeth.ToothCount = 40;
                part.Specific.WormStartsCount = 2;
                part.Specific.WormPitchDiameterMm = 0;
                break;
            case GearFamily.HarmonicDrive:
                part.Teeth.ToothCount = 100;
                part.Body.BoreDiameter = 0;
                break;
            case GearFamily.SplineExternal:
                part.Teeth.ToothCount = 10;
                break;
            case GearFamily.SplineInternal:
                part.Teeth.ToothCount = 10;
                break;
            case GearFamily.CrownFace or GearFamily.Cycloidal or GearFamily.NonCircular:
                part.Teeth.ToothCount = 16;
                break;
            case GearFamily.Rack or GearFamily.HelicalRack:
                part.Specific.RackLengthMm = 120;
                break;
        }
        return part;
    }
}
