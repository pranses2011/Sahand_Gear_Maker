namespace SahandGearMaker.App;

/// <summary>
/// منبع واحد اطلاعات سازنده (الزام سند ۰۵: «نمایش این اطلاعات باید از یک منبع تنظیمات واحد انجام شود»).
/// شمارهٔ کامل کارت بانکی هرگز در این‌جا یا هر فایل دیگری قرار نمی‌گیرد؛ حمایت مالی فقط پیوند امن است.
/// </summary>
public static class AppInfo
{
    public const string PersianName = "طراح چرخ‌دنده سهند";
    public const string EnglishName = "Sahand Gear Designer";

    /// <summary>
    /// نسخهٔ واحد از فرادادهٔ اسمبلی (MSBuild $(Version)) — فاز ۰٫۱ نقشهٔ راه:
    /// پیش از این، نسخه در چند جا (csproj/AppInfo/مستندات) جداگانه نگه‌داری می‌شد و ناهماهنگ می‌شد.
    /// </summary>
    public static string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

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
