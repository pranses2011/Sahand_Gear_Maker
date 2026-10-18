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
        new CapabilityInfo("ExternalHelical", "چرخ‌دندهٔ خارجی مارپیچ", "External helical gear", CapabilityStatus.Limited,
            "مدول نرمال/عرضی معتبر؛ بدنه با برش‌های چرخیده — دقت به تعداد برش وابسته است."),
        new CapabilityInfo("ExternalHerringbone", "چرخ‌دندهٔ جناغی (دومارپیچ)", "Herringbone gear", CapabilityStatus.Limited,
            "دو نیمهٔ مارپیچ مخالف؛ گپ مرکزی اختیاری به‌صورت استوانهٔ ساده مدل می‌شود."),
        new CapabilityInfo("InternalSpur", "چرخ‌دندهٔ داخلی", "Internal gear", CapabilityStatus.Limited,
            "دندانهٔ داخلی با addendum کوتاه؛ قید تداخل شعاعی/اختلاف دندانه به‌صورت هشدار بررسی می‌شود."),
        new CapabilityInfo("InternalHelical", "چرخ‌دندهٔ داخلی مارپیچ", "Internal helical gear", CapabilityStatus.Roadmap,
            "در نقشهٔ راه؛ الگوریتم داخلی + پیچش هم‌زمان نیازمند بررسی بیشتر."),
        new CapabilityInfo("Rack", "شانهٔ مستقیم", "Rack", CapabilityStatus.Limited,
            "پروفیل تعادلی مثلثی با نوک اینولوت‌گونه؛ برای مونتاژ شانه-پینیون با گام سازگار."),
        new CapabilityInfo("HelicalRack", "شانهٔ مارپیچ", "Helical rack", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
        new CapabilityInfo("BevelSpur", "مخروطی مستقیم", "Straight bevel", CapabilityStatus.Roadmap,
            "نیازمند هندسهٔ مخروطی گام و روش تولید پروفیل مخصوص."),
        new CapabilityInfo("BevelSpiral", "مخروطی مارپیچ/قوسی", "Spiral bevel", CapabilityStatus.Roadmap, "روش تولید وابسته به ماشین‌کاری؛ در نقشهٔ راه."),
        new CapabilityInfo("Worm", "حلزونی", "Worm", CapabilityStatus.Roadmap, "هندسهٔ تماس پیشرفته؛ در نقشهٔ راه."),
        new CapabilityInfo("WormWheel", "چرخ حلزونی", "Worm wheel", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
        new CapabilityInfo("CrossedHelical", "مارپیچ متقاطع", "Crossed helical", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
        new CapabilityInfo("CrownFace", "تاجی/پیشانی", "Crown/face gear", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
        new CapabilityInfo("Hypoid", "هیپوئیدی", "Hypoid", CapabilityStatus.Roadmap, "نیازمند کرنل تخصصی."),
        new CapabilityInfo("Cycloidal", "سیکلوئیدی", "Cycloidal", CapabilityStatus.Roadmap, "در نقشهٔ راه به‌عنوان افزونه."),
        new CapabilityInfo("NonCircular", "غیرمدور", "Non-circular", CapabilityStatus.Roadmap, "منحنی گام متغیر؛ افزونهٔ آینده."),
        new CapabilityInfo("HarmonicDrive", "درایو هارمونیک", "Harmonic drive", CapabilityStatus.Roadmap, "مدل شکل‌دهی جدا از تحلیل ساخت."),
        new CapabilityInfo("SplineExternal", "هزارخار خارجی", "External spline", CapabilityStatus.Roadmap, "مجزا از چرخ‌دندهٔ انتقال توان."),
        new CapabilityInfo("SplineInternal", "هزارخار داخلی", "Internal spline", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
    };

    /// <summary>وضعیت فرمت‌های خروجی (سند ۰۴، بخش ۲). «هدف» ≠ «پیاده‌شده».</summary>
    public static readonly IReadOnlyList<CapabilityInfo> ExportFormats = new[]
    {
        new CapabilityInfo("SGDPROJ", "پروژهٔ بومی پارامتریک", ".sgdproj", CapabilityStatus.Implemented,
            "JSON نسخه‌دار با ذخیرهٔ اتمی و بازخوانی/مقایسهٔ round-trip آزموده‌شده."),
        new CapabilityInfo("STL-BINARY", "STL باینری", "Binary STL", CapabilityStatus.Implemented,
            "خروجی مش مثلثی؛ واحد در خود فایل کد نمی‌شود و در گزارش/نام همراه ذکر می‌شود."),
        new CapabilityInfo("STL-ASCII", "STL متنی", "ASCII STL", CapabilityStatus.Limited, "برای اشکال‌زدایی/تبادل متنی؛ فایل حجیم‌تر."),
        new CapabilityInfo("OBJ", "OBJ + MTL", "Wavefront OBJ", CapabilityStatus.Implemented, "مش + فایل متریال MTL جانبی."),
        new CapabilityInfo("PLY", "PLY باینری", "Stanford PLY", CapabilityStatus.Implemented, "مش با نرمال؛ برای ابزارهای پردازش مش."),
        new CapabilityInfo("GLB", "glTF باینری (GLB)", "glTF binary", CapabilityStatus.Implemented,
            "یک‌پارچه (single-file)؛ واحد glTF متر است و تبدیل mm→m انجام می‌شود."),
        new CapabilityInfo("3MF", "3MF", "3D Manufacturing Format", CapabilityStatus.Roadmap, "در نقشهٔ راه؛ نیازمند بستهٔ ساخت ZIP معتبر."),
        new CapabilityInfo("AMF", "AMF", "Additive Manufacturing Format", CapabilityStatus.Roadmap, "در نقشهٔ راه."),
        new CapabilityInfo("VRML", "VRML", "VRML 97 .wrl", CapabilityStatus.Roadmap, "در صورت تقاضا."),
        new CapabilityInfo("STEP", "STEP AP242", "STEP B-rep", CapabilityStatus.Roadmap,
            "مش مثلثی به‌عنوان B-rep معرفی نمی‌شود؛ نیازمند هستهٔ CAD با مجوز مناسب."),
        new CapabilityInfo("IGES", "IGES", "IGES .igs", CapabilityStatus.Roadmap, "مشروط به هستهٔ CAD."),
        new CapabilityInfo("FBX", "FBX", "Autodesk FBX", CapabilityStatus.Roadmap, "کتابخانهٔ باز و پایدار بررسی نشده."),
        new CapabilityInfo("DXF", "DXF دوبعدی", "Drawing exchange", CapabilityStatus.Roadmap, "نقشهٔ دوبعدی؛ مکمل، نه مدل سه‌بعدی."),
        new CapabilityInfo("SVG", "SVG دوبعدی", "Scalable vector", CapabilityStatus.Roadmap, "نقشهٔ دوبعدی؛ در نقشهٔ راه."),
    };

    public static CapabilityStatus StatusOfFamily(GearFamily family) => family switch
    {
        GearFamily.ExternalSpur => CapabilityStatus.Implemented,
        GearFamily.ExternalHelical => CapabilityStatus.Limited,
        GearFamily.ExternalHerringbone => CapabilityStatus.Limited,
        GearFamily.InternalSpur => CapabilityStatus.Limited,
        GearFamily.Rack => CapabilityStatus.Limited,
        _ => CapabilityStatus.Roadmap,
    };

    /// <summary>آیا این خانواده هندسهٔ واقعی می‌سازد؟ (انواع Roadmap مدل جعلی نمی‌سازند.)</summary>
    public static bool BuildsGeometry(GearFamily family) => StatusOfFamily(family) != CapabilityStatus.Roadmap;
}
