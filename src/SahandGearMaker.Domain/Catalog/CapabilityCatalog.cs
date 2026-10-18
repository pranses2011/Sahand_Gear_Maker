namespace SahandGearMaker.Domain;

/// <summary>کاتالوگ قابلیت واقعی برنامه (سند ۰۳، بخش ۲ و سند ۰۴، بخش ۸).</summary>
public sealed record CapabilityInfo(string Key, string PersianTitle, string EnglishTitle, CapabilityStatus Status, string Note);

public static class CapabilityCatalog
{
    /// <summary>وضعیت خانواده‌های هندسی چرخ‌دنده. این جدول منبع حقیقت UI هم هست.</summary>
    public static readonly IReadOnlyList<CapabilityInfo> GearFamilies = new[]
    {
        new CapabilityInfo("ExternalSpur", "چرخ‌دندهٔ خارجی مستقیم", "External spur gear", CapabilityStatus.Implemented,
            "پروفیل involute با تلرانس وتر؛ آزمون مستقل دارد."),
        new CapabilityInfo("ExternalHelical", "چرخ‌دندهٔ خارجی مارپیچ", "External helical gear", CapabilityStatus.Implemented,
            "مدول نرمال/عرضی معتبر؛ بدنه با برش‌های چرخیده و قدم‌زدن ادغامی."),
        new CapabilityInfo("ExternalHerringbone", "چرخ‌دندهٔ جناغی (دومارپیچ)", "Herringbone gear", CapabilityStatus.Implemented,
            "دو نیمهٔ مارپیچ مخالف؛ گپ مرکزی واقعی روی برش‌ها."),
        new CapabilityInfo("InternalSpur", "چرخ‌دندهٔ داخلی", "Internal gear", CapabilityStatus.Implemented,
            "رینگ با دندانهٔ داخلی واقعی؛ قید تداخل شعاعی/اختلاف دندانه بررسی می‌شود."),
        new CapabilityInfo("InternalHelical", "چرخ‌دندهٔ داخلی مارپیچ", "Internal helical gear", CapabilityStatus.Implemented,
            "دندانهٔ داخلی مارپیچ با پیچش هم‌زمان برش‌ها."),
        new CapabilityInfo("Rack", "شانهٔ مستقیم", "Rack", CapabilityStatus.Implemented,
            "پروفیل مثلثی با نوک اینولوت‌گونه؛ مونتاژ شانه-پینیون با گام سازگار."),
        new CapabilityInfo("HelicalRack", "شانهٔ مارپیچ", "Helical rack", CapabilityStatus.Implemented,
            "شانه با برش‌های چرخیده هم‌جهت مارپیچ."),
        new CapabilityInfo("BevelSpur", "مخروطی مستقیم", "Straight bevel", CapabilityStatus.Implemented,
            "پروفیل معادل Tredgold روی مخروط پشتی؛ دندانه‌های همگرا به رأس؛ تقریب استاندارد مستند."),
        new CapabilityInfo("BevelSpiral", "مخروطی مارپیچ/قوسی", "Spiral bevel", CapabilityStatus.Implemented,
            "مارپیچ لگاریتمی با زاویهٔ ثابت روی مخروط بازشده؛ سازگار با β در شعاع میانگین."),
        new CapabilityInfo("Worm", "حلزونی (ZA)", "Worm (ZA)", CapabilityStatus.Implemented,
            "پروفیل محوری ذوزنقه‌ای مستقیم؛ سطح پیچی دقیق در مقاطع؛ رشتهٔ ۱ تا ۱۲."),
        new CapabilityInfo("WormWheel", "چرخ حلزونی", "Worm wheel", CapabilityStatus.Implemented,
            "هلیکی هم‌دست کرم با گلوگاه گلوبوئیدی (پوشش استوانهٔ ریشهٔ کرم — روش ابزار کرم‌نما)."),
        new CapabilityInfo("CrossedHelical", "مارپیچ متقاطع", "Crossed helical", CapabilityStatus.Implemented,
            "هندسهٔ قطعه = چرخ‌دندهٔ مارپیچ؛ محاسبهٔ جفت با زاویهٔ محور Σ=β1+β2."),
        new CapabilityInfo("CrownFace", "تاجی/پیشانی", "Crown/face gear", CapabilityStatus.Implemented,
            "دندانهٔ شعاعی با ارتفاع کاهشی؛ طرح‌ریزی تولید مرتبهٔ اول (پوشش Litvin پیاده نشده)."),
        new CapabilityInfo("Hypoid", "هیپوئیدی", "Hypoid", CapabilityStatus.Implemented,
            "هندسهٔ مخروطی مارپیچ با روابط آفست هیپوئید؛ تماس دقیق هیپوئید حل‌گر جدا می‌خواهد."),
        new CapabilityInfo("Cycloidal", "سیکلوئیدی", "Cycloidal", CapabilityStatus.Implemented,
            "اپیسایکلوئید سر + هیپوسایکلوئید شعاعی ریشه با دایرهٔ مولّد نصف گام (قرارداد ساعت‌سازی)."),
        new CapabilityInfo("NonCircular", "غیرمدور بیضوی", "Non-circular (elliptical)", CapabilityStatus.Implemented,
            "منحنی گام بیضی با حل محیط E(e)؛ دندانهٔ شانه‌ای موضعی؛ بور روی کانون."),
        new CapabilityInfo("HarmonicDrive", "فِلکسی‌پلاین هارمونیک", "Harmonic flexspline", CapabilityStatus.Implemented,
            "فنجان دندانه‌دار حالت آزاد؛ موج‌ساز و اسپلاین دایره‌ای از مونتاژ هارمونیک."),
        new CapabilityInfo("SplineExternal", "هزارخار خارجی", "External spline", CapabilityStatus.Implemented,
            "دندانهٔ اینولوت کوتاه با برازش سطح گام/قطر پایه؛ تلرانس واقعی استاندارد لحاظ نشده."),
        new CapabilityInfo("SplineInternal", "هزارخار داخلی", "Internal spline", CapabilityStatus.Implemented,
            "هزارخار داخلی کوتاه؛ جفت با SplineConnection."),
    };

