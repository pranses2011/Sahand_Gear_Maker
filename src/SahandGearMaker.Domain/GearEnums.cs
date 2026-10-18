namespace SahandGearMaker.Domain;

/// <summary>خانواده‌های هندسی چرخ‌دنده (الزام سند ۰۳، بخش ۲ — فهرست بسته و صادقانه).</summary>
public enum GearFamily
{
    /// <summary>چرخ‌دندهٔ استوانه‌ای خارجی با دندانهٔ مستقیم.</summary>
    ExternalSpur,

    /// <summary>چرخ‌دندهٔ استوانه‌ای خارجی مارپیچ (helical).</summary>
    ExternalHelical,

    /// <summary>چرخ‌دندهٔ خارجی دومارپیچ/جناغی (herringbone).</summary>
    ExternalHerringbone,

    /// <summary>چرخ‌دندهٔ داخلی (internal) — دندانه به سمت داخل.</summary>
    InternalSpur,

    /// <summary>چرخ‌دندهٔ داخلی مارپیچ.</summary>
    InternalHelical,

    /// <summary>شانهٔ مستقیم (rack).</summary>
    Rack,

    /// <summary>پینیون شانهٔ مارپیچ.</summary>
    HelicalRack,

    // انواع زیر در کاتالوگ «نقشهٔ راه» هستند و نباید مدل جعلی بسازند:
    BevelSpur,
    BevelSpiral,
    Worm,
    WormWheel,
    CrossedHelical,
    CrownFace,
    Hypoid,
    Cycloidal,
    NonCircular,
    HarmonicDrive,
    SplineExternal,
    SplineInternal,
}

/// <summary>نوع جفت‌شدن/مونتاژ (سند ۰۳، بخش ۶).</summary>
public enum MatingKind
{
    /// <summary>جفت خارجی موازی (هر دو دندانهٔ خارجی).</summary>
    ExternalPair,

    /// <summary>جفت داخلی (پینیون داخل حلقهٔ دندانهٔ داخلی).</summary>
    InternalPair,

    /// <summary>شانه و پینیون.</summary>
    RackPinion,

    /// <summary>قطار مرکب چندمرحله‌ای.</summary>
    CompoundTrain,

    /// <summary>مجموعهٔ سیاره‌ای ساده.</summary>
    SimplePlanetary,

    /// <summary>جفت کرم و چرخ کرم (محورهای متقاطع ۹۰ درجه).</summary>
    WormPair,

    /// <summary>جفت مارپیچ متقاطع (محورهای زاویه‌دار).</summary>
    CrossedHelicalPair,

    /// <summary>جفت مخروطی (محورهای متقاطع).</summary>
    BevelPair,

    /// <summary>جفت تاجی/پیشانی (پینیون عمود بر صفحهٔ دندانه).</summary>
    CrownPair,

    /// <summary>جفت غیرمدور بیضوی (چرخش حول کانون‌ها).</summary>
    NonCircularPair,

    /// <summary>مجموعهٔ درایو هارمونیک (موج‌ساز + فِلکسی‌پلاین + اسپلاین دایره‌ای).</summary>
    HarmonicAssembly,

    /// <summary>اتصال هزارخار (شفت خارجی + توپی داخلی).</summary>
    SplineConnection,

    /// <summary>جفت هیپوئید (محورهای موازی‌نبوده با آفست).</summary>
    HypoidPair,
}

/// <summary>وضعیت پشتیبانی یک قابلیت (خانواده یا فرمت) — «هدف» به معنی «پیاده‌شده» نیست.</summary>
public enum CapabilityStatus
{
    /// <summary>پیاده‌سازی‌شده و آزموده با آزمون مستقل.</summary>
    Implemented,

    /// <summary>پیاده‌سازی‌شده با محدودیت/تقریب مستند.</summary>
    Limited,

    /// <summary>در نقشهٔ راه؛ انتخاب آن در UI پیام راهنما می‌دهد.</summary>
    Roadmap,
}

/// <summary>شدت پیام اعتبارسنجی (سند ۰۳، بخش ۸).</summary>
public enum Severity
{
    /// <summary>خطا: ساخت هندسه متوقف می‌شود.</summary>
    Error,

    /// <summary>هشدار: ادامه فقط با توضیح و تأیید کاربر.</summary>
    Warning,

    /// <summary>اطلاع غیرمزاحم.</summary>
    Info,

    /// <summary>بررسی‌نشده — نباید با «سالم/تأییدشده» اشتباه شود.</summary>
    Unverified,
}
