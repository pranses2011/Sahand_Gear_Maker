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

        // مرز بیرونی: تقاطع مش با صفحهٔ z = میانه؛ حلقهٔ بیرونی‌ترین
        var outline = ExtractOuterOutline(geometry.Mesh);
        if (outline.Count > 8)
            view.Outlines.Add(outline);

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

        var outline = new List<(double X, double Y)>(bins);
        for (int i = 0; i < bins; i++)
        {
            if (rMax[i] < 0)
                continue;
            double a = (i + 0.5) / bins * 2 * Math.PI - Math.PI;
            outline.Add((cx + rMax[i] * Math.Cos(a), cy + rMax[i] * Math.Sin(a)));
        }

        // اگر سطل‌های خالی زیاد بود (شکل غیرستاره‌ای مثل شانه) → پوش محدب
        if (outline.Count < bins / 3)
            return ConvexHull(near);

        return outline;
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
