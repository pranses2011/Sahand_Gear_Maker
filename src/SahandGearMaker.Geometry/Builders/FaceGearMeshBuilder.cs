using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش چرخ‌دندهٔ تاجی/پیشانی (Crown/Face gear) — دندانه‌های شعاعی روی صفحهٔ انتهایی.
///
/// ساختار هندسی (روش طرح‌ریزی تولید پینیون، مرتبهٔ اول):
///   - بدنه: صفحهٔ حلقه‌ای از شعاع داخلی تا بیرونی با ضخامت ریشه؛
///   - دندانه‌ها: دیواره‌های شعاعی ایستاده روی صفحه با پهلوهای مخروطی (زاویهٔ فشار α)؛
///   - ارتفاع دندانه با شعاع کاهش می‌یابد (همگرایی سطح سر و ریشه در چرخ تاجی واقعی)
///     و در لبهٔ بیرونی به مقدار بسیار کوچک ولی ناصفر محدود می‌شود (پخ لبه)؛
///   - پهن دندانه از گام چرخ‌دندهٔ مجازی مشتق می‌شود: نیم‌زاویهٔ دندانه ≈ π/z منهای فضای خالی؛
///   - توپولوژی آب‌بند: همهٔ لبه‌های مشترک (پای دیواره‌ها، کمان‌های داخل/بیرون، صورت‌های
///     انتهایی) از رأس‌های مشترک ساخته می‌شوند؛ صفحهٔ روی به‌صورت «قطعه‌های فضای خالی»
///     مثلث‌بندی می‌شود و استوانه‌های داخل/بیرون از همان زنجیرهٔ مرزی تغذیه می‌شوند.
///
/// محدودیت صادقانه: پوشش دقیق اپویکولوئید پینیون (روش Litvin) پیاده‌سازی نشده؛
/// هندسهٔ حاصل «طرح‌ریزی تولید» است و در کاتالوگ همین‌گونه اعلام می‌شود.
/// </summary>
public static class FaceGearMeshBuilder
{
    public static GearBuildResult Build(
        ToothParameters teeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        var diags = GearValidator.Validate(GearFamily.CrownFace, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای چرخ تاجی نامعتبرند.", errors);

        double m = teeth.Module;
        int z = teeth.ToothCount;
        if (z < 12)
        {
            errors.Add(new GearDiagnostic("SGM-E280", Severity.Error,
                "تعداد دندانهٔ چرخ تاجی حداقل ۱۲ باید باشد.", "ToothCount"));
            throw new GeometryBuildException("تعداد دندانهٔ چرخ تاجی کم است.", errors);
        }

        double alpha = teeth.PressureAngle;
        double rp = m * z / 2.0;
        double b = Math.Clamp(teeth.FaceWidth, 2.0 * m, 0.6 * rp);
        if (teeth.FaceWidth > 0.6 * rp)
            diags.Add(new GearDiagnostic("SGM-W281", Severity.Warning,
                $"پهنای دندانهٔ تاجی به {b:F1} mm محدود شد (۰٫۶×شعاع گام).", "FaceWidth"));

        double ri = rp - b / 2.0;
        double ro = rp + b / 2.0;
        double hi = Math.Min((teeth.AddendumCoefficient + teeth.DedendumCoefficient) * m, 0.75 * b);
        double hMin = Math.Max(0.12 * m, hi * 0.06); // ارتفاع لبهٔ بیرونی (پخ نهایی)
        double plateT = Math.Max(2.0 * m, teeth.DedendumCoefficient * m * 1.2);
        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);

        double boreR = body.BoreDiameter / 2.0;
        if (boreR > 0 && boreR >= ri - 0.6 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E282", Severity.Error,
                $"بور چرخ تاجی باید کوچک‌تر از شعاع داخلی دندانه باشد (حداکثر ≈ {2.0 * Math.Max(0, ri - 0.6 * m):F1} mm).", "BoreDiameter"));
            throw new GeometryBuildException("بور با ناحیهٔ دندانهٔ تاجی تداخل دارد.", errors);
        }

        var mesh = new TriMesh();
        double pitchAng = 2.0 * Math.PI / z;
        int radialSeg = Math.Clamp((int)Math.Ceiling(b / Math.Max(1e-4, 1.2 * tol)), 8, 64);
        int arcSeg = Math.Clamp((int)Math.Ceiling(pitchAng * ro / Math.Max(1e-4, 2.0 * tol)), 2, 24);
        int innerArcSeg = Math.Clamp((int)Math.Ceiling(pitchAng * ri / Math.Max(1e-4, 2.0 * tol)), 2, 24);

        // نیم‌عرض زاویه‌ای فضای خالی (با پاککردن)
        double slotHalf = pitchAng / 4.0 + 0.02 / rp;
        double toothHalf = pitchAng / 2.0 - slotHalf;

        double H(double r) => hi * (ro - r) / (ro - ri) + hMin; // ارتفاع دندانه در شعاع r

        // ---------- پاهای دندانه‌ها (رأس‌های مشترک روی z=0) ----------
        // footNeg[k][i], footPos[k][i]: پهلوی − و + دندانهٔ k از داخل به بیرون
        var footNeg = new List<List<int>>(z);
        var footPos = new List<List<int>>(z);
        for (int k = 0; k < z; k++)
        {
            double c = k * pitchAng;
            var fn = new List<int>(radialSeg + 1);
            var fp = new List<int>(radialSeg + 1);
            for (int i = 0; i <= radialSeg; i++)
            {
                double r = ri + (ro - ri) * i / radialSeg;
                double halfFoot = toothHalf + H(r) * Math.Tan(alpha) / r;
                fn.Add(mesh.AddVertex(Polar(c - halfFoot, r, 0.0)));
                fp.Add(mesh.AddVertex(Polar(c + halfFoot, r, 0.0)));
            }
            footNeg.Add(fn);
            footPos.Add(fp);
        }

        // ---------- دیواره‌ها و سطوح بالای دندانه‌ها ----------
        for (int k = 0; k < z; k++)
        {
            double c = k * pitchAng;
            var fn = footNeg[k];
            var fp = footPos[k];

            var topN = new List<int>(radialSeg + 1);
            var topP = new List<int>(radialSeg + 1);
            for (int i = 0; i <= radialSeg; i++)
            {
                double r = ri + (ro - ri) * i / radialSeg;
                topN.Add(mesh.AddVertex(Polar(c - toothHalf, r, H(r))));
                topP.Add(mesh.AddVertex(Polar(c + toothHalf, r, H(r))));
            }

            // پهلو − : نرمال −θ
            for (int i = 0; i < radialSeg; i++)
            {
                mesh.AddTriangle(fn[i], fn[i + 1], topN[i + 1]);
                mesh.AddTriangle(fn[i], topN[i + 1], topN[i]);
            }
            // پهلو + : نرمال +θ
            for (int i = 0; i < radialSeg; i++)
            {
                mesh.AddTriangle(fp[i + 1], fp[i], topP[i]);
                mesh.AddTriangle(fp[i + 1], topP[i], topP[i + 1]);
            }
            // سطح بالا: نرمال +Z
            for (int i = 0; i < radialSeg; i++)
            {
                mesh.AddTriangle(topN[i], topN[i + 1], topP[i + 1]);
                mesh.AddTriangle(topN[i], topP[i + 1], topP[i]);
            }
            // صورت داخلی (r=ri): نرمال −r
            mesh.AddTriangle(fn[0], topN[0], topP[0]);
            mesh.AddTriangle(fn[0], topP[0], fp[0]);
            // صورت بیرونی (r=ro): نرمال +r
            mesh.AddTriangle(fn[^1], fp[^1], topP[^1]);
            mesh.AddTriangle(fn[^1], topP[^1], topN[^1]);
        }

        // ---------- نوارهای فضای خالی (z=0): قاعدهٔ قانونی بین پهلوی + دندانهٔ k و پهلوی − دندانهٔ k+1 ----------
        for (int k = 0; k < z; k++)
        {
            int kNext = (k + 1) % z;
            for (int i = 0; i < radialSeg; i++)
            {
                // نرمال +Z (روی ماده از بالا)
                mesh.AddTriangle(footPos[k][i], footPos[k][i + 1], footNeg[kNext][i + 1]);
                mesh.AddTriangle(footPos[k][i], footNeg[kNext][i + 1], footNeg[kNext][i]);
            }
        }

        // ---------- وب داخلی (z=0): چندضلعی وتری با حفرهٔ بور ----------
        // چرخهٔ رأس‌ها: وتر فضای خالی k سپس لبهٔ صورت داخلی دندانهٔ k+1 (یک‌درمیان).
        var webCycle = new List<int>();
        for (int k = 0; k < z; k++)
        {
            webCycle.Add(footPos[k][0]);
            webCycle.Add(footNeg[(k + 1) % z][0]);
        }

        var webPts = new List<Point2>(webCycle.Count);
        foreach (int idx in webCycle)
        {
            var v = mesh.VertexAt(idx);
            webPts.Add(new Point2(v.X, v.Y));
        }
        if (ShoelaceSign(webPts) < 0)
        {
            webPts.Reverse();
            webCycle.Reverse();
        }

        List<int> boreFaceTop = new();
        List<List<Point2>> webHoles = new();
        List<List<int>> webHoleIdx = new();
        if (boreR > 0)
        {
            int segB = CircleProfile.SegmentsForCircle(boreR, tol);
            var bhp = new List<Point2>(segB);
            for (int i = 0; i < segB; i++)
            {
                double ang = 2.0 * Math.PI * i / segB;
                boreFaceTop.Add(mesh.AddVertex(Polar(ang, boreR, 0.0)));
                bhp.Add(new Point2(boreR * Math.Cos(ang), boreR * Math.Sin(ang)));
            }
            var cw = new List<Point2>(bhp);
            cw.Reverse();
            webHoles.Add(cw);
            var cwIdx = new List<int>(boreFaceTop);
            cwIdx.Reverse();
            webHoleIdx.Add(cwIdx);
        }

        if (boreR > 0)
        {
            // وب = نوار قانونی بین بور و چرخهٔ وتری (بدون مثلث‌بندی آزاد)
            VertexPool.AnnulusZ(mesh, boreFaceTop, webCycle);
        }
        else
        {
            var centerIdx = mesh.AddVertex(new Vector3(0, 0, 0.0));
            VertexPool.DiskZ(mesh, webCycle, centerIdx);
        }

        // ---------- استوانهٔ بیرونی: چرخهٔ وتری (صورت دندانه + وتر فضای خالی) ----------
        var outerCycle = new List<int>();
        for (int k = 0; k < z; k++)
        {
            outerCycle.Add(footNeg[k][^1]);
            outerCycle.Add(footPos[k][^1]);
            outerCycle.Add(footPos[k][^1]); // وتر فضای خالی k از footPos[k][^1] تا footNeg[k+1][^1]
            outerCycle.Add(footNeg[(k + 1) % z][^1]);
        }
        // حذف تکراری‌های متوالی (وتر صفر برای دندانهٔ آخر)
        var outerRing = new List<int>(outerCycle.Count);
        foreach (int idx in outerCycle)
        {
            if (outerRing.Count > 0 && outerRing[^1] == idx)
                continue;
            outerRing.Add(idx);
        }
        if (outerRing.Count > 1 && outerRing[0] == outerRing[^1])
            outerRing.RemoveAt(outerRing.Count - 1);

        var outerBottom = new List<int>(outerRing.Count);
        foreach (int idx in outerRing)
            outerBottom.Add(mesh.AddVertex(new Vector3(mesh.VertexAt(idx).X, mesh.VertexAt(idx).Y, -plateT)));
        for (int i = 0; i < outerRing.Count; i++)
        {
            int j = (i + 1) % outerRing.Count;
            mesh.AddTriangle(outerRing[j], outerRing[i], outerBottom[i]);
            mesh.AddTriangle(outerRing[j], outerBottom[i], outerBottom[j]);
        }

        // ---------- بور: استوانه + پشت ----------
        if (boreR > 0)
        {
            int segB2 = boreFaceTop.Count;
            var boreBottom = new List<int>(segB2);
            for (int i = 0; i < segB2; i++)
                boreBottom.Add(mesh.AddVertex(new Vector3(
                    mesh.VertexAt(boreFaceTop[i]).X, mesh.VertexAt(boreFaceTop[i]).Y, -plateT)));
            VertexPool.WallCCW(mesh, boreFaceTop, boreBottom);

            var outerPts = new List<Point2>(outerBottom.Count);
            foreach (int idx in outerBottom)
                outerPts.Add(new Point2(mesh.VertexAt(idx).X, mesh.VertexAt(idx).Y));
            while (ShoelaceSign(outerPts) < 0)
            {
                outerPts.Reverse();
                outerBottom.Reverse();
            }
            var holePts2 = new List<Point2>();
            foreach (int idx in boreBottom)
            {
                var v = mesh.VertexAt(idx);
                holePts2.Add(new Point2(v.X, v.Y));
            }
            holePts2.Reverse();
            var holeIdx2 = new List<int>(boreBottom);
            holeIdx2.Reverse();

            var backTris = PolygonTriangulator.Triangulate(outerPts, new List<List<Point2>> { holePts2 });
            foreach (var (ta, tb, tc) in backTris)
            {
                int ia = ta < outerPts.Count ? outerBottom[ta] : holeIdx2[ta - outerPts.Count];
                int ib = tb < outerPts.Count ? outerBottom[tb] : holeIdx2[tb - outerPts.Count];
                int ic = tc < outerPts.Count ? outerBottom[tc] : holeIdx2[tc - outerPts.Count];
                mesh.AddTriangle(ia, ic, ib); // نرمال −Z
            }
        }
        else
        {
            var center = mesh.AddVertex(new Vector3(0, 0, -plateT));
            for (int i = 0; i < outerBottom.Count; i++)
            {
                int j = (i + 1) % outerBottom.Count;
                mesh.AddTriangle(center, outerBottom[j], outerBottom[i]); // نرمال −Z
            }
        }

        var fig = new GearGeometryFigures
        {
            Family = GearFamily.CrownFace,
            TransverseModule = m,
            TransversePressureAngle = alpha,
            PitchRadius = rp,
            BaseRadius = rp * Math.Cos(alpha),
            TipRadius = ro,
            RootRadius = ri,
            AngularPitch = pitchAng,
            ToothThicknessAtPitch = 2.0 * rp * toothHalf,
            HelixAngleUsed = 0,
            RootFilletRadius = 0.1 * m,
            EstimatedVolumeMm3 = Math.PI * (ro * ro - boreR * boreR) * plateT + Math.PI * (ro + ri) * 0.5 * b * hi * 0.5,
            ToothCount = z,
        };

        diags.Add(new GearDiagnostic("SGM-I283", Severity.Info,
            $"چرخ تاجی: ناحیهٔ دندانه r∈[{ri:F1}, {ro:F1}] mm، ارتفاع داخلی {hi:F2} mm با کاهش خطی تا {hMin:F2} mm در لبهٔ بیرونی؛ پهلوهای مخروطی α={UnitConverter.RadToDeg(alpha):F1}°.", "FaceWidth"));
        diags.Add(new GearDiagnostic("SGM-U284", Severity.Unverified,
            "پوشش دقیق اپویکولوئید پینیون (روش Litvin) برای چرخ تاجی محاسبه نشده؛ هندسهٔ طرح‌ریزی تولید است، نه حل تماس کامل."));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags,
            AxialSlicesUsed = radialSeg,
            EffectiveChordToleranceMm = tol,
        };
    }

    // ---------- ابزارهای محلی ----------

    private static Vector3 Polar(double theta, double radius, double z)
        => new(radius * Math.Cos(theta), radius * Math.Sin(theta), z);

    private static double AngleOf(TriMesh mesh, int idx)
    {
        var v = mesh.VertexAt(idx);
        return Math.Atan2(v.Y, v.X);
    }

    private static int MapWeb(int vi, int outerCount, List<List<int>> holeLoops)
    {
        int k = vi - outerCount;
        foreach (var h in holeLoops)
        {
            if (k < h.Count)
                return h[k];
            k -= h.Count;
        }
        throw new InvalidOperationException("شاخص وب خارج از محدوده است.");
    }

    private static double ShoelaceSign(IReadOnlyList<Point2> pts)
    {
        double s = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s;
    }
}
