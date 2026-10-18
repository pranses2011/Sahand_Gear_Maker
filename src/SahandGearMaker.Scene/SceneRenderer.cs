using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Scene;

/// <summary>
/// رندرر نرم‌افزاری حرفه‌ای (فاز ۱A نقشهٔ راه):
///  - رشته‌فراخوانی امن: Render از رشتهٔ رندر صدا زده می‌شود؛ بافرها پایدارند (P1/P2).
///  - رسترایز tile-parallel روی نوارهای افقی (P3).
///  - Backface culling با علامت حجم مش + Frustum culling با کرهٔ محیطی AABB (P4/P5).
///  - برش صفحهٔ نزدیک و صفحهٔ برش نمایشی با Sutherland–Hodgman در فضای دوربین (V2).
///  - SSAA×2 در سکون (باکس داون‌سمپل) — ضدپلگی مؤثر بدون هزینهٔ پیوسته (V1).
///  - سایه‌زنی گورا دو نور جهت‌دار + محیط + هایلایت Blinn-Phong ملایم (V3).
///  - پیش‌پردازش مثلث‌ها در بافر مسطح پایدار — بدون تخصیص فریمی، بدون List در حلقهٔ داغ.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private const double NearPlane = 0.5;
    private const double Ambient = 0.30;
    private const double SpecularStrength = 0.18;
    private const double SpecularPower = 28.0;

    private static readonly Vector3 Light1 = new Vector3(0.42, -0.34, 0.84).Normalized();
    private static readonly Vector3 Light2 = new Vector3(-0.52, 0.36, 0.42).Normalized();

    private int _w, _h, _ss;
    private byte[] _pixels = Array.Empty<byte>();
    private float[] _zbuf = Array.Empty<float>();
    private byte[] _output = Array.Empty<byte>();
    private readonly Dictionary<string, PreparedObject> _cache = new();
    private int _pw, _ph;

    /// <summary>بافر خروجی نهایی BGRA — تا فریم بعد معتبر است.</summary>
    public ReadOnlySpan<byte> Output => _output;

    public int Width => _w;
    public int Height => _h;

    public void Resize(int width, int height, int supersample)
    {
        int ss = Math.Clamp(supersample, 1, 4);
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (width == _w && height == _h && ss == _ss) return;
        _w = width; _h = height; _ss = ss;
        _pw = width * ss; _ph = height * ss;
        _pixels = new byte[_pw * _ph * 4];
        _zbuf = new float[_pw * _ph];
        _output = new byte[width * height * 4];
    }

    public void Dispose() { }

    // ---------- رندر ----------

    public RenderStats Render(IReadOnlyList<SceneObject> objects, in CameraFrame camIn, in RenderOptions optIn, double fps)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cam = camIn;  // کپی محلی برای استفاده در لامبداهای موازی
        var opt = optIn;

        ClearBackground(opt.Background);

        bool useClip = opt.ClipZ is < 1e9;
        var clipN = useClip ? new Vector3(cam.Right.Z, cam.Up.Z, cam.Forward.Z) : Vector3.Zero;
        double clipOff = useClip ? opt.ClipZ - cam.Eye.Z : 0;

        // آماده‌سازی + پیش‌پردازش مثلث‌های هر شیئ
        var batches = new List<RenderBatch>(objects.Count);
        int trisDrawn = 0, objsDrawn = 0;
        foreach (var obj in objects)
        {
            if (!obj.Visible || obj.IsReference) continue;
            var b = PrepareObject(obj, cam, opt, clipN, clipOff);
            if (b is null) continue;
            batches.Add(b);
            trisDrawn += b.PolyCount;
            objsDrawn++;
        }

        var opaque = batches.Where(b => b.Alpha >= 0.999).ToList();
        var transparent = batches.Where(b => b.Alpha < 0.999)
            .OrderByDescending(b => Vector3.Dot(b.Centroid - cam.Eye, cam.Forward))
            .ToList();

        int bandCount = Math.Clamp(Environment.ProcessorCount * 2, 2, 32);
        int bandHeight = (int)Math.Ceiling(_ph / (double)bandCount);

        if (opt.Mode == DisplayMode.Wireframe)
        {
            Parallel.For(0, bandCount, band =>
            {
                int y0 = band * bandHeight, y1 = Math.Min(_ph, y0 + bandHeight);
                foreach (var b in batches)
                    EdgesBand(b, y0, y1, zTest: false, EdgeColorOf(b, opt));
            });
        }
        else
        {
            Parallel.For(0, bandCount, band =>
            {
                int y0 = band * bandHeight, y1 = Math.Min(_ph, y0 + bandHeight);
                foreach (var b in opaque)
                    FillBand(b, y0, y1, writeZ: true);
            });

            // «سایه + لبه» فقط لبه‌های ویژه را می‌کشد (شکست‌های واقعی سطح)، نه همهٔ یال‌های
            // مثلث‌بندی — سطح منحنی یکپارچه دیده می‌شود (رفع نمای مش‌طور مثلثی).
            if (opt.Mode == DisplayMode.ShadedWithEdges)
            {
                Parallel.For(0, bandCount, band =>
                {
                    int y0 = band * bandHeight, y1 = Math.Min(_ph, y0 + bandHeight);
                    foreach (var b in opaque)
                        FeatureEdgesBand(b, y0, y1, EdgeColorOf(b, opt));
                });
            }

            foreach (var b in transparent)
            {
                Parallel.For(0, bandCount, band =>
                {
                    int y0 = band * bandHeight, y1 = Math.Min(_ph, y0 + bandHeight);
                    FillBand(b, y0, y1, writeZ: false);
                    if (opt.Mode == DisplayMode.ShadedWithEdges && b.Alpha > 0.55)
                        FeatureEdgesBand(b, y0, y1, EdgeColorOf(b, opt));
                });
            }
        }

        // مدل‌های مرجع — شبح شفاف (فاز ۲٫۳)
        foreach (var obj in objects)
        {
            if (!obj.Visible || !obj.IsReference) continue;
            var b = PrepareObject(obj, cam, opt with { ClipZ = double.PositiveInfinity }, Vector3.Zero, 0);
            if (b is null) continue;
            Parallel.For(0, bandCount, band =>
            {
                int y0 = band * bandHeight, y1 = Math.Min(_ph, y0 + bandHeight);
                FillBand(b, y0, y1, writeZ: false);
            });
            trisDrawn += b.PolyCount;
        }

        var bounds = CombinedBounds(objects);

        // فریم دوربینِ مقیاس‌شده به بافر SS — روکش‌ها (گرید/محور/برچسب) باید در همان فضای پیکسلیِ
        // هندسه رسم شوند؛ در غیر این صورت با روشن‌شدن SSAA ناگهان جابه‌جا/نصف‌مقیاس می‌شدند (رفع پرش گرید).
        var camSS = cam with
        {
            Focal = cam.Focal * _ss,
            OrthoScale = cam.OrthoScale * _ss,
            HalfW = _pw / 2.0,
            HalfH = _ph / 2.0,
        };

        if (opt.ShowGrid || opt.ShowAxes)
            SceneOverlay.DrawWorldOverlay(_pixels, _zbuf, _pw, _ph, camSS, bounds, opt);
        SceneOverlay.DrawScaleBar(_pixels, _pw, _ph, camSS, bounds);

        // پاره‌خط‌های اندازه‌گیری (فاز ۴٫۱) — همیشه روی همه‌چیز
        if (opt.MeasureLines is { Count: > 0 } lines)
        {
            foreach (var (a, b) in lines)
            {
                SceneOverlay.DrawWorldLine(_pixels, _zbuf, _pw, _ph, camSS, a, b, new Rgb(245, 158, 11), 2);
                SceneOverlay.DrawWorldLine(_pixels, _zbuf, _pw, _ph, camSS, a, b, new Rgb(120, 70, 0), 1);
            }
        }
        if (opt.MeasureLabels is { Count: > 0 } labels)
        {
            foreach (var (at, label) in labels)
            {
                // نقطهٔ صفحه‌ای متن — پروژهٔ نقطهٔ جهان
                var rel = at - camSS.Eye;
                double cz = Vector3.Dot(rel, camSS.Forward);
                if (cz <= 1) continue;
                double cx = Vector3.Dot(rel, camSS.Right), cy = Vector3.Dot(rel, camSS.Up);
                double inv = camSS.Projection == ProjectionKind.Perspective ? 1.0 / cz : 1.0;
                double sx = (camSS.Projection == ProjectionKind.Perspective ? camSS.HalfW + cx * camSS.Focal * inv : camSS.HalfW + cx * camSS.OrthoScale);
                double sy = (camSS.Projection == ProjectionKind.Perspective ? camSS.HalfH - cy * camSS.Focal * inv : camSS.HalfH - cy * camSS.OrthoScale);
                SceneOverlay.DrawTinyTextAt(_pixels, _pw, _ph, (int)sx, (int)sy, label, new Rgb(120, 60, 0));
            }
        }

        Downsample();

        // پاک‌سازی کشِ اشیاء حذف‌شده
        if (_cache.Count > objects.Count + 4)
        {
            var live = new HashSet<string>(objects.Select(o => o.Id));
            foreach (var stale in _cache.Keys.Where(k => !live.Contains(k)).ToList())
                _cache.Remove(stale);
        }

        sw.Stop();
        return new RenderStats
        {
            TrianglesDrawn = trisDrawn,
            ObjectsDrawn = objsDrawn,
            Width = _w,
            Height = _h,
            Supersample = _ss,
            Elapsed = sw.Elapsed,
            Fps = fps,
        };
    }

    private static Rgb EdgeColorOf(RenderBatch b, RenderOptions opt)
        => b.IsHighlight ? opt.HighlightColor : new Rgb(36, 48, 72);

    /// <summary>ترکیب رنگ قطعه با سهمی از رنگ انتخاب — کیو انتخاب بدون پوشاندن رنگ کاربر.</summary>
    private static Rgb BlendTint(Rgb baseColor, Rgb tint, double tintShare)
    {
        double k = Math.Clamp(tintShare, 0, 1), ik = 1 - k;
        return new Rgb(
            (byte)Math.Clamp(baseColor.R * ik + tint.R * k, 0, 255),
            (byte)Math.Clamp(baseColor.G * ik + tint.G * k, 0, 255),
            (byte)Math.Clamp(baseColor.B * ik + tint.B * k, 0, 255));
    }

    public static (Vector3 Min, Vector3 Max) CombinedBounds(IReadOnlyList<SceneObject> objects)
    {
        var min = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vector3(double.MinValue, double.MinValue, double.MinValue);
        bool any = false;
        foreach (var o in objects)
        {
            if (!o.Visible) continue;
            var b = o.WorldBounds;
            any = true;
            min = new Vector3(Math.Min(min.X, b.Min.X), Math.Min(min.Y, b.Min.Y), Math.Min(min.Z, b.Min.Z));
            max = new Vector3(Math.Max(max.X, b.Max.X), Math.Max(max.Y, b.Max.Y), Math.Max(max.Z, b.Max.Z));
        }
        return any ? (min, max) : (new Vector3(-60, -60, -25), new Vector3(60, 60, 25));
    }

    // ---------- آماده‌سازی ----------

    private RenderBatch? PrepareObject(SceneObject obj, in CameraFrame cam, in RenderOptions opt, Vector3 clipN, double clipOff)
    {
        var wb = obj.WorldBounds;
        if (!FrustumVisible(wb, cam)) return null;

        if (!_cache.TryGetValue(obj.Id, out var prep) || !ReferenceEquals(prep.Mesh, obj.Mesh) || prep.Capacity < obj.Mesh.VertexCount)
        {
            prep = new PreparedObject(obj.Mesh, obj.Mesh.VertexCount);
            _cache[obj.Id] = prep;
        }

        bool highlight = opt.HighlightId == obj.Id;
        prep.Alpha = Math.Clamp(obj.Opacity, 0.04, 1.0);
        prep.IsHighlight = highlight;
        prep.Focal = cam.Focal * _ss;
        prep.Ortho = cam.OrthoScale * _ss;
        prep.HalfW = _pw / 2.0;
        prep.HalfH = _ph / 2.0;
        prep.Perspective = cam.Projection == ProjectionKind.Perspective;
        prep.HasClip = clipOff is < 1e8 && clipN.LengthSquared > 1e-12;
        prep.ClipN = clipN; prep.ClipOff = clipOff;

        if (prep.WindingChecked is false)
        {
            prep.WindingChecked = true;
            try
            {
                var report = obj.Mesh.Validate();
                prep.Winding = Math.Abs(report.SignedVolumeMm3) < 1e-9 ? 0 : (report.SignedVolumeMm3 > 0 ? 1 : -1);
            }
            catch
            {
                prep.Winding = 0;
            }
        }

        // نورپردازی گورا + تبدیل به فضای دوربین — سری روی رأس‌ها (رشتهٔ رندر؛ رستر پیکسلی موازی است)
        var tr = obj.Transform;
        var normals = obj.EnsureVertexNormals();
        var verts = obj.Mesh.Vertices;
        int vc = obj.Mesh.VertexCount;
        // انتخاب = تینت ۳۰٪ (نه جایگزینی کامل رنگ) — رنگ خود قطعه دیده بماند (رفع «تغییر رنگ اعمال نمی‌شود»)
        var baseColor = highlight ? BlendTint(obj.Color, opt.HighlightColor, 0.30) : obj.Color;
        double cr = baseColor.R, cg = baseColor.G, cb = baseColor.B;

        var eye = cam.Eye;
        for (int i = 0; i < vc; i++)
        {
            var wpos = tr.Apply(verts[i]);
            var rel = wpos - eye;
            prep.CX[i] = (float)Vector3.Dot(rel, cam.Right);
            prep.CY[i] = (float)Vector3.Dot(rel, cam.Up);
            prep.CZ[i] = (float)Vector3.Dot(rel, cam.Forward);

            var n = tr.ApplyNormal(normals[i]);
            double n1 = Vector3.Dot(n, Light1), n2 = Vector3.Dot(n, Light2);
            if (prep.Winding == 0) { n1 = Math.Abs(n1); n2 = Math.Abs(n2); }
            double diff = Math.Max(0, n1) * 0.70 + Math.Max(0, n2) * 0.24 + Ambient;
            var h = (eye - wpos).Normalized() + Light1;
            var hh = h.Normalized();
            double spec = Math.Pow(Math.Max(0, Vector3.Dot(n, hh)), SpecularPower) * SpecularStrength;
            double k = diff + spec;
            prep.R[i] = (float)Math.Clamp(cr * k, 0, 255);
            prep.G[i] = (float)Math.Clamp(cg * k, 0, 255);
            prep.B[i] = (float)Math.Clamp(cb * k, 0, 255);
        }

        // پیش‌پردازش مثلث‌ها: برش + تصویرسازی + backface culling → بافر مسطح
        int tc = obj.Mesh.TriangleCount;
        prep.EnsurePolyCapacity(tc);
        var tris = obj.Mesh.Triangles;
        int written = 0;
        var px = prep.PolyX; var py = prep.PolyY; var pd = prep.PolyD;
        var pr = prep.PolyR; var pg = prep.PolyG; var pb = prep.PolyB;
        var ps = prep.PolyStart; var pn = prep.PolyN;

        for (int ti = 0; ti < tc; ti++)
        {
            var t = tris[ti];
            prep.ClipProject(t, written, px, py, pd, pr, pg, pb, out int count);
            if (count >= 3)
            {
                // backface culling با مساحت امضادار صفحه‌ای (فاز ۱٫۳)
                double area2 = 0;
                int s = ps[written];
                for (int k = 0; k < count; k++)
                {
                    int k2 = (k + 1) % count;
                    area2 += px[s + k] * py[s + k2] - px[s + k2] * py[s + k];
                }
                if (prep.Winding != 0 && area2 * prep.Winding < 0) continue;
                pn[written] = (byte)count;
                written++;
            }
        }
        prep.PolyCount = written;

        // مرکز برای مرتب‌سازی شفاف‌ها
        var wbC = new Vector3((wb.Min.X + wb.Max.X) / 2, (wb.Min.Y + wb.Max.Y) / 2, (wb.Min.Z + wb.Max.Z) / 2);
        prep.Centroid = wbC;

        return new RenderBatch(prep);
    }

    private static bool FrustumVisible((Vector3 Min, Vector3 Max) b, in CameraFrame cam)
    {
        var c = new Vector3((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, (b.Min.Z + b.Max.Z) / 2);
        double r = (b.Max - b.Min).Length / 2;
        var rel = c - cam.Eye;
        double cz = Vector3.Dot(rel, cam.Forward);
        if (cz + r < NearPlane) return false;
        if (cz - r > 1e-6 && cam.Projection == ProjectionKind.Perspective)
        {
            double halfTanW = cam.HalfW / cam.Focal * 1.4;
            double halfTanH = cam.HalfH / cam.Focal * 1.4;
            double cx = Vector3.Dot(rel, cam.Right), cy = Vector3.Dot(rel, cam.Up);
            if (Math.Abs(cx) - r * 1.25 > cz * halfTanW) return false;
            if (Math.Abs(cy) - r * 1.25 > cz * halfTanH) return false;
        }
        else if (cam.Projection == ProjectionKind.Orthographic)
        {
            double cx = Vector3.Dot(rel, cam.Right), cy = Vector3.Dot(rel, cam.Up);
            if (Math.Abs(cx) - r > cam.HalfW / cam.OrthoScale + 1) return false;
            if (Math.Abs(cy) - r > cam.HalfH / cam.OrthoScale + 1) return false;
        }
        return true;
    }

    // ---------- پرکردن و لبه‌ها (نوارها) ----------

    private void FillBand(RenderBatch b, int y0, int y1, bool writeZ)
    {
        var p = b.Prep;
        var px = p.PolyX; var py = p.PolyY; var pd = p.PolyD;
        var pr = p.PolyR; var pg = p.PolyG; var pb2 = p.PolyB;
        var ps = p.PolyStart; var pn = p.PolyN;
        bool blend = b.Alpha < 0.999;
        double alpha = b.Alpha, ia = 1 - alpha;

        for (int pi = 0; pi < p.PolyCount; pi++)
        {
            int s = ps[pi], n = pn[pi];
            // bbox
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int k = 0; k < n; k++)
            {
                double X = px[s + k], Y = py[s + k];
                if (X < minX) minX = X; if (X > maxX) maxX = X;
                if (Y < minY) minY = Y; if (Y > maxY) maxY = Y;
            }
            int iy0 = Math.Max(y0, (int)Math.Ceiling(minY - 0.5));
            int iy1 = Math.Min(y1 - 1, (int)Math.Floor(maxY + 0.5));
            int ix0 = Math.Max(0, (int)Math.Ceiling(minX - 0.5));
            int ix1 = Math.Min(_pw - 1, (int)Math.Floor(maxX + 0.5));
            if (iy0 > iy1 || ix0 > ix1) continue;

            // بادبزکی از رأس ۰
            for (int k = 1; k + 1 < n; k++)
            {
                double ax = px[s], ay = py[s], ad = pd[s], ar = pr[s], ag = pg[s], ab = pb2[s];
                double bx = px[s + k], by = py[s + k], bd = pd[s + k], br = pr[s + k], bg = pg[s + k], bb = pb2[s + k];
                double cx = px[s + k + 1], cy = py[s + k + 1], cd = pd[s + k + 1], crr = pr[s + k + 1], cgg = pg[s + k + 1], cbb = pb2[s + k + 1];
                double area2 = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                if (Math.Abs(area2) < 1e-12) continue;
                double inv = 1.0 / area2;

                for (int y = iy0; y <= iy1; y++)
                {
                    double yy = y + 0.5;
                    for (int x = ix0; x <= ix1; x++)
                    {
                        double xx = x + 0.5;
                        double w0 = ((bx - ax) * (yy - ay) - (xx - ax) * (by - ay)) * inv;
                        double w1 = ((xx - cx) * (by - cy) - (bx - cx) * (yy - cy)) * inv;
                        double w2 = 1.0 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        double dk = w0 * cd + w1 * ad + w2 * bd;
                        int idx = y * _pw + x;
                        if (dk <= _zbuf[idx]) continue;
                        double R = w0 * crr + w1 * ar + w2 * br;
                        double G = w0 * cgg + w1 * ag + w2 * bg;
                        double B = w0 * cbb + w1 * ab + w2 * bb;
                        int o = idx * 4;
                        if (blend)
                        {
                            _pixels[o] = (byte)Math.Clamp(B * alpha + _pixels[o] * ia, 0, 255);
                            _pixels[o + 1] = (byte)Math.Clamp(G * alpha + _pixels[o + 1] * ia, 0, 255);
                            _pixels[o + 2] = (byte)Math.Clamp(R * alpha + _pixels[o + 2] * ia, 0, 255);
                        }
                        else
                        {
                            _pixels[o] = (byte)Math.Clamp(B, 0, 255);
                            _pixels[o + 1] = (byte)Math.Clamp(G, 0, 255);
                            _pixels[o + 2] = (byte)Math.Clamp(R, 0, 255);
                        }
                        if (writeZ) _zbuf[idx] = (float)dk;
                    }
                }
            }
        }
    }

    private void EdgesBand(RenderBatch b, int y0, int y1, bool zTest, Rgb color)
    {
        var p = b.Prep;
        var px = p.PolyX; var py = p.PolyY; var pd = p.PolyD;
        var ps = p.PolyStart; var pn = p.PolyN;
        for (int pi = 0; pi < p.PolyCount; pi++)
        {
            int s = ps[pi], n = pn[pi];
            for (int k = 0; k < n; k++)
            {
                int k2 = (k + 1) % n;
                DrawLine(px[s + k], py[s + k], pd[s + k], px[s + k2], py[s + k2], pd[s + k2], y0, y1, zTest, color);
            }
        }
    }

    /// <summary>
    /// رسم لبه‌های ویژهٔ یک شیئ — یال‌هایی که زاویهٔ دیهدرال وجوه مجاور از آستانه فراتر می‌برد
    /// یا مرز باز مش‌اند؛ از مختصات دوربینِ ازپیش‌محو‌شدهٔ رأس‌ها (CX/CY/CZ) تصویر می‌شوند.
    /// </summary>
    private void FeatureEdgesBand(RenderBatch b, int y0, int y1, Rgb color)
    {
        var p = b.Prep;
        var edges = p.FeatureEdges;
        if (edges is not { Length: > 0 }) return;
        var cx = p.CX; var cy = p.CY; var cz = p.CZ;
        double focal = p.Focal, ortho = p.Ortho, hw = p.HalfW, hh = p.HalfH;
        bool persp = p.Perspective;

        for (int e = 0; e + 1 < edges.Length; e += 2)
        {
            int a = edges[e], c = edges[e + 1];
            double az = cz[a], bz = cz[c];
            if (az < NearPlane && bz < NearPlane) continue;

            double ax = cx[a], ay = cy[a], bx = cx[c], by = cy[c];
            if (az < NearPlane)
            {
                double t = (NearPlane - az) / (bz - az);
                ax += (bx - ax) * t; ay += (by - ay) * t; az = NearPlane;
            }
            else if (bz < NearPlane)
            {
                double t = (NearPlane - bz) / (az - bz);
                bx += (ax - bx) * t; by += (ay - by) * t; bz = NearPlane;
            }

            double sx0, sy0, dk0, sx1, sy1, dk1;
            if (persp)
            {
                double i0 = 1.0 / az, i1 = 1.0 / bz;
                sx0 = hw + ax * focal * i0; sy0 = hh - ay * focal * i0; dk0 = i0;
                sx1 = hw + bx * focal * i1; sy1 = hh - by * focal * i1; dk1 = i1;
            }
            else
            {
                sx0 = hw + ax * ortho; sy0 = hh - ay * ortho; dk0 = -az;
                sx1 = hw + bx * ortho; sy1 = hh - by * ortho; dk1 = -bz;
            }
            DrawLine(sx0, sy0, dk0, sx1, sy1, dk1, y0, y1, zTest: true, color);
        }
    }

    private void DrawLine(double ax, double ay, double ad, double bx, double by, double bd,
        int bandY0, int bandY1, bool zTest, Rgb color)
    {
        // کلیپ سریع به نوار
        if (Math.Max(ay, by) < bandY0 || Math.Min(ay, by) > bandY1) return;
        int x0 = (int)Math.Round(ax), y0 = (int)Math.Round(ay);
        int x1 = (int)Math.Round(bx), y1 = (int)Math.Round(by);
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        if (dx + dy == 0)
        {
            PutPixelZ(x0, y0, (ad + bd) / 2, zTest, color);
            return;
        }
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy, steps = dx + dy, guard = 0;
        while (guard++ < 200000)
        {
            double t = (double)(guard - 1) / steps;
            double dk = ad + (bd - ad) * t;
            if (y0 >= bandY0 && y0 < bandY1)
                PutPixelZ(x0, y0, dk, zTest, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private void PutPixelZ(int x, int y, double dk, bool zTest, Rgb color)
    {
        if ((uint)x >= (uint)_pw || (uint)y >= (uint)_ph) return;
        int idx = y * _pw + x;
        if (zTest && dk <= _zbuf[idx] + 1e-12) return; // لبه فقط روی سطح خودش یا جلوتر
        int o = idx * 4;
        _pixels[o] = color.B; _pixels[o + 1] = color.G; _pixels[o + 2] = color.R;
    }

    // ---------- پاک‌کردن/داون‌سمپل ----------

    private void ClearBackground(Rgb bg)
    {
        var px = _pixels; var zb = _zbuf;
        int total = _pw * _ph;
        Parallel.For(0, 4, quarter =>
        {
            int i0 = total * quarter / 4, i1 = total * (quarter + 1) / 4;
            Array.Fill(zb, float.NegativeInfinity, i0, i1 - i0);
            for (int i = i0; i < i1; i++)
            {
                int o = i * 4;
                px[o] = bg.B; px[o + 1] = bg.G; px[o + 2] = bg.R; px[o + 3] = 255;
            }
        });
    }

    private void Downsample()
    {
        if (_ss == 1)
        {
            Buffer.BlockCopy(_pixels, 0, _output, 0, _pixels.Length);
            return;
        }
        int w = _w, h = _h, ss = _ss, pw = _pw;
        var src = _pixels; var dst = _output;
        int ss2 = ss * ss;
        Parallel.For(0, h, y =>
        {
            int oRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = oRow + x * 4;
                int b = 0, g = 0, r = 0;
                for (int dy = 0; dy < ss; dy++)
                {
                    int row = (y * ss + dy) * pw * 4;
                    for (int dx = 0; dx < ss; dx++)
                    {
                        int i = row + (x * ss + dx) * 4;
                        b += src[i]; g += src[i + 1]; r += src[i + 2];
                    }
                }
                dst[o] = (byte)((b + ss2 / 2) / ss2);
                dst[o + 1] = (byte)((g + ss2 / 2) / ss2);
                dst[o + 2] = (byte)((r + ss2 / 2) / ss2);
                dst[o + 3] = 255;
            }
        });
    }

    // ---------- انواع داخلی ----------

    /// <summary>داده‌های آمادهٔ یک شیئ برای یک فریم — پوشش سبک روی PreparedObject.</summary>
    private sealed class RenderBatch(PreparedObject prep)
    {
        public PreparedObject Prep { get; } = prep;
        public double Alpha => Prep.Alpha;
        public bool IsHighlight => Prep.IsHighlight;
        public Vector3 Centroid => Prep.Centroid;
        public int PolyCount => Prep.PolyCount;
    }

    /// <summary>بافرهای پایدار بین فریم‌ها — فقط هنگام تغییر مش رشد می‌کنند (P1/P2).</summary>
    private sealed class PreparedObject(TriMesh mesh, int capacity)
    {
        public readonly TriMesh Mesh = mesh;
        public readonly int Capacity = capacity;
        public readonly float[] CX = new float[capacity], CY = new float[capacity], CZ = new float[capacity];
        public readonly float[] R = new float[capacity], G = new float[capacity], B = new float[capacity];

        public double[] PolyX = new double[1], PolyY = new double[1], PolyD = new double[1];
        public double[] PolyR = new double[1], PolyG = new double[1], PolyB = new double[1];
        public int[] PolyStart = new int[1];
        public byte[] PolyN = new byte[1];

        /// <summary>لبه‌های ویژه به‌صورت جفت‌اندیس رأس — یک‌بار per مش محاسبه و کش می‌شود.</summary>
        public int[] FeatureEdges = BuildFeatureEdges(mesh);

        public double Alpha = 1.0;
        public bool IsHighlight;
        public Vector3 Centroid;
        public double Focal, Ortho, HalfW, HalfH;
        public bool Perspective = true;
        public bool HasClip;
        public Vector3 ClipN;
        public double ClipOff;
        public int Winding;
        public bool WindingChecked;

        public void EnsurePolyCapacity(int triangleCount)
        {
            int need = triangleCount * 4 + 8;
            if (PolyStart.Length < triangleCount + 1 || PolyX.Length < need)
            {
                PolyX = new double[need]; PolyY = new double[need]; PolyD = new double[need];
                PolyR = new double[need]; PolyG = new double[need]; PolyB = new double[need];
                PolyStart = new int[triangleCount + 1];
                PolyN = new byte[triangleCount];
            }
        }

        public int PolyCount { get; set; }

        /// <summary>برش + تصویرسازی یک مثلث در بافر مسطح از موقعیت slot.</summary>
        public void ClipProject(in Triangle t, int slot,
            double[] ox, double[] oy, double[] od, double[] orr, double[] og, double[] ob, out int count)
        {
            Span<ClipV> tri = stackalloc ClipV[3]
            {
                new(CX[t.A], CY[t.A], CZ[t.A], R[t.A], G[t.A], B[t.A]),
                new(CX[t.B], CY[t.B], CZ[t.B], R[t.B], G[t.B], B[t.B]),
                new(CX[t.C], CY[t.C], CZ[t.C], R[t.C], G[t.C], B[t.C]),
            };
            Span<ClipV> buf = stackalloc ClipV[8];
            int n = ClipPlane(tri, 3, buf, 0, 0, 1, -NearPlane); // camZ >= NearPlane
            if (n < 3) { count = 0; return; }
            Span<ClipV> buf2 = stackalloc ClipV[8];
            if (HasClip)
            {
                n = ClipPlane(buf, n, buf2, -ClipN.X, -ClipN.Y, -ClipN.Z, ClipOff); // n_c·v <= off
                if (n < 3) { count = 0; return; }
                buf2.Slice(0, n).CopyTo(buf);
            }
            else
            {
                // buf همین حالا معتبر است
            }

            int start = slot * 4;
            int w = 0;
            for (int i = 0; i < n; i++)
            {
                var v = buf[i];
                double sx, sy, dk;
                if (Perspective)
                {
                    if (v.Z <= NearPlane) { count = 0; return; }
                    double inv = 1.0 / v.Z;
                    sx = HalfW + v.X * Focal * inv;
                    sy = HalfH - v.Y * Focal * inv;
                    dk = inv;
                }
                else
                {
                    sx = HalfW + v.X * Ortho;
                    sy = HalfH - v.Y * Ortho;
                    dk = -v.Z;
                }
                ox[start + w] = sx; oy[start + w] = sy; od[start + w] = dk;
                orr[start + w] = v.R; og[start + w] = v.G; ob[start + w] = v.B;
                w++;
            }
            PolyStart[slot] = start;
            count = w;
        }

        /// <summary>Sutherland–Hodgman — نگه‌داشتن نیم‌فضای nx·x + ny·y + nz·z + d ≥ 0.</summary>
        private static int ClipPlane(ReadOnlySpan<ClipV> src, int srcN, Span<ClipV> dst,
            double nx, double ny, double nz, double d)
        {
            int m = 0;
            for (int i = 0; i < srcN; i++)
            {
                var cur = src[i];
                var nxt = src[(i + 1) % srcN];
                double dc = nx * cur.X + ny * cur.Y + nz * cur.Z + d;
                double dn = nx * nxt.X + ny * nxt.Y + nz * nxt.Z + d;
                if (dc >= 0) dst[m++] = cur;
                if ((dc >= 0) != (dn >= 0) && m < dst.Length)
                {
                    double t = dc / (dc - dn);
                    dst[m++] = new ClipV(
                        cur.X + (nxt.X - cur.X) * t,
                        cur.Y + (nxt.Y - cur.Y) * t,
                        cur.Z + (nxt.Z - cur.Z) * t,
                        cur.R + (nxt.R - cur.R) * t,
                        cur.G + (nxt.G - cur.G) * t,
                        cur.B + (nxt.B - cur.B) * t);
                }
            }
            return m;
        }

        // ---------- لبه‌های ویژه ----------

        /// <summary>آستانهٔ زاویهٔ دیهدرال برای «لبهٔ ویژه» — کمتر از این، سطح صاف در نظر گرفته می‌شود.</summary>
        private const double FeatureCosThreshold = 0.9063; // ≈ 25°

        /// <summary>
        /// استخراج لبه‌های ویژهٔ مش: یال‌های دارای وجوه مجاور با نرمال واگرا (شکست واقعی سطح)
        /// و یال‌های مرزی باز. یک‌بار برای هر مش محاسبه می‌شود و بین فریم‌ها کش می‌ماند.
        /// جدول درگذشتی خطی — سریع‌تر از مرتب‌سازی/دیکشنری برای مش‌های سنگین.
        /// </summary>
        private static int[] BuildFeatureEdges(TriMesh mesh)
        {
            try
            {
                var verts = mesh.Vertices;
                var tris = mesh.Triangles;
                int tc = tris.Length;
                if (tc == 0)
                    return Array.Empty<int>();

                var normals = new Vector3[tc];
                int cap = 1;
                while (cap < tc * 6)
                    cap <<= 1;
                int mask = cap - 1;
                var slotKey = new long[cap];
                var slotA = new int[cap];
                var slotB = new int[cap];
                Array.Fill(slotA, -1);
                Array.Fill(slotB, -1);

                void Put(int u, int v, int face)
                {
                    long key = u < v ? (long)u * 400_000_009L + v : (long)v * 400_000_009L + u;
                    int h = (int)(key ^ (key >> 29)) & mask;
                    while (true)
                    {
                        if (slotA[h] < 0)
                        {
                            slotKey[h] = key;
                            slotA[h] = face;
                            return;
                        }
                        if (slotKey[h] == key)
                        {
                            if (slotB[h] == -1) slotB[h] = face;
                            else slotB[h] = -2; // یال غیرمنیفولد — از لبه‌های ویژه حذف می‌شود
                            return;
                        }
                        h = (h + 1) & mask;
                    }
                }

                for (int ti = 0; ti < tc; ti++)
                {
                    var t = tris[ti];
                    normals[ti] = t.Normal(verts);
                    Put(t.A, t.B, ti);
                    Put(t.B, t.C, ti);
                    Put(t.C, t.A, ti);
                }

                var result = new List<int>(cap / 24 + 16);
                for (int h = 0; h < cap; h++)
                {
                    if (slotA[h] < 0)
                        continue;
                    bool feature = slotB[h] == -1 // مرز باز
                        || (slotB[h] >= 0 && Vector3.Dot(normals[slotA[h]], normals[slotB[h]]) < FeatureCosThreshold);
                    if (feature)
                    {
                        long key = slotKey[h];
                        result.Add((int)(key / 400_000_009L));
                        result.Add((int)(key % 400_000_009L));
                    }
                }
                return result.ToArray();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }
    }

    private readonly struct ClipV(double x, double y, double z, double r, double g, double b)
    {
        public readonly double X = x, Y = y, Z = z, R = r, G = g, B = b;
    }
}
