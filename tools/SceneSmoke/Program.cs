using System.IO.Compression;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;

// آزمون بصری رندرر جدید روی لینوکس — خروجی PNG (بدون WPF)
const int W = 1000, H = 760;

var pinion = new GearPart { Name = "p", Family = GearFamily.ExternalSpur,
    Teeth = new ToothParameters { Module = 2.5, ToothCount = 20, FaceWidth = 16 },
    Body = new BodyFeatures { BoreDiameter = 10 } };
var wheel = new GearPart { Name = "w", Family = GearFamily.ExternalHelical,
    Teeth = new ToothParameters { Module = 2.5, ToothCount = 36, FaceWidth = 16, HelixAngle = UnitConverter.DegToRad(22) },
    Body = new BodyFeatures { BoreDiameter = 16 } };

var bp = GearGeometryEngine.BuildPart(pinion);
var bw = GearGeometryEngine.BuildPart(wheel);
double rp = 2.5 * 20 / 2.0, rw = 2.5 * 36 / 2.0;

var op = MakeObj("pinion", bp.Mesh, "#2563EB", -rp);
var ow = MakeObj("wheel", bw.Mesh, "#0D9488", rw);

var cam = new SceneCamera();
var bounds = SceneRenderer.CombinedBounds(new[] { op, ow });
double size = Math.Max(bounds.Max.X - bounds.Min.X, Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z));
cam.SetView(UnitConverter.DegToRad(38), UnitConverter.DegToRad(26), size * 2.4, Vector3.Zero, animate: false);

var renderer = new SceneRenderer();
renderer.Resize(W, H, 2);
var frame = cam.ComputeFrame(W, H);
var stats = renderer.Render(new[] { op, ow }, frame, new RenderOptions
{
    Mode = DisplayMode.ShadedWithEdges,
    ShowGrid = true,
    ShowAxes = true,
    HighlightId = "pinion",
}, 60);

Console.WriteLine($"tris={stats.TrianglesDrawn:N0} objs={stats.ObjectsDrawn} ms={stats.Elapsed.TotalMilliseconds:F0} outBytes={renderer.Output.Length}");

// تبدیل BGRA → ردیف‌های RGB با بایت فیلتر
var rgbRows = new byte[(W * 3 + 1) * H];
var px = renderer.Output.ToArray();
for (int y = 0; y < H; y++)
{
    int rowStart = y * (W * 3 + 1);
    rgbRows[rowStart] = 0;
    for (int x = 0; x < W; x++)
    {
        int src = (y * W + x) * 4;
        rgbRows[rowStart + 1 + x * 3] = px[src + 2];     // R
        rgbRows[rowStart + 1 + x * 3 + 1] = px[src + 1]; // G
        rgbRows[rowStart + 1 + x * 3 + 2] = px[src];     // B
    }
}

byte[] compressed;
using (var ms = new MemoryStream())
{
    ms.WriteByte(0x78); // zlib header (CM=8, CINFO=7)
    ms.WriteByte(0x9C);
    using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        ds.Write(rgbRows, 0, rgbRows.Length);
    // Adler-32
    uint a = 1, b2 = 0;
    foreach (var x in rgbRows) { a = (a + x) % 65521; b2 = (b2 + a) % 65521; }
    uint adler = (b2 << 16) | a;
    ms.Write(BitConverter.GetBytes(adler).Reverse().ToArray(), 0, 4);
    compressed = ms.ToArray();
}

WritePng("/home/z/my-project/download/scene-smoke.png", W, H, compressed);
Console.WriteLine("PNG نوشته شد: /home/z/my-project/download/scene-smoke.png");

static SceneObject MakeObj(string id, TriMesh mesh, string hex, double x)
{
    var o = new SceneObject { Id = id, Name = id, Mesh = mesh, LocalBounds = mesh.Bounds(), Color = Rgb.FromHex(hex),
        Transform = new ObjectTransform(new Vector3(x, 0, 0), 0, 1) };
    o.EnsureVertexNormals();
    return o;
}

static void WritePng(string path, int w, int h, byte[] compressedIdat)
{
    using var fs = new FileStream(path, FileMode.Create);
    void Chunk(string type, byte[] data)
    {
        fs.Write(BitConverter.GetBytes((uint)data.Length).Reverse().ToArray(), 0, 4);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        fs.Write(typeBytes, 0, 4);
        fs.Write(data, 0, data.Length);
        uint crc = Crc32(typeBytes, data);
        fs.Write(BitConverter.GetBytes(crc).Reverse().ToArray(), 0, 4);
    }
    fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
    var ihdr = new byte[13];
    BitConverter.GetBytes((uint)w).Reverse().ToArray().CopyTo(ihdr, 0);
    BitConverter.GetBytes((uint)h).Reverse().ToArray().CopyTo(ihdr, 4);
    ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
    Chunk("IHDR", ihdr);
    Chunk("IDAT", compressedIdat);
    Chunk("IEND", Array.Empty<byte>());
}

static uint Crc32(byte[] a, byte[] b)
{
    uint[] table = new uint[256];
    for (uint n = 0; n < 256; n++)
    {
        uint c = n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        table[n] = c;
    }
    uint crc = 0xFFFFFFFFu;
    foreach (var x in a) crc = table[(crc ^ x) & 0xFF] ^ (crc >> 8);
    foreach (var x in b) crc = table[(crc ^ x) & 0xFF] ^ (crc >> 8);
    return crc ^ 0xFFFFFFFFu;
}
