namespace SahandGearMaker.Domain;

/// <summary>
/// پارامترهای اختصاصی خانواده‌های پیشرفته (سند ۰۳، بخش ۳٫۲).
/// همهٔ فیلدها اختیاری‌اند؛ مقدار صفر/پیش‌فرض یعنی «قانون پیش‌فرض مستند همان خانواده».
/// این ساختار در فایل پروژه ذخیره می‌شود و مهاجرت schema مقادیر ناشناخته را حفظ می‌کند.
/// </summary>
public sealed class FamilyParameters
{
    // ---------- شانه ----------

    /// <summary>طول شانهٔ مستقیم/مارپیچ (mm). صفر = پیش‌فرض ۱۰ گام.</summary>
    public double RackLengthMm { get; set; }

    // ---------- حلزونی ----------

    /// <summary>
    /// قطر دایرهٔ گام کرم (mm). اگر صفر باشد از قاعدهٔ d1 = m·(z+17)/... محاسبه می‌شود؛
    /// حداقل مقدار معتبر در اعتبارسنجی کنترل می‌شود.
    /// </summary>
    public double WormPitchDiameterMm { get; set; }

    /// <summary>تعدادِ رشته‌های (starts) کرمِ جفت‌شونده — فقط برای ساخت «چرخ کرم» به‌کار می‌رود.</summary>
    public int WormStartsCount { get; set; } = 1;

    /// <summary>دستِ کرم: راست‌گرد (پیش‌فرض) یا چپ‌گرد؛ جهت پیچش چرخ کرم از آن مشتق می‌شود.</summary>
    public bool WormIsRightHanded { get; set; } = true;

    /// <summary>پاککردن گلوگاه چرخ کرم (mm) — فاصلهٔ آزاد نوک کرم تا ته چرخ. پیش‌فرض ۰٫۲۵m.</summary>
    public double WormThroatClearanceMm { get; set; }

    // ---------- مخروطی ----------

    /// <summary>زاویهٔ بین دو محور جفت مخروطی (رادیان)؛ پیش‌فرض ۹۰ درجه.</summary>
    public double BevelShaftAngle { get; set; }

    /// <summary>
    /// زاویهٔ نیم‌مخروط گام این قطعه (رادیان). صفر = خودکار (نصف زاویهٔ محورها).
    /// برای جفت ۹۰ درجه با نسبت u: δ۱ = atan(1/u) و δ۲ = 90° − δ۱.</summary>
    public double BevelPitchConeAngle { get; set; }

    /// <summary>زاویهٔ مارپیچ/قوس دندانهٔ مخروطی مارپیچ (رادیان) در شعاع میانگین؛ صفر = مستقیم.</summary>
    public double BevelSpiralAngle { get; set; }

    // ---------- هیپوئید ----------

    /// <summary>آفست دو محور هیپوئید (mm)؛ صفر یعنی جفت مخروطی معمولی.</summary>
    public double HypoidOffsetMm { get; set; }

    // ---------- غیرمدور (بیضوی) ----------

    /// <summary>خروج از مرکز بیضی (e = c/a)؛ دامنهٔ معتبر (۰، ۰٫۹).</summary>
    public double NonCircularEccentricity { get; set; } = 0.5;

    // ---------- هارمونیک ----------

    /// <summary>
    /// دامنهٔ موج تغییرشکل هارمونیک (mm) — اختلاف محور بزرگ/کوچک تقسیم بر ۲.
    /// پیش‌فرض: تقریب ۲ برابر ماژول (نسبت مرسوم تغییرشکل موج‌ساز).
    /// </summary>
    public double HarmonicWaveDeflectionMm { get; set; }

    /// <summary>ضخامت دیوارهٔ فنجان فِلکسی‌پلاین (mm). صفر = پیش‌فرض ۱٫۵٪ قطر گام.</summary>
    public double HarmonicCupWallThicknessMm { get; set; }

    // ---------- هزارخار ----------

    /// <summary>نوع برازش اتصال هزارخار: جفت‌شدن در سطح گام (پیش‌فرض) یا قطر پایه.</summary>
    public SplineFitType SplineFit { get; set; } = SplineFitType.PitchDiameter;

    /// <summary>لقی جانبی اتصال هزارخار (mm) — مثبت یعنی فاصلهٔ آزاد بین پهلوها.</summary>
    public double SplineBacklashMm { get; set; }

    /// <summary>پایهٔ استاندارد هزارخار برای نمایش (داخلی/سفارشی).</summary>
    public string? SplineStandardNote { get; set; }
}

/// <summary>نوع برازش اتصال هزارخار (DIN 5480 متداول: برازش در دایرهٔ گام/پایه).</summary>
public enum SplineFitType
{
    /// <summary>برازش در دایرهٔ گام (side fit) — متداول‌ترین حالت.</summary>
    PitchDiameter,

    /// <summary>برازش در قطر پایه (major/minor fit).</summary>
    MajorDiameter,
}
