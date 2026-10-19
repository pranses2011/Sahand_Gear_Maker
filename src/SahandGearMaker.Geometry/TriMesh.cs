using System.Runtime.InteropServices;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// مش مثلثی مستقل از UI (الزام سند ۰۳: هستهٔ هندسی وابسته به UI نباشد).
/// رئوس یکتا؛ مثلث‌ها با ایندکس؛ جهت‌گیری پادساعتگرد = نرمال بیرونی.
/// </summary>
public sealed class TriMesh
{
    private readonly List<Vector3> _vertices = new();
    private readonly List<Triangle> _triangles = new();

    public ReadOnlySpan<Vector3> Vertices => _vertices.Count > 0 ? CollectionsMarshal.AsSpan(_vertices) : ReadOnlySpan<Vector3>.Empty;
    public ReadOnlySpan<Triangle> Triangles => _triangles.Count > 0 ? CollectionsMarshal.AsSpan(_triangles) : ReadOnlySpan<Triangle>.Empty;

    public int VertexCount => _vertices.Count;
    public int TriangleCount => _triangles.Count;

    /// <summary>دسترسی مستقیم به رأس با ایندکس (برای مثلث‌بندی‌های زاویه‌محور).</summary>
    public Vector3 VertexAt(int index) => _vertices[index];

    public int AddVertex(Vector3 v)
    {
        _vertices.Add(v);
        return _vertices.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        if ((uint)a >= (uint)_vertices.Count || (uint)b >= (uint)_vertices.Count || (uint)c >= (uint)_vertices.Count)
            throw new ArgumentOutOfRangeException("ایندکس مثلث خارج از محدودهٔ رئوس است.");
        if (a == b || b == c || a == c)
            return; // مثلث یکنواخت ناخواسته نادیده گرفته می‌شود
        _triangles.Add(new Triangle(a, b, c));
    }

    /// <summary>جایگزینی مثلث i با جهت جدید (برای یکسان‌سازی جهت‌گیری نرمال‌ها).</summary>
    public void SetTriangle(int i, int a, int b, int c)
    {
        if ((uint)i >= (uint)_triangles.Count)
            throw new ArgumentOutOfRangeException(nameof(i));
        if ((uint)a >= (uint)_vertices.Count || (uint)b >= (uint)_vertices.Count || (uint)c >= (uint)_vertices.Count)
            throw new ArgumentOutOfRangeException("ایندکس مثلث خارج از محدودهٔ رئوس است.");
        _triangles[i] = new Triangle(a, b, c);
    }

    /// <summary>یکسان‌سازی جهت‌گیری مثلث‌ها (سازگار + رو به بیرون) — برای رندر و خروجی چاپ سالم.</summary>
    public MeshOrientationReport NormalizeOrientation() => MeshOrientationFixer.Orient(this);

    public void AddMesh(TriMesh other, Vector3 offset = default, double rotateZ = 0.0, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (scale <= 0 || !double.IsFinite(scale))
            scale = 1.0;
        int baseIndex = _vertices.Count;
        bool uniform = scale == 1.0;
        foreach (var v in other._vertices)
        {
            var u = uniform ? v : v * scale;
            _vertices.Add(rotateZ == 0 ? u + offset : u.RotateZ(rotateZ) + offset);
        }
        foreach (var t in other._triangles)
            _triangles.Add(new Triangle(baseIndex + t.A, baseIndex + t.B, baseIndex + t.C));
    }

    /// <summary>جعبهٔ محیطی (AABB) مش؛ برای قاب‌کردن صحنه و آزمون‌ها.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (_vertices.Count == 0)
            return (Vector3.Zero, Vector3.Zero);
        var min = _vertices[0];
        var max = _vertices[0];
        foreach (var v in _vertices)
        {
            min = new Vector3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new Vector3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }
        return (min, max);
    }

    /// <summary>
    /// اعتبارسنجی توپولوژی مش (سند ۰۴، بخش ۶):
    /// مختصات متناهی، بدون مثلث مساحت‌صفر، و هر یال جهت‌دار دقیقاً یک‌بار ظاهر شود (منیفولد/آب‌بند).
    /// </summary>
    public MeshValidationReport Validate()
    {
        var report = new MeshValidationReport();
        foreach (var v in _vertices)
        {
            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
            {
                report.Issues.Add("مختصات نامتناهی (NaN/Infinity) در رئوس مش وجود دارد.");
                break;
            }
        }

        double area = 0;
        foreach (var t in _triangles)
            area += t.Area(Vertices);

        if (_triangles.Count == 0)
            report.Issues.Add("مش هیچ مثلثی ندارد.");
        if (area <= 0)
            report.Issues.Add("مساحت کل مثلث‌ها صفر است.");

        // شمارش یال‌های جهت‌دار: در مش بستهٔ منیفولد، هر یال دقیقاً دو بار با جهت مخالف می‌آید.
        var edgeCount = new Dictionary<long, int>(_triangles.Count * 3);
        foreach (var t in _triangles)
        {
            CountEdge(t.A, t.B);
            CountEdge(t.B, t.C);
            CountEdge(t.C, t.A);
        }
        int openEdges = edgeCount.Values.Count(c => c != 2);
        if (openEdges > 0)
            report.Issues.Add($"تعداد {openEdges} یال جهت‌دار مرزی/معیوب یافت شد (مش کاملاً آب‌بند نیست).");
        report.ClosedManifold = openEdges == 0;
        report.TotalAreaMm2 = area;

        // حجم علامت‌دار با قضیهٔ واگردایی (برای مش بسته: حجم ماده)
        double volume = 0;
        foreach (var t in _triangles)
        {
            var a = _vertices[t.A];
            var b = _vertices[t.B];
            var c = _vertices[t.C];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        report.SignedVolumeMm3 = Math.Abs(volume);
        return report;

        void CountEdge(int u, int v)
        {
            long key = u < v ? (long)u * 400_000_009L + v : (long)v * 400_000_009L + u;
            edgeCount[key] = edgeCount.TryGetValue(key, out var c) ? c + 1 : 1;
        }
    }

    /// <summary>اعمال تبدیل نهایی (جابه‌جایی/چرخش) روی کپی مش — برای مونتاژ.</summary>
    public TriMesh Transformed(Vector3 offset, double rotateZ)
    {
        var copy = new TriMesh();
        foreach (var v in _vertices)
            copy._vertices.Add(rotateZ == 0 ? v + offset : v.RotateZ(rotateZ) + offset);
        foreach (var t in _triangles)
            copy._triangles.Add(t);
        return copy;
    }
}

/// <summary>گزارش اعتبارسنجی مش (سند ۰۴، بخش ۶).</summary>
public sealed class MeshValidationReport
{
    public bool ClosedManifold { get; set; }
    public double TotalAreaMm2 { get; set; }
    public double SignedVolumeMm3 { get; set; }
    public List<string> Issues { get; } = new();
    public bool IsOk => Issues.Count == 0;
}
