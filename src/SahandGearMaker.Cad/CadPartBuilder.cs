using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Cad;

/// <summary>
/// سازندهٔ جامد CAD از قطعهٔ برنامه — نسخهٔ ۰٫۵ (هستهٔ CAD).
///
/// دو مسیر:
///   ۱) منشوری تحلیلی (چرخ‌دنده‌های استوانه‌ای/داخلی/هزارخار/سیکلوئیدی/جناغی):
///      درپوش‌های صفحه‌ای با حفره، دیوارهٔ دندانه به‌صورت وجه‌های صفحه‌ای بین برش‌ها،
///      بور و حفره‌های دایره‌ای به‌صورت CYLINDRICAL_SURFACE تحلیلی و مقاطع دلخواه به‌صورت منشور؛
///   ۲) وجه‌ای از مش (مخروطی، کرم، تاجی، هارمونیک، غیرمدور): مثلث‌های هم‌صفحه ادغام
///      می‌شوند و وجه‌های صفحه‌ای می‌سازند — BREP معتبر (توصیه: با تلرانس درشت‌تر صادر کنید).
/// </summary>
public static class CadPartBuilder
{
    /// <summary>ساخت جامد CAD از قطعهٔ ساخته‌شده (نتیجهٔ موتور هندسی لازم است).</summary>
    public static (CadSolid Solid, CadBuildReport Report) Build(GearPart part, GearBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(buildResult);

        var solid = new CadSolid(Sanitize(part.Name));
        var notes = new List<string>();

        var source = CadSourceExtractor.TryExtract(part, buildResult);
        if (source is not null)
            BuildPrismatic(solid, source, notes);
        else
            BuildFromMesh(solid, buildResult.Mesh, notes);

        solid.Color = ParseColor(part.DisplayColor);

        int edgeEst = 0, vtxEst = 0;
        foreach (var f in solid.Faces)
        {
            edgeEst += f.Outer.Count + f.Inner.Sum(l => l.Count);
            vtxEst += f.Outer.Count;
        }

        return (solid, new CadBuildReport
        {
            FaceCount = solid.Faces.Count,
            VertexCount = vtxEst,
            EdgeCount = edgeEst / 2,
            Strategy = source is not null ? "منشوری تحلیلی (صفحه + استوانه)" : "وجه‌ای از مش (صفحه‌های هم‌صفحه)",
            Notes = notes,
        });
    }

    private static string Sanitize(string name)
        => string.Concat(name.Where(char.IsLetterOrDigit)).Trim();

