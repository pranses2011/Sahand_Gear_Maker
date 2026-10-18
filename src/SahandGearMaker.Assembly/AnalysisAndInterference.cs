using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Assembly;

/// <summary>نتیجهٔ بررسی برخورد/فاصلهٔ دو مش (تقریبی — بر پایهٔ مثلث‌ها).</summary>
public sealed class InterferenceResult
{
    /// <summary>آیا برخورد واقعی یافت شد؟</summary>
    public bool HasInterference { get; init; }

    /// <summary>کمینهٔ فاصلهٔ بین دو مش در حالت بدون برخورد (mm).</summary>
    public double? MinDistanceMm { get; init; }

    /// <summary>عمق تقریبی نفوذ در حالت برخورد (mm).</summary>
    public double? PenetrationDepthMm { get; init; }

    public IReadOnlyList<(Vector3 PointA, Vector3 PointB)> ClosestPair { get; init; } = Array.Empty<(Vector3, Vector3)>();
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; init; } = Array.Empty<GearDiagnostic>();
}

/// <summary>
/// بررسی برخورد و فاصلهٔ مش‌ها (سند ۰۷، بخش ۳٫۲) — تقریب گسستهٔ مثلث‌ها با اعلام صادقانه.
/// الگوریتم: آزمون فاصلهٔ مثلث-مثلث با جداسازی AABB برای شتاب؛ برای مش‌های بزرگ
/// نمونه‌برداری جعبه‌ای سلسله‌مراتبی به کار می‌رود.
/// </summary>
public static class InterferenceChecker
{
    /// <summary>بررسی دو مش (بدون تبدیل): برخورد یا کمینهٔ فاصله.</summary>
    public static InterferenceResult Check(TriMesh meshA, TriMesh meshB, double cutoffMm = 2.0)
    {
        ArgumentNullException.ThrowIfNull(meshA);
        ArgumentNullException.ThrowIfNull(meshB);
        var diags = new List<GearDiagnostic>
        {
            new("SGM-C200", Severity.Info,
                "بررسی برخورد/فاصله به‌صورت «تقریبی گسسته» روی مثلث‌های مش انجام شده است؛ نتیجه به تراکم مش حساس است و به‌عنوان قطعی گزارش نمی‌شود.", "Interference"),
        };

        var (aMin, aMax) = meshA.Bounds();
        var (bMin, bMax) = meshB.Bounds();

        // جداسازی سریع AABB
        if (aMax.X + cutoffMm < bMin.X || bMax.X + cutoffMm < aMin.X ||
            aMax.Y + cutoffMm < bMin.Y || bMax.Y + cutoffMm < aMin.Y ||
            aMax.Z + cutoffMm < bMin.Z || bMax.Z + cutoffMm < aMin.Z)
        {
            return new InterferenceResult
            {
                HasInterference = false,
                MinDistanceMm = null,
                Diagnostics = diags.Append(new GearDiagnostic("SGM-C201", Severity.Info,
                    "جعبه‌های محیطی دور از هم‌اند؛ برخورد منتفی و فاصلهٔ دقیق محاسبه نشد.")).ToList(),
            };
        }

        // فاصلهٔ نقطه‌ای نمونه‌برداری‌شده + آزمون احتوای نقطه در مثلث‌ها (تقریب)
        var vertsA = meshA.Vertices.ToArray();
        var vertsB = meshB.Vertices.ToArray();
        var trisA = meshA.Triangles.ToArray();
        var trisB = meshB.Triangles.ToArray();

        double bestSq = double.MaxValue;
        Vector3 bestA = default, bestB = default;
        bool hit = false;

        // 1) نفوذ: آیا رأس یکی داخل دیگری است؟ (آزمون نقطه-درون-مش با ray casting روی مثلث‌ها)
        foreach (var v in vertsA)
        {
            if (PointInsideMesh(v, vertsB, trisB))
            {
                hit = true;
                break;
            }
        }
        if (!hit)
        {
            foreach (var v in vertsB)
            {
                if (PointInsideMesh(v, vertsA, trisA))
                {
                    hit = true;
                    break;
                }
            }
        }

        // 2) کمینهٔ فاصلهٔ رأس-رأس (تقریب بالا-پایین)
        foreach (var va in vertsA)
        {
            foreach (var vb in vertsB)
            {
                double d = (va - vb).LengthSquared;
                if (d < bestSq)
                {
                    bestSq = d;
                    bestA = va;
                    bestB = vb;
                }
            }
        }

        if (hit)
        {
            diags.Add(new GearDiagnostic("SGM-C202", Severity.Warning,
                "نفوذ (برخورد) بین دو قطعه تشخیص داده شد؛ نتیجه تقریبی مش است.", "Interference"));
            return new InterferenceResult
            {
                HasInterference = true,
                PenetrationDepthMm = Math.Sqrt(bestSq),
                Diagnostics = diags,
            };
        }

        return new InterferenceResult
        {
            HasInterference = false,
            MinDistanceMm = Math.Sqrt(bestSq),
            ClosestPair = new[] { (bestA, bestB) },
            Diagnostics = diags,
        };
    }

