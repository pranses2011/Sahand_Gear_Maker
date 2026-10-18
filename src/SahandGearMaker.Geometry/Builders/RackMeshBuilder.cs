using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش شانهٔ مستقیم/مارپیچ — بلوک منشوری با مرز دندانه‌دار (سند ۰۳، خانوادهٔ رک).
/// مختصات: طول در X، ارتفاع دندانه در +Y، پهنا در Z؛ خط گام روی y=0.
///
/// توپولوژی آب‌بند:
///   - برش = چندضلعی بستهٔ سادهٔ CCW با n رأس (بدون نقطهٔ تکراری)؛
///   - دیوارهٔ جانبی بین برش‌های متوالی روی همهٔ n یال مرزی؛
///   - درپوش جلو/عقب = بادبزک از گوشهٔ پایین-راست به همهٔ رأس‌های دیگر (n مثلث).
/// هر یال مرزی دقیقاً یک‌بار در دیواره و یک‌بار در درپوش مصرف می‌شود.
/// </summary>
public static class RackMeshBuilder
{
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures bodyParams,
        GeometrySettings quality)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        quality ??= new GeometrySettings();

        var diags = GearValidator.Validate(family, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای شانه نامعتبرند؛ هندسه ساخته نشد.", errors);

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);
        var fig = GearFiguresCalculator.Compute(family, teeth);

        double pitch = Math.PI * teeth.Module;

        // طول شانه: پارامتر مستقل (RackLengthMm)؛ برای سازگاری با پروژه‌های قدیمی،
        // مقدار «شعاع دایرهٔ حفره‌ها» به‌عنوان طول قدیمی خوانده می‌شود (با پیام اطلاع).
        double requestedLength;
        if (bodyParams.RackLengthMm > 0)
        {
            requestedLength = bodyParams.RackLengthMm;
        }
        else if (bodyParams.LighteningHoleCircleRadius > 0)
        {
            requestedLength = bodyParams.LighteningHoleCircleRadius;
            diags.Add(new GearDiagnostic("SGM-I406", Severity.Info,
                $"طول شانه از فیلد قدیمی «شعاع دایرهٔ حفره‌ها» ({requestedLength:F1} mm) خوانده شد؛ برای پروژه‌های جدید فیلد «طول شانه» را تنظیم کنید.", "RackLengthMm"));
        }
        else
        {
            requestedLength = 10.0 * pitch;
        }

        int toothCount = Math.Max(1, (int)Math.Floor(requestedLength / pitch));
        double length = (toothCount + 0.5) * pitch;

        // ---- برش بستهٔ CCW (بدون نقطهٔ تکراری) ----
        var profile = new RackProfile(teeth, length);
        var rawSection = profile.ClosedSection(); // نقطهٔ اول در انتها تکرار شده
        var section = new List<(double X, double Y)>(rawSection.Count);
        foreach (var p in rawSection)
        {
            bool dup = false;
            foreach (var q in section)
                if (Math.Abs(p.X - q.X) < 1e-7 && Math.Abs(p.Y - q.Y) < 1e-7)
                    dup = true;
            if (!dup)
                section.Add(p);
        }
        int n = section.Count; // بدون نقطهٔ بستن
        if (n < 3)
            throw new GeometryBuildException("برش شانه رأس کافی ندارد.", errors);

        var mesh = new TriMesh();
        double width = Math.Max(teeth.FaceWidth, 1e-6);
        bool helical = Math.Abs(teeth.HelixAngle) > 1e-9;
        int slices = helical
            ? Math.Clamp(quality.AxialSliceCount, 4, 400)
            : 2;
        double shiftFactor = Math.Tan(teeth.HelixAngle); // شیب نسبی X به Z

        // ---- حلقه‌های برش ----
        var loops = new List<int>[slices + 1];
        for (int s = 0; s <= slices; s++)
        {
            double z = width * s / slices;
            double dx = helical ? shiftFactor * z : 0.0;
            loops[s] = new List<int>(n);
            foreach (var (px, py) in section)
                loops[s].Add(mesh.AddVertex(new Vector3(px + dx, py, z)));
        }

        // ---- دیوارهٔ جانبی بین برش‌های متوالی روی همهٔ یال‌های مرزی ----
        for (int s = 0; s < slices; s++)
        {
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mesh.AddTriangle(loops[s][i], loops[s + 1][i], loops[s + 1][j]);
                mesh.AddTriangle(loops[s][i], loops[s + 1][j], loops[s][j]);
            }
        }

        // ---- درپوش‌ها: بادبزک از گوشهٔ پایین-راست (رأس ۰) به همهٔ رأس‌های دیگر ----
        FanCap(mesh, loops[0], flip: true);
        FanCap(mesh, loops[slices], flip: false);

        diags.Add(new GearDiagnostic(DiagnosticCodes.PreviewMeshCoarse, Severity.Info,
            $"شانه با {toothCount} دندانه و طول مؤثر {length:F2} میلی‌متر ساخته شد."));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags,
            AxialSlicesUsed = slices + 1,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>
    /// بادبزک از رأس ۰ (گوشهٔ پایین-راست در برش CCW) به رأس‌های 1..n−1؛
    /// هر یال مرزی و هر قطر دقیقاً یک‌بار مصرف می‌شود.
    /// </summary>
    private static void FanCap(TriMesh mesh, List<int> loop, bool flip)
    {
        int n = loop.Count;
        int o = loop[0];
        for (int i = 1; i < n; i++)
        {
            int a = loop[i];
            int b = loop[(i + 1) % n];
            if (flip)
                mesh.AddTriangle(b, a, o);
            else
                mesh.AddTriangle(o, a, b);
        }
    }
}