    private static (double, double, double)? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#') || hex.Length is not (7 or 9))
            return null;
        try
        {
            int r = Convert.ToInt32(hex.Substring(hex.Length - 6, 2), 16);
            int g = Convert.ToInt32(hex.Substring(hex.Length - 4, 2), 16);
            int b = Convert.ToInt32(hex.Substring(hex.Length - 2, 2), 16);
            return (r / 255.0, g / 255.0, b / 255.0);
        }
        catch
        {
            return null;
        }
    }

    // ═══════════ مسیر منشوری ═══════════

    private static void BuildPrismatic(CadSolid solid, PrismaticCadSource s, List<string> notes)
    {
        double z0 = s.Z0, z1 = s.Z1;
        var bottomRing = s.Rings[0];
        var topRing = s.Rings[^1];
        double boreTopZ = s.HubOuterRadius > 0 ? s.HubZ1 : z1;
        bool roundBore = s.EffectiveBore == BoreShapeType.Round && s.BoreRadius > 1e-9;

        // ---- درپوش‌ها ----
        if (s.Internal)
        {
            var capBottom = new List<CadLoop> { CadBuild.CircleLoop(s.RimOuterRadius, z0, ccwAroundPlusZ: false) };
            capBottom.Add(CadBuild.PolygonLoop(Proj(bottomRing), z0, ccw: true)); // حفرهٔ دندانه — جهت: CCW حول نرمال درپوش (−Z) یعنی CW حول +Z... اصلاح زیر
            capBottom[^1] = CadBuild.PolygonLoop(Proj(bottomRing), z0, ccw: false);
            AddLighteningLoops(s, capBottom, bottomRing.Twist, z0, ccw: true);
            solid.Faces.Add(CadBuild.Cap(z0, top: false, capBottom));

            var capTop = new List<CadLoop> { CadBuild.CircleLoop(s.RimOuterRadius, z1, ccwAroundPlusZ: true) };
            capTop.Add(CadBuild.PolygonLoop(Proj(topRing), z1, ccw: false));
            AddLighteningLoops(s, capTop, topRing.Twist, z1, ccw: false);
            solid.Faces.Add(CadBuild.Cap(z1, top: true, capTop));

            CadBuild.AddCylinderWall(solid, 0, 0, s.RimOuterRadius, z0, z1, inward: false);
        }
        else
        {
            var capBottom = new List<CadLoop> { CadBuild.PolygonLoop(Proj(bottomRing), z0, ccw: false) };
            var capTop = new List<CadLoop> { CadBuild.PolygonLoop(Proj(topRing), z1, ccw: true) };
            if (roundBore)
            {
                capBottom.Add(CadBuild.CircleLoop(s.BoreRadius, z0, ccwAroundPlusZ: true));  // حفره: جهت مخالف مرز بیرونی
                capTop.Add(CadBuild.CircleLoop(s.BoreRadius, z1, ccwAroundPlusZ: false));
            }
            else if (s.BoreSection is { Count: >= 3 })
            {
                capBottom.Add(CadBuild.PolygonLoop(SectionPts(s.BoreSection), z0, ccw: true));
                capTop.Add(CadBuild.PolygonLoop(SectionPts(s.BoreSection), z1, ccw: false));
            }
            AddLighteningLoops(s, capBottom, bottomRing.Twist, z0, ccw: true);
            AddLighteningLoops(s, capTop, topRing.Twist, z1, ccw: false);
            solid.Faces.Add(CadBuild.Cap(z0, top: false, capBottom));
            solid.Faces.Add(CadBuild.Cap(z1, top: true, capTop));
        }

        // ---- دیوارهٔ دندانه بین برش‌های متوالی ----
        for (int i = 0; i + 1 < s.Rings.Count; i++)
        {
            var lo = s.Rings[i];
            var hi = s.Rings[i + 1];
            if (s.GapZ0 > 0 && Math.Abs(lo.Z - s.GapZ0) < 1e-9 && Math.Abs(hi.Z - s.GapZ1) < 1e-9)
                continue; // ناحیهٔ گپ جناغی
            CadBuild.AddTwistedWall(solid, lo.Points, hi.Points, inward: s.Internal);
        }

        // ---- گپ مرکزی جناغی ----
        if (s.GapZ0 > 0)
        {
            int i0 = s.Rings.FindIndex(r => Math.Abs(r.Z - s.GapZ0) < 1e-9);
            int i1 = s.Rings.FindIndex(r => Math.Abs(r.Z - s.GapZ1) < 1e-9);
            if (i0 >= 0 && i1 >= 0)
            {
                // طاقچهٔ انتهای نیمهٔ پایین (رو به گپ = بالا) و ابتدای نیمهٔ بالا (رو به گپ = پایین)
                var land0 = new List<CadLoop>
                {
                    CadBuild.PolygonLoop(Proj(s.Rings[i0]), s.GapZ0, ccw: true),
                    CadBuild.CircleLoop(s.GapRadius, s.GapZ0, ccwAroundPlusZ: false),
                };
                solid.Faces.Add(CadBuild.Cap(s.GapZ0, top: true, land0));
                var land1 = new List<CadLoop>
                {
                    CadBuild.PolygonLoop(Proj(s.Rings[i1]), s.GapZ1, ccw: false),
                    CadBuild.CircleLoop(s.GapRadius, s.GapZ1, ccwAroundPlusZ: true),
                };
                solid.Faces.Add(CadBuild.Cap(s.GapZ1, top: false, land1));
                CadBuild.AddCylinderWall(solid, 0, 0, s.GapRadius, s.GapZ0, s.GapZ1, inward: false);
            }
        }

        // ---- بور (تا سرِ توپی ادامه می‌یابد) ----
        if (roundBore)
            CadBuild.AddCylinderWall(solid, 0, 0, s.BoreRadius, z0, boreTopZ, inward: true);
        else if (s.BoreSection is { Count: >= 3 })
            CadBuild.AddPrismWall(solid, SectionPts(s.BoreSection).ToList(), z0, boreTopZ, inward: true);

        // ---- حفره‌های سبک‌سازی ----
        for (int h = 0; h < s.LighteningCenters.Count; h++)
        {
            var (cx, cy) = s.LighteningCenters[h];
            bool isCircle = s.LighteningSection.Count >= 12 && IsCircleish(s.LighteningSection);
            if (s.LighteningFollowTwist)
            {
                var loS = RotatedSection(s.LighteningSection, cx, cy, s.LighteningRotationRad + bottomRing.Twist, z0);
                var hiS = RotatedSection(s.LighteningSection, cx, cy, s.LighteningRotationRad + topRing.Twist, z1);
                CadBuild.AddTwistedWall(solid, loS, hiS, inward: true);
            }
            else if (isCircle)
            {
                double r = s.LighteningSection.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
                CadBuild.AddCylinderWall(solid, cx, cy, r, z0, z1, inward: true);
            }
            else if (s.LighteningSection.Count >= 3)
            {
                CadBuild.AddPrismWall(solid, Translate(s.LighteningSection, cx, cy, s.LighteningRotationRad).ToList(), z0, z1, inward: true);
            }
        }

        // ---- توپی ----
        if (s.HubOuterRadius > 0)
        {
            var hubFloor = new List<CadLoop> { CadBuild.CircleLoop(s.HubOuterRadius, s.HubZ0, ccwAroundPlusZ: false) };
            var hubTop = new List<CadLoop> { CadBuild.CircleLoop(s.HubOuterRadius, s.HubZ1, ccwAroundPlusZ: true) };
            if (roundBore)
            {
                hubFloor.Add(CadBuild.CircleLoop(s.BoreRadius, s.HubZ0, ccwAroundPlusZ: true));
                hubTop.Add(CadBuild.CircleLoop(s.BoreRadius, s.HubZ1, ccwAroundPlusZ: false));
            }
            else if (s.BoreSection is { Count: >= 3 })
            {
                hubFloor.Add(CadBuild.PolygonLoop(SectionPts(s.BoreSection), s.HubZ0, ccw: true));
                hubTop.Add(CadBuild.PolygonLoop(SectionPts(s.BoreSection), s.HubZ1, ccw: false));
            }
            solid.Faces.Add(CadBuild.Cap(s.HubZ0, top: false, hubFloor));
            CadBuild.AddCylinderWall(solid, 0, 0, s.HubOuterRadius, s.HubZ0, s.HubZ1, inward: false);
            solid.Faces.Add(CadBuild.Cap(s.HubZ1, top: true, hubTop));
        }

        notes.Add($"برش‌های پروفیل: {s.Rings.Count}؛ بور: {s.EffectiveBore}؛ حفره‌ها: {s.LighteningCenters.Count}");
    }

    /// <summary>افزودن حلقه‌های حفرهٔ سبک‌سازی به درپوش (جهت حفره = مخالف مرز بیرونی).</summary>
    private static void AddLighteningLoops(PrismaticCadSource s, List<CadLoop> capLoops, double twist, double z, bool ccw)
    {
        if (s.LighteningCenters.Count == 0 || s.LighteningSection.Count < 3)
            return;
        double rot = s.LighteningRotationRad + (s.LighteningFollowTwist ? twist : 0);
        double cos = Math.Cos(s.LighteningFollowTwist ? twist : 0);
        double sin = Math.Sin(s.LighteningFollowTwist ? twist : 0);
        bool isCircle = s.LighteningSection.Count >= 12 && IsCircleish(s.LighteningSection);
        foreach (var (cx, cy) in s.LighteningCenters)
        {
            double tx = cx * cos - cy * sin;
            double ty = cx * sin + cy * cos;
            if (isCircle && !s.LighteningFollowTwist)
            {
                double r = s.LighteningSection.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
                capLoops.Add(CadBuild.OffsetCircleLoop(tx, ty, r, z, ccwAroundPlusZ: ccw));
            }
            else
            {
                capLoops.Add(CadBuild.PolygonLoop(Translate(s.LighteningSection, tx, ty, rot), z, ccw: ccw));
            }
        }
    }

    private static IEnumerable<(double, double)> Proj(CadProfileRing ring)
        => ring.Points.Select(p => (p.X, p.Y));

    private static IEnumerable<(double, double)> SectionPts(List<Point2> section)
        => section.Select(p => (p.X, p.Y));

    private static List<Vector3> RotatedSection(List<Point2> section, double cx, double cy, double rot, double z)
    {
        double cos = Math.Cos(rot), sin = Math.Sin(rot);
        return section.Select(p => new Vector3(
            cx + p.X * cos - p.Y * sin,
            cy + p.X * sin + p.Y * cos,
            z)).ToList();
    }

    private static List<(double, double)> Translate(List<Point2> section, double cx, double cy, double rot)
    {
        double cos = Math.Cos(rot), sin = Math.Sin(rot);
        return section.Select(p => (cx + p.X * cos - p.Y * sin, cy + p.X * sin + p.Y * cos)).ToList();
    }

    private static bool IsCircleish(List<Point2> section)
    {
        if (section.Count < 12) return false;
        double rMean = section.Average(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        return section.All(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - rMean) < 0.02 * rMean + 1e-6);
    }

    // ═══════════ مسیر وجه‌ای از مش ═══════════

    /// <summary>
    /// BREP از مش: مثلث‌های کاملاً هم‌صفحه (نرمال کمی یکسان) در یک وجه ادغام می‌شوند؛
    /// مرز وجه از یال‌های تک‌مالک گروه استخراج می‌شود. برای خانواده‌های منحنی (کرم/مخروطی/…)
    /// سطح دندانه وجه‌وجه می‌ماند — توصیه: با کیفیت درشت‌تر صادر کنید.
    /// </summary>
    private static void BuildFromMesh(CadSolid solid, TriMesh mesh, List<string> notes)
    {
        var verts = mesh.Vertices.ToArray();
        var tris = mesh.Triangles.ToArray();

        var groups = new Dictionary<(int Nx, int Ny, int Nz, int D), List<int>>();
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            var n = Vector3.Cross(b - a, c - a);
            double len = n.Length;
            if (len < 1e-12) continue;
            n /= len;
            double d = -(n.X * a.X + n.Y * a.Y + n.Z * a.Z);
            var key = (Q(n.X), Q(n.Y), Q(n.Z), Qd(d));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
            list.Add(i);

            static int Q(double v) => (int)Math.Round(v * 500);
            static int Qd(double v) => (int)Math.Round(v * 100);
        }

        var edgeCount = new Dictionary<(int, int), int>();
        var emitted = new HashSet<int>();
        foreach (var g in groups.Values)
        {
            if (g.Count < 2) continue; // تک‌مثلث‌ها پایین‌تر مستقل منتشر می‌شوند

            edgeCount.Clear();
            foreach (int ti in g)
            {
                var t = tris[ti];
                AddE(t.A, t.B); AddE(t.B, t.C); AddE(t.C, t.A);
            }
            void AddE(int u, int v)
            {
                var k = u < v ? (u, v) : (v, u);
                edgeCount[k] = edgeCount.TryGetValue(k, out var c) ? c + 1 : 1;
            }

            var boundary = edgeCount.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToHashSet();
            if (boundary.Count < 3) continue;

            var loops = ChainLoops(boundary, verts);
            int chained = loops.Sum(l => l.Count);
            if (loops.Count == 0 || chained != boundary.Count)
                continue; // زنجیره‌سازی ناقص — به مثلث‌های مستقل برگرد (پایین‌تر)

            var nrm = Vector3.Zero;
            foreach (int ti in g)
            {
                var t = tris[ti];
                nrm += Vector3.Cross(verts[t.B] - verts[t.A], verts[t.C] - verts[t.A]);
            }
            nrm = nrm.Normalized();
            var refX = Vector3.Cross(nrm, Vector3.UnitZ);
            refX = refX.Length > 0.1 ? refX.Normalized() : Vector3.UnitX;

            loops.Sort((a, b) => b.Count.CompareTo(a.Count));
            var face = new CadFace
            {
                Surface = new CadPlane { Origin = verts[tris[g[0]].A], AxisZ = nrm, AxisX = refX },
                SurfaceAgreesWithOutward = true,
                Outer = loops[0],
            };
            for (int i = 1; i < loops.Count; i++)
                face.Inner.Add(loops[i]);
            solid.Faces.Add(face);
            foreach (int ti in g)
                emitted.Add(ti);
        }

        // هر مثلثی که در وجه ادغام‌شده نیامده → وجه مثلثی مستقل (سازگار یال‌ها با مرز گروه‌های مجاور)
        for (int i = 0; i < tris.Length; i++)
        {
            if (emitted.Contains(i)) continue;
            var t = tris[i];
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            var n = Vector3.Cross(b - a, c - a);
            if (n.Length < 1e-12) continue;
            n = n.Normalized();
            var refX = Vector3.Cross(n, Vector3.UnitZ);
            refX = refX.Length > 0.1 ? refX.Normalized() : Vector3.UnitX;
            var face = new CadFace
            {
                Surface = new CadPlane { Origin = a, AxisZ = n, AxisX = refX },
                SurfaceAgreesWithOutward = true,
            };
            face.Outer.Add(CadNode.At(a));
            face.Outer.Add(CadNode.At(b));
            face.Outer.Add(CadNode.At(c));
            solid.Faces.Add(face);
        }

        notes.Add($"وجه‌ای از مش: {tris.Length:N0} مثلث → {solid.Faces.Count:N0} وجه.");
    }

    private static List<CadLoop> ChainLoops(HashSet<(int A, int B)> boundaryEdges, Vector3[] verts)
    {
        var byVertex = new Dictionary<int, List<(int A, int B)>>();
        foreach (var e in boundaryEdges)
        {
            if (!byVertex.TryGetValue(e.A, out var l1)) byVertex[e.A] = l1 = new();
            l1.Add(e);
            if (!byVertex.TryGetValue(e.B, out var l2)) byVertex[e.B] = l2 = new();
            l2.Add(e);
        }

        var loops = new List<CadLoop>();
        var used = new HashSet<(int, int)>();
        foreach (var startEdge in boundaryEdges)
        {
            if (used.Contains(startEdge)) continue;
            var loop = new CadLoop();
            int cur = startEdge.A;
            int target = startEdge.B;
            used.Add(startEdge);
            loop.Add(CadNode.At(verts[cur]));
            int guard = 0;
            while (cur != target && guard++ < boundaryEdges.Count + 2)
            {
                loop.Add(CadNode.At(verts[target]));
                cur = target;
                (int A, int B)? next = null;
                if (byVertex.TryGetValue(cur, out var list))
                    foreach (var e in list)
                    {
                        if (used.Contains(e)) continue;
                        next = e;
                        break;
                    }
                if (next is not { } ne) break;
                used.Add(ne);
                target = ne.A == cur ? ne.B : ne.A;
            }
            if (loop.Count >= 3)
                loops.Add(loop);
        }
        return loops;
    }
}