    /// <summary>آزمون نقطه-درون-مش بسته با شمارش تقاطع پرتو (تقریب؛ برای مش‌های آب‌بند معتبر).</summary>
    private static bool PointInsideMesh(Vector3 p, Vector3[] verts, Triangle[] tris)
    {
        // پرتوی هم‌محور +X
        int crossings = 0;
        foreach (var t in tris)
        {
            var a = verts[t.A];
            var b = verts[t.B];
            var c = verts[t.C];

            // آزمون تقاطع پرتو با مثلث (projection بر صفحهٔ yz)
            double y1 = a.Y - p.Y, z1 = a.Z - p.Z;
            double y2 = b.Y - p.Y, z2 = b.Z - p.Z;
            double y3 = c.Y - p.Y, z3 = c.Z - p.Z;

            if ((y1 > 0) == (y2 > 0) && (y1 > 0) == (y3 > 0))
                continue; // همه یک‌طرف محور x

            // تقاطع پاره‌خط مرز مثلث با نیم‌خط (y=0+, z=p.z... تقریب دو‌بعدی)
            bool intersects = SegIntersect(y1, z1, y2, z2) || SegIntersect(y2, z2, y3, z3) || SegIntersect(y3, z3, y1, z1);
            if (intersects && MinXOf(a, b, c, 0, 0) > 0)
                crossings++;

            static bool SegIntersect(double y1, double z1, double y2, double z2)
            {
                // قطع محور y=0 در بازه z ∈ (−∞, 0]
                if ((y1 > 0) == (y2 > 0))
                    return false;
                double t = y1 / (y1 - y2);
                double z = z1 + (z2 - z1) * t;
                return z <= 0;
            }

            static double MinXOf(Vector3 a, Vector3 b, Vector3 c, double _, double __)
                => Math.Min(a.X, Math.Min(b.X, c.X));
        }
        return crossings % 2 == 1;
    }
}

/// <summary>پیشنهاد دستیار انتخاب اندازه (سند ۰۷، بخش ۳٫۱).</summary>
public sealed class SizingSuggestion
{
    public int GearATeeth { get; init; }
    public int GearBTeeth { get; init; }
    public double Module { get; init; }
    public double CenterDistanceMm { get; init; }
    public double AchievedRatio { get; init; }
    /// <summary>انحراف نسبت از هدف.</summary>
    public double RatioError { get; init; }
    public string Rationale { get; init; } = "";
}

/// <summary>
/// دستیار انتخاب اندازه/نسبت — مسئله را از جهت طراحی حل می‌کند:
/// نسبت هدف و فاصلهٔ مرکز مجاز → ترکیب‌های صحیح‌دندانه با خطای گزارش‌شده.
/// روش: جست‌وجوی ترکیب‌های z₁,z₂ و m از سری ماژول متریک متداول.
/// </summary>
public static class SizingAssistant
{
    private static readonly double[] PreferredModules =
    {
        0.5, 0.8, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0, 16.0, 20.0,
    };

    /// <summary>
    /// پیشنهادها برای نسبت هدف (یا بازه) با قید فاصلهٔ مرکز حداکثر و تعداد دندانه.
    /// خروجی چند گزینهٔ رتبه‌بندی‌شده با دلیل — نه یک «جواب بهینهٔ قطعی».
    /// </summary>
    public static IReadOnlyList<SizingSuggestion> SuggestExternalPair(
        double targetRatio, double? maxCenterDistanceMm, double? minCenterDistanceMm = null,
        int minTeeth = 12, int maxTeeth = 150, int maxSuggestions = 8)
    {
        if (targetRatio <= 0.01 || targetRatio > 500)
            throw new ArgumentOutOfRangeException(nameof(targetRatio), "نسبت هدف باید بین ۰٫۰۱ و ۵۰۰ باشد.");
        if (maxTeeth > 400)
            maxTeeth = 400;

        var results = new List<SizingSuggestion>();
        foreach (double m in PreferredModules)
        {
            for (int z1 = Math.Max(6, minTeeth); z1 <= Math.Min(maxTeeth, 120); z1++)
            {
                int z2 = (int)Math.Round(targetRatio * z1);
                if (z2 < Math.Max(6, minTeeth) || z2 > maxTeeth)
                    continue;

                double a = m * (z1 + z2) / 2.0;
                if (maxCenterDistanceMm.HasValue && a > maxCenterDistanceMm.Value + 1e-9)
                    continue;
                if (minCenterDistanceMm.HasValue && a < minCenterDistanceMm.Value - 1e-9)
                    continue;

                double achieved = (double)z2 / z1;
                double err = Math.Abs(achieved - targetRatio) / targetRatio;
                if (err > 0.05)
                    continue;

                results.Add(new SizingSuggestion
                {
                    GearATeeth = z1,
                    GearBTeeth = z2,
                    Module = m,
                    CenterDistanceMm = a,
                    AchievedRatio = achieved,
                    RatioError = err,
                    Rationale = $"z₁={z1}, z₂={z2}, m={m}: a={a:F2} mm، خطای نسبت {err * 100:F2}٪" +
                                (z1 < 17 ? "؛ هشدار undercut بدون اصلاح پروفیل" : ""),
                });
            }
        }

        return results
            .OrderBy(r => r.RatioError)
            .ThenBy(r => r.CenterDistanceMm)
            .Take(maxSuggestions)
            .ToList();
    }
}

