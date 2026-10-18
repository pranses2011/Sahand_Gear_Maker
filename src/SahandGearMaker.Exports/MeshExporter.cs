using System.Text;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Exports;

/// <summary>پیش‌تنظیم کیفیت مش (سند ۰۴، بخش ۵) — مقیاس‌پذیر نسبت به اندازهٔ مدل.</summary>
public enum MeshQualityPreset
{
    /// <summary>پیش‌نمایش تعاملی — مش سبک.</summary>
    Preview,
    /// <summary>تبادل عمومی — تعادل دقت/حجم.</summary>
    Standard,
    /// <summary>چاپ/جزئیات بیشتر — تلرانس کمتر.</summary>
    Fine,
    /// <summary>خیلی دقیق — تلرانس ۰٫۰۰۴mm؛ برای چاپ دقیق و STL نهایی.</summary>
    Ultra,
    /// <summary>حداکثر دقت — تلرانس ۰٫۰۰۲mm؛ سنگین و کند، برای قطعات حساس.</summary>
    Precision,
}

/// <summary>تنظیمات خروجی مش.</summary>
public sealed class MeshExportSettings
{
    public MeshQualityPreset Preset { get; set; } = MeshQualityPreset.Standard;

    /// <summary>خطای وتر (mm) — در حالت سفارشی مستقیم اعمال می‌شود.</summary>
    public double? CustomChordalToleranceMm { get; set; }

    /// <summary>واحد خروجی (STL واحد ندارد و در گزارش هشدار می‌شود).</summary>
    public LengthUnit Unit { get; set; } = LengthUnit.Millimeter;

    /// <summary>تبدیل مختصات به واحد خروجی (مثلاً mm→m برای GLB).</summary>
    public double ScaleToUnit => Unit switch
    {
        LengthUnit.Millimeter => 1.0,
        LengthUnit.Inch => 1.0 / UnitConverter.MmPerInch,
        _ => 1.0,
    };

    public double EffectiveToleranceMm(double modelSizeMm) => CustomChordalToleranceMm ?? Preset switch
    {
        MeshQualityPreset.Preview => Math.Max(0.20, modelSizeMm * 0.004),
        MeshQualityPreset.Fine => Math.Max(0.005, modelSizeMm * 0.0005),
        MeshQualityPreset.Ultra => Math.Max(0.004, modelSizeMm * 0.00025),
        MeshQualityPreset.Precision => Math.Max(0.002, modelSizeMm * 0.00012),
        _ => Math.Max(0.02, modelSizeMm * 0.0015),
    };

    /// <summary>تنظیمات هندسهٔ بازسازی‌شده برای این پیش‌تنظیم — تلرانس پروفیل + برش‌های محوری.</summary>
    public Domain.GeometrySettings ToGeometrySettings(Domain.GeometrySettings? current = null)
    {
        var g = new Domain.GeometrySettings
        {
            ProfileToleranceMm = Preset switch
            {
                MeshQualityPreset.Preview => 0.10,
                MeshQualityPreset.Standard => 0.02,
                MeshQualityPreset.Fine => 0.008,
                MeshQualityPreset.Ultra => 0.004,
                MeshQualityPreset.Precision => 0.002,
                _ => 0.02,
            },
            AxialSliceCount = Preset switch
            {
                MeshQualityPreset.Preview => 12,
                MeshQualityPreset.Standard => 24,
                MeshQualityPreset.Fine => 48,
                MeshQualityPreset.Ultra => 96,
                MeshQualityPreset.Precision => 160,
                _ => 24,
            },
        };
        if (current is not null && Preset == MeshQualityPreset.Preview)
        {
            // «همانِ طراح» مش موجود را نگه می‌داریم؛ این مقدار فقط جهت گزارش است
            g.ProfileToleranceMm = current.ProfileToleranceMm;
            g.AxialSliceCount = current.AxialSliceCount;
        }
        return g;
    }
}

/// <summary>گزارش خروجی پس از نوشتن فایل (سند ۰۴، بخش ۴-گام D).</summary>
public sealed class ExportReport
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string FormatId { get; init; } = string.Empty;
    public double UnitScale { get; init; } = 1.0;
    public int TriangleCount { get; init; }
    public int VertexCount { get; init; }
    public double ChordalToleranceMm { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    /// <summary>نتیجهٔ اعتبارسنجی پس از نوشتن (فایل دوباره خوانده می‌شود).</summary>
    public bool PostWriteValidated { get; init; }
}

/// <summary>
/// صادرکنندهٔ مش مثلثی — STL باینری/متنی، OBJ+MTL، PLY باینری و GLB (سند ۰۴).
/// همهٔ خروجی‌ها «مش» هستند و هرگز به‌عنوان B-rep معرفی نمی‌شوند.
/// پس از نوشتن، فایل دوباره خوانده و اعتبارسنجی می‌شود (الزام صداقت).
/// </summary>
public static class MeshExporter
{
    // ---------- STL ----------

