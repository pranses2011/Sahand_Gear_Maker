using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>گزارش عملیات یکسان‌سازی جهت‌گیری مثلث‌ها.</summary>
public readonly record struct MeshOrientationReport
{
    /// <summary>تعداد مثلث‌هایی که جهت آن‌ها معکوس شد.</summary>
    public int TrianglesFlipped { get; init; }
    /// <summary>تعداد مؤلفه‌های جدا (جامد مجزا) که پیدا شد.</summary>
    public int Components { get; init; }
    /// <summary>یال‌هایی که پس از اصلاح هنوز جهت ناسازگار دارند (ورودی غیرقابل‌جهت‌دهی).</summary>
    public int RemainingInconsistentEdges { get; init; }
    /// <summary>آیا مش از قبل سازگار بود و کاری انجام نشد؟</summary>
    public bool WasAlreadyConsistent { get; init; }
}

/// <summary>
/// یکسان‌ساز جهت‌گیری مثلث‌های مش (نسخهٔ ۰٫۵ — رفع «مش ناقص/راه‌راه»).
///
/// چرا لازم است؟ رندرر backface-culling را با «علامت حجم امضادار کل مش» انجام می‌دهد؛
/// اگر سازنده‌ای حتی تعداد محدودی مثلث را با جهت معکوس منتشر کند (مخروطی، شانه، تاجی،
/// هارمونیک)، آن نواحی یا حذف می‌شوند («قطعه فقط چند جای دیواره دیده می‌شود») یا
/// نرمال‌های رأسی میانگین‌گیری‌شده خنثی می‌شوند و سطح راه‌راه می‌شود.
///
/// الگوریتم (بدون وابستگی جدید):
///   ۱) پاس سبک: شمارش یال‌های ناسازگار؛ اگر صفر و حجم مثبت بود → خروج فوری (بدون هزینه).
///   ۲) در غیر این صورت: مؤلفه‌های هم‌بندی با BFS روی یال‌های مشترک، تخصیص «پریتی چرخش» به
///      هر مثلث (یال مشترک باید در دو مثلث در جهات مخالف پیموده شود)؛
///   ۳) اعمال معکوس‌ها + معکوس کامل مؤلفه‌های با حجم امضادار منفی (نرمال رو به بیرون).
/// ورودی‌های غیرقابل‌جهت‌دهی (خود-تقاطعی/غیراورینت‌پذیر) گزارش می‌شوند و دست‌نخورده می‌مانند.
/// </summary>
public static class MeshOrientationFixer
{
    private readonly record struct EdgeKey(int U, int V)
    {
        public static EdgeKey Create(int a, int b) => a < b ? new EdgeKey(a, b) : new EdgeKey(b, a);
    }

