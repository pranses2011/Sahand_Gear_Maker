namespace SahandGearMaker.Domain;

/// <summary>
/// پروژهٔ کامل برنامه (سند ۰۳، بخش ۷ و سند ۰۴، بخش ۳).
/// فایل بومی با پسوند <c>.sgdproj</c> ذخیره می‌شود؛ نسخهٔ schema برای مهاجرت امن.
/// </summary>
public sealed class GearProject
{
    /// <summary>نسخهٔ ساختار داده؛ با هر تغییر ناسازگار یک واحد زیاد می‌شود و مهاجرت نوشته می‌شود.</summary>
    public int SchemaVersion { get; set; } = 1;

    public string Name { get; set; } = "پروژهٔ بدون نام";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>واحد نمایشی ترجیحی کاربر (اندازهٔ واقعی مدل تغییر نمی‌کند).</summary>
    public LengthUnit DisplayUnit { get; set; } = LengthUnit.Millimeter;

    /// <summary>همهٔ قطعات پروژه.</summary>
    public List<GearPart> Parts { get; set; } = new();

    /// <summary>رابطه‌های مونتاژ/جفت‌شدن قطعات.</summary>
    public List<MatingRelation> Matings { get; set; } = new();

    /// <summary>دادهٔ ناشناختهٔ نگه‌داشته‌شده از نسخه‌های جدیدتر/قدیمی‌تر — حذف خاموش ممنوع (سند ۰۴، بخش ۳).</summary>
    public Dictionary<string, object?>? UnknownData { get; set; }

    public GearPart? FindPart(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>بازتولید همهٔ شناسه‌ها و نام‌های یکتا برای «ذخیرهٔ نسخهٔ جدید».</summary>
    public GearProject Clone()
    {
        var idMap = new Dictionary<string, string>();
        var clone = new GearProject
        {
            SchemaVersion = SchemaVersion,
            Name = Name,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = DateTime.UtcNow,
            DisplayUnit = DisplayUnit,
        };
        foreach (var part in Parts)
        {
            var newPart = part.Clone(part.Name);
            idMap[part.Id] = newPart.Id;
            clone.Parts.Add(newPart);
        }
        foreach (var mating in Matings)
        {
            clone.Matings.Add(new MatingRelation
            {
                Kind = mating.Kind,
                PartAId = idMap.TryGetValue(mating.PartAId, out var a) ? a : mating.PartAId,
                PartBId = idMap.TryGetValue(mating.PartBId, out var b) ? b : mating.PartBId,
                Note = mating.Note,
            });
        }
        return clone;
    }
}

/// <summary>رابطهٔ جفت‌شدن دو قطعه (سند ۰۳، بخش ۶) — روی شناسهٔ پایدار قطعات، نه نام آن‌ها.</summary>
public sealed class MatingRelation
{
    public MatingKind Kind { get; set; } = MatingKind.ExternalPair;

    public string PartAId { get; set; } = string.Empty;

    public string PartBId { get; set; } = string.Empty;

    /// <summary>یادداشت اختیاری کاربر دربارهٔ این رابطه.</summary>
    public string? Note { get; set; }
}