    /// <summary>وضعیت فرمت‌های خروجی (سند ۰۴، بخش ۲). «هدف» ≠ «پیاده‌شده».</summary>
    public static readonly IReadOnlyList<CapabilityInfo> ExportFormats = new[]
    {
        new CapabilityInfo("SGDPROJ", "پروژهٔ بومی پارامتریک", ".sgdproj", CapabilityStatus.Implemented,
            "JSON نسخه‌دار با ذخیرهٔ اتمی و بازخوانی/مقایسهٔ round-trip آزموده‌شده."),
        new CapabilityInfo("STL-BINARY", "STL باینری", "Binary STL", CapabilityStatus.Implemented,
            "خروجی مش مثلثی؛ واحد در خود فایل کد نمی‌شود و در گزارش/نام همراه ذکر می‌شود."),
        new CapabilityInfo("STL-ASCII", "STL متنی", "ASCII STL", CapabilityStatus.Implemented, "برای اشکال‌زدایی/تبادل متنی؛ فایل حجیم‌تر."),
        new CapabilityInfo("OBJ", "OBJ + MTL", "Wavefront OBJ", CapabilityStatus.Implemented, "مش + فایل متریال MTL جانبی."),
        new CapabilityInfo("PLY", "PLY باینری", "Stanford PLY", CapabilityStatus.Implemented, "مش با نرمال؛ برای ابزارهای پردازش مش."),
        new CapabilityInfo("GLB", "glTF باینری (GLB)", "glTF binary", CapabilityStatus.Implemented,
            "یک‌پارچه (single-file)؛ واحد glTF متر است و تبدیل mm→m انجام می‌شود."),
        new CapabilityInfo("3MF", "3MF", "3D Manufacturing Format", CapabilityStatus.Implemented,
            "بستهٔ ZIP استاندارد چاپ؛ از نسخهٔ ۰٫۲ پیاده و آزموده است."),
        new CapabilityInfo("AMF", "AMF", "Additive Manufacturing Format", CapabilityStatus.Implemented,
            "XML چاپ افزایشی؛ از نسخهٔ ۰٫۲ پیاده و آزموده است."),
        new CapabilityInfo("VRML", "VRML", "VRML 97 .wrl", CapabilityStatus.Roadmap, "در صورت تقاضا."),
        new CapabilityInfo("STEP", "STEP AP242", "STEP B-rep", CapabilityStatus.Roadmap,
            "مش مثلثی به‌عنوان B-rep معرفی نمی‌شود؛ نیازمند هستهٔ CAD با مجوز مناسب."),
        new CapabilityInfo("IGES", "IGES", "IGES .igs", CapabilityStatus.Roadmap, "مشروط به هستهٔ CAD."),
        new CapabilityInfo("FBX", "FBX", "Autodesk FBX", CapabilityStatus.Roadmap, "کتابخانهٔ باز و پایدار بررسی نشده."),
        new CapabilityInfo("DXF", "DXF پروفیل دندانه", "Drawing exchange", CapabilityStatus.Implemented,
            "پروفیل مقطع دوبعدی از نقشهٔ فنی؛ مکمل، نه مدل سه‌بعدی."),
        new CapabilityInfo("SVG", "SVG نقشهٔ فنی", "Scalable vector", CapabilityStatus.Implemented,
            "خروجی برداری نقشهٔ فنی فارسی از موتور Drawing."),
    };

    public static CapabilityStatus StatusOfFamily(GearFamily family) => family switch
    {
        GearFamily.ExternalSpur or GearFamily.ExternalHelical or GearFamily.ExternalHerringbone
            => CapabilityStatus.Implemented,
        GearFamily.InternalSpur or GearFamily.InternalHelical => CapabilityStatus.Implemented,
        GearFamily.Rack or GearFamily.HelicalRack => CapabilityStatus.Implemented,
        GearFamily.BevelSpur or GearFamily.BevelSpiral or GearFamily.Hypoid => CapabilityStatus.Implemented,
        GearFamily.Worm or GearFamily.WormWheel => CapabilityStatus.Implemented,
        GearFamily.CrossedHelical or GearFamily.CrownFace => CapabilityStatus.Implemented,
        GearFamily.Cycloidal or GearFamily.NonCircular => CapabilityStatus.Implemented,
        GearFamily.HarmonicDrive => CapabilityStatus.Implemented,
        GearFamily.SplineExternal or GearFamily.SplineInternal => CapabilityStatus.Implemented,
        _ => CapabilityStatus.Roadmap,
    };

    /// <summary>آیا این خانواده هندسهٔ واقعی می‌سازد؟ (انواع Roadmap مدل جعلی نمی‌سازند.)</summary>
    public static bool BuildsGeometry(GearFamily family) => StatusOfFamily(family) != CapabilityStatus.Roadmap;
}
