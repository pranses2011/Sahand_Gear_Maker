using System.CommandLine;
using SahandGearMaker.Assembly;
using SahandGearMaker.Domain;
using SahandGearMaker.Drawing;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using SahandGearMaker.Persistence;

namespace SahandGearMaker.Cli;

/// <summary>
/// رابط خط فرمان sgm-cli — خودکارسازی آفلاین ساخت/خروجی/نقشه بدون UI (سند ۰۷، P3-API).
/// امنیت: هیچ کد افزونه/اسکریپت خارجی اجرا نمی‌شود؛ فقط پارامترهای صریح.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var root = new RootCommand("ابزار خط فرمان سازندهٔ چرخ‌دندهٔ سحند — آفلاین و بدون telemetry");

        // ---------- info ----------
        var infoCmd = new Command("info", "نمایش وضعیت قابلیت‌ها و خانواده‌ها");
        infoCmd.SetHandler(() =>
        {
            Console.WriteLine("خانواده‌های هندسی:");
            foreach (var f in CapabilityCatalog.GearFamilies)
                Console.WriteLine($"  [{StatusMark(f.Status)}] {f.PersianTitle} ({f.EnglishTitle}) — {f.Note}");
            Console.WriteLine();
            Console.WriteLine("فرمت‌های خروجی:");
            foreach (var f in CapabilityCatalog.ExportFormats)
                Console.WriteLine($"  [{StatusMark(f.Status)}] {f.EnglishTitle} — {f.Note}");
        });

        // ---------- new ----------
        var familyOpt = new Option<string>("--family", "خانواده (نام انگلیسی enum، مثل ExternalSpur)") { IsRequired = true };
        var moduleOpt = new Option<double>("--module", () => 2.0, "ماژول (mm)");
        var teethOpt = new Option<int>("--teeth", () => 20, "تعداد دندانه");
        var widthOpt = new Option<double>("--width", () => 20, "پهنای دندانه (mm)");
        var helixOpt = new Option<double>("--helix", () => 0, "زاویهٔ مارپیچ (درجه)");
        var boreOpt = new Option<double>("--bore", () => 10, "قطر بور (mm)");
        var nameOpt = new Option<string>("--name", () => "قطعه", "نام قطعه");
        var outOpt = new Option<string>("--out", "مسیر فایل پروژهٔ خروجی .sgdproj") { IsRequired = true };

        var newCmd = new Command("new", "ساخت پروژهٔ جدید تک‌قطعه‌ای") { familyOpt, moduleOpt, teethOpt, widthOpt, helixOpt, boreOpt, nameOpt, outOpt };
        newCmd.SetHandler((family, module, teeth, width, helix, bore, name, outfile) =>
        {
            if (!Enum.TryParse<GearFamily>(family, true, out var fam))
            {
                Console.Error.WriteLine($"خانوادهٔ ناشناخته: {family} — خانواده‌های معتبر را با «info» ببینید.");
                Environment.ExitCode = 1;
                return;
            }
            var part = new GearPart
            {
                Name = name,
                Family = fam,
                Teeth = new ToothParameters
                {
                    Module = module,
                    ToothCount = teeth,
                    FaceWidth = width,
                    HelixAngle = UnitConverter.DegToRad(helix),
                },
                Body = new BodyFeatures { BoreDiameter = bore },
            };
            var project = new GearProject { Name = name };
            project.Parts.Add(part);
            ProjectFile.Save(project, outfile);
            Console.WriteLine($"پروژه ذخیره شد: {outfile} ({part.Name} / {fam})");
            Environment.ExitCode = 0;
        }, familyOpt, moduleOpt, teethOpt, widthOpt, helixOpt, boreOpt, nameOpt, outOpt);

        // ---------- export ----------
        var projArg = new Argument<string>("پروژه", "مسیر فایل .sgdproj");
        var formatOpt = new Option<string>("--format", () => "stl", "قالب: stl|stla|obj|ply|glb|3mf|amf");
        var dirOpt = new Option<string>("--outdir", () => ".", "پوشهٔ خروجی");
        var tolOpt = new Option<double>("--tol", () => 0.02, "تلرانس وتر (mm)");

        var exportCmd = new Command("export", "خروجی همهٔ قطعات پروژه") { projArg, formatOpt, dirOpt, tolOpt };
        exportCmd.SetHandler((projPath, format, outdir, tol) =>
        {
            if (!File.Exists(projPath))
            {
                Console.Error.WriteLine($"فایل یافت نشد: {projPath}");
                Environment.ExitCode = 1;
                return;
            }
            var project = ProjectFile.Load(projPath);
            var items = new List<(string, TriMesh)>();
            foreach (var part in project.Parts)
            {
                var result = GearGeometryEngine.BuildPart(part);
                items.Add((Sanitize(part.Name), result.Mesh));
            }

            var fmt = format.ToLowerInvariant() switch
            {
                "stl" => ExportFileFormat.StlBinary,
                "stla" => ExportFileFormat.StlAscii,
                "obj" => ExportFileFormat.Obj,
                "ply" => ExportFileFormat.Ply,
                "glb" => ExportFileFormat.Glb,
                "3mf" => ExportFileFormat.ThreeMf,
                "amf" => ExportFileFormat.Amf,
                _ => (ExportFileFormat?)null,
            };
            if (fmt is null)
            {
                Console.Error.WriteLine($"قالب نامعتبر: {format} (معتبر: stl|stla|obj|ply|glb|3mf|amf)");
                Environment.ExitCode = 1;
                return;
            }

            var batch = BatchExporter.ExportAll(items, outdir, fmt.Value, new MeshExportSettings
            {
                CustomChordalToleranceMm = tol,
            });
            foreach (var r in batch.Items)
                Console.WriteLine(r.Success
                    ? $"  [موفق] {r.FilePath} ({r.BytesWritten:N0} بایت)"
                    : $"  [ناموفق] {r.FilePath}: {r.Error}");
            Console.WriteLine($"خلاصه: {batch.Succeeded} موفق، {batch.Failed} ناموفق");
            Environment.ExitCode = batch.Failed == 0 ? 0 : 2;
        }, projArg, formatOpt, dirOpt, tolOpt);

        // ---------- drawing ----------
        var drawCmd = new Command("drawing", "نقشهٔ فنی SVG/PDF قطعهٔ اول پروژه") { projArg, dirOpt };
        drawCmd.SetHandler((projPath, outdir) =>
        {
            if (!File.Exists(projPath))
            {
                Console.Error.WriteLine($"فایل یافت نشد: {projPath}");
                Environment.ExitCode = 1;
                return;
            }
            var project = ProjectFile.Load(projPath);
            var part = project.Parts.FirstOrDefault();
            if (part is null)
            {
                Console.Error.WriteLine("پروژه قطعه‌ای ندارد.");
                Environment.ExitCode = 1;
                return;
            }
            var geometry = GearGeometryEngine.BuildPart(part);
            var doc = TechnicalDrawingBuilder.Build(part, geometry, project.Name);
            Directory.CreateDirectory(outdir);
            string baseName = Path.Combine(outdir, Sanitize(part.Name));
            SvgDrawingWriter.WriteToFile(doc, baseName + ".svg");
            using (var fs = new FileStream(baseName + ".pdf", FileMode.Create))
                PdfDrawingWriter.Write(fs, doc);
            Console.WriteLine($"نقشه‌ها نوشته شد: {baseName}.svg و {baseName}.pdf");
            Environment.ExitCode = 0;
        }, projArg, dirOpt);

        // ---------- analyze ----------
        var analyzeCmd = new Command("analyze", "تحلیل هندسی/جفت قطعات پروژه") { projArg };
        analyzeCmd.SetHandler((projPath) =>
        {
            if (!File.Exists(projPath))
            {
                Console.Error.WriteLine($"فایل یافت نشد: {projPath}");
                Environment.ExitCode = 1;
                return;
            }
            var project = ProjectFile.Load(projPath);
            foreach (var part in project.Parts)
            {
                Console.WriteLine($"— {part.Name} ({part.Family})");
                var report = GearAnalysisCalculator.Analyze(part.Teeth);
                Console.WriteLine($"    نسبت تماس ≈ {report.ContactRatio:F2} | z_min ≈ {report.MinTeethNoUndercut:F1} | ضخامت نوک {report.TipThicknessMm:F2} mm");
                foreach (var d in report.Diagnostics.Where(d => d.Severity != Severity.Info))
                    Console.WriteLine($"    [{d.Severity}] {d.Message}");
            }

            foreach (var mating in project.Matings)
            {
                var a = project.FindPart(mating.PartAId);
                var b = project.FindPart(mating.PartBId);
                if (a is null || b is null)
                    continue;
                var result = mating.Kind switch
                {
                    MatingKind.WormPair => AdvancedPairCalculator.CheckWormPair(a.Teeth, b.Teeth, a.Specific.WormPitchDiameterMm > 0 ? a.Specific.WormPitchDiameterMm : 10 * a.Teeth.Module),
                    MatingKind.CrossedHelicalPair => AdvancedPairCalculator.CheckCrossedHelicalPair(a.Teeth, b.Teeth),
                    MatingKind.BevelPair => AdvancedPairCalculator.CheckBevelPair(a.Teeth, b.Teeth),
                    MatingKind.CrownPair => AdvancedPairCalculator.CheckCrownPair(a.Teeth, b.Teeth),
                    MatingKind.HypoidPair => AdvancedPairCalculator.CheckHypoidPair(a.Teeth, b.Teeth, a.Specific.HypoidOffsetMm, 0.5, 0.5),
                    _ => (MatingCheckResult?)null,
                };
                if (result is not null)
                {
                    Console.WriteLine($"— جفت {a.Name} ↔ {b.Name} [{mating.Kind}]");
                    Console.WriteLine($"    سازگار: {result.IsCompatible} | نسبت: {result.Ratio:F3} | جهت: {result.RotationDirection:+0;-0}");
                    foreach (var d in result.Diagnostics.Where(d => d.Severity == Severity.Error))
                        Console.WriteLine($"    [خطا] {d.Message}");
                }
            }
            Environment.ExitCode = 0;
        }, projArg);

        // ---------- import ----------
        var importArg = new Argument<string>("فایل", "مسیر فایل STL/OBJ/PLY");
        var importCmd = new Command("import", "بارگذاری مدل مرجع (فقط بازبینی — پارامتریک نمی‌شود)") { importArg };
        importCmd.SetHandler((path) =>
        {
            var refMesh = MeshImporter.Import(path);
            var validation = refMesh.Mesh.Validate();
            Console.WriteLine($"قالب: {refMesh.Format} | رأس: {refMesh.Mesh.VertexCount:N0} | مثلث: {refMesh.Mesh.TriangleCount:N0}");
            Console.WriteLine($"آب‌بند: {validation.ClosedManifold} | حجم: {validation.SignedVolumeMm3:F1} mm³");
            foreach (var n in refMesh.Notes)
                Console.WriteLine($"  یادداشت: {n}");
            Console.WriteLine("توجه: مدل مرجع است؛ به چرخ‌دندهٔ پارامتریک تبدیل نمی‌شود (سیاست صریح).");
            Environment.ExitCode = 0;
        }, importArg);

        root.AddCommand(infoCmd);
        root.AddCommand(newCmd);
        root.AddCommand(exportCmd);
        root.AddCommand(drawCmd);
        root.AddCommand(analyzeCmd);
        root.AddCommand(importCmd);

        int rc = root.Invoke(args);
        return rc != 0 ? rc : Environment.ExitCode;
    }

    private static string StatusMark(CapabilityStatus s) => s switch
    {
        CapabilityStatus.Implemented => "پیاده‌شده",
        CapabilityStatus.Limited => "محدود",
        _ => "نقشهٔ راه",
    };

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "part" : name;
    }
}
