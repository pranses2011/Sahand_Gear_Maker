using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Drawing;

/// <summary>عنصر خط در نقشه (mm در مختصات صفحه).</summary>
public sealed record DrawingLine(double X1, double Y1, double X2, double Y2, string Style = "solid", double WidthMm = 0.25);

/// <summary>عنصر متن (راست‌چین برای فارسی).</summary>
public sealed record DrawingText(double X, double Y, string Text, double SizeMm = 2.6, string Align = "right", bool Bold = false);

/// <summary>یک نما (مجموعهٔ خطوط پروفیل + خطوط مرکز/اندازه).</summary>
public sealed class DrawingView
{
    public string Title { get; set; } = "";
    /// <summary>مرز بیرونی قطعه در صفحهٔ نما (نقاط mm).</summary>
    public List<List<(double X, double Y)>> Outlines { get; } = new();
    public List<DrawingLine> Lines { get; } = new();
    public List<DrawingText> Texts { get; } = new();
    /// <summary>مقیاس نمایش نسبت به مدل واقعی.</summary>
    public double Scale { get; set; } = 1.0;
    /// <summary>جعبهٔ محیطی نما (mm مدل).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds { get; set; }
}

/// <summary>سطر جدول پارامترها.</summary>
public sealed record ParameterRow(string Label, string Value, string Unit);

