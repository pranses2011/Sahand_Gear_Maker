using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;

namespace SahandGearMaker.Exports;

/// <summary>
/// صادرکنندهٔ 3MF (3D Manufacturing Format — ZIP با XML هسته‌ای) و AMF (XML).
/// 3MF: OPC/ZIP استاندارد با [Content_Types].xml و مدل واحد؛ واحد 3MF «میلی‌متر» است
/// (برخلاف glTF) پس تبدیل واحد لازم نیست.
/// AMF: XML با واحد صریح در تگ unit؛ مثلث‌ها با v-indices.
/// اعتبارسنجی پس از نوشتن: 3MF دوباره باز و شمارش مثلث‌ها کنترل می‌شود.
/// </summary>
public static class Format3mfAmfExporter
{
    /// <summary>خروجی 3MF از یک مش. پس از نوشتن، فایل دوباره خوانده و کنترل می‌شود.</summary>
    public static void Export3mf(TriMesh mesh, string path, string objectName = "gear")
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("مسیر خروجی خالی است.", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tempPath = path + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // [Content_Types].xml
            var ct = zip.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
            using (var w = new StreamWriter(ct.Open(), new UTF8Encoding(false)))
                w.Write("""
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
 <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
</Types>
""");

            // _rels/.rels
            var rels = zip.CreateEntry("_rels/.rels", CompressionLevel.Optimal);
            using (var w = new StreamWriter(rels.Open(), new UTF8Encoding(false)))
                w.Write("""
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>
</Relationships>
""");

            // 3D/3dmodel.model
            var model = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.Optimal);
            using (var w = new StreamWriter(model.Open(), new UTF8Encoding(false)))
            {
                var sb = new StringBuilder(64 * 1024);
                sb.Append("""
<?xml version="1.0" encoding="UTF-8"?>
<model unit="millimeter" xml:lang="fa-IR" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
 <metadata name="Title">__NAME__</metadata>
 <metadata name="Application">Sahand Gear Maker</metadata>
 <resources>
  <object id="1" type="model">
   <mesh>
    <vertices>
""");
                sb.Replace("__NAME__", EscapeXml(objectName));
                foreach (var v in mesh.Vertices)
                    sb.Append("     <vertex x=\"").Append(v.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                      .Append("\" y=\"").Append(v.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                      .Append("\" z=\"").Append(v.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                      .Append("\"/>\n");
                sb.Append("    </vertices>\n    <triangles>\n");
                foreach (var t in mesh.Triangles)
                    sb.Append("     <triangle v1=\"").Append(t.A).Append("\" v2=\"").Append(t.B)
                      .Append("\" v3=\"").Append(t.C).Append("\"/>\n");
                sb.Append("    </triangles>\n   </mesh>\n  </object>\n </resources>\n <build>\n  <item objectid=\"1\"/>\n </build>\n</model>\n");
                w.Write(sb.ToString());
            }
        }

        File.Move(tempPath, path, overwrite: true);

        // اعتبارسنجی پس از نوشتن (سیاست سند ۰۴)
        using var check = ZipFile.OpenRead(path);
        var modelEntry = check.GetEntry("3D/3dmodel.model")
            ?? throw new InvalidOperationException("3MF نوشته‌شده ساختار معتبر ندارد (مدل یافت نشد).");
        using var sr = new StreamReader(modelEntry.Open());
        string content = sr.ReadToEnd();
        int triCount = CountOccurrences(content, "<triangle ");
        if (triCount != mesh.TriangleCount)
            throw new InvalidOperationException($"3MF ناقص نوشته شد ({triCount} در برابر {mesh.TriangleCount} مثلث).");
    }

    /// <summary>خروجی AMF (XML؛ واحد در تگ unit).</summary>
    public static void ExportAmf(TriMesh mesh, string path, string objectName = "gear")
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("مسیر خروجی خالی است.", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tempPath = path + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            w.Write(("""
<?xml version="1.0" encoding="UTF-8"?>
<amf unit="millimeter" version="1.1">
 <metadata type="Name">__NAME__</metadata>
 <metadata type="Application">Sahand Gear Maker</metadata>
 <object id="0">
  <mesh>
   <vertices>
""").Replace("__NAME__", EscapeXml(objectName)));
            foreach (var v in mesh.Vertices)
                w.Write("    <vertex><coordinates><x>{0}</x><y>{1}</y><z>{2}</z></coordinates></vertex>\n",
                    v.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    v.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    v.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            w.Write("   </vertices>\n   <volume>\n");
            foreach (var t in mesh.Triangles)
                w.Write("    <triangle><v1>{0}</v1><v2>{1}</v2><v3>{2}</v3></triangle>\n", t.A, t.B, t.C);
            w.Write("   </volume>\n  </mesh>\n </object>\n</amf>\n");
        }
        File.Move(tempPath, path, overwrite: true);

        // کنترل پس از نوشتن
        string content = File.ReadAllText(path);
        int triCount = CountOccurrences(content, "<triangle>");
        if (triCount != mesh.TriangleCount)
            throw new InvalidOperationException($"AMF ناقص نوشته شد ({triCount} در برابر {mesh.TriangleCount} مثلث).");
    }

    /// <summary>خروجی دوبعدی DXF دوبعدی (پروفیل مرز قطعه در صفحهٔ XY) — R12 ASCII سازگار.</summary>
    public static void ExportProfileDxf(List<(double X, double Y)> polyline, string path, bool closed = true)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        if (polyline.Count < 2)
            throw new InvalidOperationException("پروفیل برای DXF حداقل ۲ نقطه لازم دارد.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder(32 * 1024);
        sb.Append("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n");
        sb.Append("0\nSECTION\n2\nENTITIES\n");

        var pts = closed ? polyline.Concat(new[] { polyline[0] }) : polyline;
        double px = double.NaN, py = 0;
        bool first = true;
        foreach (var (x, y) in pts)
        {
            if (!first)
            {
                sb.Append("0\nLINE\n8\nGEARPROFILE\n")
                  .Append("10\n").Append(px.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                  .Append("20\n").Append(py.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                  .Append("30\n0.0\n")
                  .Append("11\n").Append(x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                  .Append("21\n").Append(y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
                  .Append("31\n0.0\n");
            }
            px = x; py = y;
            first = false;
        }
        sb.Append("0\nENDSEC\n0\nEOF\n");
        File.WriteAllText(path, sb.ToString());
    }

    private static int CountOccurrences(string s, string token)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }

    private static string EscapeXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}

/// <summary>نتیجهٔ خروجی دسته‌ای برای یک فایل.</summary>
public sealed class BatchExportItemResult
{
    public required string FilePath { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long BytesWritten { get; init; }
}

/// <summary>نتیجهٔ کل خروجی دسته‌ای.</summary>
public sealed class BatchExportResult
{
    public required IReadOnlyList<BatchExportItemResult> Items { get; init; }
    public int Succeeded => Items.Count(i => i.Success);
    public int Failed => Items.Count(i => !i.Success);
}

/// <summary>قالب‌های خروجی قابل انتخاب در خروجی دسته‌ای.</summary>
public enum ExportFileFormat
{
    StlBinary,
    StlAscii,
    Obj,
    Ply,
    Glb,
    ThreeMf,
    Amf,
}

/// <summary>
/// خروجی دسته‌ای (سند ۰۷، بخش ۳٫۵): چند قطعه/گونه با تنظیم یکسان؛
/// شکست یک مورد بقیه را متوقف نمی‌کند و در گزارش ثبت می‌شود.
/// </summary>
public static class BatchExporter
{
    public static BatchExportResult ExportAll(
        IReadOnlyList<(string FileName, TriMesh Mesh)> items,
        string outputDirectory,
        ExportFileFormat format,
        MeshExportSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("پوشهٔ خروجی خالی است.", nameof(outputDirectory));
        Directory.CreateDirectory(outputDirectory);
        settings ??= new MeshExportSettings();

        var results = new List<BatchExportItemResult>(items.Count);
        foreach (var (fileName, mesh) in items)
        {
            string safe = MakeSafeFileName(fileName);
            string path = Path.Combine(outputDirectory, safe + ExtensionOf(format));
            try
            {
                switch (format)
                {
                    case ExportFileFormat.StlBinary:
                        MeshExporter.ExportStlBinary(mesh, path, settings);
                        break;
                    case ExportFileFormat.StlAscii:
                        MeshExporter.ExportStlAscii(mesh, path, settings);
                        break;
                    case ExportFileFormat.Obj:
                        MeshExporter.ExportObj(mesh, path, settings, Path.GetFileNameWithoutExtension(safe));
                        break;
                    case ExportFileFormat.Ply:
                        MeshExporter.ExportPlyBinary(mesh, path, settings);
                        break;
                    case ExportFileFormat.Glb:
                        MeshExporter.ExportGlb(mesh, path, settings, Path.GetFileNameWithoutExtension(safe));
                        break;
                    case ExportFileFormat.ThreeMf:
                        Format3mfAmfExporter.Export3mf(mesh, path, Path.GetFileNameWithoutExtension(safe));
                        break;
                    case ExportFileFormat.Amf:
                        Format3mfAmfExporter.ExportAmf(mesh, path, Path.GetFileNameWithoutExtension(safe));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format), $"قالب {format} در خروجی دسته‌ای پشتیبانی نمی‌شود.");
                }
                results.Add(new BatchExportItemResult
                {
                    FilePath = path,
                    Success = true,
                    BytesWritten = new FileInfo(path).Length,
                });
            }
            catch (Exception ex)
            {
                results.Add(new BatchExportItemResult
                {
                    FilePath = path,
                    Success = false,
                    Error = ex.Message,
                });
            }
        }
        return new BatchExportResult { Items = results };
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "part" : name;
    }

    private static string ExtensionOf(ExportFileFormat f) => f switch
    {
        ExportFileFormat.StlBinary or ExportFileFormat.StlAscii => ".stl",
        ExportFileFormat.Obj => ".obj",
        ExportFileFormat.Ply => ".ply",
        ExportFileFormat.Glb => ".glb",
        ExportFileFormat.ThreeMf => ".3mf",
        ExportFileFormat.Amf => ".amf",
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };
}
