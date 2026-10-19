using SahandGearMaker.MathX;

namespace SahandGearMaker.Cad;

/// <summary>نوع قطعهٔ منحنی از این رأس تا رأس بعدی حلقه.</summary>
public enum CadSegmentKind
{
    /// <summary>خط مستقیم.</summary>
    Line,

    /// <summary>کمان دایره‌ای پادساعتگرد حول ArcAxis (محور معکوس = ساعتگرد).</summary>
    Arc,
}

/// <summary>رأس حلقه + مشخصات قطعهٔ منحنی تا رأس بعدی.</summary>
public sealed class CadNode
{
    public Vector3 Point;
    public CadSegmentKind Kind = CadSegmentKind.Line;
    public Vector3 ArcCenter;
    public Vector3 ArcAxis = Vector3.UnitZ;
    public double ArcRadius;

    public static CadNode At(Vector3 p) => new() { Point = p };
    public static CadNode At(Vector3 p, Vector3 center, Vector3 axis, double radius) => new()
    {
        Point = p,
        Kind = CadSegmentKind.Arc,
        ArcCenter = center,
        ArcAxis = axis,
        ArcRadius = radius,
    };
}

/// <summary>حلقهٔ بستهٔ وجه — پادساعتگرد حول نرمالِ بیرونی ماده (قاعدهٔ دست راست).</summary>
public sealed class CadLoop
{
    public List<CadNode> Nodes { get; } = new();
    public int Count => Nodes.Count;
    public void Add(CadNode n) => Nodes.Add(n);
}

/// <summary>سطح تحلیلی زیر وجه.</summary>
public abstract class CadSurface
{
    public Vector3 Origin;
    public Vector3 AxisZ = Vector3.UnitZ;
    public Vector3 AxisX = Vector3.UnitX;
}

/// <summary>صفحه با نرمال AxisZ.</summary>
public sealed class CadPlane : CadSurface;

/// <summary>استوانه با شعاع Radius حول AxisZ (نرمال سطح: شعاعی به بیرون).</summary>
public sealed class CadCylinder : CadSurface
{
    public double Radius;
}

/// <summary>وجه BREP.</summary>
public sealed class CadFace
{
    public CadSurface Surface = null!;
    public CadLoop Outer = new();
    public List<CadLoop> Inner = new();

    /// <summary>آیا نرمالِ سطحِ تحلیلی با نرمال بیرونیِ ماده هم‌جهت است؟ (same_sense در STEP)</summary>
    public bool SurfaceAgreesWithOutward = true;
}

/// <summary>جامد BREP — پوستهٔ بسته از وجوه تحلیلی.</summary>
public sealed class CadSolid
{
    public string Name { get; }
    public List<CadFace> Faces { get; } = new();
    public (double R, double G, double B)? Color { get; set; }

    public CadSolid(string name) => Name = name;
}

/// <summary>گزارش ساخت مدل CAD.</summary>
public sealed record CadBuildReport
{
    public int FaceCount { get; init; }
    public int VertexCount { get; init; }
    public int EdgeCount { get; init; }
    public string Strategy { get; init; } = "";
    public List<string> Notes { get; init; } = new();
}

/// <summary>سازنده‌های کمکی وجه.</summary>
public static class CadBuild
{
    /// <summary>
    /// وجه درپوش افقی: حلقهٔ بیرونی + حفره‌ها (حلقه‌های آماده — چندضلعی یا کمانی).
    /// اولین حلقه بیرونی است؛ بقیه حفره‌اند.
    /// </summary>
    public static CadFace Cap(double z, bool top, List<CadLoop> loops)
    {
        var face = new CadFace
        {
            Surface = new CadPlane { Origin = new Vector3(0, 0, z), AxisZ = top ? Vector3.UnitZ : -Vector3.UnitZ, AxisX = Vector3.UnitX },
            SurfaceAgreesWithOutward = true,
        };
        if (loops.Count == 0) return face;
        face.Outer = loops[0];
        for (int i = 1; i < loops.Count; i++)
            face.Inner.Add(loops[i]);
        return face;
    }

