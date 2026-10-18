namespace SahandGearMaker.Domain;

/// <summary>نوع تصویر ذخیره‌شده (فاز ۱٫۱۷ — پرسپکتیو/ارتوگرافیک).</summary>
public enum ViewProjection
{
    Perspective,
    Orthographic,
}

/// <summary>
/// نمای نام‌دار دوربین ذخیره‌شده در پروژه (فاز ۱٫۱۶).
/// مستقل از کتابخانهٔ رندر تا ذخیره/بارگذاری پروژه وابسته به UI نماند.
/// </summary>
public sealed class SavedView
{
    public string Name { get; set; } = "نما";

    /// <summary>آزیموت (رادیان) — قرارداد موتور رندر.</summary>
    public double Azimuth { get; set; } = Math.PI / 6;

    /// <summary>ارتفاع (رادیان).</summary>
    public double Elevation { get; set; } = Math.PI / 5;

    /// <summary>فاصلهٔ دوربین (mm).</summary>
    public double Distance { get; set; } = 220;

    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }

    public ViewProjection Projection { get; set; } = ViewProjection.Perspective;
}
