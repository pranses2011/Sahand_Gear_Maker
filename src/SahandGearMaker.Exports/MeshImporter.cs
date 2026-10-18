using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Exports;

/// <summary>قطعهٔ مرجع واردشده از فایل مش (سند ۰۷، بخش ۳٫۶ — فقط «مرجع»، نه پارامتریک).</summary>
public sealed class ReferenceMesh
{
    public required TriMesh Mesh { get; init; }
    public required string SourceFileName { get; init; }
    public required string Format { get; init; }

    /// <summary>هشدارهای خواندن (مثل نرمال‌های گمشده یا مثلث‌های منفصل).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// خوانندهٔ فرمت‌های مش برای «مدل مرجع» — STL باینری/متنی، OBJ، PLY (ascii/باینری le).
/// فایل ورودی به‌هیچ‌وجه به‌صورت چرخ‌دندهٔ پارامتریک تفسیر نمی‌شود (سیاست صریح سند ۰۷).
/// محدودیت‌های محافظه‌کارانه: حداکثر ۲ میلیون مثلث و ۸ میلیون رأس؛ رشته‌ها با طول محدود.
/// </summary>
public static class MeshImporter
{
    private const int MaxTriangles = 2_000_000;
    private const long MaxFileBytes = 400L * 1024 * 1024;

    public static ReferenceMesh Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("مسیر فایل خالی است.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("فایل یافت نشد.", path);
        if (info.Length > MaxFileBytes)
            throw new InvalidOperationException($"فایل بزرگ‌تر از حد مجاز است ({info.Length / 1024 / 1024} MB > 400 MB).");

        string ext = Path.GetExtension(path).ToLowerInvariant();
        var notes = new List<string>();
        TriMesh mesh = ext switch
        {
            ".stl" => ImportStl(path, notes),
            ".obj" => ImportObj(path, notes),
            ".ply" => ImportPly(path, notes),
            _ => throw new InvalidOperationException($"پسوند «{ext}» پشتیبانی نمی‌شود (فقط STL/OBJ/PLY برای مدل مرجع)."),
        };

        if (mesh.TriangleCount == 0)
            throw new InvalidOperationException("فایل هیچ مثلث معتبری نداشت.");
        if (mesh.TriangleCount > MaxTriangles)
            throw new InvalidOperationException("تعداد مثلث‌ها از حد مجاز (۲ میلیون) بیشتر است.");

        return new ReferenceMesh
        {
            Mesh = mesh,
            SourceFileName = Path.GetFileName(path),
            Format = ext.ToUpperInvariant(),
            Notes = notes,
        };
    }

    // ---------- STL ----------

