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
        return Build(part.Family, part.Teeth, part.Body, part.GeometrySettings, part.Specific);
    }

    /// <summary>ساخت هندسه با پارامترهای صریح (بدون پارامترهای اختصاصی خانواده).</summary>
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality)
        => Build(family, teeth, body, quality, new FamilyParameters());

    /// <summary>ساخت هندسه با پارامترهای اختصاصی خانواده (مخروطی، کرم، هیپوئید، …).</summary>
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality,
        FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(specific);

        if (!IsSupported(family))
        {
            var info = CapabilityCatalog.GearFamilies.First(f => f.Key == family.ToString());
            throw new GeometryBuildException(
                $"خانوادهٔ «{info.PersianTitle}» در این نسخه پیاده‌سازی نشده است ({info.Note}). " +
                "در ماتریس قابلیت برنامه، وضعیت آن «نقشهٔ راه» است و مدل تقریبی/جعلی ساخته نمی‌شود.",
                new[] { new GearDiagnostic("SGM-E000", Severity.Error,
                    $"خانوادهٔ {family} پشتیبانی نمی‌شود.", "Family") });
        }

        var result = family switch
        {
            GearFamily.Rack or GearFamily.HelicalRack => RackMeshBuilder.Build(family, teeth, body, quality, specific),
            GearFamily.BevelSpur or GearFamily.BevelSpiral or GearFamily.Hypoid
                => BevelMeshBuilder.Build(family, teeth, body, quality, specific),
            GearFamily.Worm => WormMeshBuilder.BuildWorm(teeth, body, quality, specific),
            GearFamily.WormWheel => WormMeshBuilder.BuildWheel(teeth, body, quality, specific),
            GearFamily.CrownFace => FaceGearMeshBuilder.Build(teeth, body, quality, specific),
            GearFamily.NonCircular => NonCircularMeshBuilder.BuildGear(teeth, body, quality, specific),
            GearFamily.HarmonicDrive => FlexSplineMeshBuilder.Build(teeth, body, quality, specific),
            GearFamily.SplineExternal => BuildSpline(family, teeth, body, quality, specific, internalSpline: false),
            GearFamily.SplineInternal => BuildSpline(family, teeth, body, quality, specific, internalSpline: true),
            GearFamily.Cycloidal => BuildCycloidal(family, teeth, body, quality),
            GearFamily.CrossedHelical => CylindricalGearMeshBuilder.Build(GearFamily.ExternalHelical, teeth, body, quality),
            _ => CylindricalGearMeshBuilder.Build(family, teeth, body, quality),
        };

        // تضمین جهت‌گیری یکنواخت نرمال‌ها (نسخهٔ ۰٫۵ — رفع مش ناقص/راه‌راه در مخروطی/شانه/تاجی/هارمونیک).
        // سازنده‌های قدیمی گاهی مثلث‌هایی را با چرخش معکوس منتشر می‌کردند؛ این passo همهٔ خروجی‌ها را
        // به‌صورت سازگار و رو به بیرون درمی‌آورد تا backface-culling رندرر و خروجی STL سالم باشند.
        result.Mesh.NormalizeOrientation();
        return result;
    }

    /// <summary>
    /// هزارخار اینولوت (DIN-گونه): دندانهٔ اینولوت با عمق کوتاه (ha≈0.5m، hf≈0.6m برای تخت‌ریشه)
    /// و لقی جانبی اختصاصی؛ هندسه از همان سازندهٔ استوانه‌ای با ضرایب بازتنظیم‌شده ساخته می‌شود.
    /// </summary>
    private static GearBuildResult BuildSpline(
        GearFamily family, ToothParameters teeth, BodyFeatures body, GeometrySettings quality,
        FamilyParameters specific, bool internalSpline)
    {
        var t = CloneTeeth(teeth);
        if (Math.Abs(t.AddendumCoefficient - 1.0) < 1e-12)
            t.AddendumCoefficient = 0.5;                       // عمق کوتاه هزارخار
        if (Math.Abs(t.DedendumCoefficient - 1.25) < 1e-12)
            t.DedendumCoefficient = internalSpline ? 0.9 : 0.7; // ریشهٔ تخت‌تر
        t.BacklashAllowance += Math.Max(0, specific.SplineBacklashMm);
        t.HelixAngle = 0;

        var target = internalSpline ? GearFamily.InternalSpur : GearFamily.ExternalSpur;
        var result = CylindricalGearMeshBuilder.Build(target, t, body, quality);

        // افزودن پیام‌های اختصاصی هزارخار به تشخیص‌ها
        var diags = result.Diagnostics.ToList();
        diags.Add(new GearDiagnostic("SGM-I520", Severity.Info,
            $"اتصال هزارخار اینولوت با {t.ToothCount} خار؛ برازش: {(specific.SplineFit == SplineFitType.PitchDiameter ? "سطح گام (side fit)" : "قطر پایه (major fit)")}" +
            (string.IsNullOrWhiteSpace(specific.SplineStandardNote) ? "" : $"؛ پایهٔ نمایشی: {specific.SplineStandardNote}"), "SplineFit"));
        diags.Add(new GearDiagnostic("SGM-I521", Severity.Unverified,
            "تلرانس‌های واقعی برازش نیازمند جداول استاندارد/کرنل مهندسی جداگانه است؛ ابعاد نمایشی «تعیین‌نشده» تلقی شوند."));

        return new GearBuildResult
        {
            Mesh = result.Mesh,
            Figures = result.Figures,
            Diagnostics = diags,
            AxialSlicesUsed = result.AxialSlicesUsed,
            EffectiveChordToleranceMm = result.EffectiveChordToleranceMm,
        };
    }

    /// <summary>چرخ‌دندهٔ سیکلوئیدی: پروفیل اپیسایکلوئید (سر) + هیپوسایکلوئید (ریشه) روی سازندهٔ استوانه‌ای.</summary>
    private static GearBuildResult BuildCycloidal(GearFamily family, ToothParameters teeth, BodyFeatures body, GeometrySettings quality)
    {
        var t = CloneTeeth(teeth);
        t.HelixAngle = 0; // سیکلوئید متداول مستقیم است؛ پیچش با فرمول‌های عرضی این خانواده اعتبارسنجی نشده است.
        return CylindricalGearMeshBuilder.Build(GearFamily.ExternalSpur, t, body, quality, cycloidal: true);
    }

    internal static ToothParameters CloneTeeth(ToothParameters t) => new()
    {
        ToothCount = t.ToothCount,
        Module = t.Module,
        PressureAngle = t.PressureAngle,
        HelixAngle = t.HelixAngle,
        FaceWidth = t.FaceWidth,
        ProfileShiftCoefficient = t.ProfileShiftCoefficient,
        BacklashAllowance = t.BacklashAllowance,
        AddendumCoefficient = t.AddendumCoefficient,
        DedendumCoefficient = t.DedendumCoefficient,
        RootFilletCoefficient = t.RootFilletCoefficient,
        ProfileSystem = t.ProfileSystem,
    };
}
