using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Scene;

/// <summary>نتیجهٔ انتخاب شیئ با کلیک روی بوم (فاز ۱٫۱۹).</summary>
public sealed record PickHit(string ObjectId, Vector3 Point, double DistanceMm, int TriangleIndex);

/// <summary>
/// پیکینگ صحنه (فاز ۱٫۱۹): پرتوی از پیکسل → آزمون AABB (سریع) → Möller–Trumbore بر مثلث‌ها.
/// نزدیک‌ترین برخورد با اشیاء مرئی و قابل‌انتخاب برگردانده می‌شود.
/// </summary>
public static class ScenePicking
{
    public static PickHit? Pick(IReadOnlyList<SceneObject> objects, in CameraFrame cam,
        double px, double py, bool includeLocked = true)
    {
        var ray = cam.RayThroughPixel(px, py);
        PickHit? best = null;

        foreach (var obj in objects)
        {
            if (!obj.Visible) continue;
            if (!includeLocked && obj.Locked) continue;
            if (obj.Opacity < 0.15) continue; // تقریباً نامرئی — انتخاب نمی‌شود

            // ۱) آزمون AABB
            var b = obj.WorldBounds;
            if (!RayAabb(ray, b.Min, b.Max, out double boxT)) continue;
            if (best is { } cur && boxT > cur.DistanceMm) continue; // جعبه هم عقب‌تر از بهترین است

            // ۲) آزمون مثلث‌ها
            var tr = obj.Transform;
            var verts = obj.Mesh.Vertices;
            var tris = obj.Mesh.Triangles;
            for (int ti = 0; ti < tris.Length; ti++)
            {
                var t = tris[ti];
                var a = tr.Apply(verts[t.A]);
                var b2 = tr.Apply(verts[t.B]);
                var c = tr.Apply(verts[t.C]);
                if (RayTriangle(ray, a, b2, c, out double dist) &&
                    (best is null || dist < best.DistanceMm))
                {
                    best = new PickHit(obj.Id, ray.Origin + ray.Direction * dist, dist, ti);
                }
            }
        }
        return best;
    }

    /// <summary>نزدیک‌ترین نقطهٔ سطح در جهت پرتو — برای ابزار اندازه‌گیری (فاز ۴٫۱).</summary>
    public static PickHit? PickPoint(IReadOnlyList<SceneObject> objects, in CameraFrame cam, double px, double py)
        => Pick(objects, cam, px, py);

    public static bool RayAabb(in Ray ray, Vector3 min, Vector3 max, out double tEnter)
    {
        tEnter = 0;
        double tMin = 0, tMax = double.PositiveInfinity;
        Span<double> o = stackalloc double[3] { ray.Origin.X, ray.Origin.Y, ray.Origin.Z };
        Span<double> d = stackalloc double[3] { ray.Direction.X, ray.Direction.Y, ray.Direction.Z };
        Span<double> bmin = stackalloc double[3] { min.X, min.Y, min.Z };
        Span<double> bmax = stackalloc double[3] { max.X, max.Y, max.Z };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(d[i]) < 1e-12)
            {
                if (o[i] < bmin[i] || o[i] > bmax[i]) return false;
            }
            else
            {
                double t1 = (bmin[i] - o[i]) / d[i];
                double t2 = (bmax[i] - o[i]) / d[i];
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Math.Max(tMin, t1);
                tMax = Math.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
        }
        tEnter = tMin;
        return true;
    }

    /// <summary>Möller–Trumbore — تقاطع پرتو و مثلث.</summary>
    public static bool RayTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c, out double t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var pvec = Vector3.Cross(ray.Direction, e2);
        double det = Vector3.Dot(e1, pvec);
        if (Math.Abs(det) < 1e-12) return false;
        double inv = 1.0 / det;
        var tvec = ray.Origin - a;
        double u = Vector3.Dot(tvec, pvec) * inv;
        if (u < -1e-9 || u > 1 + 1e-9) return false;
        var qvec = Vector3.Cross(tvec, e1);
        double v = Vector3.Dot(ray.Direction, qvec) * inv;
        if (v < -1e-9 || u + v > 1 + 1e-9) return false;
        t = Vector3.Dot(e2, qvec) * inv;
        return t > 1e-6;
    }

    /// <summary>نقطهٔ جهان روی صفحهٔ گذرنده از هدف دوربین و موازی صفحهٔ دید — لنگر زوم (فاز ۱٫۱۴).</summary>
    public static Vector3? PlaneAnchorUnderCursor(in CameraFrame cam, double px, double py)
    {
        var ray = cam.RayThroughPixel(px, py);
        // صفحه: نقطه = cam.Target، نرمال = cam.Forward
        double denom = Vector3.Dot(ray.Direction, cam.Forward);
        if (Math.Abs(denom) < 1e-9) return null;
        double t = Vector3.Dot(cam.Target - ray.Origin, cam.Forward) / denom;
        if (t <= 0) return null;
        return ray.Origin + ray.Direction * t;
    }
}