    private static TriMesh ImportStl(string path, List<string> notes)
    {
        var bytes = File.ReadAllBytes(path);
        // تشخیص باینری/متنی: ascii با "solid" شروع می‌شود ولی فایل‌های باینری هم گاهی چنین دارند؛
        // روش معتبر: تطابق طول باینری (84 + 50·n)
        bool isBinary = true;
        if (bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "solid")
        {
            long n = (bytes.Length - 84) / 50L;
            isBinary = bytes.Length != 84 + 50 * n;
        }

        var mesh = new TriMesh();
        if (isBinary)
        {
            if (bytes.Length < 84)
                throw new InvalidOperationException("STL باینری ناقص است.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80));
            if (84 + 50L * count > bytes.Length)
                throw new InvalidOperationException("STL باینری: تعداد مثلث با طول فایل نمی‌خواند.");

            // رأس‌ها hash می‌شوند تا مش رأس-مشترک شود (برای نمایش/اندازه‌گیری)
            var vertexMap = new Dictionary<(float, float, float), int>(capacity: (int)Math.Min(count, 3_000_000));
            for (uint i = 0; i < count; i++)
            {
                int off = 84 + 50 * (int)i;
                var v = new Vector3[3];
                for (int k = 0; k < 3; k++)
                {
                    float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + 12 + k * 12));
                    float y = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + 16 + k * 12));
                    float z = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + 20 + k * 12));
                    v[k] = new Vector3(x, y, z);
                }
                AddTriangle(mesh, vertexMap, v[0], v[1], v[2]);
            }
        }
        else
        {
            using var sr = new StreamReader(path, Encoding.ASCII);
            string? line;
            var stack = new List<Vector3>();
            var vertexMap = new Dictionary<(float, float, float), int>();
            while ((line = sr.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    {
                        stack.Add(new Vector3(x, y, z));
                    }
                }
                else if (line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
                {
                    if (stack.Count == 3)
                        AddTriangle(mesh, vertexMap, stack[0], stack[1], stack[2]);
                    else if (stack.Count > 0)
                        notes.Add("یک facet با تعداد رأس غیر۳ نادیده گرفته شد.");
                    stack.Clear();
                }
            }
        }
        return mesh;
    }

    // ---------- OBJ ----------

    private static TriMesh ImportObj(string path, List<string> notes)
    {
        var verts = new List<Vector3>();
        var faces = new List<int[]>();
        using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 &&
                    double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    verts.Add(new Vector3(x, y, z));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var idx = new List<int>(p.Length - 1);
                for (int i = 1; i < p.Length; i++)
                {
                    var tok = p[i].Split('/')[0];
                    if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        continue;
                    int v = n > 0 ? n - 1 : verts.Count + n; // اندیس نسبی/مطلق
                    if (v >= 0 && v < verts.Count)
                        idx.Add(v);
                }
                if (idx.Count >= 3)
                    faces.Add(idx.ToArray());
                else
                    notes.Add("یک face با کمتر از ۳ رأس معتبر نادیده گرفته شد.");
            }
        }

        var mesh = new TriMesh();
        foreach (var v in verts)
            mesh.AddVertex(v);
        foreach (var f in faces)
        {
            for (int i = 1; i + 1 < f.Length; i++) // fan-triangulate برای faceهای n>3
                mesh.AddTriangle(f[0], f[i], f[i + 1]);
        }
        if (faces.Any(f => f.Length > 3))
            notes.Add("faceهای چندضلعی به بادبزک مثلث تبدیل شدند.");
        return mesh;
    }

    // ---------- PLY ----------

    private static TriMesh ImportPly(string path, List<string> notes)
    {
        var bytes = File.ReadAllBytes(path);

        // سرآیند ascii تا "end_header"
        int headerEnd = -1;
        int cursor = 0;
        string? format = null;
        int vertexCount = 0, faceCount = 0;
        string currentElement = "";
        int xOff = 0, yOff = 4, zOff = 8, stride = 12;

        while (cursor < bytes.Length)
        {
            int lineEnd = Array.IndexOf(bytes, (byte)'\n', cursor);
            if (lineEnd < 0)
                lineEnd = bytes.Length;
            int len = lineEnd - cursor;
            if (len > 0 && bytes[lineEnd - 1] == (byte)'\r')
                len--;
            string line = Encoding.ASCII.GetString(bytes, cursor, len).Trim();
            cursor = lineEnd + 1;

            if (line.StartsWith("format ", StringComparison.Ordinal))
                format = line[7..].Trim();
            else if (line.StartsWith("element vertex ", StringComparison.Ordinal))
            {
                currentElement = "vertex";
                int.TryParse(line[15..], NumberStyles.Integer, CultureInfo.InvariantCulture, out vertexCount);
            }
            else if (line.StartsWith("element face ", StringComparison.Ordinal))
            {
                currentElement = "face";
                int.TryParse(line[13..], NumberStyles.Integer, CultureInfo.InvariantCulture, out faceCount);
            }
            else if (line.StartsWith("element ", StringComparison.Ordinal))
                currentElement = line[8..].Trim();
            else if (line.StartsWith("property ", StringComparison.Ordinal) && currentElement == "vertex")
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    // property <type> <name> — فقط نوع‌های ساده پشتیبانی می‌شوند
                    int size = parts[1] switch
                    {
                        "float" or "int32" or "uint32" => 4,
                        "double" or "int64" or "uint64" => 8,
                        "short" or "ushort" => 2,
                        "char" or "uchar" => 1,
                        _ => 4,
                    };
                    string name = parts[^1];
                    if (name == "x") xOff = stride;
                    else if (name == "y") yOff = stride;
                    else if (name == "z") zOff = stride;
                    stride += size;
                }
            }
            else if (line == "end_header")
            {
                headerEnd = cursor;
                break;
            }
        }
        if (headerEnd < 0)
            throw new InvalidOperationException("PLY: سرآیند end_header ندارد.");
        if (format is null)
            throw new InvalidOperationException("PLY: خط format یافت نشد.");
        if (vertexCount < 0 || faceCount < 0)
            throw new InvalidOperationException("PLY: تعداد عناصر نامعتبر.");

        var mesh = new TriMesh();
        if (format.StartsWith("ascii", StringComparison.Ordinal))
        {
            using var ms = new MemoryStream(bytes, headerEnd, bytes.Length - headerEnd, writable: false);
            using var sr = new StreamReader(ms, Encoding.ASCII);
            var verts = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                string? line = sr.ReadLine();
                if (line is null)
                    throw new InvalidOperationException("PLY ascii: رأس‌ها ناقص‌اند.");
                var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 3 &&
                    double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    verts[i] = new Vector3(x, y, z);
            }
            foreach (var v in verts)
                mesh.AddVertex(v);

            for (int i = 0; i < faceCount; i++)
            {
                string? line = sr.ReadLine();
                if (line is null)
                    break;
                var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4 || !int.TryParse(p[0], out var n) || n < 3)
                {
                    notes.Add("یک face نامعتبر نادیده گرفته شد.");
                    continue;
                }
                var idx = new int[n];
                bool ok = true;
                for (int k = 0; k < n; k++)
                {
                    if (!int.TryParse(p[k + 1], out idx[k]) || idx[k] < 0 || idx[k] >= vertexCount)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                {
                    notes.Add("یک face با اندیس خارج از محدوده نادیده گرفته شد.");
                    continue;
                }
                for (int k = 1; k + 1 < n; k++)
                    mesh.AddTriangle(idx[0], idx[k], idx[k + 1]);
            }
        }
        else if (format.Contains("binary_little_endian", StringComparison.Ordinal))
        {
            // چیدمان رأس از سرآیند (آفست x/y/z و گام کل) خوانده می‌شود
            var verts = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int off = headerEnd + i * stride;
                if (off + stride > bytes.Length)
                    throw new InvalidOperationException("PLY باینری: رأس‌ها ناقص‌اند.");
                float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + xOff));
                float y = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + yOff));
                float z = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(off + zOff));
                verts[i] = new Vector3(x, y, z);
            }
            foreach (var v in verts)
                mesh.AddVertex(v);

            int off2 = headerEnd + vertexCount * stride;
            for (int i = 0; i < faceCount; i++)
            {
                if (off2 >= bytes.Length)
                    break;
                byte n = bytes[off2++];
                if (n < 3)
                {
                    off2 += n * 4;
                    continue;
                }
                var idx = new int[n];
                for (int k = 0; k < n; k++)
                {
                    if (off2 + 4 > bytes.Length)
                        throw new InvalidOperationException("PLY باینری: faceها ناقص‌اند.");
                    idx[k] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off2));
                    off2 += 4;
                }
                for (int k = 1; k + 1 < n; k++)
                {
                    if (idx[0] >= 0 && idx[0] < vertexCount && idx[k] >= 0 && idx[k] < vertexCount && idx[k + 1] >= 0 && idx[k + 1] < vertexCount)
                        mesh.AddTriangle(idx[0], idx[k], idx[k + 1]);
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"PLY: قالب «{format}» (مثلاً big_endian) پشتیبانی نمی‌شود.");
        }
        return mesh;
    }

    private static void AddTriangle(TriMesh mesh, Dictionary<(float, float, float), int> map, Vector3 a, Vector3 b, Vector3 c)
    {
        if (!AllFinite(a) || !AllFinite(b) || !AllFinite(c))
            return;
        mesh.AddTriangle(GetVertex(mesh, map, a), GetVertex(mesh, map, b), GetVertex(mesh, map, c));

        static int GetVertex(TriMesh mesh, Dictionary<(float, float, float), int> map, Vector3 v)
        {
            var key = ((float)v.X, (float)v.Y, (float)v.Z);
            if (map.TryGetValue(key, out var idx))
                return idx;
            idx = mesh.AddVertex(v);
            map[key] = idx;
            return idx;
        }

        static bool AllFinite(Vector3 v)
            => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    }
}
