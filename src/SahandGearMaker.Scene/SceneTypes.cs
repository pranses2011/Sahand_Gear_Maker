using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Scene;

/// <summary>رنگ RGB با مؤلفه‌های ۰..۲۵۵ — مستقل از WPF تا در آزمون‌ها هم قابل‌استفاده باشد.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        // پشتیبانی از سه‌رقمی (#F59 = #FF5599) و هشت‌رقمی ARGB (#FFF59E0B → کانال آلفا دور ریخته می‌شود)
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length == 6)
        {
            return new Rgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        return new Rgb(0x25, 0x63, 0xEB);
    }

    /// <summary>روشن‌کردن/تیره‌کردن برای هایلایت — k=1 بدون تغییر، k&gt;1 روشن‌تر.</summary>
    public Rgb Scaled(double k) => new(
        (byte)Math.Clamp(R * k, 0, 255),
        (byte)Math.Clamp(G * k, 0, 255),
        (byte)Math.Clamp(B * k, 0, 255));
}

/// <summary>تبدیل شیئ: جابه‌جایی + چرخش حول Z + مقیاس یکنواخت (فاز ۱C — کنترل‌های شیئ).</summary>
public readonly record struct ObjectTransform(
    Vector3 Position,
    double RotationZRad,
    double Scale = 1.0,
    double RotationXRad = 0.0,
    double RotationYRad = 0.0)
{
    public static readonly ObjectTransform Identity = new(Vector3.Zero, 0.0, 1.0);

    /// <summary>
    /// اعمال تبدیل: جابه‌جایی ∘ چرخش X ∘ چرخش Y ∘ چرخش Z ∘ مقیاس.
    /// ترتیب عمدی است: RotationZ همیشه «اسپینِ قطعه حول محور خودش» است و Rx/Ry جهت محور را
    /// تنظیم می‌کنند (مونتاژ کرم/مخروطی — نسخهٔ ۰٫۵).
    /// </summary>
    public Vector3 Apply(Vector3 p)
    {
        // مقیاس
        double x = p.X * Scale, y = p.Y * Scale, z = p.Z * Scale;
        // Rz (اسپین محلی)
        double cz = Math.Cos(RotationZRad), sz = Math.Sin(RotationZRad);
        (x, y) = (x * cz - y * sz, x * sz + y * cz);
        // Ry (جهت محور — مثلاً Z→X برای چرخ کرم)
        double cy = Math.Cos(RotationYRad), sy = Math.Sin(RotationYRad);
        (x, z) = (x * cy + z * sy, -x * sy + z * cy);
        // Rx
        double cx = Math.Cos(RotationXRad), sx = Math.Sin(RotationXRad);
        (y, z) = (y * cx - z * sx, y * sx + z * cx);
        return new Vector3(Position.X + x, Position.Y + y, Position.Z + z);
    }

    /// <summary>نرمال تحت تبدیل — چرخش جهت؛ مقیاس یکنواخت جهت را تغییر نمی‌دهد.</summary>
    public Vector3 ApplyNormal(Vector3 n)
    {
        double x = n.X, y = n.Y, z = n.Z;
        double cz = Math.Cos(RotationZRad), sz = Math.Sin(RotationZRad);
        (x, y) = (x * cz - y * sz, x * sz + y * cz);
        double cy = Math.Cos(RotationYRad), sy = Math.Sin(RotationYRad);
        (x, z) = (x * cy + z * sy, -x * sy + z * cy);
        double cx = Math.Cos(RotationXRad), sx = Math.Sin(RotationXRad);
        (y, z) = (y * cx - z * sx, y * sx + z * cx);
        return new Vector3(x, y, z);
    }

    public (Vector3 Min, Vector3 Max) TransformBounds((Vector3 Min, Vector3 Max) b)
    {
        // هر ۸ گوشهٔ جعبه تبدیل می‌شود — با چرخش‌های سه‌محوره، گوشه‌های کم‌شمار ناکافی‌اند
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = Apply(new Vector3(
                (i & 1) == 0 ? b.Min.X : b.Max.X,
                (i & 2) == 0 ? b.Min.Y : b.Max.Y,
                (i & 4) == 0 ? b.Min.Z : b.Max.Z));
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
        }
        return (new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }
}

/// <summary>حالت‌های نمایش بوم (سند ۰۲، بخش ۶٫۳ + فاز ۱٫۱۲).</summary>
public enum DisplayMode
{
    Shaded,
    Wireframe,
    ShadedWithEdges,
}

