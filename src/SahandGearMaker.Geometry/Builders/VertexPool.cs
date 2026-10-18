using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// استخر رأس مش با «حلقه‌های مشترک» بین وجوه مجاور؛ تضمین می‌کند یال‌های مشترک
/// (مثلاً لبهٔ بور بین دیوار بور و صفحهٔ روی) روی همان رأس‌ها ساخته شوند تا مش آب‌بند بماند.
/// </summary>
internal sealed class VertexPool
{
    private readonly TriMesh _mesh;
    private readonly List<double> _twists = new();
    private readonly List<double> _zs = new();
    private readonly List<List<int>> _profileIndices = new();

    // حلقه‌های شعاعی: کلید = (slice, دایره)
    private readonly Dictionary<(int Slice, string Ring), List<int>> _rings = new();
    private readonly Dictionary<(int Slice, string Ring), List<double>> _ringAngles = new();

    // حلقه‌های خارج از مرکز: (slice, مرکز, شعاع)
    private readonly Dictionary<(int Slice, double Cx, double Cy, double R), List<int>> _holes = new();

    private readonly Dictionary<int, int> _centers = new();

    // حلقهٔ پره (نمونه‌های شعاعی از مرز) برای هر برش
    private readonly Dictionary<int, List<int>> _spokeLoops = new();

    // حلقه/رأس در ارتفاع دلخواه (توپی و گپ)
    private readonly Dictionary<(double Z, string Key), List<int>> _zRings = new();
    private readonly Dictionary<double, int> _zCenters = new();

    public VertexPool(TriMesh mesh) => _mesh = mesh;

    public int SliceCount => _zs.Count;

    public double Twist(int slice) => _twists[slice];
    public double Z(int slice) => _zs[slice];

    /// <summary>ثبت برش جدید (زاویهٔ پیچش و ارتفاع) و بازگرداندن شناسهٔ آن.</summary>
    public int AddSlice(double twist, double z)
    {
        _twists.Add(twist);
        _zs.Add(z);
        _profileIndices.Add(new List<int>());
        return _zs.Count - 1;
    }

    /// <summary>نقطهٔ مرزی پروفیل در برش داده‌شده (با چرخش پیچش برش).</summary>
    public int ProfilePoint(int slice, PolarPoint p)
    {
        var list = _profileIndices[slice];
        double t = p.Theta + _twists[slice];
        int idx = _mesh.AddVertex(new Vector3(p.Radius * Math.Cos(t), p.Radius * Math.Sin(t), _zs[slice]));
        list.Add(idx);
        return idx;
    }

    /// <summary>رأس مرکز برش (برای دیسک بدون بور).</summary>
    public int Center(int slice)
    {
        if (_centers.TryGetValue(slice, out var idx))
            return idx;
        idx = _mesh.AddVertex(new Vector3(0, 0, _zs[slice]));
        _centers[slice] = idx;
        return idx;
    }

    /// <summary>ثبت حلقهٔ پره یک برش (نمونه‌های شعاعی مرز).</summary>
    public void SetSpokeLoop(int slice, List<int> ids) => _spokeLoops[slice] = ids;

    /// <summary>حلقهٔ پره برش.</summary>
    public List<int> SpokeLoop(int slice) => _spokeLoops[slice];