/// <summary>مدل سند نقشهٔ فنی (سند ۰۷، بخش ۳٫۳).</summary>
public sealed class TechnicalDrawingDocument
{
    public string Title { get; set; } = "نقشهٔ فنی چرخ‌دنده";
    public string ProjectName { get; set; } = "";
    public string PartName { get; set; } = "";
    public string DrawnBy { get; set; } = "Sahand Gear Maker";
    public string Date { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");
    public string UnitsNote { get; set; } = "همهٔ ابعاد بر حسب میلی‌متر";
    public string ToleranceNote { get; set; } = "تلرانس عمومی: تعیین‌نشده (استاندارد/جداول انتخاب نشده است)";

    /// <summary>قالب کاغذ: A4 عمودی به‌صورت پیش‌فرض (۲۱۰×۲۹۷).</summary>
    public double SheetWidthMm { get; set; } = 210;
    public double SheetHeightMm { get; set; } = 297;

    public List<DrawingView> Views { get; } = new();
    public List<ParameterRow> Parameters { get; } = new();
    public List<string> Notes { get; } = new();
}

/// <summary>
/// سازندهٔ سند نقشهٔ فنی از قطعهٔ واقعی — نما از مرز مش، جدول پارامتر از دادهٔ پارامتریک
/// و اندازه‌های مرجع. تلرانس بدون ورودی استاندارد «تعیین‌نشده» می‌ماند (سیاست صادقانه).
/// </summary>
public static class TechnicalDrawingBuilder
{
    public static TechnicalDrawingDocument Build(
        GearPart part, GearBuildResult geometry, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(geometry);

        var doc = new TechnicalDrawingDocument
        {
            Title = $"نقشهٔ فنی — {part.Name}",
            ProjectName = projectName ?? "پروژهٔ بدون‌نام",
            PartName = part.Name,
        };

        // ---------- نمای اصلی (نما از بالا: مرز بیرونی در XY) ----------
        var view = new DrawingView { Title = "نمای دایره‌ای (پلان)" };

        // مرز بیرونی: مسیر تحلیلی (پروفیل دقیق — رفع «دندانه‌های تیکه‌تیکه»)؛ افت → از مش
        var plan = PlanViewExtractor.TryExtract(part, geometry);
        if (plan is not null)
        {
            view.Outlines.Add(plan.Outer);
            foreach (var h in plan.Holes)
                if (h.Count >= 3)
                    view.Outlines.Add(h);
            if (plan.InternalRimRadius > 0)
                view.Lines.AddRange(CircleAsLines(0, 0, plan.InternalRimRadius, "solid", 0.3));
        }
        else
        {
            var outline = ExtractOuterOutline(geometry.Mesh);
            if (outline.Count > 8)
                view.Outlines.Add(outline);
        }

        // خطوط مرکز (صلیبی)
        var (minB, maxB) = geometry.Mesh.Bounds();
        double cx = (minB.X + maxB.X) / 2, cy = (minB.Y + maxB.Y) / 2;
        double r = Math.Max(maxB.X - minB.X, maxB.Y - minB.Y) / 2;
        double ext = r * 1.18;
        view.Lines.Add(new DrawingLine(cx - ext, cy, cx + ext, cy, "center", 0.18));
        view.Lines.Add(new DrawingLine(cx, cy - ext, cx, cy + ext, "center", 0.18));

        // دایره‌های مرجع (گام/سر/ریشه) برای خانواده‌های استوانه‌ای
        var fig = geometry.Figures;
        if (part.Family is GearFamily.ExternalSpur or GearFamily.ExternalHelical
            or GearFamily.ExternalHerringbone or GearFamily.InternalSpur or GearFamily.InternalHelical
            or GearFamily.CrossedHelical or GearFamily.Cycloidal
            or GearFamily.SplineExternal or GearFamily.SplineInternal or GearFamily.HarmonicDrive)
        {
            view.Lines.AddRange(CircleAsLines(cx, cy, fig.PitchRadius, "pitch", 0.18));
            view.Lines.AddRange(CircleAsLines(cx, cy, fig.TipRadius, "solid", 0.35));
            view.Lines.AddRange(CircleAsLines(cx, cy, fig.RootRadius, "solid", 0.2));
        }

        // اندازه‌گذاری قطر بیرونی
        double od = 2 * fig.TipRadius;
        view.Texts.Add(new DrawingText(cx + ext, cy + r + 4, $"Ø{od:F2}", 2.8, "left"));
        view.Texts.Add(new DrawingText(cx, cy + r + 9, $"z = {fig.ToothCount}، m = {part.Teeth.Module:F2} mm", 2.2, "center"));

        view.Bounds = (cx - ext, cy - ext, cx + ext, cy + ext);
        view.Scale = 1.0;
        doc.Views.Add(view);

        // ---------- نمای جانبی (ارتفاع) — نگاه از کنار: قطر سر × پهنا + بور/توپی ----------
        var side = new DrawingView { Title = "نمای جانبی (ارتفاع)" };
        BuildSideView(side, part, fig);
        if (side.Outlines.Count > 0)
            doc.Views.Add(side);

        // ---------- نمای ایزومتریک — چارچوب سیمی از یال‌های ویژه (ایراد ۱۱، نسخهٔ ۰٫۵) ----------
        var iso = new DrawingView { Title = "نمای ایزومتریک" };
        BuildIsometricView(iso, part);
        if (iso.Lines.Count > 8)
            doc.Views.Add(iso);

        // ---------- جدول پارامترها ----------
        doc.Parameters.Add(new("نوع قطعه", PersianFamilyName(part.Family), "—"));
        doc.Parameters.Add(new("تعداد دندانه", fig.ToothCount.ToString(), "—"));
        doc.Parameters.Add(new("ماژول", part.Teeth.Module.ToString("F3"), "mm"));
        doc.Parameters.Add(new("زاویهٔ فشار", UnitConverter.RadToDeg(part.Teeth.PressureAngle).ToString("F1"), "°"));
        if (Math.Abs(part.Teeth.HelixAngle) > 1e-9)
            doc.Parameters.Add(new("زاویهٔ مارپیچ", UnitConverter.RadToDeg(part.Teeth.HelixAngle).ToString("F2"), "°"));
        doc.Parameters.Add(new("پهنای دندانه", part.Teeth.FaceWidth.ToString("F2"), "mm"));
        doc.Parameters.Add(new("قطر گام", (2 * fig.PitchRadius).ToString("F3"), "mm"));
        doc.Parameters.Add(new("قطر سر", (2 * fig.TipRadius).ToString("F3"), "mm"));
        doc.Parameters.Add(new("قطر ریشه", (2 * fig.RootRadius).ToString("F3"), "mm"));
        if (part.Body.BoreDiameter > 0)
            doc.Parameters.Add(new("قطر بور", part.Body.BoreDiameter.ToString("F2"), "mm"));
        doc.Parameters.Add(new("حجم مش (تقریبی)", fig.EstimatedVolumeMm3.ToString("F0"), "mm³"));
        doc.Parameters.Add(new("تعداد مثلث مش", geometry.Mesh.TriangleCount.ToString("N0"), "—"));

        // ---------- یادداشت‌ها ----------
        doc.Notes.Add("این نقشه «گزارش هندسی» است و به‌عنوان گواهی کیفیت یا دستور ساخت نهایی تلقی نمی‌شود.");
        doc.Notes.Add("مش خروجی تقریب گسستهٔ سطح است؛ ابعاد جدول از پارامترهای مرجع محاسبه شده‌اند.");
        var unverified = geometry.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Unverified);
        if (unverified is not null)
            doc.Notes.Add($"هشدار سازنده: {unverified.Message}");

