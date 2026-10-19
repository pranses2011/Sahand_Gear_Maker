using System.Globalization;
using System.Text;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Cad;

/// <summary>
/// نگارندهٔ IGES 5.3 برای جامد BREP — نسخهٔ ۰٫۵.
///
/// ساختار: هر وجه → Type 144 (Trimmed Surface) روی سطح تحلیلی:
///   - صفحه → Type 108 (Plane)؛
///   - استوانه → Type 120 (Surface of Revolution) با مولّدِ خط موازی محور؛
/// مرزها → Type 102 (Composite Curve) از Type 110 (Line) و Type 100 (Circular Arc + تبدیل 124).
/// قالب‌بندی ستون-حساس طبق استاندارد: محتوا (۱-۶۴) | فاصله (۶۵) | شمارهٔ رکورد (۶۶-۷۲) |
/// حرف بخش (۷۳) | شمارهٔ رکورد (۷۴-۸۰)؛ شماره‌گذاری هر بخش مستقل از ۱ شروع می‌شود.
/// </summary>
public sealed class IgesWriter
{
    private const string Delim = ",";

    private readonly List<string> _start = new() { "Sahand Gear Maker 0.5 - IGES 5.3 export - units: millimeter" };
    private readonly List<string> _dir = new();
    private readonly List<string> _param = new();
    private string[] _globals = Array.Empty<string>();

    public string PartName { get; set; } = "gear";

    /// <summary>نوشتن یک جامد به رشتهٔ IGES.</summary>
    public string Write(CadSolid solid) => Write(new[] { solid });

    /// <summary>نوشتن چند جامد در یک فایل IGES (موجودیت‌ها مستقل‌اند — مونتاژ صریح نیاز به 308/408 دارد).</summary>
    public string Write(IReadOnlyList<CadSolid> solids)
    {
        if (solids.Count == 0)
            throw new ArgumentException("حداقل یک جامد لازم است.", nameof(solids));

        _dir.Clear();
        _param.Clear();

        BuildGlobal(solids[0].Name);
        foreach (var solid in solids)
            foreach (var face in solid.Faces)
                WriteFace(face);

        return Serialize();
    }

    private void BuildGlobal(string name)
    {
        string h(string s) => s.Length.ToString(CultureInfo.InvariantCulture) + "H" + s;
        _globals = new[]
        {
            string.Join(Delim,
                h(Delim), h(";"),
                h("SahandGearMaker"),
                h("SahandGearMaker.Cad"),
                h("SahandGearMaker"),
                h("0.5"),
                "32", "38", "8", "15",
                h(name),
                "0",     // Drafting Standard: 0 = none
                "2",     // Units: 2 = millimeters
                "0.01",  // Model space resolution
                "0.001", // Max coordinate deviation
                "0",     // Minimum user intended resolution
                h("SahandGearMaker CAD Kernel"),
                h(""),
                "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0"),
        };
    }

    // ═══════════ سریال‌سازی ═══════════

