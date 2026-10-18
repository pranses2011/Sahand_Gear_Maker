using System.Runtime.InteropServices;

namespace SahandGearMaker.MathX;

/// <summary>بردار سه‌بعدی سبک برای موتور هندسی و رندرر.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vector3(double x, double y, double z)
{
    public readonly double X = x;
    public readonly double Y = y;
    public readonly double Z = z;

    public static readonly Vector3 Zero = new(0, 0, 0);
    public static readonly Vector3 UnitX = new(1, 0, 0);
    public static readonly Vector3 UnitY = new(0, 1, 0);
    public static readonly Vector3 UnitZ = new(0, 0, 1);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3 operator *(Vector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3 operator *(double s, Vector3 a) => a * s;
    public static Vector3 operator /(Vector3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vector3 Normalized()
    {
        double len = Length;
        return len < 1e-300 ? Zero : this / len;
    }

    /// <summary>چرخش حول محور Z با زاویهٔ داده‌شده (رادیان) — برای پیچش مارپیچ.</summary>
    public Vector3 RotateZ(double angleRad)
    {
        double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
        return new Vector3(X * c - Y * s, X * s + Y * c, Z);
    }

    public static Vector3 Lerp(Vector3 a, Vector3 b, double t) => a + (b - a) * t;

    public double DistanceTo(Vector3 other) => (this - other).Length;

    public override string ToString() => FormattableString.Invariant($"({X:G6}, {Y:G6}, {Z:G6})");
}

/// <summary>مثلث با ایندکس رأس؛ جهت‌گیری پادساعتگرد = نرمال رو به بیرون.</summary>
public readonly struct Triangle(int a, int b, int c)
{
    public readonly int A = a;
    public readonly int B = b;
    public readonly int C = c;

    /// <summary>نرمال واحد مثلث (قانون دست راست).</summary>
    public Vector3 Normal(ReadOnlySpan<Vector3> vertices)
    {
        var e1 = vertices[B] - vertices[A];
        var e2 = vertices[C] - vertices[A];
        return Vector3.Cross(e1, e2).Normalized();
    }

    public double Area(ReadOnlySpan<Vector3> vertices)
        => Vector3.Cross(vertices[B] - vertices[A], vertices[C] - vertices[A]).Length / 2.0;
}
