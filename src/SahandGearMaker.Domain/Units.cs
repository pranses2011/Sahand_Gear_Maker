namespace SahandGearMaker.Domain;

/// <summary>واحد طول پذیرفته‌شده در برنامه. واحد داخلی مدل همیشه میلی‌متر است.</summary>
public enum LengthUnit
{
    Millimeter,
    Inch,
}

/// <summary>واحد زاویه برای ورودی نمایشی. واحد داخلی محاسبات همیشه رادیان است.</summary>
public enum AngleUnit
{
    Degree,
    Radian,
}

/// <summary>
/// تبدیل واحدهای طول و زاویه — داخلی: میلی‌متر و رادیان.
/// تبدیل‌ها برگشت‌پذیرند و اندازهٔ واقعی مدل را تغییر نمی‌دهند (الزام سند ۰۲، بخش ۹).
/// </summary>
public static class UnitConverter
{
    public const double MmPerInch = 25.4;

    /// <summary>مقدار ورودی بر حسب واحد داده‌شده را به میلی‌متر (واحد داخلی) می‌برد.</summary>
    public static double ToInternalLength(double value, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => value,
        LengthUnit.Inch => value * MmPerInch,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>مقدار داخلی (میلی‌متر) را به واحد هدف نمایش می‌برد.</summary>
    public static double FromInternalLength(double valueMm, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => valueMm,
        LengthUnit.Inch => valueMm / MmPerInch,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>درجه به رادیان.</summary>
    public static double DegToRad(double deg) => deg * Math.PI / 180.0;

    /// <summary>رادیان به درجه.</summary>
    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>برچسب کوتاه واحد برای نمایش در UI.</summary>
    public static string Label(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => "mm",
        LengthUnit.Inch => "in",
        _ => "?",
    };
}
