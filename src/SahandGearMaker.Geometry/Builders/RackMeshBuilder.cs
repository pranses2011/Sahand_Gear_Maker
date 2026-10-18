using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش شانهٔ مستقیم/مارپیچ — بلوک با دندانه‌های ذوزنقه‌ای (سند ۰۳، خانوادهٔ رک).
/// مختصات: طول در X، ارتفاع دندانه در +Y، پهنا در Z؛ خط گام روی y=0.
/// مرز دندانه‌دار رویهٔ بالا، «خط آفست» هم‌شکل در عمق بدنه، و مستطیل بیرونی؛
/// نوار دندانه‌آفست + مستطیل ⇒ سطح بستهٔ آب‌بند.
/// شانهٔ مارپیچ با جابه‌جایی تدریجی برش‌ها در X ساخته می‌شود.
/// </summary>
public static class RackMeshBuilder
{
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures qualityOnly,
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
        // طول شانه از فیلد شعاع دایرهٔ حفره‌ها (در شانه به‌عنوان «طول» تفسیر می‌شود) یا پیش‌فرض
        double requestedLength = qualityOnly.LighteningHoleCircleRadius > 0
            ? qualityOnly.LighteningHoleCircleRadius
            : 10.0 * pitch;
        int toothCount = Math.Max(1, (int)Math.Floor(requestedLength / pitch));
        double length = (toothCount + 0.5) * pitch;

        var profile = new RackProfile(teeth, length);
        var toothed = profile.BoundaryXY(); // فقط بخش دندانه‌دار بالای خط گام، CCW

        double rootY = -teeth.DedendumCoefficient * teeth.Module; // خط ریشه
        double offset = rootY - Math.Max(0.5 * teeth.Module, 0.5); // خط آفست داخل بدنه
        double bottom = rootY - 2.0 * teeth.DedendumCoefficient * teeth.Module;
        double half = length / 2.0;

        var mesh = new TriMesh();
        double width = Math.Max(teeth.FaceWidth, 1e-6);
        bool helical = Math.Abs(teeth.HelixAngle) > 1e-9;
        int slices = helical ? Math.Clamp(quality.AxialSliceCount, 4, 400) : 2;
        double shiftFactor = Math.Tan(teeth.HelixAngle); // شیب نسبی X به Z

        double t0 = toothed[0].X;
        double t1 = toothed[^1].X;

        var toothLoops = new List<int>[slices + 1];
        var offsetLoops = new List<int>[slices + 1];
        var rectLoops = new List<int>[slices + 1];

        for (int s = 0; s <= slices; s++)
        {
            double z = width * s / slices;
            double dx = helical ? shiftFactor * z : 0.0;

            toothLoops[s] = toothed.Select(p => mesh.AddVertex(new Vector3(p.X + dx, p.Y, z))).ToList();
            offsetLoops[s] = toothed.Select(p => mesh.AddVertex(new Vector3(p.X + dx, offset, z))).ToList();
            rectLoops[s] = new List<int>
            {
                mesh.AddVertex(new Vector3(t1 + dx, offset, z)), // راست-بالا (شروع CCW از راست)
                mesh.AddVertex(new Vector3(half + dx, offset, z)),
                mesh.AddVertex(new Vector3(half + dx, bottom, z)),
                mesh.AddVertex(new Vector3(-half + dx, bottom, z)),
                mesh.AddVertex(new Vector3(-half + dx, offset, z)),
                mesh.AddVertex(new Vector3(t0 + dx, offset, z)), // چپ-بالا
            };
        }

        for (int s = 0; s < slices; s++)
        {
            VertexPool.WallCCW(mesh, toothLoops[s], toothLoops[s + 1]);   // رویهٔ دندانه‌دار
            VertexPool.WallCCW(mesh, offsetLoops[s], offsetLoops[s + 1]); // خط آفست
            VertexPool.WallCCW(mesh, rectLoops[s], rectLoops[s + 1]);     // بدنهٔ مستطیلی
        }

        // نوار بین مرز دندانه‌دار و آفست — مرز CCW (بالا) و آفست قرینه (پایین)
        for (int sIdx = 0; sIdx <= slices; sIdx++)
        {
            var top = toothLoops[sIdx];
            var low = offsetLoops[sIdx];
            int n = top.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mesh.AddTriangle(top[i], low[i], low[j]);
                mesh.AddTriangle(top[i], low[j], top[j]);
            }
        }

        // درپوش‌های دو سر (صفحه‌های YZ) — از قلهٔ دندانهٔ انتهایی تا گوشهٔ مستطیل
        for (int s = 0; s < slices; s++)
        {
            EndWall(mesh, toothLoops[s][^1], offsetLoops[s][^1], rectLoops[s][1], rectLoops[s][2],
                             toothLoops[s + 1][^1], offsetLoops[s + 1][^1], rectLoops[s + 1][1], rectLoops[s + 1][2]);
            EndWall(mesh, toothLoops[s][0], offsetLoops[s][0], rectLoops[s][5], rectLoops[s][4],
                             toothLoops[s + 1][0], offsetLoops[s + 1][0], rectLoops[s + 1][5], rectLoops[s + 1][4]);
        }

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

    /// <summary>دیوار سر شانه: زنجیرهٔ (قلهٔ دندانهٔ انتهایی، آفست، گوشهٔ مستطیل بالا، گوشهٔ پایین) بین دو برش.</summary>
    private static void EndWall(TriMesh mesh, int t0, int o0, int r0, int b0, int t1, int o1, int r1, int b1)
    {
        mesh.AddTriangle(t0, o0, o1);
        mesh.AddTriangle(t0, o1, t1);
        mesh.AddTriangle(o0, r0, r1);
        mesh.AddTriangle(o0, r1, o1);
        mesh.AddTriangle(r0, b0, b1);
        mesh.AddTriangle(r0, b1, r1);
    }
}
