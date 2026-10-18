using System.Text.Json;
using SahandGearMaker.Domain;

namespace SahandGearMaker.Persistence;

/// <summary>یک گونه (Variant) طراحی: نام مستقل + کپی پارامتریک قطعات + ملاحظات.</summary>
public sealed class GearVariant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "گونهٔ جدید";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>قطعات گونه (کپی مستقل از قطعات پروژه).</summary>
    public List<GearPart> Parts { get; set; } = new();

    /// <summary>شناسهٔ گونهٔ پایه (خالی = مستقیماً از حالت جاری پروژه ساخته شده).</summary>
    public string? BaseVariantId { get; set; }

    public string? Notes { get; set; }
}

/// <summary>مدیریت گونه‌های یک پروژه: ساخت/فهرست/فعال‌سازی/مقایسه (سند ۰۷، بخش ۳٫۵).</summary>
public static class VariantManager
{
    /// <summary>ساخت گونهٔ جدید از وضعیت جاری پروژه.</summary>
    public static GearVariant Capture(GearProject project, string name, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("نام گونه نمی‌تواند خالی باشد.", nameof(name));

        var variant = new GearVariant
        {
            Name = name,
            Notes = notes,
            Parts = project.Parts.Select(p => p.Clone()).ToList(),
        };
        return variant;
    }

    /// <summary>اعمال گونه روی پروژه (جایگزینی قطعات؛ رابطه‌های مونتاژ با نگاشت نام هر بار بازسازی نمی‌شوند — با شناسهٔ پایدار نگه می‌مانند).</summary>
    public static void Apply(GearProject project, GearVariant variant)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(variant);
        project.Parts = variant.Parts.Select(p => p.Clone()).ToList();
        project.ModifiedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// مقایسهٔ پارامتری دو گونه: تفاوت هر فیلد مشترک (بر پایهٔ نام قطعه) با مقدار قبلی/جدید.
    /// </summary>
    public static IReadOnlyList<VariantDiff> Compare(GearVariant a, GearVariant b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var diffs = new List<VariantDiff>();
        var mapA = a.Parts.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
        var mapB = b.Parts.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());

        foreach (var (name, pa) in mapA)
        {
            if (!mapB.TryGetValue(name, out var pb))
            {
                diffs.Add(new VariantDiff(name, "وجود قطعه", "موجود", "حذف‌شده"));
                continue;
            }
            ComparePart(pa, pb, diffs);
        }
        foreach (var name in mapB.Keys.Except(mapA.Keys))
            diffs.Add(new VariantDiff(name, "وجود قطعه", "جدید", "موجود"));

        return diffs;

        static void ComparePart(GearPart pa, GearPart pb, List<VariantDiff> list)
        {
            void Add(string field, string va, string vb)
            {
                if (!string.Equals(va, vb, StringComparison.Ordinal))
                    list.Add(new VariantDiff(pa.Name, field, va, vb));
            }

            Add("خانواده", pa.Family.ToString(), pb.Family.ToString());
            Add("تعداد دندانه", pa.Teeth.ToothCount.ToString(), pb.Teeth.ToothCount.ToString());
            Add("ماژول (mm)", pa.Teeth.Module.ToString("F3"), pb.Teeth.Module.ToString("F3"));
            Add("زاویهٔ فشار (°)", UnitConverter.RadToDeg(pa.Teeth.PressureAngle).ToString("F1"),
                UnitConverter.RadToDeg(pb.Teeth.PressureAngle).ToString("F1"));
            Add("زاویهٔ مارپیچ (°)", UnitConverter.RadToDeg(pa.Teeth.HelixAngle).ToString("F2"),
                UnitConverter.RadToDeg(pb.Teeth.HelixAngle).ToString("F2"));
            Add("پهنای دندانه (mm)", pa.Teeth.FaceWidth.ToString("F2"), pb.Teeth.FaceWidth.ToString("F2"));
            Add("بور (mm)", pa.Body.BoreDiameter.ToString("F2"), pb.Body.BoreDiameter.ToString("F2"));
            Add("توپی (mm)", pa.Body.HubDiameter.ToString("F1"), pb.Body.HubDiameter.ToString("F1"));
        }
    }
}

/// <summary>یک تفاوت بین دو گونه.</summary>
public sealed record VariantDiff(string PartName, string Field, string OldValue, string NewValue);

/// <summary>فایل گونه‌های پروژه: نگهداری/بازیابی با ذخیرهٔ اتمی.</summary>
public static class VariantFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public const string Extension = ".sgdvars";

    public static void Save(IReadOnlyList<GearVariant> variants, string path)
    {
        ArgumentNullException.ThrowIfNull(variants);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(variants, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static List<GearVariant> Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("فایل گونه‌ها یافت نشد.", path);
        var result = JsonSerializer.Deserialize<List<GearVariant>>(File.ReadAllText(path), Options);
        return result ?? new List<GearVariant>();
    }
}

/// <summary>سرویس ذخیرهٔ خودکار با بازیابی پس از خرابی (سند ۰۷، بستهٔ A).</summary>
public sealed class AutosaveService
{
    private readonly string _autosavePath;
    private readonly TimeSpan _interval;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private readonly object _gate = new();

    public AutosaveService(string projectFilePath, TimeSpan? interval = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("مسیر پروژه خالی است.", nameof(projectFilePath));
        _autosavePath = projectFilePath + ".autosave";
        _interval = interval ?? TimeSpan.FromMinutes(2);
    }

    public bool IsEnabled { get; set; } = true;

    public string AutosavePath => _autosavePath;

    /// <summary>آیا زمان ذخیرهٔ خودکار رسیده است؟</summary>
    public bool ShouldSaveNow(DateTime utcNow) => IsEnabled && utcNow - _lastSaveUtc >= _interval;

    /// <summary>ذخیرهٔ خودکار (اگر زمانش رسیده باشد). فایل اصلی هرگز دست نمی‌خورد.</summary>
    public bool TryAutosave(GearProject project, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_gate)
        {
            if (!ShouldSaveNow(utcNow))
                return false;
            try
            {
                ProjectFile.Save(project, _autosavePath);
                _lastSaveUtc = utcNow;
                return true;
            }
            catch (IOException)
            {
                return false; // دیسک پر/قفل — بی‌صدا رد می‌شود؛ فایل اصلی سالم است
            }
        }
    }

    /// <summary>آیا نسخهٔ بازیابی موجود است؟</summary>
    public bool HasRecovery => File.Exists(_autosavePath);

    /// <summary>بارگذاری نسخهٔ بازیابی (null اگر نباشد).</summary>
    public GearProject? TryRecover()
    {
        if (!HasRecovery)
            return null;
        try
        {
            return ProjectFile.Load(_autosavePath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>حذف نسخهٔ بازیابی (پس از بازیابی موفق یا درخواست کاربر).</summary>
    public void DiscardRecovery()
    {
        try
        {
            if (File.Exists(_autosavePath))
                File.Delete(_autosavePath);
        }
        catch (IOException)
        {
            // حذف ناموفق بی‌خطر است؛ دفعهٔ بعد بازنویسی می‌شود
        }
    }
}
