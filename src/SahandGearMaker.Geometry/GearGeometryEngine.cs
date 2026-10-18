using SahandGearMaker.Domain;

namespace SahandGearMaker.Geometry;

/// <summary>
/// نقطهٔ ورود واحد ساخت هندسه — انتخاب سازندهٔ مناسب خانواده و اعلام صادقانهٔ پشتیبانی.
/// انواع «نقشهٔ راه» مدل جعلی نمی‌سازند؛ خطای روشن می‌دهند (الزام سند ۰۳، قانون گزارش قابلیت).
/// </summary>
public static class GearGeometryEngine
{
    /// <summary>آیا این خانواده در نسخهٔ فعلی هندسهٔ واقعی می‌سازد؟</summary>
    public static bool IsSupported(GearFamily family) => CapabilityCatalog.BuildsGeometry(family);

    /// <summary>ساخت هندسهٔ کامل یک قطعه؛ برای انواع پشتیبانی‌نشده خطای راهنما می‌دهد.</summary>
    public static GearBuildResult BuildPart(GearPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Build(part.Family, part.Teeth, part.Body, part.GeometrySettings);
    }

    /// <summary>ساخت هندسه با پارامترهای صریح.</summary>
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality)
    {
        if (!IsSupported(family))
        {
            var info = CapabilityCatalog.GearFamilies.First(f => f.Key == family.ToString());
            throw new GeometryBuildException(
                $"خانوادهٔ «{info.PersianTitle}» در این نسخه پیاده‌سازی نشده است ({info.Note}). " +
                "در ماتریس قابلیت برنامه، وضعیت آن «نقشهٔ راه» است و مدل تقریبی/جعلی ساخته نمی‌شود.",
                new[] { new GearDiagnostic("SGM-E000", Severity.Error,
                    $"خانوادهٔ {family} پشتیبانی نمی‌شود.", "Family") });
        }

        return family switch
        {
            GearFamily.Rack => RackMeshBuilder.Build(family, teeth, body, quality),
            _ => CylindricalGearMeshBuilder.Build(family, teeth, body, quality),
        };
    }
}