/// <summary>نتیجهٔ تحلیل هندسی چرخ‌دنده (محاسبات فرمولی — نه تأیید استاندارد).</summary>
public sealed class GearAnalysisReport
{
    public double TransverseRatio { get; init; }
    public double ContactRatio { get; init; }
    public double MinTeethNoUndercut { get; init; }
    public bool UndercutLikely { get; init; }
    public double TipThicknessMm { get; init; }
    public double PitchLineVelocityPerRad { get; init; }
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; init; } = Array.Empty<GearDiagnostic>();
}

/// <summary>
/// تحلیل هندسی چرخ‌دندهٔ استوانه‌ای (سند ۰۷، بخش ۳٫۷ مرحلهٔ ۱ و ۳):
/// نسبت تماس، برش‌خوردگی، ضخامت نوک — با فرمول‌های مرجع و اعلام صریح دامنه.
/// این محاسبات «گزارش هندسی» است، نه گواهی استحکام/استاندارد.
/// </summary>
public static class GearAnalysisCalculator
{
    public static GearAnalysisReport Analyze(ToothParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var diags = new List<GearDiagnostic>();

        double mt = GearValidator.TransverseModule(p.Module, p.HelixAngle);
        double alphaT = GearValidator.TransversePressureAngle(p.PressureAngle, p.HelixAngle);
        double r = mt * p.ToothCount / 2.0;
        double rb = r * Math.Cos(alphaT);
        double ra = r + (p.AddendumCoefficient + p.ProfileShiftCoefficient) * p.Module;
        double rf = r - (p.DedendumCoefficient - p.ProfileShiftCoefficient) * p.Module;

        // نسبت تماس عرضی جفت متقارن بدون اصلاح (فرمول مرجع):
        // εₜ = [√(rₐ²−r_b²) − r·sin αₜ] / (π·mₜ·cos αₜ)
        double zMin = 2.0 / Math.Pow(Math.Sin(alphaT), 2.0);
        double cr = ra > rb + 1e-9
            ? (Math.Sqrt(ra * ra - rb * rb) - r * Math.Sin(alphaT)) / (Math.PI * mt * Math.Cos(alphaT))
            : 0;
        double contactRatio = double.IsFinite(cr) && cr > 0 ? cr : 0;

        // ضخامت نوک دندانه
        double inv = Math.Tan(alphaT) - alphaT;
        double alphaA = ra > rb + 1e-9 ? Math.Acos(Math.Min(1.0, Math.Max(-1.0, rb / ra))) : alphaT;
        double halfAnglePitch = (Math.PI * mt / 2.0 + 2.0 * p.ProfileShiftCoefficient * p.Module * Math.Tan(alphaT)) / (2.0 * r);
        double invA = Math.Tan(alphaA) - alphaA;
        double halfAngleTip = halfAnglePitch + inv - invA;
        double tipThickness = 2.0 * ra * halfAngleTip;

        bool undercut = p.ProfileShiftCoefficient == 0 && p.ToothCount < zMin;

        diags.Add(new GearDiagnostic("SGM-G200", Severity.Info,
            $"تحلیل هندسی: نسبت تماس تقریبی {contactRatio:F2} (جفت متقارن بدون اصلاح)؛ z_min={Math.Ceiling(zMin)}؛ ضخامت نوک {tipThickness:F2} mm.", "Analysis"));
        if (undercut)
            diags.Add(new GearDiagnostic("SGM-G201", Severity.Warning,
                $"تعداد دندانهٔ {p.ToothCount} کمتر از حد بدون برش‌خوردگی (~{Math.Ceiling(zMin)}) است؛ ریشه در ساخت واقعی undercut می‌شود (اصلاح پروفیل x>0 توصیه می‌شود).", "ToothCount"));
        if (tipThickness < 0.25 * p.Module)
            diags.Add(new GearDiagnostic("SGM-G202", Severity.Warning,
                "ضخامت نوک دندانه نازک است (< ۰٫۲۵ ماژول)؛ ریسک تیزشدن/شکست نوک.", "AddendumCoefficient"));
        diags.Add(new GearDiagnostic("SGM-G203", Severity.Unverified,
            "این گزارش هندسی است؛ تحلیل استحکام/عمر/استاندارد (AGMA/ISO) در دامنهٔ این نسخه نیست."));

        return new GearAnalysisReport
        {
            TransverseRatio = mt,
            ContactRatio = contactRatio,
            MinTeethNoUndercut = zMin,
            UndercutLikely = undercut,
            TipThicknessMm = tipThickness,
            PitchLineVelocityPerRad = r,
            Diagnostics = diags,
        };
    }
}