        return doc;
    }

    /// <summary>سند سبک فقط از مش (بدون پارامترهای قطعه) — برای خروجی DXF پروفیل از مش تجمیعی.</summary>
    public static TechnicalDrawingDocument BuildFromMesh(TriMesh mesh, string? partName = null)
    {
        var doc = new TechnicalDrawingDocument
        {
            Title = "پروفیل مقطع",
            PartName = partName ?? "mesh",
            ProjectName = "—",
        };
        var view = new DrawingView { Title = "نمای دایره‌ای (پلان)" };
        var outline = ExtractOuterOutline(mesh);
        if (outline.Count > 8)
            view.Outlines.Add(outline);
        var (minB, maxB) = mesh.Bounds();
        double cx = (minB.X + maxB.X) / 2, cy = (minB.Y + maxB.Y) / 2;
        double r = Math.Max(maxB.X - minB.X, maxB.Y - minB.Y) / 2;
        view.Bounds = (cx - r * 1.18, cy - r * 1.18, cx + r * 1.18, cy + r * 1.18);
        doc.Views.Add(view);
        return doc;
    }

    /// <summary>
    /// نمای جانبی (ارتفاع): از کنار — کانتور قطر سر × پهنای دندانه با بور و توپی (خانوادهٔ استوانه‌ای)
    /// یا سیلوئت عمومی از مش برای بقیهٔ خانواده‌ها (نمای دوم برای نقشهٔ فنی کامل‌تر).
    /// </summary>
    private static void BuildSideView(DrawingView side, GearPart part, GearGeometryFigures fig)
    {
        bool cylindrical = part.Family is GearFamily.ExternalSpur or GearFamily.ExternalHelical
            or GearFamily.ExternalHerringbone or GearFamily.InternalSpur or GearFamily.InternalHelical
            or GearFamily.CrossedHelical or GearFamily.Cycloidal
            or GearFamily.SplineExternal or GearFamily.SplineInternal or GearFamily.HarmonicDrive;

        if (cylindrical)
        {
            double rTip = fig.TipRadius;
            double w = Math.Max(1e-6, part.Teeth.FaceWidth);
            double z0 = 0, z1 = w;

            // کانتور بیرونی (مستطیل قطر سر × پهنا)
            var outline = new List<(double X, double Y)>
            {
                (-rTip, z0), (rTip, z0), (rTip, z1), (-rTip, z1), (-rTip, z0),
            };
            side.Outlines.Add(outline);

            // قطر ریشه (خطوط مرجع بالای سطح دندانه‌ها)
            double rRoot = fig.RootRadius;
            if (rRoot > 0 && rRoot < rTip)
            {
                side.Lines.Add(new DrawingLine(-rRoot, z0, -rRoot, z1, "solid", 0.2));
                side.Lines.Add(new DrawingLine(rRoot, z0, rRoot, z1, "solid", 0.2));
            }

            // بور (اگر باشد)
            double rBore = part.Body.BoreDiameter / 2.0;
            if (rBore > 1e-6 && rBore < rTip)
            {
                side.Lines.Add(new DrawingLine(-rBore, z0, -rBore, z1, "solid", 0.25));
                side.Lines.Add(new DrawingLine(rBore, z0, rBore, z1, "solid", 0.25));
                side.Texts.Add(new DrawingText(0, z1 + 3, $"Øبور {2 * rBore:F2}", 2.2, "center"));
            }

            // توپی (برآمدگی سمت +Z)
            if (part.Body.HubDiameter > 0 && part.Body.HubLength > 0)
            {
                double rHub = Math.Min(part.Body.HubDiameter / 2.0, rTip);
                double zh = Math.Min(part.Body.HubLength, w);
                side.Lines.Add(new DrawingLine(-rHub, z1, -rHub, z1 + zh, "solid", 0.25));
                side.Lines.Add(new DrawingLine(rHub, z1, rHub, z1 + zh, "solid", 0.25));
                side.Lines.Add(new DrawingLine(-rHub, z1 + zh, rHub, z1 + zh, "solid", 0.25));
            }

            // خط مرکز محوری
            side.Lines.Add(new DrawingLine(-rTip * 1.12, w / 2, rTip * 1.12, w / 2, "center", 0.18));

            // اندازهٔ پهنا
            side.Texts.Add(new DrawingText(rTip * 1.05, z0 + w / 2, $"پهنا {w:F2}", 2.2, "left"));

            side.Bounds = (-rTip * 1.2, -1, rTip * 1.2, w + Math.Max(4, part.Body.HubLength));
        }
        else
        {
            // سیلوئت عمومی: تصویر مش روی صفحهٔ XZ (نمایش تقریبیِ صادقانه برای خانواده‌های خاص)
            var outline = ExtractXZSilhouette(part);
            if (outline.Count > 4)
            {
                side.Outlines.Add(outline);
                double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
                double minY = outline.Min(p => p.Y), maxY = outline.Max(p => p.Y);
                side.Lines.Add(new DrawingLine(minX - 4, (minY + maxY) / 2, maxX + 4, (minY + maxY) / 2, "center", 0.18));
                side.Bounds = (minX - 5, minY - 2, maxX + 5, maxY + 2);
            }
        }
    }