    private string Serialize()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _start.Count; i++)
            sb.Append(PadTo(_start[i], 72)).Append('S').Append((i + 1).ToString("D7")).Append('\n');

        int gSeq = 0;
        foreach (var g in _globals)
            foreach (var c in SplitRecords(g, 72))
            {
                gSeq++;
                sb.Append(PadTo(c, 72)).Append('G').Append(gSeq.ToString("D7")).Append('\n');
            }

        for (int i = 0; i < _dir.Count; i += 2)
        {
            int de = i / 2 + 1;
            sb.Append(_dir[i]).Append('D').Append((de * 2 - 1).ToString("D7")).Append('\n');
            sb.Append(_dir[i + 1]).Append('D').Append((de * 2).ToString("D7")).Append('\n');
        }

        for (int i = 0; i < _param.Count; i++)
        {
            int seq = i + 1;
            sb.Append(PadTo(_param[i], 64))   // ستون‌های ۱-۶۴
              .Append(' ')                     // ستون ۶۵
              .Append(seq.ToString("D7"))      // ستون‌های ۶۶-۷۲
              .Append('P')                     // ستون ۷۳
              .Append(seq.ToString("D7"))      // ستون‌های ۷۴-۸۰
              .Append('\n');
        }

        sb.Append(PadTo($"S{_start.Count}G{gSeq}D{_dir.Count / 2}P{_param.Count}", 72))
          .Append('T').Append(1.ToString("D7")).Append('\n');
        return sb.ToString();
    }

    private static string PadTo(string s, int len)
        => s.Length >= len ? s.Substring(0, len) : s + new string(' ', len - s.Length);

    private static List<string> SplitRecords(string s, int width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(s)) { result.Add(""); return result; }
        for (int i = 0; i < s.Length; i += width)
            result.Add(s.Substring(i, Math.Min(width, s.Length - i)));
        return result;
    }

    // ═══════════ ثبت موجودیت ═══════════

    private int AddEntity(int type, int form, string parameters, int transformDe = 0)
    {
        int de = _dir.Count / 2 + 1;
        int paramStart = _param.Count + 1;

        var records = SplitParamRecords(parameters);
        foreach (var r in records)
            _param.Add(r);

        // ۹ فیلد × ۸ ستون (۷۲ ستون) — «D» + شمارهٔ رکورد در سریال‌سازی افزوده می‌شود
        string L1 = F(type) + F(paramStart) + F(0) + F(0) + F(0) + F(0) + F(transformDe) + F(0) + "00000000";
        string L2 = F(type) + F(0) + F(0) + F(records.Count) + F(form) + F(0) + F(0) + F(0) + F(0);
        _dir.Add(L1);
        _dir.Add(L2);
        return de;
    }

    private static string F(int v) => v.ToString(CultureInfo.InvariantCulture).PadLeft(8);

    private static List<string> SplitParamRecords(string p)
    {
        var records = new List<string>();
        var parts = p.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        foreach (var part in parts)
        {
            if (current.Length == 0)
                current.Append(part);
            else if (current.Length + 1 + part.Length <= 64)
                current.Append(',').Append(part);
            else
            {
                records.Add(current.ToString());
                current.Clear().Append(part);
            }
        }
        if (current.Length > 0)
            records.Add(current.ToString());
        if (records.Count == 0)
            records.Add(";");
        else
            records[^1] += ";";
        return records;
    }

    // ═══════════ هندسه ═══════════

    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private void WriteFace(CadFace face)
    {
        int surface = face.Surface switch
        {
            CadPlane pl => PlaneEntity(pl),
            CadCylinder cyl => CylinderEntity(cyl),
            _ => throw new NotSupportedException("نوع سطح CAD پشتیبانی نمی‌شود."),
        };

        int outer = CompositeCurve(face.Outer);
        var innerIds = face.Inner.Select(CompositeCurve).ToList();

        // 144: PTS، P1=0 (مرز بیرونی صریح)، N، PTO، PTI1..PTIn
        var ps = new List<string>
        {
            surface.ToString(CultureInfo.InvariantCulture),
            "0",
            innerIds.Count.ToString(CultureInfo.InvariantCulture),
            outer.ToString(CultureInfo.InvariantCulture),
        };
        foreach (int id in innerIds)
            ps.Add(id.ToString(CultureInfo.InvariantCulture));
        AddEntity(144, 0, string.Join(Delim, ps));
    }

    private int PlaneEntity(CadPlane p)
    {
        // 108: نقطهٔ صفحه + نرمال + مرجع (مرز از 144 می‌آید)
        string param = string.Join(Delim,
            N(p.Origin.X), N(p.Origin.Y), N(p.Origin.Z),
            N(p.AxisZ.X), N(p.AxisZ.Y), N(p.AxisZ.Z),
            N(p.AxisX.X), N(p.AxisX.Y), N(p.AxisX.Z));
        return AddEntity(108, 0, param);
    }

    private int CylinderEntity(CadCylinder c)
    {
        // 120: محور (خط 110) + مولّد (خط 110 موازی محور در فاصلهٔ r) + زاویهٔ ۰ تا ۳۶۰
        double r = c.Radius;
        var p0 = c.Origin;
        var p1 = p0 + Vector3.UnitZ * 100;
        int axisLine = AddEntity(110, 0, string.Join(Delim,
            N(p0.X), N(p0.Y), N(p0.Z), N(p1.X), N(p1.Y), N(p1.Z)));
        var g0 = p0 + new Vector3(r, 0, 0);
        var g1 = g0 + Vector3.UnitZ * 100;
        int genLine = AddEntity(110, 0, string.Join(Delim,
            N(g0.X), N(g0.Y), N(g0.Z), N(g1.X), N(g1.Y), N(g1.Z)));
        return AddEntity(120, 0, $"{axisLine},{genLine},0,360");
    }

    private int CompositeCurve(CadLoop loop)
    {
        var segments = new List<int>();
        int n = loop.Nodes.Count;
        for (int i = 0; i < n; i++)
        {
            var from = loop.Nodes[i];
            var to = loop.Nodes[(i + 1) % n];
            if ((to.Point - from.Point).Length < 1e-9)
                continue;
            if (from.Kind == CadSegmentKind.Line)
            {
                segments.Add(AddEntity(110, 0, string.Join(Delim,
                    N(from.Point.X), N(from.Point.Y), N(from.Point.Z),
                    N(to.Point.X), N(to.Point.Y), N(to.Point.Z))));
            }
            else
            {
                segments.Add(ArcEntity(from, to.Point));
            }
        }
        if (segments.Count == 0)
            return AddEntity(110, 0, "0,0,0,1,0,0");
        if (segments.Count == 1)
            return segments[0];
        string param = segments.Count.ToString(CultureInfo.InvariantCulture) + Delim + string.Join(Delim, segments);
        return AddEntity(102, 0, param);
    }

    /// <summary>کمان 100 با تبدیل 124 (صفحهٔ کمان؛ شروع روی X محلی).</summary>
    private int ArcEntity(CadNode from, Vector3 endPoint)
    {
        var axis = from.ArcAxis.Normalized();
        var xDir = (from.Point - from.ArcCenter).Normalized();
        var yDir = Vector3.Cross(axis, xDir);
        string t = string.Join(Delim,
            N(xDir.X), N(yDir.X), N(axis.X), N(from.ArcCenter.X),
            N(xDir.Y), N(yDir.Y), N(axis.Y), N(from.ArcCenter.Y),
            N(xDir.Z), N(yDir.Z), N(axis.Z), N(from.ArcCenter.Z),
            "1");
        int transformDe = AddEntity(124, 0, t);

        double r = from.ArcRadius;
        var v1 = endPoint - from.ArcCenter;
        double x2 = Vector3.Dot(v1, xDir);
        double y2 = Vector3.Dot(v1, yDir);
        // 100: ZT=0، سپس شروع (r, 0) و پایان (x2, y2) در دستگاه محلیِ 124
        string param = $"0,{N(r)},0,{N(x2)},{N(y2)}";
        return AddEntity(100, 0, param, transformDe);
    }
}

