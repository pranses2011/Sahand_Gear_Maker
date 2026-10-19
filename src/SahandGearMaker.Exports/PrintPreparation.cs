using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Exports;

/// <summary>گزینه‌های بهینه‌سازی برای چاپ سه‌بعدی — نسخهٔ ۰٫۵ (ایراد ۱۸).</summary>
public sealed class PrintOptimizationOptions
{
    /// <summary>بهینه‌سازی فعال باشد؟</summary>
    public bool Enabled { get; set; }

    /// <summary>قطعه روی پهنه بخوابد (محور عمودی — پیش‌فرض؛ مناسب دقت شعاعی بور).</summary>
    public bool LayFlatVertical { get; set; } = true;

    /// <summary>محور افقی شود (دندانه‌ها محکم‌تر لایه‌به‌لایه چاپ می‌شوند).</summary>
    public bool LayFlatHorizontal { get; set; }

    /// <summary>چسباندن رئوس تکراری با تلرانس (mm) — 0 = بدون جوش.</summary>
    public double WeldToleranceMm { get; set; } = 0.001;

    /// <summary>حذف مثلث‌های یکنواخت (مساحت ~ صفر).</summary>
    public bool RemoveDegenerate { get; set; } = true;

    /// <summary>چگالی ماده برای برآورد جرم (g/cm³) — پیش‌فرض PLA.</summary>
    public double DensityGCm3 { get; set; } = 1.24;
}

/// <summary>گزارش بهینه‌سازی چاپ.</summary>
public sealed record PrintOptimizationReport
{
    public bool Watertight { get; init; }
    public bool OutwardNormals { get; init; }
    public int TrianglesBefore { get; init; }
    public int TrianglesAfter { get; init; }
    public int DegenerateRemoved { get; init; }
    public int VerticesWelded { get; init; }
    public double VolumeMm3 { get; init; }
    public double MassGrams { get; init; }
    public (double X, double Y, double Z) SizeMm { get; init; }
    public string OrientationNote { get; init; } = "";
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// آماده‌ساز مش برای چاپ سه‌بعدی (ایراد ۱۸):
/// جهت‌گیری بهینه (پهلو/طاقت)، جوش رئوس، حذف یکنواخت‌ها، تأیید آب‌بندی/جهت نرمال،
/// و گزارش حجم/جرم/ابعاد برای اسلایسر.
/// </summary>
public static class PrintPreparation
{
    /// <summary>اعمال بهینه‌سازی روی مش (درجا تغییر جهت می‌دهد؛ خروجی مش آمادهٔ چاپ).</summary>
    public static PrintOptimizationReport Prepare(TriMesh mesh, PrintOptimizationOptions opt)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(opt);

        int trisBefore = mesh.TriangleCount;
        var warnings = new List<string>();

        // ---- جهت‌گیری ----
        string orientationNote = "بدون چرخش (همان جهت مدل)";
        if (opt.Enabled && opt.LayFlatHorizontal)
        {
            RotateAboutX(mesh, Math.PI / 2.0); // محور Z → افقی؛ سطح مقطع عمودی می‌شود
            orientationNote = "محور افقی شد — دندانه‌ها در راستای لایه‌ها محکم‌تر چاپ می‌شوند (دقت شعاعی بور کمی افت می‌کند)";
        }
        else if (opt.Enabled && opt.LayFlatVertical)
        {
            orientationNote = "محور عمودی (سطح تخت روی میز) — بور و دندانه‌ها دقیق‌ترین شعاع را دارند";
        }

        // ---- حذف یکنواخت‌ها ----
        int degenerateRemoved = 0;
        if (opt.Enabled && opt.RemoveDegenerate)
            degenerateRemoved = RemoveDegenerateTriangles(mesh);

        // ---- جوش رئوس ----
        int welded = 0;
        if (opt.Enabled && opt.WeldToleranceMm > 0)
            welded = WeldVertices(mesh, opt.WeldToleranceMm);