    /// <summary>
    /// حلقهٔ دایرهٔ کامل با دو کمان نیم‌دایره — نقاط شکست در زاویهٔ ۰ و π هم‌جا با
    /// شکست دیوارهٔ استوانه‌ای است تا یال‌ها بین درپوش و دیواره به اشتراک گذاشته شوند.
    /// </summary>
    public static CadLoop CircleLoop(double radius, double z, bool ccwAroundPlusZ)
    {
        var loop = new CadLoop();
        var p0 = new Vector3(radius, 0, z);
        var p1 = new Vector3(-radius, 0, z);
        var center = new Vector3(0, 0, z);
        var axis = ccwAroundPlusZ ? Vector3.UnitZ : -Vector3.UnitZ;
        loop.Add(CadNode.At(p0, center, axis, radius)); // نیمهٔ اول: ۰→π حول axis
        loop.Add(CadNode.At(p1, center, axis, radius)); // نیمهٔ دوم: π→۲π حول axis
        return loop;
    }

    /// <summary>حلقهٔ دایرهٔ خارج از مرکز (سوراخ‌های سبک‌سازی گرد) — دو کمان نیم‌دایره.</summary>
    public static CadLoop OffsetCircleLoop(double cx, double cy, double radius, double z, bool ccwAroundPlusZ)
    {
        var loop = new CadLoop();
        var p0 = new Vector3(cx + radius, cy, z);
        var p1 = new Vector3(cx - radius, cy, z);
        var center = new Vector3(cx, cy, z);
        var axis = ccwAroundPlusZ ? Vector3.UnitZ : -Vector3.UnitZ;
        loop.Add(CadNode.At(p0, center, axis, radius));
        loop.Add(CadNode.At(p1, center, axis, radius));
        return loop;
    }

    /// <summary>حلقهٔ چندضلعی CCW (ccw=true حول +Z) یا ساعتگرد.</summary>
    public static CadLoop PolygonLoop(IEnumerable<(double X, double Y)> pts, double z, bool ccw)
    {
        var list = pts.ToList();
        var loop = new CadLoop();
        if (list.Count < 3)
            return loop;
        if (ccw)
            foreach (var p in list)
                loop.Add(CadNode.At(new Vector3(p.X, p.Y, z)));
        else
            for (int i = list.Count - 1; i >= 0; i--)
                loop.Add(CadNode.At(new Vector3(list[i].X, list[i].Y, z)));
        return loop;
    }

    /// <summary>
    /// دیوارهٔ استوانه‌ای کامل بین z0 و z1 حول محور Z جهانی — به دو نیم‌استوانه شکسته می‌شود
    /// تا یال درز تک‌رأسی (دایرهٔ کامل با رأس آغاز=پایان) رخ ندهد.
    /// </summary>
    /// <param name="inward">true = جدارهٔ سوراخ (ماده بیرون استوانه؛ نرمال به سمت محور).</param>
    public static void AddCylinderWall(CadSolid solid, double cx, double cy, double radius,
        double z0, double z1, bool inward)
    {
        for (int half = 0; half < 2; half++)
        {
            double a0 = Math.PI * half;
            double a1 = a0 + Math.PI;
            Vector3 P(double a, double z) => new(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z);

            var face = new CadFace
            {
                Surface = new CadCylinder { Origin = new Vector3(cx, cy, 0), AxisZ = Vector3.UnitZ, AxisX = Vector3.UnitX, Radius = radius },
                SurfaceAgreesWithOutward = !inward,
            };
            var loop = new CadLoop();
            if (!inward)
            {
                // CCW حول نرمال شعاعی-بیرون: (a0,z0)→(a1,z0)→(a1,z1)→(a0,z1)
                var cB = new Vector3(cx, cy, z0); // مرکز کمان پایین — در ارتفاع خود کمان (هم‌مرکز با درپوش)
                var cT = new Vector3(cx, cy, z1);
                loop.Add(CadNode.At(P(a0, z0), cB, Vector3.UnitZ, radius)); // کمان a0→a1 (CCW حول +Z)
                loop.Add(CadNode.At(P(a1, z0)));                             // خط عمودی
                loop.Add(CadNode.At(P(a1, z1), cT, -Vector3.UnitZ, radius)); // کمان a1→a0 (CCW حول −Z)
                loop.Add(CadNode.At(P(a0, z1)));                             // خط عمودی پایین
            }
            else
            {
                // CCW حول نرمال رو-به-محور: (a0,z0)→(a0,z1)→(a1,z1)→(a1,z0)
                var cB = new Vector3(cx, cy, z0);
                var cT = new Vector3(cx, cy, z1);
                loop.Add(CadNode.At(P(a0, z0)));                             // خط عمودی بالا
                loop.Add(CadNode.At(P(a0, z1), cT, Vector3.UnitZ, radius)); // کمان a0→a1
                loop.Add(CadNode.At(P(a1, z1)));                             // خط عمودی پایین
                loop.Add(CadNode.At(P(a1, z0), cB, -Vector3.UnitZ, radius)); // کمان a1→a0
            }
            face.Outer = loop;
            solid.Faces.Add(face);
        }
    }