    /// <summary>سیلوئت جانبی از مش: بیشینه/کمینهٔ Z در سطل‌های X (نمای ارتفاع تقریبی).</summary>
    private static List<(double X, double Y)> ExtractXZSilhouette(GearPart part)
    {
        var outline = new List<(double X, double Y)>();
        try
        {
            var build = SahandGearMaker.Geometry.GearGeometryEngine.BuildPart(part);
            var mesh = build.Mesh;
            var (min, max) = mesh.Bounds();
            const int bins = 160;
            double w = Math.Max(1e-9, max.X - min.X);
            var zTop = new double[bins];
            var zBot = new double[bins];
            Array.Fill(zTop, double.NaN);
            Array.Fill(zBot, double.NaN);
            foreach (var v in mesh.Vertices)
            {
                int b = Math.Clamp((int)((v.X - min.X) / w * bins), 0, bins - 1);
                if (double.IsNaN(zTop[b]) || v.Z > zTop[b]) zTop[b] = v.Z;
                if (double.IsNaN(zBot[b]) || v.Z < zBot[b]) zBot[b] = v.Z;
            }
            for (int i = 0; i < bins; i++)
            {
                if (double.IsNaN(zTop[i]))
                    continue;
                double x = min.X + (i + 0.5) / bins * w;
                outline.Add((x, zBot[i]));
            }
            for (int i = bins - 1; i >= 0; i--)
            {
                if (double.IsNaN(zTop[i]))
                    continue;
                double x = min.X + (i + 0.5) / bins * w;
                outline.Add((x, zTop[i]));
            }
        }
        catch
        {
            // ساخت ناموفق — نمای جانبی خالی می‌ماند (نمای دایره‌ای کافی است)
        }
        return outline;
    }

    /// <summary>مرز بیرونی نما: بیشینهٔ شعاع در سطل‌های زاویه‌ای (چرخ‌دنده‌ها ستاره‌ای‌شکل‌اند).</summary>
    private static List<(double X, double Y)> ExtractOuterOutline(TriMesh mesh)
    {
        var (min, max) = mesh.Bounds();
        double cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
        double zMid = (min.Z + max.Z) / 2;

        // رأس‌های نزدیک به صفحهٔ میانه (برش نمایشی)
        var near = new List<(double X, double Y)>();
        foreach (var v in mesh.Vertices)
        {
            if (Math.Abs(v.Z - zMid) <= Math.Max(0.6, (max.Z - min.Z) * 0.08))
                near.Add((v.X, v.Y));
        }
        if (near.Count < 8)
        {
            foreach (var v in mesh.Vertices)
                near.Add((v.X, v.Y));
        }

        // سطل‌بندی زاویه‌ای: بیشینهٔ شعاع در هر سطل → مرز دندانه‌دار
        // سطل‌های خالی با درون‌یابی خطی همسایه‌ها پر می‌شوند (رفع قطع‌قطه شدن خط)
        const int bins = 720;
        var rMax = new double[bins];
        Array.Fill(rMax, -1.0);
        foreach (var (x, y) in near)
        {
            double dx = x - cx, dy = y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r < 1e-9)
                continue;
            int b = (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * bins);
            if (b >= 0 && b < bins && r > rMax[b])
                rMax[b] = r;
        }