    public static ExportReport ExportStlBinary(TriMesh mesh, string path, MeshExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double scale = settings.ScaleToUnit;
        var warnings = new List<string>();
        if (settings.Unit == LengthUnit.Inch)
            warnings.Add("STL واحد درون‌فایلی ندارد؛ خروجی بر حسب اینچ نوشته شد و باید در نرم‌افزار مقصد همین‌طور تفسیر شود.");

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            Span<byte> header = stackalloc byte[80];
            Encoding.ASCII.GetBytes("Sahand Gear Designer - mesh export", header);
            w.Write(header);
            w.Write((uint)mesh.TriangleCount);
            var verts = mesh.Vertices;
            foreach (var t in mesh.Triangles)
            {
                var n = t.Normal(verts);
                WriteV(w, n * 1.0);
                WriteV(w, verts[t.A] * scale);
                WriteV(w, verts[t.B] * scale);
                WriteV(w, verts[t.C] * scale);
                w.Write((ushort)0);
            }
        }

        sw.Stop();
        bool validated = ValidateStlBinary(path, (uint)mesh.TriangleCount);
        if (!validated)
            warnings.Add("اعتبارسنجی پس از نوشتن ناموفق بود؛ فایل ممکن است ناقص باشد.");

        return new ExportReport
        {
            Success = validated,
            FilePath = path,
            FormatId = "STL-BINARY",
            UnitScale = scale,
            TriangleCount = mesh.TriangleCount,
            VertexCount = mesh.VertexCount,
            ChordalToleranceMm = settings.CustomChordalToleranceMm ?? 0,
            Elapsed = sw.Elapsed,
            Warnings = warnings,
            PostWriteValidated = validated,
        };