    /// <summary>
    /// دیوارهٔ منشوری از مقطع دوبعدی (بور D/شش‌گوش/…) — وجوه صفحه‌ای بین جفت‌نقطه‌های متناظر
    /// در دو ارتفاع؛ حلقه‌ها CCW حول نرمال بیرونی هر وجه.
    /// </summary>
    public static void AddPrismWall(CadSolid solid, IReadOnlyList<(double X, double Y)> section,
        double z0, double z1, bool inward)
    {
        int n = section.Count;
        if (n < 3) return;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var (ax, ay) = section[i];
            var (bx, by) = section[j];
            if (Math.Abs(ax - bx) < 1e-9 && Math.Abs(ay - by) < 1e-9)
                continue;

            var u = new Vector3(bx - ax, by - ay, 0);
            var len = u.Length;
            if (len < 1e-9) continue;
            var tangent = u / len;
            // نرمال بیرونی دیوارهٔ منشور با مقطع CCW: tangent × (بالا) → رو به بیرون
            var normalOut = new Vector3(tangent.Y, -tangent.X, 0); // (dx,dy,0)→(dy,−dx,0) = چرخش −۹۰° = بیرونی برای CCW
            if (inward)
                normalOut = -normalOut;

            var face = new CadFace
            {
                Surface = new CadPlane
                {
                    Origin = new Vector3(ax, ay, z0),
                    AxisZ = normalOut.Normalized(),
                    AxisX = Vector3.UnitZ,
                },
                SurfaceAgreesWithOutward = true,
            };
            // حلقهٔ CCW حول normalOut: با نگاه از بیرون، پایین→بالا در راستای مناسب
            // برای دیوارهٔ خارجی (CCW مقطع): (a,z0)→(b,z0)→(b,z1)→(a,z1)
            var loop = new CadLoop();
            if (!inward)
            {
                loop.Add(CadNode.At(new Vector3(ax, ay, z0)));
                loop.Add(CadNode.At(new Vector3(bx, by, z0)));
                loop.Add(CadNode.At(new Vector3(bx, by, z1)));
                loop.Add(CadNode.At(new Vector3(ax, ay, z1)));
            }
            else
            {
                loop.Add(CadNode.At(new Vector3(bx, by, z0)));
                loop.Add(CadNode.At(new Vector3(ax, ay, z0)));
                loop.Add(CadNode.At(new Vector3(ax, ay, z1)));
                loop.Add(CadNode.At(new Vector3(bx, by, z1)));
            }
            face.Outer = loop;
            solid.Faces.Add(face);
        }
    }

    /// <summary>
    /// دیوارهٔ بین دو حلقهٔ چرخیده (دندانهٔ مارپیچ/جناغی): هر قطعهٔ پروفیل → وجه صفحه‌ای چهارگوش؛
    /// چهارگوش ناقرینه به دو مثلث می‌شکند.
    /// </summary>
    /// <param name="inward">true برای داخلی (ماده بیرون حلقه است — نرمال رو به محور).</param>
    public static void AddTwistedWall(CadSolid solid, IReadOnlyList<Vector3> lower, IReadOnlyList<Vector3> upper, bool inward = false)
    {
        int n = Math.Min(lower.Count, upper.Count);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var a0 = lower[i];
            var b0 = lower[j];
            var a1 = upper[i];
            var b1 = upper[j];
            if ((b0 - a0).Length < 1e-9 && (b1 - a1).Length < 1e-9)
                continue;

            // نرمال بیرونی ماده: برای مقطع CCW، (tangent × up) شعاعی به بیرون است
            var tangent = b0 - a0;
            var up = a1 - a0;
            var normalOut = Vector3.Cross(tangent, up);
            var nlen = normalOut.Length;
            normalOut = nlen > 1e-12 ? normalOut / nlen : new Vector3(tangent.Y, -tangent.X, 0);
            if (inward)
                normalOut = -normalOut;

            bool EmitQuad(Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
            {
                // مسطح بودن: فاصلهٔ q2 و q3 از صفحهٔ (q0,q1,q2)
                var nn = Vector3.Cross(q1 - q0, q2 - q0);
                double l = nn.Length;
                if (l < 1e-12) return false;
                nn /= l;
                if (Math.Abs(Vector3.Dot(nn, q3 - q0)) > 1e-6 * (1 + (q1 - q0).Length))
                    return false;

                var face = new CadFace
                {
                    Surface = new CadPlane { Origin = q0, AxisZ = normalOut, AxisX = up },
                    SurfaceAgreesWithOutward = true,
                };
                var loop = new CadLoop();
                loop.Add(CadNode.At(q0));
                loop.Add(CadNode.At(q1));
                loop.Add(CadNode.At(q2));
                loop.Add(CadNode.At(q3));
                face.Outer = loop;
                solid.Faces.Add(face);
                return true;
            }

            void EmitTri(Vector3 q0, Vector3 q1, Vector3 q2)
            {
                var face = new CadFace
                {
                    Surface = new CadPlane { Origin = q0, AxisZ = normalOut, AxisX = up },
                    SurfaceAgreesWithOutward = true,
                };
                var loop = new CadLoop();
                loop.Add(CadNode.At(q0));
                loop.Add(CadNode.At(q1));
                loop.Add(CadNode.At(q2));
                face.Outer = loop;
                solid.Faces.Add(face);
            }

            if (!EmitQuad(a0, b0, b1, a1))
            {
                EmitTri(a0, b0, a1);
                EmitTri(b0, b1, a1);
            }
        }
    }
}

