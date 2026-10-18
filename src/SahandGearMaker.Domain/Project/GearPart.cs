using System.Text.Json.Serialization;

namespace SahandGearMaker.Domain;

/// <summary>
/// یک قطعهٔ چرخ‌دنده در پروژه (سند ۰۳، بخش ۷ — مدل داده).
/// دادهٔ پارامتریک مرجع است؛ مش/هندسهٔ موقت جدا ساخته و هرگز جایگزین این داده نمی‌شود.
/// </summary>
public sealed class GearPart
{
    /// <summary>شناسهٔ پایدار قطعه در پروژه؛ تغییر نام، عضویت مونتاژ را خراب نمی‌کند.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>نام قابل‌نمایش فارسی برای درخت مونتاژ (مستقل از نام فایل).</summary>
    public string Name { get; set; } = "چرخ‌دندهٔ جدید";

    public GearFamily Family { get; set; } = GearFamily.ExternalSpur;

    public ToothParameters Teeth { get; set; } = new();

    public BodyFeatures Body { get; set; } = new();

    /// <summary>محل قرارگیری در مونتاژ: x, y بر حسب میلی‌متر و زاویهٔ چرخش اولیهٔ theta بر حسب رادیان حول محور Z.</summary>
    public Placement Placement { get; set; } = new();

    /// <summary>رنگ نمایشی RGB (هگز) — فقط نمایش؛ خواص ساخت/استحکام را تضمین نمی‌کند.</summary>
    public string DisplayColor { get; set; } = "#2563EB";

    /// <summary>هشدارها/خطاهای ثبت‌شدهٔ آخرین ساخت هندسه (برای گزارش و نمایش وضعیت).</summary>
    public List<GearDiagnostic> Validation { get; set; } = new();

    /// <summary>تنظیمات هندسی موقت (تلرانس وتر پیش‌نمایش و…).</summary>
    public GeometrySettings GeometrySettings { get; set; } = new();

    /// <summary>نسخهٔ طرح این قطعه برای شناسایی تغییرات.</summary>
    public long Revision { get; set; }

    public GearPart Clone(string? newName = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = newName ?? Name,
        Family = Family,
        Teeth = new ToothParameters
        {
            ToothCount = Teeth.ToothCount,
            Module = Teeth.Module,
            PressureAngle = Teeth.PressureAngle,
            HelixAngle = Teeth.HelixAngle,
            FaceWidth = Teeth.FaceWidth,
            ProfileShiftCoefficient = Teeth.ProfileShiftCoefficient,
            BacklashAllowance = Teeth.BacklashAllowance,
            AddendumCoefficient = Teeth.AddendumCoefficient,
            DedendumCoefficient = Teeth.DedendumCoefficient,
            RootFilletCoefficient = Teeth.RootFilletCoefficient,
        },
        Body = new BodyFeatures
        {
            BoreDiameter = Body.BoreDiameter,
            LighteningHoleCount = Body.LighteningHoleCount,
            LighteningHoleDiameter = Body.LighteningHoleDiameter,
            LighteningHoleCircleRadius = Body.LighteningHoleCircleRadius,
            HubDiameter = Body.HubDiameter,
            HubLength = Body.HubLength,
            KeywayWidth = Body.KeywayWidth,
            KeywayDepth = Body.KeywayDepth,
        },
        Placement = new Placement { X = Placement.X, Y = Placement.Y, Theta = Placement.Theta },
        DisplayColor = DisplayColor,
        GeometrySettings = new GeometrySettings { ProfileToleranceMm = GeometrySettings.ProfileToleranceMm },
    };
}

/// <summary>قرارگیری قطعه در صفحهٔ مونتاژ (XY)، محور چرخش Z.</summary>
public sealed class Placement
{
    /// <summary>مختصات X مرکز (mm).</summary>
    public double X { get; set; }

    /// <summary>مختصات Y مرکز (mm).</summary>
    public double Y { get; set; }

    /// <summary>زاویهٔ چرخش اولیهٔ دوران حول محور Z (رادیان).</summary>
    public double Theta { get; set; }
}

/// <summary>تنظیم کیفیت هندسی/مش — تلرانس وتر به‌جای تعداد نقطهٔ ثابت (سند ۰۴، بخش ۵).</summary>
public sealed class GeometrySettings
{
    /// <summary>حداکثر خطای وتر برای نمونه‌برداری منحنی اینولوت (mm).</summary>
    public double ProfileToleranceMm { get; set; } = 0.02;

    /// <summary>تعداد برش در طول پهنای دندانه برای بدنهٔ مارپیچ/جناغی.</summary>
    public int AxialSliceCount { get; set; } = 24;
}
