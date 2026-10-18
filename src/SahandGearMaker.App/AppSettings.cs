using System.IO;
using System.Text.Json;

namespace SahandGearMaker.App;

/// <summary>
/// تنظیمات کاربر (فاز ۵٫۲) — ذخیره در %APPDATA%/SahandGearDesigner/settings.json.
/// همهٔ اثرها واقعی‌اند: سرعت/جهت ماوس روی بوم، تم بوم، گرید/محور پیش‌فرض، واحد پیش‌فرض خروجی.
/// هیچ داده‌ای به بیرون ارسال نمی‌شود (سیاست حریم خصوصی سند ۰۵).
/// </summary>
public sealed class AppSettings
{
    public double RotateSpeed { get; set; } = 0.008;
    public int InvertHorizontal { get; set; } = 1;

    /// <summary>تم بوم: روشن یا تیره (فقط بوم سه‌بعدی؛ رنگ‌های رابط برای خوانایی روشن می‌مانند).</summary>
    public bool DarkCanvas { get; set; }

    public bool DefaultShowGrid { get; set; } = true;
    public bool DefaultShowAxes { get; set; } = true;

    /// <summary>واحد پیش‌فرض خروجی: false = mm، true = inch.</summary>
    public bool ExportUnitInch { get; set; }

    /// <summary>ذخیرهٔ خودکار فعال/غیرفعال.</summary>
    public bool AutosaveEnabled { get; set; } = true;

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SahandGearDesigner", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