/// <summary>تبدیل‌های جای‌گذاری روی جامد CAD — هم‌ارز Placement صحنه (X/Y/Z/θ حول Z/مقیاس).</summary>
public static class CadTransform
{
    /// <summary>اعمال جای‌گذاری صحنه روی یک کپی از جامد.</summary>
    public static CadSolid Transformed(CadSolid solid, double x, double y, double z, double thetaRad, double scale)
    {
        var result = new CadSolid(solid.Name) { Color = solid.Color };
        double cos = Math.Cos(thetaRad), sin = Math.Sin(thetaRad);
        scale = scale <= 0 ? 1.0 : scale;

        Vector3 T(Vector3 p) => new(
            x + scale * (p.X * cos - p.Y * sin),
            y + scale * (p.X * sin + p.Y * cos),
            z + scale * p.Z);

        Vector3 Dir(Vector3 d) => new(
            d.X * cos - d.Y * sin,
            d.X * sin + d.Y * cos,
            d.Z);

        foreach (var f in solid.Faces)
        {
            var nf = new CadFace { SurfaceAgreesWithOutward = f.SurfaceAgreesWithOutward };
            switch (f.Surface)
            {
                case CadPlane pl:
                    nf.Surface = new CadPlane { Origin = T(pl.Origin), AxisZ = Dir(pl.AxisZ), AxisX = Dir(pl.AxisX) };
                    break;
                case CadCylinder cyl:
                    nf.Surface = new CadCylinder { Origin = T(cyl.Origin), AxisZ = Dir(cyl.AxisZ), AxisX = Dir(cyl.AxisX), Radius = cyl.Radius * scale };
                    break;
            }
            nf.Outer = TransformLoop(f.Outer);
            foreach (var h in f.Inner)
                nf.Inner.Add(TransformLoop(h));
            result.Faces.Add(nf);
        }
        return result;

        CadLoop TransformLoop(CadLoop loop)
        {
            var nl = new CadLoop();
            foreach (var n in loop.Nodes)
            {
                if (n.Kind == CadSegmentKind.Line)
                    nl.Add(CadNode.At(T(n.Point)));
                else
                    nl.Add(CadNode.At(T(n.Point), T(n.ArcCenter), Dir(n.ArcAxis).Normalized(), n.ArcRadius * scale));
            }
            return nl;
        }
    }
}