        // پر کردن سطل‌های خالی با درون‌یابی شعاعی بین همسایه‌های پر (رفع تیکه‌تیکه شدن)
        var filled = new double[bins];
        Array.Fill(filled, -1.0);
        for (int i = 0; i < bins; i++)
        {
            if (rMax[i] >= 0)
                continue;
            // نزدیک‌ترین سطل‌های پر در دو جهت
            int prev = -1, next = -1;
            for (int d = 1; d < bins / 4 && (prev < 0 || next < 0); d++)
            {
                if (prev < 0 && rMax[((i - d) % bins + bins) % bins] >= 0) prev = ((i - d) % bins + bins) % bins;
                if (next < 0 && rMax[(i + d) % bins] >= 0) next = (i + d) % bins;
            }
            if (prev >= 0 && next >= 0)
            {
                double t = (double)((i - prev + bins) % bins) / (((next - prev) + bins) % bins);
                filled[i] = rMax[prev] + (rMax[next] - rMax[prev]) * Math.Min(1, Math.Max(0, t));
            }
        }
        for (int i = 0; i < bins; i++)
            if (filled[i] >= 0 && rMax[i] < 0)
                rMax[i] = filled[i];

        var outline = new List<(double X, double Y)>(bins);
        for (int i = 0; i < bins; i++)
        {
            if (rMax[i] < 0)
                continue;
            double a = (i + 0.5) / bins * 2 * Math.PI - Math.PI;
            outline.Add((cx + rMax[i] * Math.Cos(a), cy + rMax[i] * Math.Sin(a)));
        }

        // اگر هنوز سطل‌های خالی زیاد بود (شکل غیرستاره‌ای مثل شانه) → پوش محدب
        if (outline.Count < bins / 3)
            return ConvexHull(near);

