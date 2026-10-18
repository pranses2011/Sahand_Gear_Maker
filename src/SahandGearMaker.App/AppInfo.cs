namespace SahandGearMaker.App;

/// <summary>
/// منبع واحد اطلاعات سازنده (الزام سند ۰۵: «نمایش این اطلاعات باید از یک منبع تنظیمات واحد انجام شود»).
/// شمارهٔ کامل کارت بانکی هرگز در این‌جا یا هر فایل دیگری قرار نمی‌گیرد؛ حمایت مالی فقط پیوند امن است.
/// </summary>
public static class AppInfo
{
    public const string PersianName = "طراح چرخ‌دنده سهند";
    public const string EnglishName = "Sahand Gear Designer";
    public const string Version = "0.1.0";

    public const string Author = "سعید نجفی";
    public const string Phone = "09144220430";
    public const string Email = "Pranses2011@GMail.com";
    public const string Website = "EA-Fixer.ir";

    /// <summary>پیوند اختیاری حمایت مالی — قابل تنظیم؛ بدون درج شمارهٔ کارت در برنامه/مخزن.</summary>
    public const string SupportUrl = "";

    /// <summary>آدرس سرویس بازخورد سمت‌سرور — خالی یعنی «پیکربندی نشده» و ارسال غیرفعال است.</summary>
    public const string FeedbackEndpoint = "";

    public static bool FeedbackConfigured => !string.IsNullOrWhiteSpace(FeedbackEndpoint)
        && FeedbackEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