        // ---- اعتبارسنجی ----
        var report = mesh.Validate();
        bool watertight = report.ClosedManifold;
        double volume = report.SignedVolumeMm3;

        // جهت نرمال: حجم امضادار مثبت = رو به بیرون
        double signed = SignedVolume(mesh);
        bool outward = signed > 0;
        if (opt.Enabled && !outward && watertight)
        {
            FlipAll(mesh);
            outward = true;
            volume = -signed;
        }

        var (min, max) = mesh.Bounds();
        double mass = volume / 1000.0 * opt.DensityGCm3;

        if (!watertight)
            warnings.Add("مش آب‌بند نیست — اسلایسر ممکن است حفره یا پرشدگی ناقص بسازد.");
        if (trisBefore > 400_000)
            warnings.Add("مش سنگین است؛ برای چاپ، کیفیت Standard کافی است (فایل سبک‌تر و چاپ یکسان).");

        return new PrintOptimizationReport
        {
            Watertight = watertight,
            OutwardNormals = outward,
            TrianglesBefore = trisBefore,
            TrianglesAfter = mesh.TriangleCount,
            DegenerateRemoved = degenerateRemoved,
            VerticesWelded = welded,
            VolumeMm3 = volume,
            MassGrams = mass,
            SizeMm = (max.X - min.X, max.Y - min.Y, max.Z - min.Z),
            OrientationNote = orientationNote,
            Warnings = warnings,
        };
    }

    private static void RotateAboutX(TriMesh mesh, double angle)
    {
        // چرخش رئوس حول X: (y,z) → (y·cosθ − z·sinθ, y·sinθ + z·cosθ)
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.VertexAt(i);
            double y = v.Y * cos - v.Z * sin;
            double z = v.Y * sin + v.Z * cos;
            mesh.SetVertex(i, new Vector3(v.X, y, z));
        }
    }

    private static int RemoveDegenerateTriangles(TriMesh mesh)
    {
        var verts = mesh.Vertices.ToArray();
        var keep = new List<Triangle>(mesh.TriangleCount);
        int removed = 0;
        foreach (var t in mesh.Triangles)
        {
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            if (Vector3.Cross(b - a, c - a).Length < 1e-10)
            {
                removed++;
                continue;
            }
            keep.Add(t);
        }
        if (removed > 0)
            mesh.ReplaceTriangles(keep);
        return removed;
    }

    private static int WeldVertices(TriMesh mesh, double tol)
    {
        // جوش رئوس هم‌مکان با شبکهٔ مکعبی؛ بازنگاشت ایندکس‌ها
        var cell = new Dictionary<(long, long, long), int>(mesh.VertexCount);
        var remap = new int[mesh.VertexCount];
        var newPositions = new List<Vector3>(mesh.VertexCount);
        double inv = 1.0 / Math.Max(tol, 1e-6);

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.VertexAt(i);
            var key = ((long)Math.Floor(v.X * inv), (long)Math.Floor(v.Y * inv), (long)Math.Floor(v.Z * inv));
            if (!cell.TryGetValue(key, out int target))
            {
                target = newPositions.Count;
                newPositions.Add(v);
                cell[key] = target;
            }
            remap[i] = target;
        }

        int weldedCount = mesh.VertexCount - newPositions.Count;
        if (weldedCount <= 0)
            return 0;

        mesh.RebuildWith(newPositions, remap);
        return weldedCount;
    }

    private static double SignedVolume(TriMesh mesh)
    {
        var verts = mesh.Vertices;
        double volume = 0;
        foreach (var t in mesh.Triangles)
        {
            var a = verts[t.A]; var b = verts[t.B]; var c = verts[t.C];
            volume += (a.X * (b.Y * c.Z - c.Y * b.Z) - a.Y * (b.X * c.Z - c.X * b.Z) + a.Z * (b.X * c.Y - c.X * b.Y)) / 6.0;
        }
        return volume;
    }

    private static void FlipAll(TriMesh mesh)
    {
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            var t = mesh.Triangles[i];
            mesh.SetTriangle(i, t.A, t.C, t.B);
        }
    }
}