        return outline;
    }

    // ═══════════ نمای ایزومتریک (ایراد ۱۱ — نسخهٔ ۰٫۵) ═══════════

    /// <summary>
    /// چارچوب سیمی ایزومتریک از یال‌های ویژهٔ مش (دیهدرال > ۲۵° یا مرز باز) —
    /// تصویر استاندارد مهندسی: u = (x−y)·cos30°، v = z − (x+y)·sin30°.
    /// برای سبکی، مش با تلرانس درشت‌تر بازسازی می‌شود.
    /// </summary>
    private static void BuildIsometricView(DrawingView iso, GearPart part)
    {
        TriMesh mesh;
        try
        {
            var coarse = SahandGearMaker.Geometry.GearGeometryEngine.Build(
                part.Family, part.Teeth, part.Body,
                new GeometrySettings { ProfileToleranceMm = 0.12, AxialSliceCount = 20 },
                part.Specific);
            mesh = coarse.Mesh;
        }
        catch (Exception)
        {
            return; // نمای ایزومتریک اختیاری است — نقشه بدون آن هم معتبر است
        }

        var edges = FeatureEdges(mesh, dihedralDeg: 28);
        if (edges.Count == 0)
            return;

        const double cos30 = 0.8660254037844387;
        const double sin30 = 0.5;
        (double U, double V) Proj(SahandGearMaker.MathX.Vector3 p)
            => ((p.X - p.Y) * cos30, p.Z - (p.X + p.Y) * sin30);

        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var (a, b) in edges)
        {
            var pa = Proj(a);
            var pb = Proj(b);
            iso.Lines.Add(new DrawingLine(pa.U, pa.V, pb.U, pb.V, "solid", 0.16));
            minU = Math.Min(minU, Math.Min(pa.U, pb.U));
            maxU = Math.Max(maxU, Math.Max(pa.U, pb.U));
            minV = Math.Min(minV, Math.Min(pa.V, pb.V));
            maxV = Math.Max(maxV, Math.Max(pa.V, pb.V));
        }

        // مرکز صحنه و محور راهنما
        double cu = (minU + maxU) / 2, cv = (minV + maxV) / 2;
        double ext = Math.Max(maxU - minU, maxV - minV) * 0.12 + 2;
        iso.Lines.Add(new DrawingLine(cu - ext, cv, cu + ext, cv, "center", 0.14));
        iso.Lines.Add(new DrawingLine(cu, cv - ext, cu, cv + ext, "center", 0.14));
        iso.Bounds = (minU - ext, minV - ext, maxU + ext, maxV + ext);
        iso.Scale = 1.0;
    }

    /// <summary>یال‌های ویژهٔ مش: زاویهٔ دوهدرال بیش از آستانه یا یال مرزی.</summary>
    private static List<(SahandGearMaker.MathX.Vector3 A, SahandGearMaker.MathX.Vector3 B)> FeatureEdges(TriMesh mesh, double dihedralDeg)
    {
        var verts = mesh.Vertices.ToArray();
        var tris = mesh.Triangles.ToArray();
        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            Add(t.A, t.B); Add(t.B, t.C); Add(t.C, t.A);
            void Add(int u, int v)
            {
                var k = u < v ? (u, v) : (v, u);
                if (!edgeTris.TryGetValue(k, out var l))
                {
                    l = new List<int>(2);
                    edgeTris[k] = l;
                }
                if (l.Count < 2)
                    l.Add(i);
            }
        }

        double cosLimit = Math.Cos(dihedralDeg * Math.PI / 180.0);
        var result = new List<(SahandGearMaker.MathX.Vector3, SahandGearMaker.MathX.Vector3)>();
        foreach (var (k, list) in edgeTris)
        {
            bool keep;
            if (list.Count == 1)
            {
                keep = true; // مرز باز
            }
            else
            {
                var t0 = tris[list[0]];
                var t1 = tris[list[1]];
                var n0 = SahandGearMaker.MathX.Vector3.Cross(verts[t0.B] - verts[t0.A], verts[t0.C] - verts[t0.A]);
                var n1 = SahandGearMaker.MathX.Vector3.Cross(verts[t1.B] - verts[t1.A], verts[t1.C] - verts[t1.A]);
                double l0 = n0.Length, l1 = n1.Length;
                if (l0 < 1e-12 || l1 < 1e-12) continue;
                keep = SahandGearMaker.MathX.Vector3.Dot(n0 / l0, n1 / l1) < cosLimit;
            }

            if (keep)
                result.Add((verts[k.Item1], verts[k.Item2]));
        }
        return result;
    }

    private static List<(double X, double Y)> ConvexHull(List<(double X, double Y)> points)
    {
        if (points.Count < 3)
            return points;
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double X, double Y)>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    private static List<DrawingLine> CircleAsLines(double cx, double cy, double radius, string style, double width)
    {
        var lines = new List<DrawingLine>();
        int seg = Math.Clamp((int)Math.Ceiling(2 * Math.PI * radius / 1.2), 48, 360);
        double px = cx + radius, py = cy;
        for (int i = 1; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg;
            double x = cx + radius * Math.Cos(a), y = cy + radius * Math.Sin(a);
            lines.Add(new DrawingLine(px, py, x, y, style, width));
            px = x; py = y;
        }
        return lines;
    }

    private static string PersianFamilyName(GearFamily f) => f switch
    {
        GearFamily.ExternalSpur => "چرخ‌دندهٔ خارجی مستقیم",
        GearFamily.ExternalHelical => "چرخ‌دندهٔ خارجی مارپیچ",
        GearFamily.ExternalHerringbone => "چرخ‌دندهٔ جناغی",
        GearFamily.InternalSpur => "چرخ‌دندهٔ داخلی مستقیم",
        GearFamily.InternalHelical => "چرخ‌دندهٔ داخلی مارپیچ",
        GearFamily.Rack => "شانهٔ مستقیم",
        GearFamily.HelicalRack => "شانهٔ مارپیچ",
        GearFamily.BevelSpur => "مخروطی مستقیم",
        GearFamily.BevelSpiral => "مخروطی مارپیچ",
        GearFamily.Worm => "کرم (ZA)",
        GearFamily.WormWheel => "چرخ کرم",
        GearFamily.CrossedHelical => "مارپیچ متقاطع",
        GearFamily.CrownFace => "تاجی/پیشانی",
        GearFamily.Hypoid => "هیپوئیدی",
        GearFamily.Cycloidal => "سیکلوئیدی",
        GearFamily.NonCircular => "غیرمدور بیضوی",
        GearFamily.HarmonicDrive => "فِلکسی‌پلاین هارمونیک",
        GearFamily.SplineExternal => "هزارخار خارجی",
        GearFamily.SplineInternal => "هزارخار داخلی",
        _ => f.ToString(),
    };
}
