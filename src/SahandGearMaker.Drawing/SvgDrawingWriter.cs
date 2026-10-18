using System.Globalization;
using System.Text;

namespace SahandGearMaker.Drawing;

/// <summary>
/// نویسندهٔ SVG نقشهٔ فنی — برداری با متن فارسی کامل (شکل‌دهی توسط مرورگر).
/// RTL با direction="rtl" و text-anchor. مقیاس: 1 واحد SVG = 1 mm با viewBox.
/// </summary>
public static class SvgDrawingWriter
{
    /// <summary>ساخت SVG کامل سند (رشته).</summary>
    public static string Write(TechnicalDrawingDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var sb = new StringBuilder(96 * 1024);
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(doc.SheetWidthMm)} {F(doc.SheetHeightMm)}\" " +
                      $"width=\"{F(doc.SheetWidthMm)}mm\" height=\"{F(doc.SheetHeightMm)}mm\" " +
                      "font-family=\"Vazirmatn, Noto Naskh Arabic, Tahoma, sans-serif\">");

        // پس‌زمینه و کادر
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{F(doc.SheetWidthMm)}\" height=\"{F(doc.SheetHeightMm)}\" fill=\"white\"/>");
        sb.AppendLine($"<rect x=\"8\" y=\"8\" width=\"{F(doc.SheetWidthMm - 16)}\" height=\"{F(doc.SheetHeightMm - 16)}\" " +
                      "fill=\"none\" stroke=\"#111\" stroke-width=\"0.7\"/>");

        // تعریف خط‌چین‌ها
        sb.AppendLine("<defs>");
        sb.AppendLine("<marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">");
        sb.AppendLine("<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#111\"/></marker>");
        sb.AppendLine("</defs>");

        EmitViews(sb, doc);
        EmitParameterTable(sb, doc);
        EmitNotes(sb, doc);
        EmitTitleBlock(sb, doc);

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>ذخیره در فایل (ذخیرهٔ اتمی).</summary>
    public static void WriteToFile(TechnicalDrawingDocument doc, string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Write(doc), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void EmitViews(StringBuilder sb, TechnicalDrawingDocument doc)
    {
        double areaTop = doc.SheetHeightMm - 16;
        double areaBottom = 138;

        foreach (var view in doc.Views)
        {
            double vw = view.Bounds.MaxX - view.Bounds.MinX;
            double vh = view.Bounds.MaxY - view.Bounds.MinY;
            double availW = doc.SheetWidthMm - 30;
            double availH = areaTop - 10 - areaBottom;
            double scale = Math.Clamp(Math.Min(availW / Math.Max(1e-6, vw), availH / Math.Max(1e-6, vh)), 0.02, 1.5);

            double vx = view.Bounds.MinX, vy = view.Bounds.MinY;
            double baseY = areaTop - 10 - vh * scale;
            double Tx(double x, double y) => (x - vx) * scale + 15;
            double Ty(double x, double y) => baseY + (y - vy) * scale;

            sb.AppendLine("<g>");
            foreach (var line in view.Lines)
            {
                string dash = line.Style switch
                {
                    "center" => " stroke-dasharray=\"1.8 1.1 0.7 1.1\"",
                    "pitch" => " stroke-dasharray=\"3.2 1.4\"",
                    _ => "",
                };
                sb.AppendLine($"<line x1=\"{F(Tx(line.X1, line.Y1))}\" y1=\"{F(Ty(line.X1, line.Y1))}\" " +
                              $"x2=\"{F(Tx(line.X2, line.Y2))}\" y2=\"{F(Ty(line.X2, line.Y2))}\" " +
                              $"stroke=\"#111\" stroke-width=\"{F(line.WidthMm)}\"{dash}/>");
            }

            foreach (var outline in view.Outlines)
            {
                if (outline.Count < 2)
                    continue;
                var pts = string.Join(" ", outline.Select(p => $"{F(Tx(p.X, p.Y))},{F(Ty(p.X, p.Y))}"));
                sb.AppendLine($"<polygon points=\"{pts}\" fill=\"none\" stroke=\"#111\" stroke-width=\"0.45\"/>");
            }

            foreach (var t in view.Texts)
            {
                string anchor = t.Align switch
                {
                    "left" => "start",
                    "center" => "middle",
                    _ => "end",
                };
                sb.AppendLine($"<text x=\"{F(Tx(t.X, t.Y))}\" y=\"{F(Ty(t.X, t.Y))}\" font-size=\"{F(t.SizeMm)}\" " +
                              $"text-anchor=\"{anchor}\" direction=\"rtl\" fill=\"#111\"{(t.Bold ? " font-weight=\"bold\"" : "")}>{Esc(t.Text)}</text>");
            }
            sb.AppendLine("</g>");

            sb.AppendLine($"<text x=\"{F(doc.SheetWidthMm / 2)}\" y=\"{F(areaBottom + 4)}\" font-size=\"3.2\" text-anchor=\"middle\" " +
                          $"direction=\"rtl\" fill=\"#111\">{Esc(view.Title)} — مقیاس 1:{1 / scale:F1}</text>");
        }
    }

    private static void EmitParameterTable(StringBuilder sb, TechnicalDrawingDocument doc)
    {
        double tableTop = 130, rowH = 6;
        double x0 = 14, x1 = doc.SheetWidthMm - 14;

        sb.AppendLine($"<line x1=\"{F(x0)}\" y1=\"{F(tableTop)}\" x2=\"{F(x1)}\" y2=\"{F(tableTop)}\" stroke=\"#111\" stroke-width=\"0.5\"/>");
        sb.AppendLine($"<text x=\"{F((x0 + x1) / 2)}\" y=\"{F(tableTop - 5)}\" font-size=\"3\" text-anchor=\"middle\" direction=\"rtl\">جدول پارامترهای هندسی</text>");

        double y = tableTop - rowH;
        foreach (var row in doc.Parameters)
        {
            sb.AppendLine($"<text x=\"{F(x1 - 3)}\" y=\"{F(y)}\" font-size=\"2.5\" text-anchor=\"end\" direction=\"rtl\">{Esc(row.Label)}</text>");
            string value = row.Unit is "—" or "" ? row.Value : $"{row.Value} {row.Unit}";
            sb.AppendLine($"<text x=\"{F(x0 + 3)}\" y=\"{F(y)}\" font-size=\"2.5\" text-anchor=\"start\">{Esc(value)}</text>");
            y -= rowH;
        }
    }

    private static void EmitNotes(StringBuilder sb, TechnicalDrawingDocument doc)
    {
        double y = 60;
        sb.AppendLine($"<text x=\"{F(doc.SheetWidthMm - 16)}\" y=\"{F(y)}\" font-size=\"2.8\" text-anchor=\"end\" direction=\"rtl\">یادداشت‌ها:</text>");
        y += 5.6;
        foreach (var note in doc.Notes)
        {
            sb.AppendLine($"<text x=\"{F(doc.SheetWidthMm - 18)}\" y=\"{F(y)}\" font-size=\"2.2\" text-anchor=\"end\" direction=\"rtl\">• {Esc(note)}</text>");
            y += 5.2;
        }
    }

    private static void EmitTitleBlock(StringBuilder sb, TechnicalDrawingDocument doc)
    {
        double y0 = 12, y1 = 42, x0 = 14, x1 = doc.SheetWidthMm - 14;
        sb.AppendLine($"<rect x=\"{F(x0)}\" y=\"{F(y0)}\" width=\"{F(x1 - x0)}\" height=\"{F(y1 - y0)}\" fill=\"none\" stroke=\"#111\" stroke-width=\"0.8\"/>");
        sb.AppendLine($"<line x1=\"{F(x0)}\" y1=\"{F(y0 + 10.5)}\" x2=\"{F(x1)}\" y2=\"{F(y0 + 10.5)}\" stroke=\"#111\" stroke-width=\"0.4\"/>");

        sb.AppendLine($"<text x=\"{F((x0 + x1) / 2)}\" y=\"{F(y0 + 3.4)}\" font-size=\"3.6\" text-anchor=\"middle\" direction=\"rtl\" font-weight=\"bold\">{Esc(doc.Title)}</text>");
        sb.AppendLine($"<text x=\"{F(x1 - 3)}\" y=\"{F(y0 + 13.6)}\" font-size=\"2.6\" text-anchor=\"end\" direction=\"rtl\">پروژه: {Esc(doc.ProjectName)}   قطعه: {Esc(doc.PartName)}</text>");
        sb.AppendLine($"<text x=\"{F(x1 - 3)}\" y=\"{F(y0 + 20.2)}\" font-size=\"2.4\" text-anchor=\"end\" direction=\"rtl\">ترسیم: {Esc(doc.DrawnBy)}   تاریخ: {Esc(doc.Date)}</text>");
        sb.AppendLine($"<text x=\"{F(x0 + 3)}\" y=\"{F(y0 + 13.6)}\" font-size=\"2.4\" text-anchor=\"start\">{Esc(doc.UnitsNote)}</text>");
        sb.AppendLine($"<text x=\"{F(x0 + 3)}\" y=\"{F(y0 + 20.2)}\" font-size=\"2.2\" text-anchor=\"start\">{Esc(doc.ToleranceNote)}</text>");
    }
}
