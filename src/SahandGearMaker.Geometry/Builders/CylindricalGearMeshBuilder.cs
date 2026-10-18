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
/// سازندهٔ مش چرخ‌دندهٔ استوانه‌ای (خارجی ساده/مارپیچ/جناغی و داخلی) — سند ۰۳ بخش ۴ و ۵.
/// طرح مش: مرز پروفیل در چند «برش عرضی» با پیچش(z) نمونه‌برداری می‌شود؛
/// سطح جانبی دندانه بین برش‌ها، دیوارهٔ بور/حفره‌ها، صفحه‌های بالا/پایین، توپی و گپ مرکزی جناغی
/// همه از یک استخر رأس مشترک ساخته می‌شوند تا مش تا حد امکان آب‌بند (watertight) بماند.
/// </summary>
public static class CylindricalGearMeshBuilder
{
    private const string BoreRing = "bore";
    private const string SpokeRing = "spoke";
    private const string HubInner = "hub-in";
    private const string HubOuter = "hub-out";
    private const string GapRing = "gap";

    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality)
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
        IProfileGenerator profile = internalGear
            ? new CircleProfile(fig.RootRadius)
            : new InvoluteToothProfile(fig, fig.RootFilletRadius);

        var boundary = profile.BoundaryPoints(tol);
        if (boundary.Count < 8)
            throw new GeometryBuildException("پروفیل تولیدشده نقاط کافی ندارد.", errors);

        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);
        var extraWarnings = new List<GearDiagnostic>();
        var b = new BuilderContext(mesh, pool, boundary, fig, teeth, body, tol, internalGear, extraWarnings);

        b.ComputeRadii();
        b.BuildSlices(family, quality);
        b.BuildProfileLoops();
        b.BuildSpokeRing(quality);
        b.BuildCaps(body);
        b.BuildToothWalls();
        b.BuildBoreWalls(body);
        b.BuildLighteningHoles(body, quality);
        b.BuildHub(body);
        b.BuildHerringboneGap(family, teeth, quality);

        if (mesh.TriangleCount == 0)
            throw new GeometryBuildException("مش خالی ساخته شد؛ ورودی‌ها را بررسی کنید.", diags);

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags.Concat(extraWarnings).ToList(),
            AxialSlicesUsed = pool.SliceCount,
            EffectiveChordToleranceMm = tol,
        };
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
        public double SpokeRadius { get; set; }
        public (double Theta, double Radius)[] SpokeSamples { get; set; } = Array.Empty<(double, double)>();
        public double RootRadiusUsed { get; set; }

        /// <summary>شعاع مرز در زاویهٔ دلخواه — درون‌یابی خطی روی نقاط مرز (مرز نسبت به مرکز ستاره‌ای است).</summary>
        public double BoundaryRadiusAt(double theta)
        {
            double TwoPi = 2.0 * Math.PI;
            var pts = Boundary;
            double t0 = pts[0].Theta;
            double tEnd = pts[^1].Theta;
            double span = tEnd - t0;

            double local = theta;
            while (local < t0) local += span;
            while (local > tEnd) local -= span;
            if (span < 1e-12) return pts[0].Radius;

            // جست‌وجوی دودویی روی زاویه‌های صعودی
            int lo = 0, hi = pts.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (pts[mid].Theta <= local) lo = mid; else hi = mid;
            }
            double a = pts[lo].Theta, b = pts[hi].Theta;
            if (b - a < 1e-12) return pts[lo].Radius;
            double k = (local - a) / (b - a);
            return pts[lo].Radius + (pts[hi].Radius - pts[lo].Radius) * k;
        }
    }

    private static void ComputeRadii(this BuilderContext b)
    {
        double rTip = b.Fig.TipRadius;
        double rRoot = b.Fig.RootRadius;

        if (b.Internal)
        {
            // ماده بین rim داخلی و ریشه؛ بور همان rim است
            b.RootRadiusUsed = Math.Min(rRoot, rTip);
            b.BoreRadius = Math.Max(0.5 * b.Body.BoreDiameter, 0.3 * b.Teeth.Module);
            if (b.BoreRadius >= b.RootRadiusUsed)
                b.BoreRadius = Math.Max(0.25 * b.RootRadiusUsed, 0.3 * b.Teeth.Module);
        }
        else
        {
            b.RootRadiusUsed = Math.Max(rRoot, b.Fig.BaseRadius);
            double defaultBore = 0.5 * b.Body.BoreDiameter;
            if (defaultBore >= b.RootRadiusUsed * 0.98)
                defaultBore = 0.5 * b.RootRadiusUsed; // سقف: بور حداکثر تا ریشه
            b.BoreRadius = defaultBore;
        }

        // حلقهٔ پره (spoke) بین بور و مرز — همیشه کوچک‌تر از کمینهٔ مرز در نیم‌صفحهٔ مجاز
        double minBoundary = double.MaxValue;
        foreach (var p in b.Boundary)
            minBoundary = Math.Min(minBoundary, b.Internal ? Math.Max(p.Radius, b.BoreRadius + 1e-6) : p.Radius);
        if (b.Internal)
            minBoundary = b.RootRadiusUsed;

        double lo = b.BoreRadius;
        double hi = Math.Min(minBoundary * 0.98, b.Internal ? minBoundary : b.RootRadiusUsed);
        b.SpokeRadius = b.BoreRadius <= 1e-9 ? 0 : lo + 0.5 * Math.Max(0, hi - lo);
    }

    private static void BuildSlices(this BuilderContext b, GearFamily family, GeometrySettings q)
    {
        double w = b.FaceWidth;

        if (family == GearFamily.ExternalHerringbone)
        {
            double gap = Math.Clamp(0.05 * w, 0, w * 0.2);
            double half = (w - gap) / 2.0;
            int n = Math.Clamp(q.AxialSliceCount, 4, 200); // برش‌های هر نیمه
            double rPitch = b.Fig.PitchRadius;
            double beta = b.Fig.HelixAngleUsed;
            double leadFactor = Math.Tan(beta) / rPitch;

            // نیمهٔ پایینی (پیچش مثبت) و بالایی (منفی) + گپ مرکزی
            b.Pool.AddSlice(+leadFactor * 0.0, 0);
            for (int i = 1; i <= n; i++)
            {
                double zLocal = half * i / n;
                b.Pool.AddSlice(leadFactor * zLocal, zLocal);
            }
            double zHub = half;
            for (int i = 0; i <= 4; i++) // گپ مرکزی
                b.Pool.AddSlice(0.0, zHub + gap * i / 4);
            double zTopStart = half + gap;
            for (int i = 0; i <= n; i++)
            {
                double zLocal = half * (1.0 - (double)i / n);
                b.Pool.AddSlice(-leadFactor * zLocal, zTopStart + (half - zLocal) + 0); // پیچش معکوس
            }
            return;
        }

        if (Math.Abs(b.Fig.HelixAngleUsed) > 1e-9)
        {
            int n = Math.Clamp(q.AxialSliceCount, 2, 400);
            double leadFactor = Math.Tan(b.Fig.HelixAngleUsed) / b.Fig.PitchRadius;
            for (int i = 0; i <= n; i++)
            {
                double z = w * i / n;
                b.Pool.AddSlice(leadFactor * z, z);
            }
            return;
        }

        b.Pool.AddSlice(0, 0);
        b.Pool.AddSlice(0, w);
    }

    private static void BuildProfileLoops(this BuilderContext b)
    {
        for (int s = 0; s < b.Pool.SliceCount; s++)
        {
            double twist = b.Pool.Twist(s);
            foreach (var p in b.Boundary)
                b.Pool.ProfilePoint(s, p);
        }
    }

    private static void BuildSpokeRing(this BuilderContext b, GeometrySettings q)
    {
        int n = Math.Clamp(CircleProfile.SegmentsForCircle(Math.Max(b.SpokeRadius, 1e-6), b.Tol), 16, 360);
        var samples = new (double, double)[n];
        for (int i = 0; i < n; i++)
        {
            double theta = 2.0 * Math.PI * i / n;
            samples[i] = (theta, b.BoundaryRadiusAt(theta));
        }
        b.SpokeSamples = samples;

        for (int s = 0; s < b.Pool.SliceCount; s++)
        {
            double twist = b.Pool.Twist(s);
            var ids = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                double t = samples[i].Theta + twist;
                ids.Add(b.Pool.Free(s, samples[i].Radius * Math.Cos(t), samples[i].Radius * Math.Sin(t)));
            }
            b.Pool.SetSpokeLoop(s, ids);
        }
    }

    private static void BuildCaps(this BuilderContext b, BodyFeatures body)
    {
        int top = b.Pool.SliceCount - 1;

        // صفحهٔ پایین (نرمال −Z)
        if (b.SpokeRadius <= 1e-9)
        {
            var center = b.Pool.Center(0);
            VertexPool.DiskNZ(b.Mesh, b.Pool.SpokeLoop(0), center);
        }
        else if (body.KeywayWidth > 0 && !b.Internal)
        {
            var kw = b.Pool.RingWithKeyway(0, b.BoreRadius, body.KeywayWidth, body.KeywayDepth, b.Tol);
            VertexPool.AnnulusNZ(b.Mesh, kw, b.Pool.SpokeLoop(0));
        }
        else
        {
            var bore = b.Pool.Ring(0, BoreRing, b.BoreRadius, b.Tol);
            VertexPool.AnnulusNZ(b.Mesh, bore, b.Pool.SpokeLoop(0));
        }

        // صفحهٔ بالا (نرمال +Z)
        if (b.SpokeRadius <= 1e-9)
        {
            var center = b.Pool.Center(top);
            VertexPool.DiskZ(b.Mesh, b.Pool.SpokeLoop(top), center);
        }
        else if (body.KeywayWidth > 0 && !b.Internal)
        {
            var kw = b.Pool.RingWithKeyway(top, b.BoreRadius, body.KeywayWidth, body.KeywayDepth, b.Tol);
            VertexPool.AnnulusZ(b.Mesh, kw, b.Pool.SpokeLoop(top));
        }
        else
        {
            var bore = b.Pool.Ring(top, BoreRing, b.BoreRadius, b.Tol);
            VertexPool.AnnulusZ(b.Mesh, bore, b.Pool.SpokeLoop(top));
        }
    }

    private static void BuildToothWalls(this BuilderContext b)
    {
        for (int s = 0; s + 1 < b.Pool.SliceCount; s++)
        {
            double dz = b.Pool.Z(s + 1) - b.Pool.Z(s);
            double dTw = Math.Abs(b.Pool.Twist(s + 1) - b.Pool.Twist(s));
            if (Math.Abs(dz) < 1e-12 && dTw < 1e-12)
                continue; // برش‌های تکراری (گپ مرکزی جناغی)
            VertexPool.WallCCW(b.Mesh, b.Pool.ProfileLoop(s), b.Pool.ProfileLoop(s + 1));
        }
    }

    private static void BuildBoreWalls(this BuilderContext b, BodyFeatures body)
    {
        if (b.BoreRadius <= 1e-9)
            return;

        bool hasHub = !b.Internal && body.HubDiameter > 0 && body.HubDiameter / 2.0 > b.BoreRadius;
        int top = b.Pool.SliceCount - 1;

        if (!hasHub)
        {
            var lo = BoreLoop(b, 0, body);
            var hi = BoreLoop(b, top, body);
            VertexPool.WallCCW(b.Mesh, lo, hi);
            return;
        }

        // دیوارهٔ بور در دو پهلو؛ میانه = ابتدای توپی
        double zHub = HubStartZ(b);
        int mid = ClosestSlice(b, zHub);
        var bottom = BoreLoop(b, 0, body);
        var middle = BoreLoop(b, mid, body);
        var upper = BoreLoop(b, top, body);
        VertexPool.WallCCW(b.Mesh, bottom, middle);
        VertexPool.WallCCW(b.Mesh, middle, upper);
    }

    private static List<int> BoreLoop(BuilderContext b, int slice, BodyFeatures body)
        => body.KeywayWidth > 0 && !b.Internal
            ? b.Pool.RingWithKeyway(slice, b.BoreRadius, body.KeywayWidth, body.KeywayDepth, b.Tol)
            : b.Pool.Ring(slice, BoreRing, b.BoreRadius, b.Tol);

    private static double HubStartZ(BuilderContext b)
    {
        // توپی از سطح بالای بدنه شروع می‌شود
        return b.FaceWidth;
    }

    private static int ClosestSlice(BuilderContext b, double z)
    {
        int best = 0;
        double bestDiff = double.MaxValue;
        for (int s = 0; s < b.Pool.SliceCount; s++)
        {
            double d = Math.Abs(b.Pool.Z(s) - z);
            if (d < bestDiff) { bestDiff = d; best = s; }
        }
        return best;
    }

    private static void BuildLighteningHoles(this BuilderContext b, BodyFeatures body, GeometrySettings q)
    {
        if (b.Internal || body.LighteningHoleCount <= 0 || body.LighteningHoleDiameter <= 0)
            return;
        // توجه: سوراخ‌های سبک‌سازی وقتی بور هم باشد ناحیهٔ میانه چندمؤلفه‌ای می‌شود؛
        // برای نسخهٔ اول فقط در حالت بدون بور فعال است (محدودیت مستندشده).
        if (b.Body.BoreDiameter > 0)
        {
            b.ExtraWarnings.Add(new GearDiagnostic(
                DiagnosticCodes.MeshTooHeavy, Severity.Warning,
                "سوراخ‌های سبک‌سازی در حضور بور مرکزی در این نسخه نادیده گرفته می‌شوند؛ برای حفره‌ها، بور را صفر بگذارید.",
                "LighteningHoleCount"));
            return;
        }

        double rHole = body.LighteningHoleDiameter / 2.0;
        double rCircle = body.LighteningHoleCircleRadius;
        double rMaxAllowed = Math.Max(0, Math.Min(b.RootRadiusUsed - rHole - 0.5 * b.Teeth.Module, b.SpokeRadius));
        if (rCircle <= 0 || rMaxAllowed <= rHole)
            return; // بی‌معنی/بیرون از بدنه — بی‌خطر نادیده گرفته می‌شود

        rCircle = Math.Min(rCircle, rMaxAllowed);
        double startAngle = 0;
        for (int h = 0; h < body.LighteningHoleCount; h++)
        {
            double a = startAngle + 2.0 * Math.PI * h / body.LighteningHoleCount;
            double cx = rCircle * Math.Cos(a), cy = rCircle * Math.Sin(a);
            int top = b.Pool.SliceCount - 1;
            var lo = b.Pool.HoleRing(0, cx, cy, rHole, b.Tol);
            var hi = b.Pool.HoleRing(top, cx, cy, rHole, b.Tol);
            VertexPool.DiskNZ(b.Mesh, lo, lo[0]); // دیسک پایین با نرمال −Z (مرکز روی خود حلقه = دایرهٔ کامل)
            VertexPool.WallCCW(b.Mesh, lo, hi);
            VertexPool.DiskZ(b.Mesh, hi, hi[0]);
        }
    }

    private static void BuildHub(this BuilderContext b, BodyFeatures body)
    {
        if (b.Internal)
            return;
        double hubR = body.HubDiameter / 2.0;
        if (hubR <= b.BoreRadius || body.HubLength <= 0)
            return;
        if (hubR >= b.RootRadiusUsed)
            return; // توپی بزرگ‌تر از ریشه بی‌معنی است؛ نادیده گرفته می‌شود

        double zBottom = b.FaceWidth;
        double zTop = b.FaceWidth + body.HubLength;
        double tol = b.Tol;

        var boreBottom = b.Pool.RingAtZ(zBottom, BoreRing, b.BoreRadius, tol);
        var hubInBottom = b.Pool.RingAtZ(zBottom, HubInner, b.BoreRadius, tol);
        var hubOutBottom = b.Pool.RingAtZ(zBottom, HubOuter, hubR, tol);
        var hubInTop = b.Pool.RingAtZ(zTop, HubInner, b.BoreRadius, tol);
        var hubOutTop = b.Pool.RingAtZ(zTop, HubOuter, hubR, tol);

        // کف توپی (حلقهٔ بور→توپی) با نرمال +Z
        VertexPool.AnnulusZ(b.Mesh, hubInBottom, hubOutBottom);
        // دیوارهٔ بیرونی توپی
        VertexPool.WallCCW(b.Mesh, hubOutBottom, hubOutTop);
        // دیوارهٔ داخل توپی (ادامهٔ بور)
        VertexPool.WallCCW(b.Mesh, hubInBottom, hubInTop);
        // سر توپی با نرمال +Z
        VertexPool.AnnulusZ(b.Mesh, hubInTop, hubOutTop);
    }

    private static void BuildHerringboneGap(this BuilderContext b, GearFamily family, GeometrySettings q)
    {
        if (family != GearFamily.ExternalHerringbone)
            return;
        double w = b.FaceWidth;
        double gap = Math.Clamp(0.05 * w, 0, w * 0.2);
        if (gap <= 1e-9)
            return;

        double half = (w - gap) / 2.0;
        double zLo = half, zHi = half + gap;
        double tol = b.Tol;
        int segments = Math.Clamp(CircleProfile.SegmentsForCircle(b.RootRadiusUsed, tol), 24, 360);

        var outerLo = b.Pool.RingAtZ(zLo, GapRing + "-o-lo", b.RootRadiusUsed, tol);
        var outerHi = b.Pool.RingAtZ(zHi, GapRing + "-o-hi", b.RootRadiusUsed, tol);
        VertexPool.WallCCW(b.Mesh, outerLo, outerHi);

        if (b.BoreRadius > 1e-9)
        {
            var innerLo = b.Pool.RingAtZ(zLo, GapRing + "-i-lo", b.BoreRadius, tol);
            var innerHi = b.Pool.RingAtZ(zHi, GapRing + "-i-hi", b.BoreRadius, tol);
            VertexPool.WallCCW(b.Mesh, innerLo, innerHi);
            VertexPool.AnnulusNZ(b.Mesh, innerLo, outerLo); // کف گپ
            VertexPool.AnnulusZ(b.Mesh, innerHi, outerHi);  // سر گپ
        }
        else
        {
            var cLo = b.Pool.CenterAtZ(zLo);
            var cHi = b.Pool.CenterAtZ(zHi);
            VertexPool.DiskNZ(b.Mesh, outerLo, cLo);
            VertexPool.DiskZ(b.Mesh, outerHi, cHi);
        }
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