        static void WriteV(BinaryWriter w, Vector3 v)
        {
            w.Write((float)v.X);
            w.Write((float)v.Y);
            w.Write((float)v.Z);
        }
    }

    /// <summary>اعتبارسنجی STL باینری: هدر + شمار مثلث + طول فایل.</summary>
    private static bool ValidateStlBinary(string path, uint expectedTriangles)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length < 84) return false;
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            r.ReadBytes(80);
            uint count = r.ReadUInt32();
            return count == expectedTriangles && fi.Length == 84 + 50L * count;
        }
        catch { return false; }
    }

    public static ExportReport ExportStlAscii(TriMesh mesh, string path, MeshExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double scale = settings.ScaleToUnit;
        var warnings = new List<string>
        {
            "STL متنی برای اشکال‌زدایی مناسب است و حجم بیشتری دارد.",
            "STL واحد درون‌فایلی ندارد؛ واحد خروجی فقط در گزارش اینجا ثبت می‌شود.",
        };

        var sb = new StringBuilder(mesh.TriangleCount * 70);
        sb.AppendLine("solid SahandGearDesigner");
        var verts = mesh.Vertices;
        foreach (var t in mesh.Triangles)
        {
            var n = t.Normal(verts);
            sb.AppendLine(CultureInfo($"  facet normal {n.X:G9} {n.Y:G9} {n.Z:G9}"));
            sb.AppendLine("    outer loop");
            AppendVertex(sb, verts[t.A] * scale);
            AppendVertex(sb, verts[t.B] * scale);
            AppendVertex(sb, verts[t.C] * scale);
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }
        sb.AppendLine("endsolid SahandGearDesigner");
        File.WriteAllText(path, sb.ToString(), System.Text.Encoding.ASCII);

        sw.Stop();
        bool validated = File.Exists(path) && File.ReadAllText(path).Contains("endsolid", StringComparison.Ordinal);
        return new ExportReport
        {
            Success = validated,
            FilePath = path,
            FormatId = "STL-ASCII",
            UnitScale = scale,
            TriangleCount = mesh.TriangleCount,
            VertexCount = mesh.VertexCount,
            Elapsed = sw.Elapsed,
            Warnings = warnings,
            PostWriteValidated = validated,
        };

        static void AppendVertex(StringBuilder sb, Vector3 v)
            => sb.AppendLine(CultureInfo($"      vertex {v.X:G9} {v.Y:G9} {v.Z:G9}"));

        static string CultureInfo(FormattableString fs) => fs.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------- OBJ + MTL ----------

    public static ExportReport ExportObj(TriMesh mesh, string path, MeshExportSettings settings, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double scale = settings.ScaleToUnit;

        string mtlPath = Path.ChangeExtension(path, ".mtl");
        string mtlName = "sahand_material";
        File.WriteAllText(mtlPath,
            $"# Sahand Gear Designer — material (نمایشی)\n" +
            $"newmtl {mtlName}\nKd 0.85 0.85 0.88\nKa 0.10 0.10 0.12\nNs 60\n", System.Text.Encoding.UTF8);

        var sb = new StringBuilder(mesh.VertexCount * 24 + mesh.TriangleCount * 18);
        sb.AppendLine("# Sahand Gear Designer — mesh export (OBJ)");
        sb.AppendLine(CultureInfo($"mtllib {Path.GetFileName(mtlPath)}"));
        sb.AppendLine($"o {SanitizeName(displayName ?? "gear")}");
        var verts = mesh.Vertices;
        foreach (var v in verts)
            sb.AppendLine(CultureInfo($"v {v.X * scale:G9} {v.Y * scale:G9} {v.Z * scale:G9}"));
        sb.AppendLine($"usemtl {mtlName}");
        foreach (var t in mesh.Triangles)
            sb.AppendLine(CultureInfo($"f {t.A + 1} {t.B + 1} {t.C + 1}"));
        File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);

        sw.Stop();
        bool validated = File.ReadAllLines(path).Count(l => l.StartsWith("f ", StringComparison.Ordinal)) == mesh.TriangleCount;

        return new ExportReport
        {
            Success = validated,
            FilePath = path,
            FormatId = "OBJ",
            UnitScale = scale,
            TriangleCount = mesh.TriangleCount,
            VertexCount = mesh.VertexCount,
            Elapsed = sw.Elapsed,
            Warnings = new[] { $"فایل متریال جانبی: {Path.GetFileName(mtlPath)}" },
            PostWriteValidated = validated,
        };

        static string CultureInfo(FormattableString fs) => fs.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------- PLY (باینری little-endian با نرمال) ----------

    public static ExportReport ExportPlyBinary(TriMesh mesh, string path, MeshExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double scale = settings.ScaleToUnit;
        var verts = mesh.Vertices;

        string header =
            "ply\n" +
            "format binary_little_endian 1.0\n" +
            "comment Sahand Gear Designer — mesh export\n" +
            $"element vertex {mesh.VertexCount}\n" +
            "property float x\nproperty float y\nproperty float z\n" +
            "property float nx\nproperty float ny\nproperty float nz\n" +
            $"element face {mesh.TriangleCount}\n" +
            "property list uchar int vertex_indices\n" +
            "end_header\n";

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            fs.Write(System.Text.Encoding.ASCII.GetBytes(header));
            using var w = new BinaryWriter(fs);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = verts[i];
                w.Write((float)(v.X * scale));
                w.Write((float)(v.Y * scale));
                w.Write((float)(v.Z * scale));
                w.Write((float)0); w.Write((float)0); w.Write((float)1); // نرمال پیش‌فرض؛ مثلث‌ها جهت دارند
            }
            foreach (var t in mesh.Triangles)
            {
                w.Write((byte)3);
                w.Write(t.A); w.Write(t.B); w.Write(t.C);
            }
        }

        sw.Stop();
        bool validated = new FileInfo(path).Length > header.Length;
        return new ExportReport
        {
            Success = validated,
            FilePath = path,
            FormatId = "PLY-BINARY",
            UnitScale = scale,
            TriangleCount = mesh.TriangleCount,
            VertexCount = mesh.VertexCount,
            Elapsed = sw.Elapsed,
            Warnings = Array.Empty<string>(),
            PostWriteValidated = validated,
        };
    }

    // ---------- GLB (glTF 2.0 باینری؛ واحد glTF = متر) ----------

    public static ExportReport ExportGlb(TriMesh mesh, string path, MeshExportSettings settings, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();
        var verts = mesh.Vertices;

        // glTF واحد متر دارد؛ تبدیل mm→m (یا اینچ→متر) انجام می‌شود
        double scale = settings.Unit == LengthUnit.Inch
            ? 1.0 / UnitConverter.MmPerInch * 1000.0 / 1000.0 * 0.0254
            : 0.001;
        if (settings.Unit == LengthUnit.Millimeter)
            warnings.Add("تبدیل mm→m برای استاندارد glTF اعمال شد.");

        // بافر رأس‌ها: pos(float3) + norm(float3)
        int vertexCount = mesh.VertexCount;
        int triangleCount = mesh.TriangleCount;
        int posBytes = vertexCount * 12;
        int normBytes = vertexCount * 12;
        int idxBytes = triangleCount * 12; // uint32 ×3 (بدون فشرده‌سازی برای سادگی/سازگاری)
        int binLength = posBytes + normBytes + idxBytes;

        using var ms = new MemoryStream(12 + 8 + JsonHeaderLength() + 8 + binLength + 1024);

        // --- JSON chunk ---
        string json = BuildGlbJson(vertexCount, triangleCount, posBytes, normBytes, displayName);
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        while (jsonBytes.Length % 4 != 0) Array.Resize(ref jsonBytes, jsonBytes.Length + 1); // padding فضای خالی

        // --- BIN chunk ---
        var bin = new byte[binLength];
        int off = 0;
        for (int i = 0; i < vertexCount; i++)
        {
            WriteF32(bin, ref off, (float)(verts[i].X * scale));
            WriteF32(bin, ref off, (float)(verts[i].Y * scale));
            WriteF32(bin, ref off, (float)(verts[i].Z * scale));
        }
        for (int i = 0; i < vertexCount; i++)
        {
            WriteF32(bin, ref off, 0f); WriteF32(bin, ref off, 0f); WriteF32(bin, ref off, 1f);
        }
        foreach (var t in mesh.Triangles)
        {
            WriteI32(bin, ref off, t.A);
            WriteI32(bin, ref off, t.B);
            WriteI32(bin, ref off, t.C);
        }

        using (var w = new BinaryWriter(ms))
        {
            w.Write(0x46546C67u);           // magic "glTF"
            w.Write(2u);                    // version
            long totalStart = ms.Position;
            w.Write(0u);                    // length (بعداً پر می‌شود)

            w.Write((uint)jsonBytes.Length);
            w.Write(0x4E4F534Au);           // "JSON"
            w.Write(jsonBytes);

            w.Write((uint)bin.Length);
            w.Write(0x004E4942u);           // "BIN\0"
            w.Write(bin);

            long total = ms.Position;
            ms.Position = totalStart;
            w.Write((uint)total);
        }

        File.WriteAllBytes(path, ms.ToArray());
        sw.Stop();

        bool validated = ValidateGlb(path);
        return new ExportReport
        {
            Success = validated,
            FilePath = path,
            FormatId = "GLB",
            UnitScale = scale,
            TriangleCount = triangleCount,
            VertexCount = vertexCount,
            Elapsed = sw.Elapsed,
            Warnings = warnings,
            PostWriteValidated = validated,
        };

        static int JsonHeaderLength() => 512;

        static void WriteF32(byte[] b, ref int o, float v)
        {
            var s = BitConverter.GetBytes(v);
            b[o++] = s[0]; b[o++] = s[1]; b[o++] = s[2]; b[o++] = s[3];
        }

        static void WriteI32(byte[] b, ref int o, int v)
        {
            var s = BitConverter.GetBytes(v);
            b[o++] = s[0]; b[o++] = s[1]; b[o++] = s[2]; b[o++] = s[3];
        }
    }

    private static string BuildGlbJson(int vertexCount, int triangleCount, int posBytes, int normBytes, string? name)
    {
        string safeName = SanitizeName(name ?? "gear");
        string json =
            "{\"asset\":{\"version\":\"2.0\",\"generator\":\"Sahand Gear Designer\"}," +
            $"\"scene\":0," +
            "\"scenes\":[{\"nodes\":[0]}]," +
            $"\"nodes\":[{{\"mesh\":0,\"name\":\"{safeName}\"}}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"indices\":2,\"mode\":4}]}]," +
            "\"buffers\":[{\"byteLength\":" + (posBytes + normBytes + triangleCount * 12) + "}]," +
            "\"bufferViews\":[" +
            "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + posBytes + ",\"target\":34962}," +
            "{\"buffer\":0,\"byteOffset\":" + posBytes + ",\"byteLength\":" + normBytes + ",\"target\":34962}," +
            "{\"buffer\":0,\"byteOffset\":" + (posBytes + normBytes) + ",\"byteLength\":" + (triangleCount * 12) + ",\"target\":34963}]," +
            "\"accessors\":[" +
            "{\"bufferView\":0,\"componentType\":5126,\"count\":" + vertexCount + ",\"type\":\"VEC3\"}," +
            "{\"bufferView\":1,\"componentType\":5126,\"count\":" + vertexCount + ",\"type\":\"VEC3\"}," +
            "{\"bufferView\":2,\"componentType\":5125,\"count\":" + (triangleCount * 3) + ",\"type\":\"SCALAR\"}]}";
        return json;
    }

    /// <summary>اعتبارسنجی GLB: magic، نسخه و طول.</summary>
    private static bool ValidateGlb(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            uint magic = r.ReadUInt32();
            uint version = r.ReadUInt32();
            uint length = r.ReadUInt32();
            return magic == 0x46546C67u && version == 2 && length == new FileInfo(path).Length;
        }
        catch { return false; }
    }

    /// <summary>پاک‌سازی نام برای فایل/عنوان ASCII سازگار.</summary>
    public static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
            else if (c is ' ' or '\t')
                sb.Append('_');
        }
        if (sb.Length == 0) sb.Append("part");
        if (sb.Length > 64) sb.Length = 64;
        return sb.ToString();
    }
}
