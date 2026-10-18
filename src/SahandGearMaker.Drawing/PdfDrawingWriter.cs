using System.Globalization;
using System.Text;

namespace SahandGearMaker.Drawing;

/// <summary>
/// نویسندهٔ PDF مینیمال برای نقشهٔ فنی — برداری + متن فارسی با فونت تعبیه‌شده.
///
/// روش متن فارسی: شکل‌دهی زمینه‌ای (ArabicShaper) → فرم‌های نمایشی → گلیف‌های
/// uniXXXX فونت وزیرمتن؛ تخصیص بایت با Encoding Differences پویا (بایت 0x80-0xFF
/// به نام گلیف نگاشت می‌شود؛ ASCII هویتی می‌ماند). متن راست‌چین با مختصات x
/// پیش‌رونده نوشته می‌شود (بدون وابستگی به bidi نمایشگر).
/// ساختار فایل: 1=Catalog, 2=Pages, 3=Page, 4=Contents, 5=Font, 6=Descriptor, 7=FontFile2.
/// </summary>
public static class PdfDrawingWriter
{
    private const double PtPerMm = 72.0 / 25.4;

    /// <summary>ساخت PDF از سند نقشه (A4 عمودی). مختصات سند: mm از گوشهٔ پایین-چپ.</summary>
    public static void Write(Stream output, TechnicalDrawingDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(output);

        var font = LoadEmbeddedFont();

        // ---------- تولید محتوا (گلیف‌ها را جمع می‌کند) ----------
        var ctx = new EmitContext(font);
        string content = EmitContent(ctx, doc);
        byte[] contentBytes = Encoding.Latin1.GetBytes(content);

        // ---------- اشیا ----------
        double pageW = doc.SheetWidthMm * PtPerMm;
        double pageH = doc.SheetHeightMm * PtPerMm;

        byte[] fontData = font.RawData;

        // Differences از گلیف‌های تخصیص‌یافته
        var diff = new StringBuilder();
        foreach (var (name, b) in ctx.GlyphBytes.OrderBy(kv => kv.Value))
            diff.Append(' ').Append(b).Append(" /").Append(name);

        // Widths: بازهٔ 32..255 (ASCII هویتی + بایت‌های اختصاصی)
        var widths = new int[256];
        for (int b = 0x20; b <= 0x7E; b++)
            widths[b] = font.AdvanceWidth(font.GlyphIdOf(b));
        foreach (var (name, b) in ctx.GlyphBytes)
            widths[b] = font.AdvanceWidth(font.GlyphIdOf(ParseUniName(name)));
        var widthsStr = new StringBuilder();
        for (int b = 0x20; b <= 0xFF; b++)
            widthsStr.Append(widths[b]).Append(' ');

        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageW)} {F(pageH)}] " +
            "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"__STREAM__{contentBytes.Length}",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Vazirmatn /FirstChar 32 /LastChar 255 " +
            $"/Widths [ {widthsStr}] /FontDescriptor 6 0 R " +
            $"/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [{diff} ] >> >>",
            "<< /Type /FontDescriptor /FontName /Vazirmatn /Flags 32 " +
            "/FontBBox [-1100 -500 2400 1300] /ItalicAngle 0 /Ascent 1030 /Descent -370 /CapHeight 700 /StemV 80 " +
            "/FontFile2 7 0 R >>",
            $"__STREAM__{fontData.Length}",
        };

        var buf = new MemoryStream();
        WritePdf(buf, objects, contentBytes, fontData);
        buf.Position = 0;
        buf.CopyTo(output);
    }

    private static void WritePdf(Stream s, string[] objectDicts, byte[] contentBytes, byte[] fontData)
    {
        using var w = new StreamWriter(s, Encoding.Latin1, leaveOpen: true) { NewLine = "\n" };
        w.Write("%PDF-1.7\n");
        w.Flush();

        var offsets = new long[objectDicts.Length + 1];

        for (int i = 0; i < objectDicts.Length; i++)
        {
            offsets[i + 1] = s.Position;
            w.WriteLine($"{i + 1} 0 obj");
            string od = objectDicts[i];
            if (od.StartsWith("__STREAM__", StringComparison.Ordinal))
            {
                int len = int.Parse(od["__STREAM__".Length..], CultureInfo.InvariantCulture);
                byte[] data = i == 3 ? contentBytes : fontData;
                w.WriteLine($"<< /Length {len} >>");
                w.WriteLine("stream");
                w.Flush();
                s.Write(data, 0, data.Length);
                w.WriteLine();
                w.WriteLine("endstream");
            }
            else
            {
                w.WriteLine(od);
            }
            w.WriteLine("endobj");
            w.Flush();
        }

        long xref = s.Position;
        w.WriteLine($"xref\n0 {objectDicts.Length + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= objectDicts.Length; i++)
            w.WriteLine($"{offsets[i]:D10} 00000 n ");
        w.WriteLine($"trailer\n<< /Size {objectDicts.Length + 1} /Root 1 0 R >>");
        w.WriteLine($"startxref\n{xref}");
        w.WriteLine("%%EOF");
        w.Flush();
    }

    private static TrueTypeFont LoadEmbeddedFont()
    {
        var asm = typeof(PdfDrawingWriter).Assembly;
        string name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Vazirmatn-Regular.ttf", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("فونت وزیرمتن تعبیه‌شده در مجموعه یافت نشد.");
        using var fs = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return TrueTypeFont.Parse(ms.ToArray());
    }

    private static int ParseUniName(string name) => Convert.ToInt32(name[3..], 16);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------- تولید محتوا ----------

    private sealed class EmitContext(TrueTypeFont font)
    {
        public TrueTypeFont Font { get; } = font;
        public Dictionary<string, byte> GlyphBytes { get; } = new();

        public byte ByteOf(int codepoint)
        {
            if (codepoint is >= 0x20 and <= 0x7E)
                return (byte)codepoint;
            string name = "uni" + codepoint.ToString("X4");
            if (GlyphBytes.TryGetValue(name, out var b))
                return b;
            if (GlyphBytes.Count >= 120)
                throw new InvalidOperationException("ظرفیت گلیف‌های PDF پر شد (بیش از ۱۲۰ گلیف متمایز در یک سند).");
            b = (byte)(0x80 + GlyphBytes.Count);
            GlyphBytes[name] = b;
            return b;
        }

        public double AdvancePt(int codepoint, double sizePt)
        {
            int gid = Font.GlyphIdOf(codepoint);
            return Font.AdvanceWidth(gid) / (double)Font.UnitsPerEm * sizePt;
        }

        public double MeasurePt(IEnumerable<int> shaped, double sizePt)
        {
            double w = 0;
            foreach (int cp in shaped)
            {
                if (cp == 0x200C)
                    continue;
                w += AdvancePt(cp, sizePt);
            }
            return w;
        }
    }

    private static string EmitContent(EmitContext ctx, TechnicalDrawingDocument doc)
    {
        var sb = new StringBuilder(96 * 1024);
        EmitFrame(sb, doc);
        EmitViews(sb, ctx, doc);
        EmitParameterTable(sb, ctx, doc);
        EmitNotes(sb, ctx, doc);
        EmitTitleBlock(sb, ctx, doc);
        return sb.ToString();
    }

    private static (double X, double Y) ToPage(double xMm, double yMm, double pageHmm)
        => (xMm * PtPerMm, (pageHmm - yMm) * PtPerMm);

    private static void EmitFrame(StringBuilder sb, TechnicalDrawingDocument doc)
    {
        var (x0, y0) = ToPage(8, 8, doc.SheetHeightMm);
        var (x1, y1) = ToPage(doc.SheetWidthMm - 8, doc.SheetHeightMm - 8, doc.SheetHeightMm);
        sb.Append($"0.7 w {F(x0)} {F(y0)} m {F(x1)} {F(y0)} l {F(x1)} {F(y1)} l {F(x0)} {F(y1)} l h S\n");
    }

    private static void EmitViews(StringBuilder sb, EmitContext ctx, TechnicalDrawingDocument doc)
    {
        double areaTop = doc.SheetHeightMm - 16;
        double areaBottom = 138;

        foreach (var view in doc.Views)
        {
            double vw = view.Bounds.MaxX - view.Bounds.MinX;
            double vh = view.Bounds.MaxY - view.Bounds.MinY;
            double availW = doc.SheetWidthMm - 30;
            double availH = areaTop - 10 - areaBottom;
            double scale = Math.Min(availW / Math.Max(1e-6, vw), availH / Math.Max(1e-6, vh));
            scale = Math.Clamp(scale, 0.02, 1.5);

            double vx = view.Bounds.MinX;
            double vy = view.Bounds.MinY;
            double baseY = areaTop - 10 - vh * scale;

            (double X, double Y) Map(double x, double y)
                => ToPage((x - vx) * scale + 15, (y - vy) * scale + baseY, doc.SheetHeightMm);

            foreach (var line in view.Lines)
            {
                var (px1, py1) = Map(line.X1, line.Y1);
                var (px2, py2) = Map(line.X2, line.Y2);
                string dash = line.Style switch
                {
                    "center" => "[1.8 1.1 0.7 1.1] 0 d",
                    "pitch" => "[3.2 1.4] 0 d",
                    _ => "[] 0 d",
                };
                sb.Append($"{dash} {F(line.WidthMm)} w {F(px1)} {F(py1)} m {F(px2)} {F(py2)} l S\n");
            }
            sb.Append("[] 0 d\n");

            foreach (var outline in view.Outlines)
            {
                if (outline.Count < 2)
                    continue;
                var first = Map(outline[0].X, outline[0].Y);
                sb.Append($"0.45 w {F(first.X)} {F(first.Y)} m ");
                for (int i = 1; i < outline.Count; i++)
                {
                    var p = Map(outline[i].X, outline[i].Y);
                    sb.Append($"{F(p.X)} {F(p.Y)} l ");
                }
                sb.Append("h S\n");
            }

            foreach (var t in view.Texts)
            {
                var (px, py) = Map(t.X, t.Y);
                EmitText(sb, ctx, t.Text, px, py, t.SizeMm, t.Align);
            }

            // عنوان نما (وسط صفحه زیر ناحیه)
            EmitText(sb, ctx, $"{view.Title} — مقیاس 1:{1 / scale:F1}", doc.SheetWidthMm / 2 * PtPerMm, (areaBottom + 4) * PtPerMm, 3.2, "center");
        }
    }

    private static void EmitParameterTable(StringBuilder sb, EmitContext ctx, TechnicalDrawingDocument doc)
    {
        double tableTop = 130;
        double rowH = 6.0;
        double x0 = 14, x1 = doc.SheetWidthMm - 14;

        var (hx0, hy) = ToPage(x0, tableTop, doc.SheetHeightMm);
        var (hx1, _) = ToPage(x1, tableTop, doc.SheetHeightMm);
        sb.Append($"0.5 w {F(hx0)} {F(hy)} m {F(hx1)} {F(hy)} l S\n");
        EmitText(sb, ctx, "جدول پارامترهای هندسی", (x0 + x1) / 2 * PtPerMm, (tableTop - 5) * PtPerMm, 3.0, "center");

        double labelX = (x1 - 3) * PtPerMm;
        double valueX = (x0 + 3) * PtPerMm;
        double y = tableTop - rowH;
        foreach (var row in doc.Parameters)
        {
            EmitText(sb, ctx, row.Label, labelX, y * PtPerMm, 2.5, "right");
            string value = row.Unit is "—" or "" ? row.Value : $"{row.Value} {row.Unit}";
            EmitText(sb, ctx, value, valueX, y * PtPerMm, 2.5, "left");
            y -= rowH;
        }
    }

    private static void EmitNotes(StringBuilder sb, EmitContext ctx, TechnicalDrawingDocument doc)
    {
        double y = 60;
        EmitText(sb, ctx, "یادداشت‌ها:", (doc.SheetWidthMm - 16) * PtPerMm, y * PtPerMm, 2.8, "right");
        y += 5.6;
        foreach (var note in doc.Notes)
        {
            EmitText(sb, ctx, "• " + note, (doc.SheetWidthMm - 18) * PtPerMm, y * PtPerMm, 2.2, "right");
            y += 5.2;
        }
    }

    private static void EmitTitleBlock(StringBuilder sb, EmitContext ctx, TechnicalDrawingDocument doc)
    {
        double y0 = 12, y1 = 42, x0 = 14, x1 = doc.SheetWidthMm - 14;
        var (px0, py0) = ToPage(x0, y0, doc.SheetHeightMm);
        var (px1, py1) = ToPage(x1, y1, doc.SheetHeightMm);
        sb.Append($"0.8 w {F(px0)} {F(py0)} m {F(px1)} {F(py0)} l {F(px1)} {F(py1)} l {F(px0)} {F(py1)} l h S\n");
        var (_, pyMid) = ToPage(0, y0 + 10.5, doc.SheetHeightMm);
        sb.Append($"0.4 w {F(px0)} {F(pyMid)} m {F(px1)} {F(pyMid)} l S\n");

        EmitText(sb, ctx, doc.Title, (x0 + x1) / 2 * PtPerMm, (y0 + 3.4) * PtPerMm, 3.6, "center");
        EmitText(sb, ctx, $"پروژه: {doc.ProjectName}   قطعه: {doc.PartName}", x1 - 3, (y0 + 13.6) * PtPerMm, 2.6, "right");
        EmitText(sb, ctx, $"ترسیم: {doc.DrawnBy}   تاریخ: {doc.Date}", x1 - 3, (y0 + 20.2) * PtPerMm, 2.4, "right");
        EmitText(sb, ctx, doc.UnitsNote, x0 + 3, (y0 + 13.6) * PtPerMm, 2.4, "left");
        EmitText(sb, ctx, doc.ToleranceNote, x0 + 3, (y0 + 20.2) * PtPerMm, 2.2, "left");
    }

    /// <summary>نوشتن متن (فارسی/لاتین) — راست‌چین با bidi ساده (اعداد/لاتین جزیرهٔ LTR).</summary>
    private static void EmitText(StringBuilder sb, EmitContext ctx, string text, double xPt, double yPt, double sizeMm, string align)
    {
        if (string.IsNullOrEmpty(text))
            return;
        double sizePt = sizeMm * PtPerMm;
        var shaped = ArabicShaper.Shape(text);
        if (shaped.Count == 0)
            return;

        double width = ctx.MeasurePt(shaped, sizePt);
        if (width <= 0)
            width = sizePt * 0.6 * shaped.Count;

        double cursor = align switch
        {
            "left" => xPt + width,
            "center" => xPt + width / 2,
            _ => xPt, // راست
        };

        // گلیف‌ها (ZWNJ حذف)
        var glyphs = new List<(int Cp, byte B, double Adv)>(shaped.Count);
        foreach (int cp in shaped)
        {
            if (cp == 0x200C)
                continue;
            glyphs.Add((cp, ctx.ByteOf(cp), ctx.AdvancePt(cp, sizePt)));
        }

        static bool IsRtl(int cp) => cp is >= 0x0600 and <= 0x06FF or >= 0xFB50 and <= 0xFEFF;

        sb.Append("BT\n").Append($"/F1 {F(sizePt)} Tf\n");

        int i = 0;
        while (i < glyphs.Count)
        {
            bool rtlRun = IsRtl(glyphs[i].Cp);
            int j = i;
            while (j < glyphs.Count && IsRtl(glyphs[j].Cp) == rtlRun)
                j++;

            if (rtlRun)
            {
                for (int k = i; k < j; k++)
                {
                    cursor -= glyphs[k].Adv;
                    sb.Append($"1 0 0 1 {F(cursor)} {F(yPt)} Tm <{glyphs[k].B:X2}> Tj\n");
                }
            }
            else
            {
                // جزیرهٔ LTR: اشغال [cursor−W, cursor] و چیدمان چپ‌به‌راست
                double runW = 0;
                for (int k = i; k < j; k++)
                    runW += glyphs[k].Adv;
                double runStart = cursor - runW;
                double off = runStart;
                for (int k = i; k < j; k++)
                {
                    sb.Append($"1 0 0 1 {F(off)} {F(yPt)} Tm <{glyphs[k].B:X2}> Tj\n");
                    off += glyphs[k].Adv;
                }
                cursor = runStart;
            }
            i = j;
        }
        sb.Append("ET\n");
    }
}
