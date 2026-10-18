using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>نتیجهٔ ساخت هندسه: مش + ارقام + پیام‌ها + تنظیم مؤثر کیفیت.</summary>
public sealed class GearBuildResult
{
    public required TriMesh Mesh { get; init; }
    public required GearGeometryFigures Figures { get; init; }
    public required IReadOnlyList<GearDiagnostic> Diagnostics { get; init; }
    /// <summary>تعداد برش عرضی واقعاً استفاده‌شده (برای گزارش دقت مارپیچ).</summary>
    public int AxialSlicesUsed { get; init; }
    /// <summary>خطای وتر مؤثر (mm).</summary>
    public double EffectiveChordToleranceMm { get; init; }
}

/// <summary>
/// سازندهٔ مش چرخ‌دندهٔ استوانه‌ای (خارجی ساده/مارپیچ/جناغی و داخلی ساده/مارپیچ) — سند ۰۳ بخش ۴ و ۵.
///
/// طرح مش:
///   - مرز پروفیل (خارجی: دندانه‌دار؛ داخلی: مرز فضای خالی پینیون) در «برش‌های عرضی» با پیچش(z) نمونه‌برداری می‌شود؛
///   - سطح جانبی دندانه بین برش‌ها با قدم‌زدن ادغامی ساخته می‌شود؛
///   - صفحه‌های روی/زیر با مثلث‌بندی ناحیهٔ محصور (مرز پروفیل، بور/شیار خار، حفره‌های سبک‌سازی، رینگ بیرونی داخلی)
///     با ear-clipping ساخته می‌شوند — سوراخ سبک‌سازی همراه بور پشتیبانی می‌شود؛
///   - چرخ‌دندهٔ داخلی به‌صورت «رینگ با دندانهٔ داخلی» ساخته می‌شود (ماده بین مرز دندانه و قطر بیرونی رینگ)؛
///   - گپ مرکزی جناغی، پخ بور و پخ نوک دندانه اختیاری‌اند و آب‌بندی مش حفظ می‌شود.
/// </summary>
public static class CylindricalGearMeshBuilder
{
    private const string BoreRing = "bore";
    private const string RimRing = "rim";
    private const string HubInner = "hub-in";
    private const string HubOuter = "hub-out";

    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality,
        bool cycloidal = false)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();

        var diags = GearValidator.Validate(family, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترها نامعتبرند؛ هندسه ساخته نشد.", errors);

        var fig = GearFiguresCalculator.Compute(family, teeth);
        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);

        bool internalGear = family is GearFamily.InternalSpur or GearFamily.InternalHelical;

        // داخلی هم پروفیل دندانه‌دار می‌گیرد: مرز «فضای خالی پینیون» = مرز داخلی مادهٔ رینگ
        // پخ نوک برای هر دو نوع مجاز است (برای داخلی، گوشه‌های رو به داخل را می‌برد)
        double tipChamfer = body.TipChamferMm;
        IProfileGenerator profileGen = cycloidal
            ? new CycloidalToothProfile(fig, fig.RootFilletRadius)
            : new InvoluteToothProfile(fig, fig.RootFilletRadius, tipChamfer);
        var boundary = CleanBoundary(profileGen.BoundaryPoints(tol));
        if (boundary.Count < 8)
            throw new GeometryBuildException("پروفیل تولیدشده نقاط کافی ندارد.", errors);
        if (cycloidal)
            diags.Add(new GearDiagnostic("SGM-I530", Severity.Info,
                "پروفیل سیکلوئیدی (اپیسایکلوئید سر + هیپوسایکلوئید ریشه) با دایرهٔ مولّد نصف‌شعاع گام؛ متداول در ساعت‌سازی و ابزار دقیق.", "ProfileSystem"));
        if (tipChamfer > 1e-9)
            diags.Add(new GearDiagnostic("SGM-I402", Severity.Info,
                $"پخ {Math.Min(tipChamfer, 0.45 * (fig.TipRadius - Math.Max(fig.RootRadius, fig.BaseRadius))):F2} میلی‌متری نوک دندانه اعمال شد.", "TipChamferMm"));

        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);
        var extra = new List<GearDiagnostic>();
        var b = new BuilderContext(mesh, pool, boundary, fig, teeth, body, tol, internalGear, extra);

        b.ComputeRadii();
        int gapStart = -1, gapEnd = -1;
        b.BuildSlices(family, quality, ref gapStart, ref gapEnd);
        b.BuildProfileLoops();
        b.BuildCaps(body);
        b.BuildToothWalls(gapStart, gapEnd);
        b.BuildBoreOrRimWalls(body);
        b.BuildLighteningHoles(body);
        b.BuildHub(body);
        b.BuildHerringboneGap(family, body, gapStart, gapEnd);

        if (mesh.TriangleCount == 0)
            throw new GeometryBuildException("مش خالی ساخته شد؛ ورودی‌ها را بررسی کنید.", diags);

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags.Concat(extra).ToList(),
            AxialSlicesUsed = pool.SliceCount,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>
    /// حذف نقاط تکراری متوالی (و تکراری ابتدا/انتها) از مرز — قبل از ساخت هر حلقه‌ای انجام می‌شود
    /// تا برش‌ها، دیواره‌ها و درپوش‌ها همه از یک فهرست رأس مشترک استفاده کنند (آب‌بندی مش).
    /// </summary>
    internal static List<PolarPoint> CleanBoundary(List<PolarPoint> raw)
    {
        var result = new List<PolarPoint>(raw.Count);
        foreach (var p in raw)
        {
            if (result.Count > 0)
            {
                var q = result[^1];
                if (Math.Abs(p.Theta - q.Theta) < 1e-12 && Math.Abs(p.Radius - q.Radius) < 1e-9)
                    continue;
            }
            result.Add(p);
        }
        while (result.Count > 1)
        {
            var a = result[0];
            var b = result[^1];
            double dTheta = Math.Abs(a.Theta - b.Theta);
            if (dTheta < 1e-12 || Math.Abs(dTheta - 2.0 * Math.PI) < 1e-9)
            {
                if (Math.Abs(a.Radius - b.Radius) < 1e-9)
                    result.RemoveAt(result.Count - 1);
                else
                    break;
            }
            else
                break;
        }
        return result;
    }

    private sealed class BuilderContext(
        TriMesh mesh,
        VertexPool pool,
        List<PolarPoint> boundary,
        GearGeometryFigures fig,
        ToothParameters teeth,
        BodyFeatures body,
        double tol,
        bool internalGear,
        List<GearDiagnostic> extraWarnings)
    {
        public TriMesh Mesh => mesh;
        public VertexPool Pool => pool;
        public List<PolarPoint> Boundary => boundary;
        public GearGeometryFigures Fig => fig;
        public ToothParameters Teeth => teeth;
        public BodyFeatures Body => body;
        public double Tol => tol;
        public bool Internal => internalGear;
        public List<GearDiagnostic> ExtraWarnings => extraWarnings;

        public double FaceWidth => teeth.FaceWidth;
        public double BoreRadius { get; set; }
        public double RimOuterRadius { get; set; }
        public double RootRadiusUsed { get; set; }

        /// <summary>شعاع مرز در زاویهٔ دلخواه — درون‌یابی خطی روی نقاط مرز (مرز نسبت به مرکز ستاره‌ای است).</summary>
        public double BoundaryRadiusAt(double theta)
        {
            double span = Boundary[^1].Theta - Boundary[0].Theta;

            double local = theta;
            while (local < Boundary[0].Theta) local += span;
            while (local > Boundary[^1].Theta) local -= span;
            if (span < 1e-12) return Boundary[0].Radius;

            int lo = 0, hi = Boundary.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Boundary[mid].Theta <= local) lo = mid; else hi = mid;
            }
            double a = Boundary[lo].Theta, bAng = Boundary[hi].Theta;
            if (bAng - a < 1e-12) return Boundary[lo].Radius;
            double k = (local - a) / (bAng - a);
            return Boundary[lo].Radius + (Boundary[hi].Radius - Boundary[lo].Radius) * k;
        }

        /// <summary>حلقهٔ بور (با شیار خار در صورت فعال بودن) در برش مشخص.</summary>
        public List<int> BoreLoop(int slice)
            => Body.KeywayWidth > 0 && !Internal
                ? Pool.RingWithKeyway(slice, BoreRadius, Body.KeywayWidth, Body.KeywayDepth, Tol, Body.KeywayEndRadius)
                : Pool.Ring(slice, BoreRing, BoreRadius, Tol);
    }

    private static void ComputeRadii(this BuilderContext b)
    {
        double rTip = b.Fig.TipRadius;
        double rRoot = b.Fig.RootRadius;

        if (b.Internal)
        {
            // چرخ‌دندهٔ داخلی = رینگ: ماده بین مرز دندانه (داخلی) و قطر بیرونی رینگ
            b.RootRadiusUsed = Math.Max(rRoot, rTip); // بیرونی‌ترین نقطهٔ مرز (ریشه)
            double minRim = Math.Max(0.25 * b.Teeth.Module, 1.0);
            b.RimOuterRadius = b.RootRadiusUsed + minRim;
            double requested = 0.5 * b.Body.BoreDiameter;
            if (requested > b.RimOuterRadius)
            {
                b.RimOuterRadius = requested;
                b.ExtraWarnings.Add(new GearDiagnostic("SGM-I403", Severity.Info,
                    "برای چرخ‌دندهٔ داخلی، «قطر بور» به‌عنوان قطر بیرونی رینگ تفسیر و اعمال شد.", "BoreDiameter"));
            }
            b.BoreRadius = 0; // سوراخ مرکزی ندارد؛ گوه از سمت رینگ بیرونی است
        }
        else
        {
            b.RootRadiusUsed = Math.Max(rRoot, b.Fig.BaseRadius);
            double defaultBore = 0.5 * b.Body.BoreDiameter;
            if (defaultBore >= b.RootRadiusUsed * 0.98)
                defaultBore = 0.5 * b.RootRadiusUsed; // سقف: بور حداکثر تا ریشه
            b.BoreRadius = defaultBore;
            b.RimOuterRadius = 0;
        }
    }

    // ---------- برش‌ها ----------

    /// <summary>
    /// تعداد برش تطبیقی برای پیچش: بیشینهٔ گام زاویه‌ای بین برش‌ها از تلرانس وتر می‌آید
    /// (خطای سطح جانبی ≈ r·Δψ²/8 ≤ tol) — دیگر به تعداد ثابت وابسته نیست.
    /// </summary>
    private static int AdaptiveSliceCount(double leadFactor, double width, double rOuter, double tol, int requestedMin)
    {
        double totalTwist = Math.Abs(leadFactor) * width;
        if (totalTwist < 1e-12)
            return 1;
        double rEff = Math.Max(rOuter, 1e-6);
        double dPsiMax = Math.Sqrt(Math.Max(1e-12, 8.0 * tol / rEff));
        int adaptive = (int)Math.Ceiling(totalTwist / dPsiMax);
        return Math.Clamp(Math.Max(adaptive, Math.Min(requestedMin, 64)), 2, 400);
    }

    private static void BuildSlices(this BuilderContext b, GearFamily family, GeometrySettings q, ref int gapStart, ref int gapEnd)
    {
        double w = b.FaceWidth;
        double leadFactor = Math.Tan(b.Fig.HelixAngleUsed) / Math.Max(1e-9, b.Fig.PitchRadius);

        if (family == GearFamily.ExternalHerringbone)
        {
            double half = w / 2.0;
            double gap = Math.Clamp(b.Body.HerringboneCentralGapMm, 0, Math.Max(0, w * 0.6));
            if (gap > 1e-9 && b.Body.HerringboneCentralGapMm > w * 0.6)
                b.ExtraWarnings.Add(new GearDiagnostic("SGM-W410", Severity.Warning,
                    $"گپ مرکزی درخواستی ({b.Body.HerringboneCentralGapMm:F1} mm) از ۶۰٪ پهنا بیشتر است و به {gap:F1} mm محدود شد.", "HerringboneCentralGapMm"));

            if (gap <= 1e-9)
            {
                // جناغی پیوسته: پیچش صفر در وسط، مثبت در نیمهٔ پایین و منفی در نیمهٔ بالا
                int n = AdaptiveSliceCount(leadFactor, half, b.Fig.TipRadius, b.Tol, Math.Clamp(q.AxialSliceCount, 4, 200));
                b.Pool.AddSlice(0.0, 0);
                for (int i = 1; i <= n; i++)
                    b.Pool.AddSlice(leadFactor * (half * i / n), half * i / n);
                for (int i = 1; i <= n; i++)
                {
                    double zLocal = half * i / n;
                    b.Pool.AddSlice(-leadFactor * zLocal, half + zLocal);
                }
                return;
            }

            // جناغی با گپ مرکزی: [0, g1] دندانهٔ +، (g1, g2) شیار استوانه‌ای در دایرهٔ ریشه، [g2, w] دندانهٔ −
            double g1 = half - gap / 2.0, g2 = half + gap / 2.0;
            int nLower = AdaptiveSliceCount(leadFactor, g1, b.Fig.TipRadius, b.Tol, Math.Clamp(q.AxialSliceCount, 4, 200));
            int nUpper = AdaptiveSliceCount(leadFactor, w - g2, b.Fig.TipRadius, b.Tol, Math.Clamp(q.AxialSliceCount, 4, 200));

            b.Pool.AddSlice(0.0, 0);
            for (int i = 1; i <= nLower; i++)
                b.Pool.AddSlice(leadFactor * (g1 * i / nLower), g1 * i / nLower);
            gapStart = b.Pool.SliceCount - 1;

            b.Pool.AddSlice(-leadFactor * (g2 - half), g2);
            gapEnd = b.Pool.SliceCount - 1;
            for (int i = 1; i <= nUpper; i++)
            {
                double z = g2 + (w - g2) * i / nUpper;
                b.Pool.AddSlice(-leadFactor * (z - half), z);
            }
            return;
        }

        if (Math.Abs(b.Fig.HelixAngleUsed) > 1e-9)
        {
            int n = AdaptiveSliceCount(leadFactor, w, b.Fig.TipRadius, b.Tol, Math.Clamp(q.AxialSliceCount, 2, 400));
            for (int i = 0; i <= n; i++)
                b.Pool.AddSlice(leadFactor * (w * i / n), w * i / n);
            return;
        }

        b.Pool.AddSlice(0, 0);
        b.Pool.AddSlice(0, w);
    }

    private static void BuildProfileLoops(this BuilderContext b)
    {
        for (int s = 0; s < b.Pool.SliceCount; s++)
            foreach (var p in b.Boundary)
                b.Pool.ProfilePoint(s, p);
    }

    // ---------- درپوش‌ها (مثلث‌بندی ناحیه با حفره) ----------

    /// <summary>
    /// درپوش تخت برش اول/آخر: ناحیهٔ ماده بین مرز پروفیل و بور (خارجی) یا بین رینگ و مرز دندانه (داخلی)،
    /// با حفره‌های سبک‌سازی. خروجی: حلقهٔ داخلیِ به‌کاررفته (برای اتصال آب‌بند به توپی).
    /// </summary>
    private static List<int>? BuildCap(this BuilderContext b, int slice, bool top)
    {
        var pts = new List<Point2>();
        var idxMap = new List<int>();
        var holes = new List<List<Point2>>();

        void AddLoopAsOuter(IReadOnlyList<int> loop)
        {
            int first = pts.Count;
            for (int i = 0; i < loop.Count; i++)
            {
                var v = b.Mesh.VertexAt(loop[i]);
                if (pts.Count > first)
                {
                    var pv = pts[^1];
                    if (Math.Abs(pv.X - v.X) < 1e-9 && Math.Abs(pv.Y - v.Y) < 1e-9)
                        continue;
                }
                pts.Add(new Point2(v.X, v.Y));
                idxMap.Add(loop[i]);
            }
            if (pts.Count > first + 1) // حذف تکراری انتهایی/ابتدایی
            {
                var v0 = b.Mesh.VertexAt(loop[0]);
                if (Math.Abs(pts[^1].X - v0.X) < 1e-9 && Math.Abs(pts[^1].Y - v0.Y) < 1e-9)
                {
                    pts.RemoveAt(pts.Count - 1);
                    idxMap.RemoveAt(idxMap.Count - 1);
                }
            }
        }

        void AddHoleLoop(IReadOnlyList<int> loop)
        {
            // حفره‌ها باید ساعتگرد (CW) باشند؛ حلقه‌های سازنده CCW‌اند ← معکوس درج می‌شوند
            var hole = new List<Point2>(loop.Count);
            for (int i = loop.Count - 1; i >= 0; i--)
            {
                var v = b.Mesh.VertexAt(loop[i]);
                if (hole.Count > 0)
                {
                    var pv = hole[^1];
                    if (Math.Abs(pv.X - v.X) < 1e-9 && Math.Abs(pv.Y - v.Y) < 1e-9)
                        continue;
                }
                hole.Add(new Point2(v.X, v.Y));
                idxMap.Add(loop[i]);
            }
            if (hole.Count >= 3)
                holes.Add(hole);
        }

        List<int>? innerLoopUsed = null;

        if (b.Internal)
        {
            // بیرونی = دایرهٔ رینگ؛ حفرهٔ اول = مرز دندانه (فضای خالی پینیون)
            var rim = b.Pool.Ring(slice, RimRing, b.RimOuterRadius, b.Tol);
            AddLoopAsOuter(rim);
            AddHoleLoop(b.Pool.ProfileLoop(slice));

            // حفره‌های سبک‌سازی داخل پهنای رینگ
            AddLighteningHoleLoops(b, slice, holes, idxMap);
        }
        else if (b.BoreRadius <= 1e-9)
        {
            AddLoopAsOuter(b.Pool.ProfileLoop(slice));
            AddLighteningHoleLoops(b, slice, holes, idxMap);
        }
        else
        {
            AddLoopAsOuter(b.Pool.ProfileLoop(slice));

            // پخ بور: درپوش تا دایرهٔ بزرگ‌تر (دهانهٔ پخ‌دار) بسته می‌شود؛ مخروط پخ جداگانه ساخته می‌شود
            bool chamferThisCap = b.Body.BoreChamferMm > 1e-9 && b.Body.KeywayWidth <= 0 && !(top && HasHub(b));
            double openR = b.BoreRadius;
            if (chamferThisCap)
                openR += ClampBoreChamfer(b, b.Body.BoreChamferMm);

            if (chamferThisCap)
            {
                double z = b.Pool.Z(slice);
                var openLoop = b.Pool.RingAtZ(z, $"bore-open-{(top ? "t" : "b")}", openR, b.Tol);
                AddHoleLoop(openLoop);
                innerLoopUsed = openLoop;
            }
            else
            {
                var bore = b.BoreLoop(slice);
                AddHoleLoop(bore);
                innerLoopUsed = bore;
            }

            AddLighteningHoleLoops(b, slice, holes, idxMap);
        }

        if (pts.Count < 3)
            return null;

        var triangles = PolygonTriangulator.Triangulate(pts, holes.Count > 0 ? holes : null);
        foreach (var (ia, ib, ic) in triangles)
        {
            if (top)
                b.Mesh.AddTriangle(idxMap[ia], idxMap[ib], idxMap[ic]); // +Z
            else
                b.Mesh.AddTriangle(idxMap[ia], idxMap[ic], idxMap[ib]); // −Z
        }
        return innerLoopUsed;
    }

    private static void AddLighteningHoleLoops(BuilderContext b, int slice, List<List<Point2>> holes, List<int> idxMap)
    {
        var centers = LighteningHoleCenters(b);
        if (centers == null)
            return;
        double rHole = b.Body.LighteningHoleDiameter / 2.0;
        foreach (var (cx, cy) in centers)
        {
            var ring = b.Pool.HoleRing(slice, cx, cy, rHole, b.Tol);
            var hole = new List<Point2>(ring.Count);
            for (int i = ring.Count - 1; i >= 0; i--) // CW برای حفره
            {
                var v = b.Mesh.VertexAt(ring[i]);
                hole.Add(new Point2(v.X, v.Y));
                idxMap.Add(ring[i]);
            }
            if (hole.Count >= 3)
                holes.Add(hole);
        }
    }

    private static double ClampBoreChamfer(BuilderContext b, double requested)
        => Math.Clamp(requested, 0, Math.Max(0, b.BoreRadius * 0.45));

    private static bool HasHub(BuilderContext b)
        => !b.Internal && b.Body.HubDiameter > 0 && b.Body.HubDiameter / 2.0 > b.BoreRadius && b.Body.HubLength > 0;

    private static void BuildCaps(this BuilderContext b, BodyFeatures body)
    {
        int top = b.Pool.SliceCount - 1;
        b.BuildCap(0, top: false);
        _ = b.BuildCap(top, top: true); // حلقهٔ داخلی درپوش بالا در BuildHub بازاستفاده می‌شود
    }

    // ---------- دیواره‌ها ----------

    private static void BuildToothWalls(this BuilderContext b, int gapStart, int gapEnd)
    {
        // خارجی: ماده داخل مرز ← نرمال بیرون؛ داخلی: ماده بیرون مرز (رینگ) ← نرمال رو به مرکز
        bool outerFacing = !b.Internal;
        for (int s = 0; s + 1 < b.Pool.SliceCount; s++)
        {
            if (s == gapStart && s + 1 == gapEnd)
                continue; // ناحیهٔ گپ مرکزی جناغی — شیار جداگانه ساخته می‌شود
            double dz = b.Pool.Z(s + 1) - b.Pool.Z(s);
            double dTw = Math.Abs(b.Pool.Twist(s + 1) - b.Pool.Twist(s));
            if (Math.Abs(dz) < 1e-12 && dTw < 1e-12)
                continue; // برش‌های تکراری
            if (outerFacing)
                VertexPool.WallCCWOuter(b.Mesh, b.Pool.ProfileLoop(s), b.Pool.ProfileLoop(s + 1));
            else
                VertexPool.WallCCW(b.Mesh, b.Pool.ProfileLoop(s), b.Pool.ProfileLoop(s + 1));
        }
    }

    private static void BuildBoreOrRimWalls(this BuilderContext b, BodyFeatures body)
    {
        int top = b.Pool.SliceCount - 1;

        if (b.Internal)
        {
            // دیوارهٔ بیرونی رینگ — سطح بیرونی ماده
            var rimLo = b.Pool.Ring(0, RimRing, b.RimOuterRadius, b.Tol);
            var rimHi = b.Pool.Ring(top, RimRing, b.RimOuterRadius, b.Tol);
            VertexPool.WallCCWOuter(b.Mesh, rimLo, rimHi);
            return;
        }

        if (b.BoreRadius <= 1e-9)
            return;

        bool chamfer = body.BoreChamferMm > 1e-9 && body.KeywayWidth <= 0;
        if (!chamfer)
        {
            VertexPool.WallCCW(b.Mesh, b.BoreLoop(0), b.BoreLoop(top));
            return;
        }

        // پخ بور در دو سر؛ دیوارهٔ اصلی بور بین دو حلقهٔ داخلی مخروط‌ها
        double c = ClampBoreChamfer(b, body.BoreChamferMm);
        double w = b.FaceWidth;
        double zLo = b.Pool.Z(0), zHi = b.Pool.Z(top);

        var openLo = b.Pool.RingAtZ(zLo, "bore-open-b", b.BoreRadius + c, b.Tol);
        var innerLo = b.Pool.RingAtZ(zLo + c, "bore-in-b", b.BoreRadius, b.Tol);
        VertexPool.WallCCW(b.Mesh, openLo, innerLo); // مخروط پخ پایین (رو به داخل سوراخ)

        if (!HasHub(b))
        {
            var innerHi = b.Pool.RingAtZ(zHi - c, "bore-in-t", b.BoreRadius, b.Tol);
            var openHi = b.Pool.RingAtZ(zHi, "bore-open-t", b.BoreRadius + c, b.Tol);
            VertexPool.WallCCW(b.Mesh, innerLo, innerHi); // دیوارهٔ بور
            VertexPool.WallCCW(b.Mesh, innerHi, openHi);  // مخروط پخ بالا
        }
        else
        {
            // توپی روی درپوش بالا می‌نشیند؛ پخ بالا حذف و دیوارهٔ بور تا برش بالا ادامه می‌یابد
            VertexPool.WallCCW(b.Mesh, innerLo, b.BoreLoop(top));
            b.ExtraWarnings.Add(new GearDiagnostic("SGM-I404", Severity.Info,
                "پخ دهانهٔ بالای بور به‌دلیل وجود توپی نادیده گرفته شد (زیر کف توپی پنهان است).", "BoreChamferMm"));
        }
    }

    private static List<(double Cx, double Cy)>? LighteningHoleCenters(BuilderContext b)
    {
        if (b.Body.LighteningHoleCount <= 0 || b.Body.LighteningHoleDiameter <= 0)
            return null;

        double rHole = b.Body.LighteningHoleDiameter / 2.0;

        double rIn, rOut;
        if (b.Internal)
        {
            rIn = b.RootRadiusUsed + rHole + 0.2 * b.Teeth.Module;
            rOut = b.RimOuterRadius - rHole - 0.2 * b.Teeth.Module;
        }
        else
        {
            double minBoundary = b.Boundary.Min(p => p.Radius);
            double rWebOuter = Math.Min(minBoundary, b.RootRadiusUsed) - rHole - 0.5 * b.Teeth.Module;
            double rWebInner = b.BoreRadius > 1e-9
                ? b.BoreRadius + rHole + Math.Max(0.4 * b.Teeth.Module, 1.0)
                : rHole + 0.5 * b.Teeth.Module;
            rIn = rWebInner;
            rOut = rWebOuter;
        }

        double rCircle = b.Body.LighteningHoleCircleRadius;
        if (rCircle <= 0)
            rCircle = (rIn + rOut) / 2.0;
        if (rOut <= rIn + 1e-9)
        {
            b.ExtraWarnings.Add(new GearDiagnostic(DiagnosticCodes.MeshTooHeavy, Severity.Warning,
                "فضای کافی برای سوراخ‌های سبک‌سازی نیست؛ حفره‌ها نادیده گرفته شدند.", "LighteningHoleCount"));
            return null;
        }
        rCircle = Math.Clamp(rCircle, rIn, rOut);

        var centers = new List<(double, double)>(b.Body.LighteningHoleCount);
        for (int h = 0; h < b.Body.LighteningHoleCount; h++)
        {
            double a = 2.0 * Math.PI * h / b.Body.LighteningHoleCount;
            centers.Add((rCircle * Math.Cos(a), rCircle * Math.Sin(a)));
        }
        return centers;
    }

    private static void BuildLighteningHoles(this BuilderContext b, BodyFeatures body)
    {
        var centers = LighteningHoleCenters(b);
        if (centers == null)
            return;

        double rHole = body.LighteningHoleDiameter / 2.0;
        int top = b.Pool.SliceCount - 1;
        foreach (var (cx, cy) in centers)
        {
            var lo = b.Pool.HoleRing(0, cx, cy, rHole, b.Tol);
            var hi = b.Pool.HoleRing(top, cx, cy, rHole, b.Tol);
            VertexPool.WallCCW(b.Mesh, lo, hi);
        }
    }

    private static void BuildHub(this BuilderContext b, BodyFeatures body)
    {
        if (b.Internal || !HasHub(b))
            return;
        double hubR = body.HubDiameter / 2.0;
        if (hubR >= b.RootRadiusUsed)
            return; // توپی بزرگ‌تر از ریشه بی‌معنی است؛ نادیده گرفته می‌شود

        int top = b.Pool.SliceCount - 1;
        double zBottom = b.Pool.Z(top);
        double zTop = zBottom + body.HubLength;
        double tol = b.Tol;

        // حلقهٔ داخلی کف توپی = حلقهٔ اختصاصی (جامد توپی مستقل از جامد بدنه، هم‌مکان اما بدون اشتراک یال)
        var hubInBottom = b.BoreRadius <= 1e-9
            ? null
            : b.Pool.RingAtZ(zBottom, HubInner, b.BoreRadius, tol);
        var hubOutBottom = b.Pool.RingAtZ(zBottom, HubOuter, hubR, tol);
        var hubInTop = b.BoreRadius <= 1e-9
            ? null
            : b.Pool.RingAtZ(zTop, HubInner, b.BoreRadius, tol);
        var hubOutTop = b.Pool.RingAtZ(zTop, HubOuter, hubR, tol);

        if (hubInBottom != null)
            VertexPool.AnnulusNZ(b.Mesh, hubInBottom, hubOutBottom); // کف توپی (نرمال −Z رو به پایین)
        else
            VertexPool.DiskNZ(b.Mesh, hubOutBottom, b.Pool.CenterAtZ(zBottom)); // کف توپی بدون بور = دیسک کامل
        VertexPool.WallCCWOuter(b.Mesh, hubOutBottom, hubOutTop);    // دیوارهٔ بیرونی توپی
        if (b.BoreRadius > 1e-9)
            VertexPool.WallCCW(b.Mesh, hubInBottom!, hubInTop!);  // دیوارهٔ داخل توپی (ادامهٔ بور)
        else
            VertexPool.DiskZ(b.Mesh, hubOutTop, b.Pool.CenterAtZ(zTop)); // سر توپی بدون بور = دیسک کامل
        if (hubInBottom != null && hubInTop != null)
            VertexPool.AnnulusZ(b.Mesh, hubInTop, hubOutTop);       // سر توپی (نرمال +Z)
    }

    /// <summary>شیار مرکزی جناغی: طاقچهٔ تخت در دو سر + استوانهٔ دایرهٔ ریشه در میانه.</summary>
    private static void BuildHerringboneGap(this BuilderContext b, GearFamily family, BodyFeatures body, int gapStart, int gapEnd)
    {
        if (family != GearFamily.ExternalHerringbone || gapStart < 0 || gapEnd < 0)
            return;

        double rGap = b.Boundary.Min(p => p.Radius);
        double z1 = b.Pool.Z(gapStart), z2 = b.Pool.Z(gapEnd);

        var ring1 = b.Pool.RingAtZ(z1, "gap1", rGap, b.Tol);
        var ring2 = b.Pool.RingAtZ(z2, "gap2", rGap, b.Tol);

        // طاقچهٔ تخت در انتهای دندانه‌های نیمهٔ پایین (نرمال +Z رو به گپ)
        VertexPool.AnnulusZ(b.Mesh, ring1, b.Pool.ProfileLoop(gapStart));
        // استوانهٔ شیار — سطح بیرونی ماده
        VertexPool.WallCCWOuter(b.Mesh, ring1, ring2);
        // طاقچهٔ تخت در ابتدای دندانه‌های نیمهٔ بالا (نرمال −Z رو به گپ)
        VertexPool.AnnulusNZ(b.Mesh, ring2, b.Pool.ProfileLoop(gapEnd));

        b.ExtraWarnings.Add(new GearDiagnostic("SGM-I405", Severity.Info,
            $"گپ مرکزی {z2 - z1:F2} میلی‌متری تا دایرهٔ ریشه ساخته شد.", "HerringboneCentralGapMm"));
    }
}

/// <summary>خطای ساخت هندسه با پیام‌های قابل نمایش به کاربر.</summary>
public sealed class GeometryBuildException : Exception
{
    public IReadOnlyList<GearDiagnostic> Diagnostics { get; }

    public GeometryBuildException(string message, IReadOnlyList<GearDiagnostic> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
