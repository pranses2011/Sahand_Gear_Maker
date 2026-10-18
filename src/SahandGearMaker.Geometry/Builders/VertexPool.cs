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
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments + _twists[slice];
            ids.Add(_mesh.AddVertex(new Vector3(radius * Math.Cos(a), radius * Math.Sin(a), _zs[slice])));
        }
        _rings[key] = ids;
        return ids;
    }

    /// <summary>حلقهٔ بور با شیار خار (برش + مستطیل خار در بالای دایره).</summary>
    public List<int> RingWithKeyway(int slice, double radius, double keywayWidth, double keywayDepth, double chordTol)
    {
        if (keywayWidth <= 0 || keywayDepth <= 0)
            return Ring(slice, "bore", radius, chordTol);

        var key = (slice, $"bore+kw{keywayWidth:F4}x{keywayDepth:F4}");
        if (_rings.TryGetValue(key, out var cached))
            return cached;

        double halfW = Math.Min(keywayWidth / 2.0, radius * Math.Sin(Math.PI / 4.0));
        double aEdge = Math.Asin(Math.Min(0.999, halfW / radius)); // زاویهٔ لبهٔ خار روی دایره
        double depth = Math.Min(keywayDepth, radius * 0.5);

        // کمان پایین دایره + سه گوشهٔ مستطیل خار؛ حلقه CCW می‌ماند
        int arcBottom = Math.Max(8, CircleProfile.SegmentsForCircle(radius, chordTol) / 2);
        var ids = new List<int>(arcBottom + 8);
        double t = _twists[slice];
        double z = _zs[slice];
        Vector3 V(double ang, double r) => new(r * Math.Cos(ang + t), r * Math.Sin(ang + t), z);

        for (int i = 0; i <= arcBottom; i++) // کمان از (π+aEdge) تا (2π−aEdge)
        {
            double a = Math.PI + aEdge + (Math.PI - 2.0 * aEdge) * i / arcBottom;
            ids.Add(_mesh.AddVertex(V(a, radius)));
        }
        ids.Add(_mesh.AddVertex(V(Math.PI - aEdge, radius)));         // لبهٔ چپ روی دایره
        ids.Add(_mesh.AddVertex(V(Math.PI - aEdge, radius + depth))); // گوشهٔ بیرونی چپ خار
        ids.Add(_mesh.AddVertex(V(Math.PI, radius + depth)));         // وسط خار
        ids.Add(_mesh.AddVertex(V(Math.PI + aEdge, radius + depth))); // گوشهٔ بیرونی راست خار
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

    public IReadOnlyList<int> ProfileLoop(int slice) => _profileIndices[slice];

    // ---------- سازنده‌های وجه با جهت‌گیری صحیح نرمال ----------

    /// <summary>دیوار بین دو حلقه هم‌تعداد؛ CCW حلقه ⇒ نرمال رو به بیرون ماده.</summary>
    public static void WallCCW(TriMesh mesh, IReadOnlyList<int> lower, IReadOnlyList<int> upper)
    {
        int n = Math.Min(lower.Count, upper.Count);
        if (n < 3 || lower.Count != upper.Count)
            throw new InvalidOperationException("دیوار بین حلقه‌های ناهم‌تعداد ساخته نمی‌شود.");
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(upper[i], upper[j], lower[j]);
            mesh.AddTriangle(upper[i], lower[j], lower[i]);
        }
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

    /// <summary>نوار بین دو حلقه هم‌تعداد در یک صفحه؛ CCW ⇒ نرمال +Z.</summary>
    public static void AnnulusZ(TriMesh mesh, IReadOnlyList<int> inner, IReadOnlyList<int> outer)
    {
        int n = Math.Min(inner.Count, outer.Count);
        if (inner.Count != outer.Count)
            throw new InvalidOperationException("نوار بین حلقه‌های ناهم‌تعداد ساخته نمی‌شود.");
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(inner[i], inner[j], outer[j]);
            mesh.AddTriangle(inner[i], outer[j], outer[i]);
        }
    }

    /// <summary>نوار بین دو حلقه؛ CCW ⇒ نرمال −Z.</summary>
    public static void AnnulusNZ(TriMesh mesh, IReadOnlyList<int> inner, IReadOnlyList<int> outer)
    {
        int n = Math.Min(inner.Count, outer.Count);
        if (inner.Count != outer.Count)
            throw new InvalidOperationException("نوار بین حلقه‌های ناهم‌تعداد ساخته نمی‌شود.");
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(inner[i], outer[i], outer[j]);
            mesh.AddTriangle(inner[i], outer[j], inner[j]);
        }
    }
}