/// <summary>نوع تصویر — پرسپکتیو یا ارتوگرافیک (فاز ۱٫۱۷).</summary>
public enum ProjectionKind
{
    Perspective,
    Orthographic,
}

/// <summary>یک شیئ قابل‌رندر در صحنه — معمولاً نگاشت یک‌به‌یک با GearPart.</summary>
public sealed class SceneObject
{
    public required string Id { get; init; }
    public string Name { get; set; } = string.Empty;

    /// <summary>مش مثلثی — با هر بازسازی هندسه می‌تواند تعویض شود؛ درون رشتهٔ UI.</summary>
    public required TriMesh Mesh { get; set; }

    /// <summary>نرمال رأس‌های پیش‌محاسبه‌شده (فاز ۱٫۶) — با مش هم‌طول؛ null = هنوز محاسبه نشده.</summary>
    public Vector3[]? VertexNormals { get; set; }

    public ObjectTransform Transform { get; set; } = ObjectTransform.Identity;
    public Rgb Color { get; set; } = new(0x25, 0x63, 0xEB);

    /// <summary>کدری نمایشی: 1 = مات؛ کمتر = شفاف (فاز ۱٫۲۲).</summary>
    public double Opacity { get; set; } = 1.0;

    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }

    /// <summary>مدل مرجع واردشده (STL/OBJ/PLY) — به‌صورت شبح شفاف رسم می‌شود (فاز ۲٫۳).</summary>
    public bool IsReference { get; init; }

    public (Vector3 Min, Vector3 Max) LocalBounds { get; set; }

    public Vector3[] EnsureVertexNormals()
    {
        if (VertexNormals is { } n && n.Length == Mesh.VertexCount) return n;
        var normals = new Vector3[Mesh.VertexCount];
        var verts = Mesh.Vertices;
        foreach (var t in Mesh.Triangles)
        {
            var fn = t.Normal(verts);
            normals[t.A] += fn;
            normals[t.B] += fn;
            normals[t.C] += fn;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            var L = normals[i].Length;
            normals[i] = L > 1e-12 ? normals[i] / L : Vector3.UnitZ;
        }
        VertexNormals = normals;
        return normals;
    }

    public (Vector3 Min, Vector3 Max) WorldBounds => Transform.TransformBounds(LocalBounds);
}

/// <summary>گزینه‌های یک فریم رندر.</summary>
public sealed record RenderOptions
{
    public DisplayMode Mode { get; init; } = DisplayMode.ShadedWithEdges;
    public bool ShowGrid { get; init; } = true;
    public bool ShowAxes { get; init; } = true;

    /// <summary>شناسهٔ شیئ هایلایت‌شده (انتخاب با کلیک — فاز ۱٫۱۹).</summary>
    public string? HighlightId { get; init; }

    /// <summary>رنگ انتخاب/هایلایت.</summary>
    public Rgb HighlightColor { get; init; } = new(0xF5, 0x9E, 0x0B);

    /// <summary>صفحهٔ برش نمایشی (mm) — رأس‌های با Z بزرگ‌تر بریده می‌شوند؛ +∞ = خاموش (فاز ۴٫۲).</summary>
    public double ClipZ { get; init; } = double.PositiveInfinity;

    /// <summary>رنگ پس‌زمینه.</summary>
    public Rgb Background { get; init; } = new(0xEA, 0xEE, 0xF4);

    /// <summary>اندازهٔ گام گرید مرجع (mm)؛ صفر = خودکار از ابعاد صحنه.</summary>
    public double GridStepMm { get; init; }

    /// <summary>پاره‌خط‌های اندازه‌گیری فاز ۴٫۱ — دو نقطه = فاصله؛ سه نقطه = زاویه.</summary>
    public IReadOnlyList<(MathX.Vector3 A, MathX.Vector3 B)>? MeasureLines { get; init; }

    /// <summary>برچسب‌های عددی کنار پاره‌خط‌های اندازه‌گیری.</summary>
    public IReadOnlyList<(MathX.Vector3 At, string Label)>? MeasureLabels { get; init; }
}

/// <summary>آمار یک فریم — برای HUD بوم (فاز ۱٫۸).</summary>
public sealed record RenderStats
{
    public int TrianglesDrawn { get; init; }
    public int ObjectsDrawn { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Supersample { get; init; }
    public TimeSpan Elapsed { get; init; }
    public double Fps { get; init; }
}
