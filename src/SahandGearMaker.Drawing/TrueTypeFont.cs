using System.Buffers.Binary;

namespace SahandGearMaker.Drawing;

/// <summary>
/// خوانندهٔ متریک فونت TrueType (زیرمجموعهٔ لازم برای PDF):
/// جدول‌های head (unitsPerEm)، cmap (3,1) و hmtx/hhea (عرض پیشرفته گلیف‌ها).
/// فقط-خواندن؛ بدون تغییر فایل.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<int, int> _cmap = new(); // unicode → glyph id
    private readonly ushort[] _widths = Array.Empty<ushort>(); // per glyph (units)
    private readonly ushort _numGlyphs;

    public int UnitsPerEm { get; }
    public string GlyphName(int glyphId) => "uni" + glyphId.ToString("X4");

    private TrueTypeFont(byte[] data, Dictionary<int, int> cmap, ushort[] widths, ushort numGlyphs, int upem)
    {
        _data = data;
        _cmap = cmap;
        _widths = widths;
        _numGlyphs = numGlyphs;
        UnitsPerEm = upem;
    }

    public byte[] RawData => _data;

    public static TrueTypeFont Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12)
            throw new InvalidDataException("فایل TTF ناقص است.");

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));

        // فهرست جدول‌ها
        int offset = -1, cmapOff = -1, hmtxOff = -1, hheaOff = -1;
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + 16 * i;
            if (rec + 16 > data.Length)
                throw new InvalidDataException("فهرست جدول‌های TTF ناقص است.");
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec));
            int off = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
            if (tag == 0x68656164u) offset = off;        // 'head'
            else if (tag == 0x636D6170u) cmapOff = off;   // 'cmap'
            else if (tag == 0x686D7478u) hmtxOff = off;   // 'hmtx'
            else if (tag == 0x68686561u) hheaOff = off;   // 'hhea'
        }
        if (offset < 0 || cmapOff < 0 || hmtxOff < 0 || hheaOff < 0)
            throw new InvalidDataException("TTF: یکی از جدول‌های head/cmap/hmtx/hhea یافت نشد.");

        int upem = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 18));
        if (upem <= 0)
            upem = 1000;

        int numHMetrics = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hheaOff + 34));

        // cmap: انتخاب زیرجدول (3,1) یا (0,x)
        int subOff = -1;
        {
            int numSub = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cmapOff + 2));
            int bestScore = -1;
            for (int i = 0; i < numSub; i++)
            {
                int rec = cmapOff + 4 + 8 * i;
                ushort platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec));
                ushort encoding = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec + 2));
                int sub = cmapOff + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 4));
                int score = (platform, encoding) switch
                {
                    (3, 1) => 4, // ویندوز یونی‌کد — مرجع
                    (0, 3) => 3,
                    (0, _) => 2,
                    (3, 0) => 1, // symbol
                    _ => 0,
                };
                if (score > bestScore && sub > 0 && sub < data.Length)
                {
                    bestScore = score;
                    subOff = sub;
                }
            }
        }
        if (subOff < 0)
            throw new InvalidDataException("TTF: زیرجدول cmap مناسب یافت نشد.");

        var cmap = new Dictionary<int, int>(1500);
        ParseCmapSubtable(data, subOff, cmap);

        // hmtx: عرض پیشرفته گلیف‌ها
        int maxpOff = FindTable(data, 0x6D617870); // 'maxp' برای numGlyphs
        if (maxpOff < 0)
            throw new InvalidDataException("TTF: جدول maxp یافت نشد.");
        ushort numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(maxpOff + 4));

        var widths = new ushort[numGlyphs];
        for (int g = 0; g < numGlyphs; g++)
        {
            if (g < numHMetrics)
                widths[g] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hmtxOff + 4 * g));
            else if (numHMetrics > 0)
                widths[g] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hmtxOff + 4 * (numHMetrics - 1)));
        }

        return new TrueTypeFont(data, cmap, widths, numGlyphs, upem);
    }

    private static int FindTable(byte[] data, uint tag)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec)) == tag)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
        }
        return -1;
    }

    private static void ParseCmapSubtable(byte[] data, int off, Dictionary<int, int> cmap)
    {
        ushort format = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off));
        switch (format)
        {
            case 4:
            {
                ushort segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off + 6));
                int segCount = segCountX2 / 2;
                int endBase = off + 14;
                int startBase = endBase + segCountX2 + 2;
                int deltaBase = startBase + segCountX2;
                int rangeBase = deltaBase + segCountX2;
                for (int s = 0; s < segCount; s++)
                {
                    int end = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(endBase + 2 * s));
                    int start = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(startBase + 2 * s));
                    int delta = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(deltaBase + 2 * s));
                    int rangeOff = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rangeBase + 2 * s));
                    if (start == 0xFFFF)
                        continue;
                    for (int c = start; c <= end && c != 0xFFFF; c++)
                    {
                        int g;
                        if (rangeOff == 0)
                        {
                            g = (c + delta) & 0xFFFF;
                        }
                        else
                        {
                            int gi = rangeBase + 2 * s + rangeOff + 2 * (c - start);
                            if (gi + 2 > data.Length)
                                continue;
                            g = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(gi));
                            if (g != 0)
                                g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0)
                            cmap[c] = g;
                    }
                }
                break;
            }
            case 6:
            {
                ushort first = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off + 6));
                ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off + 8));
                for (int i = 0; i < count; i++)
                {
                    int g = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off + 10 + 2 * i));
                    if (g != 0)
                        cmap[first + i] = g;
                }
                break;
            }
            case 12:
            {
                int nGroups = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(off + 12));
                for (int i = 0; i < nGroups; i++)
                {
                    int rec = off + 16 + 12 * i;
                    uint start = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec));
                    uint end = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 4));
                    uint startG = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8));
                    for (uint c = start; c <= end && c <= 0x10FFFF; c++)
                    {
                        if (startG + (c - start) is > 0 and < 0x100000)
                            cmap[(int)c] = (int)(startG + (c - start));
                    }
                }
                break;
            }
            default:
                throw new InvalidDataException($"TTF: قالب cmap {format} پشتیبانی نمی‌شود.");
        }
    }

    /// <summary>شناسهٔ گلیف برای کدپوینت (۰ اگر موجود نباشد).</summary>
    public int GlyphIdOf(int unicode) => _cmap.TryGetValue(unicode, out var g) ? g : 0;

    /// <summary>عرض پیشرفته گلیف بر حسب واحد فونت.</summary>
    public int AdvanceWidth(int glyphId)
        => glyphId >= 0 && glyphId < _widths.Length ? _widths[glyphId] : 0;

    /// <summary>عرض پیشرفته متن شکل‌گرفته (mm با اندازهٔ داده‌شده).</summary>
    public double MeasureTextWidthMm(IEnumerable<int> shapedCodepoints, double sizeMm)
    {
        double units = 0;
        foreach (int cp in shapedCodepoints)
        {
            if (cp == 0x200C)
                continue; // ZWNJ
            int gid = GlyphIdOf(cp);
            if (gid == 0 && cp < 0x80)
                gid = cp; // فرض هویتی برای ASCII
            units += AdvanceWidth(gid);
        }
        return units / UnitsPerEm * sizeMm;
    }
}
