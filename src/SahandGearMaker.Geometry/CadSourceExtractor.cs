using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>یک حلقهٔ پروفیل در ارتفاع مشخص با پیچش اعمال‌شده.</summary>
public sealed record CadProfileRing
{
    public double Z { get; init; }
    public double Twist { get; init; }

    /// <summary>نقاط مرز پروفیل در دستگاه دکارتی (پیچش اعمال‌شده) — CCW.</summary>
    public List<Vector3> Points { get; init; } = new();
}

/// <summary>
/// دادهٔ منبع برای ساخت جامد CAD منشوری از قطعهٔ استوانه‌ای — نسخهٔ ۰٫۵.
/// همهٔ حلقه‌ها/مقاطع دقیقاً هم‌ارز همان داده‌هایی است که سازندهٔ مش مصرف می‌کند.
/// </summary>
public sealed class PrismaticCadSource
{
    public GearFamily Family { get; init; }
    public GearGeometryFigures Figures { get; init; } = null!;

    /// <summary>برش‌های پروفیل از پایین به بالا (مارپیچ/جناغی چند برش دارد).</summary>
    public List<CadProfileRing> Rings { get; } = new();

    public double Z0 { get; init; }
    public double Z1 { get; init; }

    /// <summary>مقطع بور (CCW)؛ null = بدون بور. برای بور دایره‌ای، BoreRadius معتبر است.</summary>
    public List<Point2>? BoreSection { get; init; }
    public double BoreRadius { get; init; }
    public BoreShapeType EffectiveBore { get; init; }

    public bool Internal { get; init; }
    public double RimOuterRadius { get; init; }

    public double HubOuterRadius { get; init; }
    public double HubZ0 { get; init; }
    public double HubZ1 { get; init; }

    public List<(double Cx, double Cy)> LighteningCenters { get; init; } = new();
    public List<Point2> LighteningSection { get; init; } = new();
    public double LighteningRotationRad { get; init; }
    public bool LighteningFollowTwist { get; init; }

    /// <summary>گپ مرکزی جناغی: بازهٔ [GapZ0, GapZ1] با استوانهٔ ریشهٔ GapRadius (−۱ = بدون گپ).</summary>
    public double GapZ0 { get; init; } = -1;
    public double GapZ1 { get; init; } = -1;
    public double GapRadius { get; init; }

    public double RootRadiusUsed { get; init; }
}

/// <summary>
/// استخراج دادهٔ منبع CAD برای خانواده‌های منشوری (استوانه‌ای/شانه) — مسیر عمومی از طریق
/// موتور هندسی؛ برای بقیهٔ خانواده‌ها null برمی‌گردد (سازندهٔ CAD به BREP وجه‌ای از مش برمی‌گردد).
/// </summary>
public static class CadSourceExtractor
{
    /// <summary>آیا این خانواده منبع منشوری تحلیلی دارد؟</summary>
    public static bool IsPrismatic(GearFamily family)
        => family is GearFamily.ExternalSpur or GearFamily.ExternalHelical or GearFamily.ExternalHerringbone
            or GearFamily.InternalSpur or GearFamily.InternalHelical or GearFamily.CrossedHelical
            or GearFamily.Cycloidal or GearFamily.SplineExternal or GearFamily.SplineInternal
            or GearFamily.Rack or GearFamily.HelicalRack;

    /// <summary>استخراج منبع منشوری؛ null = این خانواده منشوری نیست.</summary>
    public static PrismaticCadSource? TryExtract(GearPart part, GearBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(result);
        if (!IsPrismatic(part.Family))
            return null;

        if (part.Family is GearFamily.Rack or GearFamily.HelicalRack)
            return null; // شانه مسیر جدا دارد (در CadPartBuilder مستقیم ساخته می‌شود)

        return CylindricalGearMeshBuilder.ExtractCadSource(part, result);
    }
}
