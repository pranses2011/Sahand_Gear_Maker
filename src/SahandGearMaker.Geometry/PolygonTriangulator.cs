using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>نقطهٔ دوبعدی برای مثلث‌بندی سطح مقطع و صفحه‌های با حفره.</summary>
public readonly struct Point2(double x, double y)
{
    public readonly double X = x;
    public readonly double Y = y;
}

/// <summary>
/// مثلث‌بند چندضلعی ساده با حفره (ear-clipping با پل‌زدن حفره‌ها به مرز بیرونی).
/// کاربرد: صفحهٔ روی/زیر بدنه با سوراخ‌های سبک‌سازی و بور، سطح مقطع و رندر نقشهٔ دوبعدی.
/// قرارداد: حلقهٔ بیرونی پادساعتگرد (CCW) و حفره‌ها ساعتگرد (CW) داده شوند؛
/// خروجی مثلث‌ها هم‌جهت با حلقهٔ بیرونی (CCW) است.
/// </summary>
public static class PolygonTriangulator
{
    /// <summary>مثلث‌بندی ناحیهٔ محصور بین حلقهٔ بیرونی و حفره‌ها؛ خروجی ایندکس‌محور به رئوس.</summary>
    public static List<(int A, int B, int C)> Triangulate(
        IReadOnlyList<Point2> outer,
        IReadOnlyList<IReadOnlyList<Point2>>? holes)
    {
        ArgumentNullException.ThrowIfNull(outer);
        if (outer.Count < 3)
            throw new InvalidOperationException("حلقهٔ بیرونی دست‌کم ۳ رأس لازم دارد.");

        // جمع رئوس و نگاشت ایندکس سراسری
        var vertices = new List<Point2>(outer.Count + (holes?.Sum(h => h.Count) ?? 0) + 8);
        foreach (var p in outer) vertices.Add(p);

        var polygon = new List<int>(outer.Count + 16);
        for (int i = 0; i < outer.Count; i++) polygon.Add(i);

        if (holes is { Count: > 0 })
        {
            // حفره‌ها به ترتیب نزولی بیشینهٔ X پردازش می‌شوند (روش مرجع Eberly)
            var ordered = holes
                .Where(h => h.Count >= 3)
                .OrderByDescending(h => h.Max(p => p.X))
                .ToList();
            foreach (var hole in ordered)
                BridgeHole(vertices, polygon, hole);
        }

        var triangles = EarClip(vertices, polygon);

        // ناحیهٔ کل باید مثبت (CCW) باشد؛ در غیر این صورت جهت مرز ورودی معکوس بوده است.
        double area = triangles.Sum(t => SignedArea(vertices[t.A], vertices[t.B], vertices[t.C]));
        if (area < 0)
            triangles = triangles.Select(t => (t.A, t.C, t.B)).ToList();
        return triangles;
    }

    /// <summary>مساحت مطلق ناحیهٔ بیرونی منهای حفره‌ها (کنترل سلامت مثلث‌بندی در آزمون‌ها).</summary>
    public static double NetArea(IReadOnlyList<Point2> outer, IReadOnlyList<IReadOnlyList<Point2>>? holes)
    {
        double a = Math.Abs(Shoelace(outer));
        if (holes != null)
            foreach (var h in holes)
                a -= Math.Abs(Shoelace(h));
        return a;
    }

    // ---------- پل‌زدن حفره به مرز فعلی ----------

    private static void BridgeHole(List<Point2> vertices, List<int> polygon, IReadOnlyList<Point2> hole)
    {
        // مطمئن شویم حفره ساعتگرد است (رو به داخل ماده)
        if (Shoelace(hole) > 0)
            hole = hole.Reverse().ToList();

        // رأس حفره با بیشینهٔ X (ترجیح: بیشترین Y در تساوی)
        int mIdx = 0;
        for (int i = 1; i < hole.Count; i++)
        {
            if (hole[i].X > hole[mIdx].X + 1e-12 ||
                (Math.Abs(hole[i].X - hole[mIdx].X) <= 1e-12 && hole[i].Y > hole[mIdx].Y))
                mIdx = i;
        }
        var m = hole[mIdx];

        // پرتو +X از M: نزدیک‌ترین تقاطع با یال‌های چندضلعی فعلی
        double bestT = double.MaxValue;
        int bestEdge = -1;
        Point2 bestPoint = default;
        for (int e = 0; e < polygon.Count; e++)
        {
            var a = vertices[polygon[e]];
            var b = vertices[polygon[(e + 1) % polygon.Count]];
            if (Math.Abs(a.Y - b.Y) < 1e-12)
                continue; // موازی با پرتو
            // تقاطع پرتو y=m.Y با یال [a,b]
            double x = a.X + (m.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            double yMin = Math.Min(a.Y, b.Y), yMax = Math.Max(a.Y, b.Y);
            if (m.Y < yMin - 1e-12 || m.Y > yMax + 1e-12)
                continue;
            double t = x - m.X;
            if (t > 1e-9 && t < bestT)
            {
                // در صورت گذر از رأس، نقطهٔ تقاطع دقیقاً روی رأس قابل قبول است
                bestT = t;
                bestEdge = e;
                bestPoint = new Point2(x, m.Y);
            }
        }

        if (bestEdge < 0)
        {
            // هیچ تقاطعی در +X نبود (حفره بیرون مرز؟) — به دنبال نزدیک‌ترین رأس چندضلعی پل بزن
            bestEdge = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                double d = DistSq(vertices[polygon[i]], m);
                if (d < bestD) { bestD = d; bestEdge = i; }
            }
            bestPoint = vertices[polygon[bestEdge]];
        }

        int pIdx = vertices.Count;
        vertices.Add(bestPoint);

        // حلقهٔ حفره از M به ترتیب ساعتگرد: M, h(m+1), ..., h(m-1)
        int holeStart = vertices.Count;
        for (int i = 0; i < hole.Count; i++)
            vertices.Add(hole[(mIdx + i) % hole.Count]);
        // رأس تکراری برای بستن پل (M دوباره) — گوش‌خوار با مساحت صفر مدیریت می‌شود
        int mRepeat = vertices.Count;
        vertices.Add(hole[mIdx]);
        int pRepeat = vertices.Count;
        vertices.Add(bestPoint);

        // درج پل در محل یال bestEdge: بین رأس bestEdge و رأس بعدی
        int insertAt = bestEdge + 1;
        var bridge = new List<int> { pIdx, holeStart };
        for (int i = 1; i < hole.Count; i++)
            bridge.Add(holeStart + i);
        bridge.Add(mRepeat);
        bridge.Add(pRepeat);
        polygon.InsertRange(insertAt, bridge);
    }

