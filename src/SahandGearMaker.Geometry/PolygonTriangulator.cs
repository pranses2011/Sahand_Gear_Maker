namespace SahandGearMaker.Geometry;

/// <summary>نقطهٔ دوبعدی برای مثلث‌بندی سطح مقطع و صفحه‌های با حفره.</summary>
public readonly struct Point2(double x, double y)
{
    public readonly double X = x;
    public readonly double Y = y;
}

/// <summary>
/// مثلث‌بند چندضلعی ساده با حفره (ear-clipping با پل‌زدن رأس‌به‌رأس حفره‌ها).
/// کاربرد: صفحهٔ روی/زیر بدنه با سوراخ‌های سبک‌سازی و بور، سطح مقطع و رندر نقشهٔ دوبعدی.
///
/// قرارداد شاخص‌ها (مهم):
///   شاخص‌های مثلث‌های خروجی به آرایهٔ مجازی «رئوس حلقهٔ بیرونی به ترتیب، سپس رئوس هر حفره به ترتیب ورودی»
///   اشاره می‌کنند و هیچ رأس جدیدی ساخته نمی‌شود؛ فراخواننده همین ترتیب را در نگاشت خود حفظ می‌کند.
/// قرارداد جهت: حلقهٔ بیرونی CCW و حفره‌ها CW؛ خروجی مثلث‌ها CCW (نرمال +Z) است.
/// </summary>
public static class PolygonTriangulator
{
    /// <summary>مثلث‌بندی ناحیهٔ محصور بین حلقهٔ بیرونی و حفره‌ها؛ شاخص‌ها به ترتیب outer سپس holes.</summary>
    public static List<(int A, int B, int C)> Triangulate(
        IReadOnlyList<Point2> outer,
        IReadOnlyList<IReadOnlyList<Point2>>? holes)
    {
        ArgumentNullException.ThrowIfNull(outer);
        if (outer.Count < 3)
            throw new InvalidOperationException("حلقهٔ بیرونی دست‌کم ۳ رأس لازم دارد.");
        if (Shoelace(outer) < 0)
            throw new InvalidOperationException("حلقهٔ بیرونی باید پادساعتگرد (CCW) باشد.");

        // آرایهٔ مجازی رئوس: [outer][hole0][hole1]... — هیچ رأسی اضافه نمی‌شود
        int totalCount = outer.Count;
        if (holes != null)
            foreach (var h in holes)
            {
                if (h.Count < 3)
                    throw new InvalidOperationException("حفره دست‌کم ۳ رأس لازم دارد.");
                if (Shoelace(h) > 0)
                    throw new InvalidOperationException("حفره‌ها باید ساعتگرد (CW) باشند.");
                totalCount += h.Count;
            }

        var polygon = new List<int>(totalCount);
        for (int i = 0; i < outer.Count; i++) polygon.Add(i);

        if (holes is { Count: > 0 })
        {
            // حفره‌ها به ترتیب نزولی بیشینهٔ X پردازش می‌شوند (روش مرجع Eberly)
            var ordered = holes
                .Select((h, idx) => (h, idx))
                .OrderByDescending(t => t.h.Max(p => p.X))
                .ToList();
            foreach (var (hole, _) in ordered)
                BridgeHole(hole, outer, holes, polygon);
        }

        Point2 Vertex(int i) => VertexOf(i, outer, holes);
        var triangles = EarClip(polygon, Vertex);

        // ناحیهٔ کل باید مثبت (CCW) باشد
        double area = 0;
        foreach (var t in triangles)
            area += Tri(Vertex(t.A), Vertex(t.B), Vertex(t.C));
        if (area < -1e-9)
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

    // ---------- پل‌زدن حفره به چندضلعی فعلی (بدون رأس جدید) ----------

    /// <summary>
    /// حفره با «رأس بیشینهٔ X» به نزدیک‌ترین رأس قابل‌دید چندضلعی فعلی پل زده می‌شود؛
    /// قطری که هیچ یالی را قطع نکند داخل ماده است و مثلث‌بندی معتبر می‌ماند.
    /// </summary>
    private static void BridgeHole(
        IReadOnlyList<Point2> hole,
        IReadOnlyList<Point2> outer,
        IReadOnlyList<IReadOnlyList<Point2>> holes,
        List<int> polygon)
    {
        // رأس حفره با بیشینهٔ X (ترجیح: بیشترین Y در تساوی)
        int mIdx = 0;
        for (int i = 1; i < hole.Count; i++)
        {
            if (hole[i].X > hole[mIdx].X + 1e-12 ||
                (Math.Abs(hole[i].X - hole[mIdx].X) <= 1e-12 && hole[i].Y > hole[mIdx].Y))
                mIdx = i;
        }
        var m = hole[mIdx];

        // نزدیک‌ترین رأس چندضلعی که پاره‌خط M–V هیچ یالی را قطع نکند
        int bestVertex = -1;
        double bestD = double.MaxValue;

        for (int pass = 0; pass < 2 && bestVertex < 0; pass++)
        {
            foreach (int v in polygon)
            {
                var p = VertexOf(v, outer, holes);
                double d = DistSq(p, m);
                if (d < 1e-18 || d >= bestD)
                    continue;
                if (pass == 0 && p.X < m.X - 1e-9)
                    continue; // گذر اول: فقط رئوس سمت +X (ترجیح جهت پل)
                if (SegmentClear(p, m, polygon, outer, holes))
                {
                    bestD = d;
                    bestVertex = v;
                }
            }
        }

        if (bestVertex < 0)
            bestVertex = polygon[0]; // بازگشت ایمن: نزدیک‌ترین رأس موجود

        // شاخص سراسری رئوس حفره
        int holeBase = outer.Count;
        if (holes != null)
            foreach (var h in holes)
            {
                if (ReferenceEquals(h, hole))
                    break;
                holeBase += h.Count;
            }

        // درج پل بعد از bestVertex: [V, M, h(m+1), ..., h(m-1), M, V]
        // تکرار هر دو سر پل (V و M) باعث می‌شود یال پل در دو جهت پیموده شود و
        // مثلث‌های خروجی آن را دقیقاً دو بار (یکی در هر جهت) پوشش دهند — آب‌بندی کامل.
        int pos = polygon.IndexOf(bestVertex);
        var bridge = new List<int>(hole.Count + 3) { holeBase + mIdx };
        for (int i = 1; i < hole.Count; i++)
            bridge.Add(holeBase + (mIdx + i) % hole.Count);
        bridge.Add(bridge[0]); // تکرار M برای بستن حلقهٔ حفره
        bridge.Add(bestVertex); // تکرار V برای خروج از پل
        polygon.InsertRange(pos + 1, bridge);
    }

    private static Point2 VertexOf(int globalIndex, IReadOnlyList<Point2> outer, IReadOnlyList<IReadOnlyList<Point2>>? holes)
    {
        if (globalIndex < outer.Count)
            return outer[globalIndex];
        int k = globalIndex - outer.Count;
        if (holes == null) return default;
        foreach (var h in holes)
        {
            if (k < h.Count) return h[k];
            k -= h.Count;
        }
        return default;
    }

    /// <summary>آیا پاره‌خط a–b هیچ یال چندضلعی فعلی را قطع نمی‌کند (به‌جز یال‌های همسایه دو سر)؟</summary>
    private static bool SegmentClear(
        Point2 a, Point2 b,
        List<int> polygon,
        IReadOnlyList<Point2> outer,
        IReadOnlyList<IReadOnlyList<Point2>>? holes)
    {
        int n = polygon.Count;
        for (int e = 0; e < n; e++)
        {
            int u = polygon[e], v = polygon[(e + 1) % n];
            if (u == v)
                continue;
            var pu = VertexOf(u, outer, holes);
            var pv = VertexOf(v, outer, holes);
            // یال‌های همسایه با نقاط انتهایی نادیده گرفته می‌شوند (تلاقی در رأس مشترک مجاز است)
            if (SamePoint(pu, a) || SamePoint(pu, b) || SamePoint(pv, a) || SamePoint(pv, b))
                continue;
            if (SegmentsIntersect(a, b, pu, pv))
                return false;
        }
        return true;
    }

    private static bool SamePoint(Point2 p, Point2 q)
        => Math.Abs(p.X - q.X) < 1e-9 && Math.Abs(p.Y - q.Y) < 1e-9;

    // ---------- گوش‌خواری ----------

    private static List<(int A, int B, int C)> EarClip(List<int> polygon, Func<int, Point2> vertexOf)
    {
        var triangles = new List<(int, int, int)>(Math.Max(0, polygon.Count - 2));
        var guard = 0;
        while (polygon.Count > 3 && guard++ < 20_000_000)
        {
            int ear = FindEar(polygon, vertexOf);
            if (ear < 0)
            {
                SplitPolygon(polygon, vertexOf, triangles);
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
            double a = Tri(vertexOf(polygon[0]), vertexOf(polygon[1]), vertexOf(polygon[2]));
            if (Math.Abs(a) > 1e-12)
                triangles.Add(a > 0
                    ? (polygon[0], polygon[1], polygon[2])
                    : (polygon[0], polygon[2], polygon[1]));
        }
        return triangles;
    }

    private static void SplitPolygon(
        List<int> polygon,
        Func<int, Point2> vertexOf,
        List<(int, int, int)> triangles)
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
                if (!IsSafeDiagonal(polygon, vertexOf, i, j))
                    continue;

                var part1 = new List<int>();
                for (int k = i; k <= j; k++) part1.Add(polygon[k]);
                var part2 = new List<int>();
                for (int k = j; k < n; k++) part2.Add(polygon[k]);
                for (int k = 0; k <= i; k++) part2.Add(polygon[k]);
                polygon.Clear();
                polygon.AddRange(part2);
                if (part1.Count >= 3)
                    triangles.AddRange(EarClip(part1, vertexOf));
                return;
            }
        }
        polygon.RemoveAt(0); // آخرین راه نجات
    }

    /// <summary>یافتن گوش محدب؛ در چندضلعی‌های بزرگ فقط رئوس مقعر بررسی می‌شوند (بهینه‌سازی مرجع).</summary>
    private static int FindEar(List<int> polygon, Func<int, Point2> vertexOf)
    {
        int n = polygon.Count;
        HashSet<int>? reflex = null;
        if (n > 24)
        {
            reflex = new HashSet<int>(n / 2);
            for (int i = 0; i < n; i++)
            {
                var ip = vertexOf(polygon[(i + n - 1) % n]);
                var ic = vertexOf(polygon[i]);
                var inx = vertexOf(polygon[(i + 1) % n]);
                if (Tri(ip, ic, inx) < -1e-12)
                    reflex.Add(polygon[i]);
            }
        }

        for (int i = 0; i < n; i++)
        {
            int ia = polygon[(i + n - 1) % n], ib = polygon[i], ic = polygon[(i + 1) % n];
            var a = vertexOf(ia);
            var b = vertexOf(ib);
            var c = vertexOf(ic);
            double area = Tri(a, b, c);
            if (area < -1e-12)
                continue; // مقعر

            if (area <= 1e-12)
                return i; // گوش هم‌خط: حذف بی‌خطر است و مثلث صفر-مساحت آن یال‌های مرزی را می‌پوشاند

            double minX = Math.Min(a.X, Math.Min(b.X, c.X)) - 1e-12;
            double maxX = Math.Max(a.X, Math.Max(b.X, c.X)) + 1e-12;
            double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 1e-12;
            double maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) + 1e-12;

            bool isEar = true;
            for (int j = 0; j < n; j++)
            {
                int ip = polygon[j];
                if (ip == ia || ip == ib || ip == ic)
                    continue;
                if (reflex != null && !reflex.Contains(ip))
                    continue; // فقط رئوس مقعر می‌توانند داخل گوش باشند
                var p = vertexOf(ip);
                if (p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY)
                    continue;
                if (StrictlyInside(p, a, b, c))
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

    /// <summary>قطری امن: با هیچ یالی قطع نمی‌شود و نقطهٔ میانی آن داخل چندضلعی است.</summary>
    private static bool IsSafeDiagonal(List<int> polygon, Func<int, Point2> vertexOf, int i, int j)
    {
        var a = vertexOf(polygon[i]);
        var b = vertexOf(polygon[j]);
        var mid = new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        int n = polygon.Count;
        for (int e = 0; e < n; e++)
        {
            int u = polygon[e], v = polygon[(e + 1) % n];
            if (u == polygon[i] || u == polygon[j] || v == polygon[i] || v == polygon[j])
                continue;
            if (SegmentsIntersect(a, b, vertexOf(u), vertexOf(v)))
                return false;
        }

        bool inside = false;
        for (int e = 0, ePrev = n - 1; e < n; ePrev = e++)
        {
            var p1 = vertexOf(polygon[ePrev]);
            var p2 = vertexOf(polygon[e]);
            if ((p2.Y > mid.Y) != (p1.Y > mid.Y) &&
                mid.X < (p1.X - p2.X) * (mid.Y - p2.Y) / (p1.Y - p2.Y) + p2.X)
                inside = !inside;
        }
        return inside;
    }

    // ---------- هندسهٔ کمکی ----------

    /// <summary>نقطهٔ سخت‌درون مثلث (اکیداً)؛ نقاط روی لبه مانع گوش‌خواری نیستند (رفع قوس‌های هم‌خط).</summary>
    private static bool StrictlyInside(Point2 p, Point2 a, Point2 b, Point2 c, double eps = 1e-12)
        => Tri(p, a, b) > eps && Tri(p, b, c) > eps && Tri(p, c, a) > eps;

    /// <summary>نقطه در مثلث با تلرانس مرزی (برای کاربردهای عمومی).</summary>
    private static bool PointInTriangle(Point2 p, Point2 a, Point2 b, Point2 c, double eps = 1e-12)
    {
        double d1 = Tri(p, a, b);
        double d2 = Tri(p, b, c);
        double d3 = Tri(p, c, a);
        bool hasNeg = d1 < -eps || d2 < -eps || d3 < -eps;
        bool hasPos = d1 > eps || d2 > eps || d3 > eps;
        return !(hasNeg && hasPos);
    }

    /// <summary>مساحت علامت‌دار مثلث (×۲).</summary>
    private static double Tri(Point2 a, Point2 b, Point2 c)
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

    private static bool SegmentsIntersect(Point2 p1, Point2 p2, Point2 p3, Point2 p4)
    {
        double d1 = Cross(p3, p4, p1);
        double d2 = Cross(p3, p4, p2);
        double d3 = Cross(p1, p2, p3);
        double d4 = Cross(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(Point2 o, Point2 a, Point2 b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