    /// <summary>یکسان‌سازی جهت‌گیری مثلث‌های یک مش، درجا.</summary>
    public static MeshOrientationReport Orient(TriMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int triCount = mesh.TriangleCount;
        if (triCount == 0)
            return default;

        var tris = mesh.Triangles.ToArray();

        // ---------- پاس سبک: آیا اصلاً کار لازم است؟ ----------
        double volume = 0;
        var dirCount = new Dictionary<EdgeKey, int>(triCount * 3);
        var triVolumes = new double[triCount];
        for (int i = 0; i < triCount; i++)
        {
            var t = tris[i];
            var a = mesh.VertexAt(t.A);
            var b = mesh.VertexAt(t.B);
            var c = mesh.VertexAt(t.C);
            double v = (a.X * (b.Y * c.Z - c.Y * b.Z) - a.Y * (b.X * c.Z - c.X * b.Z) + a.Z * (b.X * c.Y - c.X * b.Y)) / 6.0;
            triVolumes[i] = v;
            volume += v;
            AddDir(dirCount, t.A, t.B);
            AddDir(dirCount, t.B, t.C);
            AddDir(dirCount, t.C, t.A);
        }

        int inconsistent = CountInconsistent(dirCount);
        if (inconsistent == 0 && volume > 0)
            return new MeshOrientationReport { WasAlreadyConsistent = true };

        // ---------- مالتی‌پس یال → مثلث‌ها ----------
        var edgeOwners = new Dictionary<EdgeKey, List<int>>(triCount * 3);
        for (int i = 0; i < triCount; i++)
        {
            var t = tris[i];
            Register(edgeOwners, EdgeKey.Create(t.A, t.B), i);
            Register(edgeOwners, EdgeKey.Create(t.B, t.C), i);
            Register(edgeOwners, EdgeKey.Create(t.C, t.A), i);
        }

        // ---------- BFS مؤلفه‌ها + پریتی جهت ----------
        var flip = new bool[triCount];
        var visited = new bool[triCount];
        var componentMembers = new List<List<int>>();
        var componentVolumes = new List<double>();
        var queue = new Queue<int>();

        for (int start = 0; start < triCount; start++)
        {
            if (visited[start])
                continue;
            int comp = componentMembers.Count;
            var members = new List<int>();
            componentMembers.Add(members);
            componentVolumes.Add(0);

            visited[start] = true;
            flip[start] = false;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                members.Add(i);
                componentVolumes[comp] += flip[i] ? -triVolumes[i] : triVolumes[i];

                var t = tris[i];
                Propagate(i, t.A, t.B);
                Propagate(i, t.B, t.C);
                Propagate(i, t.C, t.A);

                void Propagate(int owner, int u, int v)
                {
                    var key = EdgeKey.Create(u, v);
                    if (!edgeOwners.TryGetValue(key, out var owners) || owners.Count != 2)
                        return; // یال مرزی یا غیرمنیفولد

                    int other = owners[0] == owner ? owners[1] : owners[0];
                    bool sameDirection = TriangleHasDirectedEdge(tris[other], u, v);

                    if (visited[other])
                        return; // سازگاری از طریق شمارش پایانی بررسی می‌شود

                    visited[other] = true;
                    // اگر جهت خالص یکسان است، همسایه باید معکوس شود تا یال در جهات مخالف پیموده شود
                    flip[other] = sameDirection ? !flip[owner] : flip[owner];
                    queue.Enqueue(other);
                }
            }
        }

        // ---------- معکوس‌سازی مؤلفه‌های با حجم منفی ----------
        for (int c = 0; c < componentMembers.Count; c++)
        {
            if (componentVolumes[c] >= 0)
                continue;
            foreach (int i in componentMembers[c])
                flip[i] = !flip[i];
        }

        // ---------- اعمال ----------
        int flippedCount = 0;
        for (int i = 0; i < triCount; i++)
        {
            if (!flip[i])
                continue;
            var t = tris[i];
            mesh.SetTriangle(i, t.A, t.C, t.B);
            flippedCount++;
        }

        int afterInconsistent = CountInconsistentEdges(mesh);

        return new MeshOrientationReport
        {
            TrianglesFlipped = flippedCount,
            Components = componentMembers.Count,
            RemainingInconsistentEdges = afterInconsistent,
            WasAlreadyConsistent = false,
        };
    }

    private static void AddDir(Dictionary<EdgeKey, int> map, int u, int v)
    {
        var key = EdgeKey.Create(u, v);
        int dir = u < v ? 1 : -1;
        map[key] = map.TryGetValue(key, out var d) ? d + dir : dir;
    }

    private static void Register(Dictionary<EdgeKey, List<int>> map, EdgeKey key, int owner)
    {
        if (map.TryGetValue(key, out var list))
        {
            if (list.Count < 2)
                list.Add(owner);
        }
        else
        {
            map[key] = new List<int>(2) { owner };
        }
    }

    private static bool TriangleHasDirectedEdge(Triangle t, int u, int v)
        => (t.A == u && t.B == v) || (t.B == u && t.C == v) || (t.C == u && t.A == v);

    private static int CountInconsistent(Dictionary<EdgeKey, int> dirCount)
    {
        int inconsistent = 0;
        foreach (var d in dirCount.Values)
            if (Math.Abs(d) >= 2)
                inconsistent++;
        return inconsistent;
    }

    private static int CountInconsistentEdges(TriMesh mesh)
    {
        var tris = mesh.Triangles.ToArray();
        var dirCount = new Dictionary<EdgeKey, int>(tris.Length * 3);
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            AddDir(dirCount, t.A, t.B);
            AddDir(dirCount, t.B, t.C);
            AddDir(dirCount, t.C, t.A);
        }
        return CountInconsistent(dirCount);
    }
}
