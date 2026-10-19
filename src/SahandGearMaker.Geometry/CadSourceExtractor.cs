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

/// <summary>حلقه‌های نمای پلان (نقشهٔ فنی) — پروفیل تحلیلی بدون پیچش (نسخهٔ ۰٫۵).</summary>
public sealed class PlanViewLoops
{
    /// <summary>مرز بیرونی (دندانه‌دار) — CCW.</summary>
    public List<(double X, double Y)> Outer { get; } = new();

    /// <summary>حفره‌ها: بور، حفره‌های سبک‌سازی، مرز دندانهٔ داخلی.</summary>
    public List<List<(double X, double Y)>> Holes { get; } = new();

    /// <summary>شعاع بیرونی رینگ برای چرخ‌دندهٔ داخلی (۰ = داخلی نیست).</summary>
    public double InternalRimRadius { get; set; }
}

/// <summary>
/// استخراج حلقه‌های نمای پلان از پروفیل تحلیلی — دقیق و بدون قطع‌قطع شدن سطل‌بندی مش
/// (رفع «دندانه‌های نقشه تیکه‌تیکه» — ایراد ۱۳ کاربر، نسخهٔ ۰٫۵).
/// </summary>
public static class PlanViewExtractor
{
    /// <summary>حلقه‌های پلان برای خانواده‌های منشوری؛ null = از این مسیر نمی‌شود (از مش بخوانید).</summary>
    public static PlanViewLoops? TryExtract(GearPart part, GearBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(buildResult);
        if (part.Family is not (GearFamily.ExternalSpur or GearFamily.ExternalHelical or GearFamily.ExternalHerringbone
            or GearFamily.InternalSpur or GearFamily.InternalHelical or GearFamily.CrossedHelical
            or GearFamily.Cycloidal or GearFamily.SplineExternal or GearFamily.SplineInternal))
        {
            return null;
        }

        var src = CylindricalGearMeshBuilder.ExtractCadSource(part, buildResult);
        var loops = new PlanViewLoops();

        // مرز پروفیل برش اول: پیچش صفر → نمای واقعی دندانه (بدون اعوجاج مارپیچ)
        var ring = src.Rings[0];
        foreach (var p in ring.Points)
            loops.Outer.Add((p.X, p.Y));

        if (src.Internal)
        {
            loops.InternalRimRadius = src.RimOuterRadius;
            // مرز دندانه خودش حفرهٔ اصلی است (بیرونی = دایرهٔ رینگ)
            loops.Holes.Add(new List<(double, double)>(loops.Outer));
            loops.Outer.Clear();
            for (int i = 0; i < 64; i++)
            {
                double a = 2.0 * Math.PI * i / 64;
                loops.Outer.Add((src.RimOuterRadius * Math.Cos(a), src.RimOuterRadius * Math.Sin(a)));
            }
        }
        else if (src.BoreSection is { Count: >= 3 })
        {
            loops.Holes.Add(src.BoreSection.Select(p => (p.X, p.Y)).ToList());
        }
        else if (src.EffectiveBore == BoreShapeType.Round && src.BoreRadius > 1e-9)
        {
            var circle = new List<(double, double)>();
            for (int i = 0; i < 48; i++)
            {
                double a = 2.0 * Math.PI * i / 48;
                circle.Add((src.BoreRadius * Math.Cos(a), src.BoreRadius * Math.Sin(a)));
            }
            loops.Holes.Add(circle);
        }

        // حفره‌های سبک‌سازی (بدون چرخش — حالت پیش‌فرض)
        if (src.LighteningCenters.Count > 0 && src.LighteningSection.Count >= 3)
        {
            double rot = src.LighteningRotationRad;
            double cos = Math.Cos(rot), sin = Math.Sin(rot);
            foreach (var (cx, cy) in src.LighteningCenters)
            {
                loops.Holes.Add(src.LighteningSection
                    .Select(p => (cx + p.X * cos - p.Y * sin, cy + p.X * sin + p.Y * cos))
                    .ToList());
            }
        }

        return loops.Outer.Count >= 8 ? loops : null;
    }
}