    /// <summary>حلقهٔ دایرهٔ هم‌مرکز در ارتفاع دلخواه (خارج از برش‌های اصلی — توپی/گپ).</summary>
    public List<int> RingAtZ(double z, string key, double radius, double chordTol)
    {
        var k = (Math.Round(z, 6), key);
        if (_zRings.TryGetValue(k, out var cached))
            return cached;
        int segments = CircleProfile.SegmentsForCircle(radius, chordTol);
        var ids = new List<int>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            ids.Add(_mesh.AddVertex(new Vector3(radius * Math.Cos(a), radius * Math.Sin(a), z)));
        }
        _zRings[k] = ids;
        return ids;
    }

    /// <summary>رأس مرکز در ارتفاع دلخواه.</summary>
    public int CenterAtZ(double z)
    {
        double key = Math.Round(z, 6);
        if (_zCenters.TryGetValue(key, out var idx))
            return idx;
        idx = _mesh.AddVertex(new Vector3(0, 0, z));
        _zCenters[key] = idx;
        return idx;
    }

    /// <summary>حلقهٔ دایرهٔ هم‌مرکز (بور/ریشه/توپی) — یکتا در هر برش؛ زاویه‌ها از 0 شروع و CCW.</summary>
    public List<int> Ring(int slice, string ringKey, double radius, double chordTol)
    {
        var key = (slice, ringKey);
        if (_rings.TryGetValue(key, out var cached))
            return cached;

        int segments = CircleProfile.SegmentsForCircle(radius, chordTol);
        var ids = new List<int>(segments);
        var angles = new List<double>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            angles.Add(a);
            ids.Add(_mesh.AddVertex(new Vector3(radius * Math.Cos(a + _twists[slice]), radius * Math.Sin(a + _twists[slice]), _zs[slice])));
        }
        _rings[key] = ids;
        _ringAngles[key] = angles;
        return ids;
    }

    /// <summary>زاویه‌های پایهٔ حلقهٔ ثبت‌شده (پیش از پیچش) — برای هم‌زاویه‌سازی حلقهٔ پره.</summary>
    public IReadOnlyList<double> RingBaseAngles(int slice, string ringKey)
        => _ringAngles.TryGetValue((slice, ringKey), out var a) ? a : Array.Empty<double>();

    /// <summary>حلقهٔ بور با شیار خار (برش + شیار روی دایره؛ انتهای تخت یا نیم‌دایره).</summary>
    public List<int> RingWithKeyway(int slice, double radius, double keywayWidth, double keywayDepth, double chordTol, double endRadius = 0.0)
    {
        if (keywayWidth <= 0 || keywayDepth <= 0)
            return Ring(slice, "bore", radius, chordTol);

        var key = (slice, $"bore+kw{keywayWidth:F4}x{keywayDepth:F4}x{endRadius:F4}");
        if (_rings.TryGetValue(key, out var cached))
            return cached;

        double halfW = Math.Min(keywayWidth / 2.0, radius * Math.Sin(Math.PI / 4.0));
        double aEdge = Math.Asin(Math.Min(0.999, halfW / radius)); // زاویهٔ لبهٔ خار روی دایره
        double depth = Math.Min(keywayDepth, radius * 0.5);

        // کمان کامل دایره + شیار؛ حلقه CCW می‌ماند
        // پیمایش: کمان پایین (π+aEdge → 2π−aEdge) + کمان بالا (2π−aEdge → π−aEdge از طریق ۰)
        //          + لبهٔ چپ + ته شیار + لبهٔ راست — دور کامل بدون نقطهٔ تکراری
        int arcBottom = Math.Max(8, CircleProfile.SegmentsForCircle(radius, chordTol) / 2);
        var ids = new List<int>(2 * arcBottom + 12);
        double t = _twists[slice];
        double z = _zs[slice];
        Vector3 V(double ang, double r) => new(r * Math.Cos(ang + t), r * Math.Sin(ang + t), z);

        for (int i = 1; i <= arcBottom; i++) // کمان پایین از (π+aEdge) تا (2π−aEdge)
        {
            double a = Math.PI + aEdge + (Math.PI - 2.0 * aEdge) * i / arcBottom;
            ids.Add(_mesh.AddVertex(V(a, radius)));
        }
        for (int i = 1; i < arcBottom; i++) // کمان بالا از (2π−aEdge) تا پیش از (π−aEdge)
        {
            double a = 2.0 * Math.PI - aEdge + Math.PI * i / arcBottom;
            ids.Add(_mesh.AddVertex(V(a, radius)));
        }
        ids.Add(_mesh.AddVertex(V(Math.PI - aEdge, radius)));         // لبهٔ چپ روی دایره

        if (endRadius > 1e-9)
        {
            // شیار با ته نیم‌دایره (فرم فرِیزِ انتهایی) — شعاع سقف‌گذاری‌شده
            double rE = Math.Min(endRadius, halfW);
            double rCorner = radius + depth - rE; // شعاع مرکز کمان ته
            int arcSteps = Math.Clamp((int)Math.Ceiling(Math.PI * rE / Math.Max(1e-9, 4.0 * chordTol)), 4, 24);

            ids.Add(_mesh.AddVertex(V(Math.PI - aEdge, rCorner)));      // کنارهٔ چپ عمودی
            for (int i = 0; i <= arcSteps; i++) // کمان ته از چپ به راست (CCW)
            {
                double a = -Math.PI / 2.0 + Math.PI * i / arcSteps; // از −۹۰° به +۹۰° حول مرکز
                double rr = rCorner + rE * Math.Sin(a);
                double aa = Math.PI + (rE * Math.Cos(a)) / Math.Max(1e-9, rCorner);
                ids.Add(_mesh.AddVertex(V(aa, rr)));
            }
            ids.Add(_mesh.AddVertex(V(Math.PI + aEdge, rCorner)));    // کنارهٔ راست عمودی
        }
        else
        {
            ids.Add(_mesh.AddVertex(V(Math.PI - aEdge, radius + depth))); // گوشهٔ بیرونی چپ خار
            ids.Add(_mesh.AddVertex(V(Math.PI, radius + depth)));         // وسط خار
            ids.Add(_mesh.AddVertex(V(Math.PI + aEdge, radius + depth))); // گوشهٔ بیرونی راست خار
        }

        ids.Add(_mesh.AddVertex(V(Math.PI + aEdge, radius)));         // لبهٔ راست روی دایره

        _rings[key] = ids;
        return ids;
    }

    /// <summary>حلقهٔ دایرهٔ خارج از مرکز (سوراخ‌های سبک‌سازی)؛ زاویه‌ها CCW حول مرکز خودش.</summary>
    public List<int> HoleRing(int slice, double cx, double cy, double radius, double chordTol)
    {
        var key = (slice, Math.Round(cx, 6), Math.Round(cy, 6), Math.Round(radius, 6));
        if (_holes.TryGetValue(key, out var cached))
            return cached;

        double twist = _twists[slice];
        double tx = cx * Math.Cos(twist) - cy * Math.Sin(twist);
        double ty = cx * Math.Sin(twist) + cy * Math.Cos(twist);

        int segments = Math.Clamp(CircleProfile.SegmentsForCircle(radius, chordTol) / 2, 8, 96);
        var ids = new List<int>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            ids.Add(_mesh.AddVertex(new Vector3(tx + radius * Math.Cos(a), ty + radius * Math.Sin(a), _zs[slice])));
        }
        _holes[key] = ids;
        return ids;
    }

    /// <summary>رأس دلخواه (خارج از حلقه‌ها) روی برش.</summary>
    public int Free(int slice, double x, double y)
        => _mesh.AddVertex(new Vector3(x, y, _zs[slice]));

    public List<int> ProfileLoop(int slice) => _profileIndices[slice];

    // ---------- سازنده‌های وجه با جهت‌گیری صحیح نرمال ----------

    /// <summary>
    /// دیوار بین دو حلقه (احتمالاً ناهم‌تعداد) با «قدم‌زدن ادغامی» برای سطح داخلی (بور/حفره):
    /// حلقه‌های CCW یعنی نرمال رو به داخل حفره (بیرون از ماده).
    /// </summary>
    public static void WallCCW(TriMesh mesh, IReadOnlyList<int> lower, IReadOnlyList<int> upper)
    {
        if (lower.Count < 3 || upper.Count < 3)
            throw new InvalidOperationException("دیوار به حلقهٔ با کمتر از ۳ رأس ساخته نمی‌شود.");
        if (lower.Count == upper.Count)
        {
            int n = lower.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mesh.AddTriangle(upper[i], upper[j], lower[j]);
                mesh.AddTriangle(upper[i], lower[j], lower[i]);
            }
            return;
        }

        int li = 0, ui = 0;
        int total = lower.Count + upper.Count;
        for (int step = 0; step < total && (li < lower.Count || ui < upper.Count); step++)
        {
            double lNext = AngleOf(mesh, lower[(li + 1) % lower.Count]);
            double uNext = AngleOf(mesh, upper[(ui + 1) % upper.Count]);
            bool advanceUpper;
            if (li >= lower.Count) advanceUpper = true;
            else if (ui >= upper.Count) advanceUpper = false;
            else advanceUpper = uNext <= lNext;

            if (advanceUpper)
            {
                mesh.AddTriangle(upper[ui % upper.Count], upper[(ui + 1) % upper.Count], lower[li % lower.Count]);
                ui++;
            }
            else
            {
                mesh.AddTriangle(upper[ui % upper.Count], lower[(li + 1) % lower.Count], lower[li % lower.Count]);
                li++;
            }
        }
    }

    /// <summary>
    /// دیوار بین دو حلقه برای سطح بیرونی ماده (دیوارهٔ دندانه، رینگ، توپی).
    /// حلقه‌های CCW یعنی نرمال رو به بیرون از ماده.
    /// </summary>
    public static void WallCCWOuter(TriMesh mesh, IReadOnlyList<int> lower, IReadOnlyList<int> upper)
    {
        if (lower.Count < 3 || upper.Count < 3)
            throw new InvalidOperationException("دیوار به حلقهٔ با کمتر از ۳ رأس ساخته نمی‌شود.");
        if (lower.Count == upper.Count)
        {
            int n = lower.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                mesh.AddTriangle(lower[i], lower[j], upper[j]);
                mesh.AddTriangle(lower[i], upper[j], upper[i]);
            }
            return;
        }

        int li = 0, ui = 0;
        int total = lower.Count + upper.Count;
        for (int step = 0; step < total && (li < lower.Count || ui < upper.Count); step++)
        {
            double lNext = AngleOf(mesh, lower[(li + 1) % lower.Count]);
            double uNext = AngleOf(mesh, upper[(ui + 1) % upper.Count]);
            bool advanceUpper;
            if (li >= lower.Count) advanceUpper = true;
            else if (ui >= upper.Count) advanceUpper = false;
            else advanceUpper = uNext <= lNext;

            if (advanceUpper)
            {
                mesh.AddTriangle(lower[li % lower.Count], upper[ui % upper.Count], upper[(ui + 1) % upper.Count]);
                ui++;
            }
            else
            {
                mesh.AddTriangle(lower[li % lower.Count], lower[(li + 1) % lower.Count], upper[ui % upper.Count]);
                li++;
            }
        }
    }

    private static double AngleOf(TriMesh mesh, int vertex)
    {
        var v = mesh.VertexAt(vertex);
        return Math.Atan2(v.Y, v.X);
    }

    /// <summary>حلقه با مرکز → دیسک تخت با نرمال +Z.</summary>
    public static void DiskZ(TriMesh mesh, IReadOnlyList<int> ring, int centerIdx)
    {
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(centerIdx, ring[i], ring[j]);
        }
    }

    /// <summary>حلقه با مرکز → دیسک تخت با نرمال −Z.</summary>
    public static void DiskNZ(TriMesh mesh, IReadOnlyList<int> ring, int centerIdx)
    {
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(centerIdx, ring[j], ring[i]);
        }
    }

    /// <summary>نوار بین دو حلقه هم‌مرکز؛ CCW یعنی نرمال +Z (روی ماده از بالا).</summary>
    public static void AnnulusZ(TriMesh mesh, IReadOnlyList<int> inner, IReadOnlyList<int> outer)
    {
        int n = Math.Min(inner.Count, outer.Count);
        if (inner.Count != outer.Count)
        {
            MergeWalkAnnulus(mesh, inner, outer, flip: true);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(inner[i], outer[i], outer[j]);
            mesh.AddTriangle(inner[i], outer[j], inner[j]);
        }
    }

    /// <summary>
    /// درپوش صفحه‌ای بین مرز پروفیل (بیرونی)، بور و توپی (اختیاری).
    /// بور↔پروفیل: مثلث‌بندی تک‌حفره (الگوی امن سازندهٔ استوانه‌ای؛ پروفیل شیارخار‌دار مجاز).
    /// توپی↔پروفیل: قدم‌زدن ادغامی زاویه‌ای (هر دو حلقهٔ صعودی زاویه‌ای).
    /// بدون بور: بادبزک از مرکز (پروفیل ستاره‌ای).
    /// </summary>
    internal static void AnnulusCap(
        TriMesh mesh, IReadOnlyList<int> profileLoop, IReadOnlyList<int>? boreLoop,
        IReadOnlyList<int>? middleLoop, bool negativeZ)
    {
        var outer = DedupLoop(mesh, profileLoop);
        if (outer.Count < 3)
            return;

        if (boreLoop is not { Count: > 2 })
        {
            if (middleLoop is { Count: > 2 })
            {
                // بدون بور، با توپی: درپوش = نوار توپی ↔ پروفیل (بور از داخل توپی مستقیم عبور می‌کند)
                var mid0 = DedupLoop(mesh, middleLoop);
                if (negativeZ)
                    AnnulusNZ(mesh, mid0, outer);
                else
                    AnnulusZ(mesh, mid0, outer);
                return;
            }
            double z = mesh.VertexAt(outer[0]).Z;
            var center = mesh.AddVertex(new Vector3(0, 0, z));
            if (negativeZ)
                DiskNZ(mesh, outer, center);
            else
                DiskZ(mesh, outer, center);
            return;
        }

        var bore = DedupLoop(mesh, boreLoop);
        var boreHole = new List<Point2>(bore.Count);
        var boreIdxRev = new List<int>(bore.Count);
        for (int i = bore.Count - 1; i >= 0; i--)
        {
            var v = mesh.VertexAt(bore[i]);
            boreHole.Add(new Point2(v.X, v.Y));
            boreIdxRev.Add(bore[i]);
        }

        if (middleLoop is { Count: > 2 })
        {
            var mid = DedupLoop(mesh, middleLoop);

            // توپی ↔ پروفیل: قدم‌زدن ادغامی
            if (negativeZ)
                AnnulusNZ(mesh, mid, outer);
            else
                AnnulusZ(mesh, mid, outer);

            // بور ↔ توپی: مثلث‌بندی تک‌حفره
            var hubPts = new List<Point2>(mid.Count);
            foreach (int idx in mid)
            {
                var v = mesh.VertexAt(idx);
                hubPts.Add(new Point2(v.X, v.Y));
            }
            var tris = PolygonTriangulator.Triangulate(hubPts, new List<List<Point2>> { boreHole });
            int hubCount = hubPts.Count;
            foreach (var (a, b, c) in tris)
            {
                int ia = a < hubCount ? mid[a] : boreIdxRev[a - hubCount];
                int ib = b < hubCount ? mid[b] : boreIdxRev[b - hubCount];
                int ic = c < hubCount ? mid[c] : boreIdxRev[c - hubCount];
                if (negativeZ)
                    mesh.AddTriangle(ia, ic, ib);
                else
                    mesh.AddTriangle(ia, ib, ic);
            }
        }
        else
        {
            // بور ↔ پروفیل: مثلث‌بندی تک‌حفره
            var profPts = new List<Point2>(outer.Count);
            foreach (int idx in outer)
            {
                var v = mesh.VertexAt(idx);
                profPts.Add(new Point2(v.X, v.Y));
            }
            var tris = PolygonTriangulator.Triangulate(profPts, new List<List<Point2>> { boreHole });
            int profCount = profPts.Count;
            foreach (var (a, b, c) in tris)
            {
                int ia = a < profCount ? outer[a] : boreIdxRev[a - profCount];
                int ib = b < profCount ? outer[b] : boreIdxRev[b - profCount];
                int ic = c < profCount ? outer[c] : boreIdxRev[c - profCount];
                if (negativeZ)
                    mesh.AddTriangle(ia, ic, ib);
                else
                    mesh.AddTriangle(ia, ib, ic);
            }
        }
    }

    /// <summary>حذف رئوس متوالی هم‌مکان از یک حلقه (نگاشت ایندکس حفظ می‌شود).</summary>
    internal static List<int> DedupLoop(TriMesh mesh, IReadOnlyList<int> loop)
    {
        var result = new List<int>(loop.Count);
        foreach (int idx in loop)
        {
            if (result.Count > 0)
            {
                var a = mesh.VertexAt(result[^1]);
                var b = mesh.VertexAt(idx);
                if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9)
                    continue;
            }
            result.Add(idx);
        }
        while (result.Count > 1)
        {
            var a = mesh.VertexAt(result[0]);
            var b = mesh.VertexAt(result[^1]);
            if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9)
                result.RemoveAt(result.Count - 1);
            else
                break;
        }
        return result;
    }

    /// <summary>نوار بین دو حلقه هم‌مرکز؛ CCW یعنی نرمال −Z (زیر ماده).</summary>
    public static void AnnulusNZ(TriMesh mesh, IReadOnlyList<int> inner, IReadOnlyList<int> outer)
    {
        int n = Math.Min(inner.Count, outer.Count);
        if (inner.Count != outer.Count)
        {
            MergeWalkAnnulus(mesh, inner, outer, flip: false);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(inner[i], inner[j], outer[j]);
            mesh.AddTriangle(inner[i], outer[j], outer[i]);
        }
    }

    /// <summary>مثلث‌بندی نوار با قدم‌زدن ادغامی روی زاویه‌ها — همیشه آب‌بند، صرف‌نظر از تعداد رأس‌ها.</summary>
    private static void MergeWalkAnnulus(TriMesh mesh, IReadOnlyList<int> inner, IReadOnlyList<int> outer, bool flip)
    {
        int ii = 0, oi = 0;
        int total = inner.Count + outer.Count;
        for (int step = 0; step < total && (ii < inner.Count || oi < outer.Count); step++)
        {
            double iNext = AngleOf(mesh, inner[(ii + 1) % inner.Count]);
            double oNext = AngleOf(mesh, outer[(oi + 1) % outer.Count]);
            bool advanceOuter;
            if (ii >= inner.Count) advanceOuter = true;
            else if (oi >= outer.Count) advanceOuter = false;
            else advanceOuter = oNext <= iNext;

            if (advanceOuter)
            {
                int o0 = outer[oi % outer.Count], o1 = outer[(oi + 1) % outer.Count];
                int iv = inner[ii % inner.Count];
                if (flip)
                {
                    mesh.AddTriangle(iv, o0, o1);
                }
                else
                {
                    mesh.AddTriangle(iv, o1, o0);
                }
                oi++;
            }
            else
            {
                int i0 = inner[ii % inner.Count], i1 = inner[(ii + 1) % inner.Count];
                int ov = outer[oi % outer.Count];
                if (flip)
                {
                    mesh.AddTriangle(i0, ov, i1);
                }
                else
                {
                    mesh.AddTriangle(i0, i1, ov);
                }
                ii++;
            }
        }
    }
}