    // ---------- گوش‌خواری ----------

    private static List<(int A, int B, int C)> EarClip(List<Point2> vertices, List<int> polygon)
    {
        var triangles = new List<(int, int, int)>(Math.Max(0, polygon.Count - 2));
        var guard = 0;
        while (polygon.Count > 3 && guard++ < 10_000_000)
        {
            int ear = FindEar(vertices, polygon);
            if (ear < 0)
            {
                // چندضلعی تحریف‌شده — با قطری امن دو تکه می‌کنیم
                SplitPolygon(vertices, polygon, triangles);
                if (polygon.Count <= 2)
                    break;
                continue;
            }
            int prev = polygon[(ear + polygon.Count - 1) % polygon.Count];
            int curr = polygon[ear];
            int next = polygon[(ear + 1) % polygon.Count];
            triangles.Add((prev, curr, next));
            polygon.RemoveAt(ear);
        }

        if (polygon.Count == 3)
        {
            double a = SignedArea(vertices[polygon[0]], vertices[polygon[1]], vertices[polygon[2]]);
            if (Math.Abs(a) > 1e-12)
                triangles.Add(a > 0
                    ? (polygon[0], polygon[1], polygon[2])
                    : (polygon[0], polygon[2], polygon[1]));
        }
        return triangles;
    }

    private static int FindEar(List<Point2> vertices, List<int> polygon)
    {
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            int ia = polygon[(i + n - 1) % n], ib = polygon[i], ic = polygon[(i + 1) % n];
            var a = vertices[ia];
            var b = vertices[ib];
            var c = vertices[ic];
            if (SignedArea(a, b, c) <= 1e-12)
                continue; // مقعر یا هم‌خط

            bool isEar = true;
            for (int j = 0; j < n; j++)
            {
                int ip = polygon[j];
                if (ip == ia || ip == ib || ip == ic)
                    continue;
                if (PointInTriangle(vertices[ip], a, b, c))
                {
                    isEar = false;
                    break;
                }
            }
            if (isEar)
                return i;
        }
        return -1;
    }

    /// <summary>شکستن چندضلعی بدشکل با قطری ایمن (fallback). قطری که همهٔ رئوس دیگر یک‌طرفه‌اند.</summary>
    private static void SplitPolygon(List<Point2> vertices, List<int> polygon, List<(int, int, int)> triangles)
    {
        int n = polygon.Count;
        if (n <= 3)
            return;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                    continue;
                if (IsSafeDiagonal(vertices, polygon, i, j))
                {
                    // دو زیرچندضلعی را جدا گوش‌خوار می‌کنیم
                    var part1 = new List<int>();
                    for (int k = i; k <= j; k++) part1.Add(polygon[k]);
                    var part2 = new List<int>();
                    for (int k = j; k < n; k++) part2.Add(polygon[k]);
                    for (int k = 0; k <= i; k++) part2.Add(polygon[k]);
                    polygon.Clear();
                    polygon.AddRange(part2);
                    triangles.AddRange(EarClip(vertices, part1));
                    return;
                }
            }
        }
        // آخرین راه نجات: اولین رأس را بردار (تکرار ایمن نیست ولی حلقه را پیش می‌برد)
        polygon.RemoveAt(0);
    }

    private static bool IsSafeDiagonal(List<Point2> vertices, List<int> polygon, int i, int j)
    {
        var a = vertices[polygon[i]];
        var b = vertices[polygon[j]];
        for (int k = 0; k < polygon.Count; k++)
        {
            if (k == i || k == j)
                continue;
            if (PointInTriangle(vertices[polygon[k]], a, b, a, 1e-9))
                return false;
        }
        return true;
    }

    // ---------- هندسهٔ کمکی ----------

    private static bool PointInTriangle(Point2 p, Point2 a, Point2 b, Point2 c, double eps = 1e-12)
    {
        double d1 = SignedArea(p, a, b);
        double d2 = SignedArea(p, b, c);
        double d3 = SignedArea(p, c, a);
        bool hasNeg = d1 < -eps || d2 < -eps || d3 < -eps;
        bool hasPos = d1 > eps || d2 > eps || d3 > eps;
        return !(hasNeg && hasPos);
    }

    internal static double SignedArea(Point2 a, Point2 b, Point2 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double Shoelace(IReadOnlyList<Point2> loop)
    {
        double s = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2.0;
    }

    private static double DistSq(Point2 a, Point2 b)
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
