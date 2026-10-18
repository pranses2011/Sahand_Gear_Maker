namespace SahandGearMaker.App;

/// <summary>
/// منبع واحد رشته‌های رابط کاربری (الزام سند ۰۲: «همهٔ متن‌ها از منبع قابل ترجمه بیایند»).
/// کلیدها ثابت‌اند؛ افزودن زبان دیگر = افزودن فرهنگ جدید به همین کلاس.
/// </summary>
public static class L
{
    private static readonly Dictionary<string, string> Fa = new()
    {
        // عمومی
        ["AppName"] = "طراح چرخ‌دنده سهند",
        ["AppNameEn"] = "Sahand Gear Designer",
        ["Home"] = "خانه",
        ["Designer"] = "طراح چرخ‌دنده",
        ["AssemblyPage"] = "مونتاژ",
        ["Export"] = "خروجی",
        ["Settings"] = "تنظیمات",
        ["About"] = "درباره",
        ["Save"] = "ذخیره",
        ["SaveAs"] = "ذخیره با نام جدید",
        ["Open"] = "بازکردن",
        ["NewProject"] = "پروژهٔ جدید",
        ["RecentProjects"] = "پروژه‌های اخیر",
        ["Samples"] = "نمونه‌ها",
        ["Help"] = "راهنما",
        ["Undo"] = "واگرد",
        ["Redo"] = "ازنو",
        ["Close"] = "بستن",
        ["Cancel"] = "انصراف",
        ["Ok"] = "تأیید",
        ["Yes"] = "بله",
        ["No"] = "خیر",
        ["Copy"] = "کپی",
        ["CopyDetails"] = "کپی جزئیات",
        ["Warnings"] = "هشدارها",
        ["Errors"] = "خطاها",
        ["Info"] = "اطلاعات",
        ["TriCount"] = "شمار مثلث",
        ["Unit"] = "واحد",
        ["Offline"] = "آفلاین",
        ["Ready"] = "آماده",

        // خانواده‌ها
        ["Family.ExternalSpur"] = "خارجی مستقیم",
        ["Family.ExternalHelical"] = "خارجی مارپیچ",
        ["Family.ExternalHerringbone"] = "جناغی (دومارپیچ)",
        ["Family.InternalSpur"] = "داخلی",
        ["Family.Rack"] = "شانهٔ مستقیم",
        ["Family.NotSupported"] = "این نوع در نسخهٔ فعلی پشتیبانی نمی‌شود (نقشهٔ راه)",

        // پارامترها
        ["Param.ToothCount"] = "تعداد دندانه",
        ["Param.Module"] = "ماژول",
        ["Param.PressureAngle"] = "زاویهٔ فشار",
        ["Param.HelixAngle"] = "زاویهٔ مارپیچ",
        ["Param.FaceWidth"] = "پهنای دندانه",
        ["Param.ProfileShift"] = "اصلاح پروفیل",
        ["Param.Backlash"] = "لقی",
        ["Param.BoreDiameter"] = "قطر سوراخ مرکزی",
        ["Param.KeywayWidth"] = "پهنای شیار خار",
        ["Param.HubDiameter"] = "قطر توپی",
        ["Param.HubLength"] = "طول توپی",
        ["Param.RackLength"] = "طول شانه",

        // ارقام
        ["Fig.PitchDiameter"] = "قطر گام",
        ["Fig.TipDiameter"] = "قطر سر",
        ["Fig.RootDiameter"] = "قطر ریشه",
        ["Fig.BaseDiameter"] = "قطر پایه",

        // خروجی
        ["Export.Format"] = "فرمت",
        ["Export.Quality"] = "کیفیت مش",
        ["Export.Preview"] = "پیش‌نمایش",
        ["Export.Standard"] = "معمولی",
        ["Export.Fine"] = "دقیق",
        ["Export.Unit"] = "واحد خروجی",
        ["Export.Start"] = "خروجی بگیر",
        ["Export.Result"] = "نتیجهٔ خروجی",
        ["Export.MeshWarning"] = "خروجی، مش مثلثی است نه مدل B-rep دقیق CAD",

        // بازخورد
        ["Feedback.Title"] = "ارسال نظر یا گزارش مشکل",
        ["Feedback.Category"] = "دسته",
        ["Feedback.Suggestion"] = "پیشنهاد",
        ["Feedback.Bug"] = "گزارش مشکل",
        ["Feedback.Other"] = "سایر",
        ["Feedback.Message"] = "متن پیام",
        ["Feedback.Send"] = "ارسال",
        ["Feedback.NotConfigured"] = "سرویس ارسال پیکربندی نشده است؛ پیام شما به‌صورت محلی ذخیره می‌شود.",
        ["Feedback.Queued"] = "در صف محلی ذخیره شد",
        ["Feedback.Privacy"] = "سیاست حریم خصوصی",

        // درباره
        ["About.Author"] = "طراح و نویسنده",
        ["About.Phone"] = "تلفن",
        ["About.Email"] = "ایمیل",
        ["About.Website"] = "وب‌سایت",
        ["About.Privacy"] = "این برنامه کاملاً آفلاین کار می‌کند؛ هیچ داده‌ای بدون رضایت شما ارسال نمی‌شود.",
        ["About.CapabilityNote"] = "وضعیت قابلیت‌ها و فرمت‌ها را در مستندات و ماتریس قابلیت برنامه ببینید.",
    };

    /// <summary>واکشی رشتهٔ بومی‌شده؛ اگر کلید نبود، خود کلید برگردده می‌شود (تشخیص آسان در بازبینی).</summary>
    public static string Get(string key) => Fa.TryGetValue(key, out var v) ? v : key;

    /// <summary>معادل فنی انگلیسی کنار نام فارسی (سند ۰۲: «فارسی با معادل انگلیسی»).</summary>
    public static string FaEn(string fa, string en) => $"{fa} ({en})";
}