/// <summary>خدمات فایل CAD.</summary>
public static partial class CadFile
{
    /// <summary>نوشتن جامد در فایل STEP (AP214).</summary>
    public static CadBuildReport WriteStep(CadSolid solid, string path)
    {
        var writer = new StepWriter();
        string content = writer.Write(solid);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return new CadBuildReport
        {
            FaceCount = solid.Faces.Count,
            Strategy = "STEP AP214",
        };
    }

    /// <summary>نوشتن جامد در فایل IGES 5.3.</summary>
    public static CadBuildReport WriteIges(CadSolid solid, string path)
    {
        var writer = new IgesWriter { PartName = solid.Name };
        string content = writer.Write(solid);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return new CadBuildReport
        {
            FaceCount = solid.Faces.Count,
            Strategy = "IGES 5.3",
        };
    }

    /// <summary>نوشتن چند جامد (مونتاژ) در یک فایل STEP AP214.</summary>
    public static CadBuildReport WriteStep2(IReadOnlyList<CadSolid> solids, string path)
    {
        var writer = new StepWriter();
        string content = writer.Write(solids);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return new CadBuildReport
        {
            FaceCount = solids.Sum(s => s.Faces.Count),
            Strategy = "STEP AP214 (مونتاژ)",
        };
    }

    /// <summary>نوشتن چند جامد (مونتاژ) در یک فایل IGES 5.3.</summary>
    public static CadBuildReport WriteIges2(IReadOnlyList<CadSolid> solids, string path)
    {
        var writer = new IgesWriter { PartName = solids.Count == 1 ? solids[0].Name : "assembly" };
        string content = writer.Write(solids);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return new CadBuildReport
        {
            FaceCount = solids.Sum(s => s.Faces.Count),
            Strategy = "IGES 5.3 (مونتاژ)",
        };
    }
}
