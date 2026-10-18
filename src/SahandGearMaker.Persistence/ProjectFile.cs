using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SahandGearMaker.Domain;

namespace SahandGearMaker.Persistence;

/// <summary>
/// مدیریت فایل پروژهٔ بومی <c>.sgdproj</c> (سند ۰۴، بخش ۳).
/// ذخیره‌سازی اتمی: نوشتن در فایل موقت → بررسی → جایگزینی؛ قطع برنامه فایل قبلی را خراب نمی‌کند.
/// نسخهٔ schema در فایل ثبت می‌شود و دادهٔ ناشناخته حفظ می‌شود (حذف خاموش ممنوع).
/// </summary>
public static class ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".sgdproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // متن فارسی بدون escape
    };

    /// <summary>ذخیرهٔ اتمی پروژه در مسیر داده‌شده. در خطا، فایل قبلی دست‌نخورده می‌ماند.</summary>
    public static void Save(GearProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        project.ModifiedUtc = DateTime.UtcNow;
        project.SchemaVersion = CurrentSchemaVersion;

        string tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var json = JsonSerializer.Serialize(project, Options);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            // اعتبارسنجی پیش از جایگزینی: فایل موقت باید بدون خطا deserialize شود
            using var check = JsonDocument.Parse(File.ReadAllText(tempPath));
            if (check.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("ساختار فایل پروژه معتبر نیست.");

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* تلاش برای پاک‌سازی */ }
            throw;
        }
    }

    /// <summary>بارگذاری پروژه با مهاجرت نسخه و حفظ دادهٔ ناشناخته.</summary>
    public static GearProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("فایل پروژه پیدا نشد.", path);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int version = root.TryGetProperty(nameof(GearProject.SchemaVersion), out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : CurrentSchemaVersion;

        var project = version <= CurrentSchemaVersion
            ? JsonSerializer.Deserialize<GearProject>(json, Options)
            : LoadWithUnknownPreservation(json);

        if (project is null)
            throw new InvalidOperationException("فایل پروژه قابل خواندن نیست.");

        Migrate(project, version);
        return project;
    }

    /// <summary>
    /// بارگذاری فایل از نسخهٔ جدیدتر: فیلدهای شناخته‌شده خوانده و بقیه در UnknownData نگه داشته می‌شود؛
    /// چیزی حذف نمی‌شود تا با نسخهٔ جدیدتر برنامه دوباره ذخیره شود.
    /// </summary>
    private static GearProject LoadWithUnknownPreservation(string json)
    {
        var project = new GearProject();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name)
            {
                case nameof(GearProject.Name):
                    project.Name = prop.Value.GetString() ?? project.Name;
                    break;
                case nameof(GearProject.SchemaVersion):
                    project.SchemaVersion = prop.Value.GetInt32();
                    break;
                case nameof(GearProject.DisplayUnit):
                    project.DisplayUnit = prop.Value.GetInt32() == 1 ? LengthUnit.Inch : LengthUnit.Millimeter;
                    break;
                case nameof(GearProject.CreatedUtc):
                    project.CreatedUtc = prop.Value.GetDateTime();
                    break;
                case nameof(GearProject.ModifiedUtc):
                    project.ModifiedUtc = prop.Value.GetDateTime();
                    break;
                case nameof(GearProject.Parts):
                    project.Parts = JsonSerializer.Deserialize<List<GearPart>>(prop.Value.GetRawText(), Options) ?? new();
                    break;
                case nameof(GearProject.Matings):
                    project.Matings = JsonSerializer.Deserialize<List<MatingRelation>>(prop.Value.GetRawText(), Options) ?? new();
                    break;
                default:
                    (project.UnknownData ??= new())[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText(), Options);
                    break;
            }
        }
        return project;
    }

    /// <summary>مهاجرت نسخه‌های قدیمی؛ فعلاً نسخهٔ 1 آخرین است.</summary>
    private static void Migrate(GearProject project, int fromVersion)
    {
        // fromVersion < 1 → هیچ چیز (نسخهٔ 1 مبناست)
        // مهاجرت‌های آینده اینجا با گزارش صریح اضافه می‌شوند.
        if (fromVersion < CurrentSchemaVersion)
            project.SchemaVersion = CurrentSchemaVersion;
    }
}
