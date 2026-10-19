using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SahandGearMaker.Domain;
using SahandGearMaker.Drawing;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using SahandGearMaker.Persistence;

namespace SahandGearMaker.App.ViewModels;

/// <summary>
/// وضعیت صادقانهٔ هر فرمت — فقط موارد پیاده‌شده فعال‌اند (سند ۰۴، بخش ۴-گام B).
/// فهرست فقط «نام کوتاه» را نشان می‌دهد؛ توضیح کامل در کارت کنار فهرست (بازطراحی صفحهٔ خروجی).
/// </summary>
public sealed record ExportFormatOption(
    string Id,
    string ShortName,
    string Title,
    bool Enabled,
    string Note,
    string Description,
    string Extension,
    string UseCase);

/// <summary>محدودهٔ خروجی (رفع «با چند قطعه فقط اولین قطعه خروجی گرفته می‌شد»).</summary>
public enum ExportScope
{
    /// <summary>فقط قطعهٔ انتخابی در تب طراح.</summary>
    SelectedPart,
    /// <summary>همهٔ قطعات با جای‌گذاری صحنه در یک فایل (مونتاژ).</summary>
    MergedAssembly,
    /// <summary>هر قطعه در فایل جدا (پیشوند نام قطعه).</summary>
    EachPart,
}

/// <summary>مدل نمایش ویجت خروجی (سند ۰۴، بخش ۴).</summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly DesignerViewModel _designer;

    /// <summary>دسترسی عمومی به طراح برای خروجی دسته‌ای (فاز ۲٫۵).</summary>
    public DesignerViewModel Designer => _designer;

    /// <summary>واحد پیش‌فرض از تنظیمات (فاز ۵٫۲) — اگر true، خروجی تازه با inch شروع می‌شود.</summary>
    public bool DefaultUnitInch { get; set; }

    public ObservableCollection<ExportFormatOption> Formats { get; } = new()
    {
        new("STL-BINARY", "STL باینری", "STL باینری (چاپ سه‌بعدی)", true,
            "مش مثلثی؛ واحد در گزارش همراه ذکر می‌شود",
            "قالب استاندارد و جهانی چاپ سه‌بعدی. مثلث‌ها را بدون رنگ/متریال ذخیره می‌کند و در همهٔ اسلایسرها (Cura، PrusaSlicer، Bambu Studio…) باز می‌شود.",
            "*.stl", "چاپ سه‌بعدی — تبادل عمومی"),
        new("STL-ASCII", "STL متنی", "STL متنی (ASCII)", true,
            "برای اشکال‌زدایی؛ حجم بیشتر",
            "همان STL اما در قالب متنی خوانا. حجم بسیار بیشتر و سرعت کمتر؛ برای بررسی دستی فایل یا اسکریپت‌های تحلیلی مناسب است.",
            "*.stl", "اشکال‌زدایی و بررسی"),
        new("OBJ", "OBJ", "OBJ + MTL", true,
            "مش با فایل متریال جانبی",
            "قالب Wavefront با فایل متریال جانبی (MTL). گروه‌بندی و نام‌گذاری اشیاء را حفظ می‌کند و در Blender/Maya/3ds Max استاندارد است.",
            "*.obj", "گرافیک و رندر سه‌بعدی"),
        new("PLY", "PLY باینری", "PLY باینری", true,
            "ابزارهای پردازش مش",
            "قالب Stanford با ذخیره‌سازی باینری بهینه. برای ابزارهای پردازش مش، اسکن سه‌بعدی و نرم‌افزارهای مهندسی معکوس مناسب است.",
            "*.ply", "پردازش مش و اسکن"),
        new("GLB", "GLB", "glTF باینری (GLB)", true,
            "نمایش سه‌بعدی؛ واحد متر",
            "استاندارد وب سه‌بعدی (Khronos) در یک فایل باینری. مختصات به متر تبدیل می‌شود و در نمایشگرهای آنلاین Three.js/Babylon مستقیم باز می‌شود.",
            "*.glb", "وب و AR/VR"),
        new("3MF", "3MF", "3MF (بستهٔ ZIP استاندارد چاپ)", true,
            "مش مثلثی؛ سازگار با چاپگرهای مدرن",
            "جانشین مدرن STL از کنسرسیوم 3MF. واحد، رنگ و متادیتای کامل را ذخیره می‌کند و در اسلایسرهای جدید پشتیبانی می‌شود.",
            "*.3mf", "چاپ سه‌بعدی حرفه‌ای"),
        new("AMF", "AMF", "AMF (XML چاپ افزایشی)", true,
            "مش مثلثی؛ متن‌باز",
            "قالب XML متن‌باز برای ساخت افزایشی با پشتیبانی رنگ و مواد متعدد. حجم بیشتر از 3MF اما کاملاً خوانا و قابل‌گسترش است.",
            "*.amf", "چاپ افزایشی پیشرفته"),
        new("DXF", "DXF پروفیل", "DXF پروفیل دندانه (۲بعدی)", true,
            "پروفیل مقطع برای CAD دوبعدی — نه B-rep سه‌بعدی",
            "پروفیل مقطع مرزی دندانه را به‌صورت خط بستهٔ دوبعدی می‌نویسد. برای برش لیزری/وایر و ترسیم در AutoCAD مناسب است؛ جسم سه‌بعدی نیست.",
            "*.dxf", "برش لیزری — CAD دوبعدی"),
        new("STEP", "STEP", "STEP AP214 (B-rep تحلیلی)", true,
            "هستهٔ CAD داخلی — سطوح صفحه/استوانه، نه مش",
            "استاندارد جهانی تبادل CAD با هستهٔ داخلی سهند: چرخ‌دنده‌های استوانه‌ای با سطوح تحلیلی (PLANE/CYLINDRICAL_SURFACE) و بور/حفره‌های دقیق صادر می‌شوند؛ خانواده‌های منحنی (کرم/مخروطی/…) به‌صورت وجه‌های صفحه‌ای ارائه می‌شوند. در SolidWorks/FreeCAD/Fusion/Onshape باز می‌شود.",
            "*.step", "مهندسی و تبادل CAD"),
        new("IGES", "IGES", "IGES 5.3 (B-rep)", true,
            "هستهٔ CAD داخلی — سطوح تحلیلی",
            "استاندارد کلاسیک تبادل CAD (نسخهٔ 5.3) با همان هستهٔ داخلی: هر وجه به‌صورت سطح بریده (Type 144) روی صفحه/استوانه. برای نرم‌افزارهای قدیمی‌تر که STEP ندارند.",
            "*.igs", "مهندسی — سامانه‌های قدیمی"),
    };

    [ObservableProperty]
    private ExportFormatOption? _selectedFormat;

    [ObservableProperty]
    private int _qualityIndex = 1; // 0=همان طراح 1=معمولی 2=دقیق 3=خیلی دقیق 4=حداکثر

    [ObservableProperty]
    private int _unitIndex = 0; // 0=mm 1=inch

    /// <summary>محدودهٔ خروجی: قطعهٔ انتخابی / مونتاژ تجمیعی / هر قطعه جدا.</summary>
    [ObservableProperty]
    private ExportScope _scope = ExportScope.SelectedPart;

    /// <summary>بهینه‌سازی برای چاپ سه‌بعدی (ایراد ۱۸) — فعال/غیرفعال.</summary>
    [ObservableProperty]
    private bool _printOptimize;

    /// <summary>جهت چاپ: 0 = محور عمودی (پیش‌فرض)، 1 = محور افقی (دندانه‌های محکم‌تر).</summary>
    [ObservableProperty]
    private int _printOrientation = 0;

    /// <summary>چگالی ماده (g/cm³) برای برآورد جرم — پیش‌فرض PLA.</summary>
    [ObservableProperty]
    private double _printDensity = 1.24;

    /// <summary>گزارش آخرین بهینه‌سازی چاپ.</summary>
    public PrintOptimizationReport? LastPrintReport { get; private set; }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<string> Report { get; } = new();

    public ExportViewModel(DesignerViewModel designer)
    {
        _designer = designer;
        SelectedFormat = Formats[0];
    }

    // ---------- ساخت هندسه با کیفیت خروجی ----------

    /// <summary>
    /// مش قطعه را با تلرانس پیش‌تنظیم خروجی بازمی‌سازد — کیفیت «خیلی دقیق/حداکثر» واقعاً
    /// هندسهٔ ظریف‌تری می‌سازد (تلرانس وتر و برش‌های مارپیچ بیشتر)، نه فقط گزارش بهتر (رفع کیفیت پایین خروجی).
    /// </summary>
    private GearBuildResult BuildForExport(GearPart part, GearBuildResult existing, MeshExportSettings settings)
    {
        if (settings.Preset == MeshQualityPreset.Preview)
            return existing; // «همانِ طراح» — بدون بازسازی

        try
        {
            return GearGeometryEngine.Build(
                part.Family, part.Teeth, part.Body,
                settings.ToGeometrySettings(part.GeometrySettings), part.Specific);
        }
        catch (GeometryBuildException)
        {
            return existing; // اگر با تلرانس سخت‌گیرانه ساخت نشد، مش موجود صادقانه استفاده می‌شود
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (SelectedFormat is null || !SelectedFormat.Enabled)
        {
            StatusText = "این فرمت در نسخهٔ فعلی فعال نیست؛ وضعیت آن در کارت توضیح ذکر شده است.";
            return;
        }

        var build = _designer.LastBuild;
        if (build is null)
        {
            StatusText = "ابتدا یک قطعهٔ معتبر بسازید؛ هندسه‌ای برای خروجی وجود ندارد.";
            return;
        }

        if (Scope != ExportScope.SelectedPart && _designer.BuildableParts().Count == 0)
        {
            StatusText = "برای خروجی مونتاژ/همهٔ قطعات، حداقل یک قطعهٔ ساخته‌شده لازم است.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "خروجی",
            Filter = SelectedFormat.Id switch
            {
                "STL-BINARY" => "STL باینری|*.stl",
                "STL-ASCII" => "STL متنی|*.stl",
                "OBJ" => "Wavefront OBJ|*.obj",
                "PLY" => "Stanford PLY|*.ply",
                "GLB" => "glTF باینری|*.glb",
                "3MF" => "3MF|*.3mf",
                "AMF" => "AMF|*.amf",
                "DXF" => "DXF پروفیل|*.dxf",
                "STEP" => "STEP AP214|*.step;*.stp",
                "IGES" => "IGES 5.3|*.igs;*.iges",
                _ => "همهٔ فایل‌ها|*.*",
            },
            FileName = MeshExporter.SanitizeName(Scope switch
            {
                ExportScope.MergedAssembly => _designer.ProjectName is { Length: > 0 } n ? n : "assembly",
                _ => _designer.SelectedPart?.Name ?? "gear",
            }),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return;

        var settings = new MeshExportSettings
        {
            Preset = QualityIndex switch
            {
                0 => MeshQualityPreset.Preview,
                2 => MeshQualityPreset.Fine,
                3 => MeshQualityPreset.Ultra,
                4 => MeshQualityPreset.Precision,
                _ => MeshQualityPreset.Standard,
            },
            Unit = UnitIndex == 1 ? LengthUnit.Inch : LengthUnit.Millimeter,
        };

        IsBusy = true;
        Report.Clear();
        var sw = Stopwatch.StartNew();
        try
        {
            var part = _designer.SelectedPart;

            // ---- فرمت‌های هستهٔ CAD (STEP/IGES) — مسیر تحلیلی جدا ----
            if (SelectedFormat.Id is "STEP" or "IGES")
            {
                var cadResult = ExportCad(dlg.FileName, settings, sw);
                IsBusy = false;
                return;
            }

            ExportReport result;
            if (Scope == ExportScope.MergedAssembly)
            {
                // مونتاژ تجمیعی: همهٔ قطعات با جای‌گذاری صحنه (X/Y/Z/چرخش/مقیاس) در یک فایل
                var merged = new TriMesh();
                int parts = 0;
                foreach (var (p, b) in _designer.BuildableParts())
                {
                    var mesh = BuildForExport(p, b, settings).Mesh;
                    merged.AddMesh(mesh,
                        new MathX.Vector3(p.Placement.X, p.Placement.Y, p.Placement.Z),
                        p.Placement.Theta,
                        p.Placement.Scale <= 0 ? 1.0 : p.Placement.Scale);
                    parts++;
                }
                Report.Add($"تجمیع {parts} قطعه با جای‌گذاری صحنه (مونتاژ)");
                result = ExportMesh(merged, dlg.FileName, settings, "مونتاژ");
            }
            else if (Scope == ExportScope.EachPart)
            {
                // هر قطعه در فایل جدا با پسوند نام قطعه
                string dir = System.IO.Path.GetDirectoryName(dlg.FileName)!;
                string stem = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                string ext = System.IO.Path.GetExtension(dlg.FileName);
                var all = _designer.BuildableParts().ToList();
                int ok = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var (p, b) = all[i];
                    var mesh = BuildForExport(p, b, settings).Mesh;
                    string file = System.IO.Path.Combine(dir,
                        $"{stem}-{MeshExporter.SanitizeName(p.Name)}{ext}");
                    var r = ExportMesh(mesh, file, settings, p.Name);
                    Report.Add(r.Success ? $"✓ {p.Name} → {System.IO.Path.GetFileName(file)} ({r.TriangleCount:N0} مثلث)"
                                         : $"✗ {p.Name}: {string.Join("؛ ", r.Warnings)}");
                    if (r.Success) ok++;
                }
                StatusText = $"خروجی جدا برای {ok} قطعه از {all.Count} نوشته شد.";
                result = new ExportReport
                {
                    Success = ok > 0,
                    FilePath = dir,
                    FormatId = SelectedFormat.Id,
                    TriangleCount = all.Sum(kv => kv.Build.Mesh.TriangleCount),
                    VertexCount = all.Sum(kv => kv.Build.Mesh.VertexCount),
                    Elapsed = sw.Elapsed,
                    PostWriteValidated = true,
                };
                Report.Add($"پوشه: {dir}");
                IsBusy = false;
                return;
            }
            else
            {
                var exportBuild = BuildForExport(part!, build, settings);
                result = ExportMesh(exportBuild.Mesh, dlg.FileName, settings, part?.Name);
                if (!ReferenceEquals(exportBuild, build))
                    Report.Add($"بازسازی با کیفیت خروجی: تلرانس وتر {exportBuild.EffectiveChordToleranceMm:0.####}mm، {exportBuild.AxialSlicesUsed} برش");
            }

            Report.Insert(0, $"فرمت: {result.FormatId}");
            Report.Add($"فایل: {result.FilePath}");
            Report.Add($"تعداد مثلث: {result.TriangleCount:N0}");
            Report.Add($"تعداد رأس: {result.VertexCount:N0}");
            Report.Add($"زمان: {result.Elapsed.TotalMilliseconds:F0} میلی‌ثانیه");
            Report.Add($"اعتبارسنجی پس از نوشتن: {(result.PostWriteValidated ? "موفق" : "ناموفق!")}");
            Report.Add("نوع هندسه: مش مثلثی (نه B-rep دقیق CAD)");
            foreach (var w in result.Warnings)
                Report.Add($"هشدار: {w}");

            StatusText = result.Success
                ? "خروجی با موفقیت نوشته و اعتبارسنجی شد."
                : "خروجی نوشته شد اما اعتبارسنجی ناکام ماند؛ فایل را بررسی کنید.";
        }
        catch (Exception ex)
        {
            StatusText = $"خروجی ناموفق: {ex.Message}";
            Report.Add($"خطا: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            IsBusy = false;
        }
    }

    /// <summary>نوشتن مش با فرمت انتخابی — یک نقطهٔ مشترک برای همهٔ محدوده‌ها.</summary>
    private ExportReport ExportMesh(TriMesh mesh, string path, MeshExportSettings settings, string? name)
    {
        // بهینه‌سازی چاپ روی یک کپی — مش صحنه هرگز تغییر نمی‌کند
        if (PrintOptimize)
        {
            var copy = mesh.Transformed(MathX.Vector3.Zero, 0.0);
            var report = PrintPreparation.Prepare(copy, new PrintOptimizationOptions
            {
                Enabled = true,
                LayFlatVertical = PrintOrientation == 0,
                LayFlatHorizontal = PrintOrientation == 1,
                DensityGCm3 = PrintDensity,
            });
            LastPrintReport = report;
            Report.Add($"بهینه‌سازی چاپ: آب‌بند {(report.Watertight ? "✓" : "✗")}، نرمال رو-بیرون {(report.OutwardNormals ? "✓" : "✗")}، " +
                       $"حجم {report.VolumeMm3 / 1000.0:F1} cm³، جرم ≈ {report.MassGrams:F0} g ({PrintDensity:0.##} g/cm³)، " +
                       $"ابعاد {report.SizeMm.X:0.##}×{report.SizeMm.Y:0.##}×{report.SizeMm.Z:0.##} mm");
            Report.Add($"جهت چاپ: {report.OrientationNote}");
            if (report.DegenerateRemoved > 0 || report.VerticesWelded > 0)
                Report.Add($"پاک‌سازی: {report.DegenerateRemoved} مثلث یکنواخت حذف، {report.VerticesWelded} رأس جوش");
            foreach (var w in report.Warnings)
                Report.Add($"هشدار چاپ: {w}");
            mesh = copy;
        }
        else
        {
            LastPrintReport = null;
        }

        return SelectedFormat!.Id switch
        {
            "STL-BINARY" => MeshExporter.ExportStlBinary(mesh, path, settings),
            "STL-ASCII" => MeshExporter.ExportStlAscii(mesh, path, settings),
            "OBJ" => MeshExporter.ExportObj(mesh, path, settings, name),
            "PLY" => MeshExporter.ExportPlyBinary(mesh, path, settings),
            "GLB" => MeshExporter.ExportGlb(mesh, path, settings, name),
            "3MF" => WrapContainer(() => Format3mfAmfExporter.Export3mf(mesh, path, name ?? "gear"), "3MF", path, mesh, settings),
            "AMF" => WrapContainer(() => Format3mfAmfExporter.ExportAmf(mesh, path, name ?? "gear"), "AMF", path, mesh, settings),
            "DXF" => WrapContainer(() => ExportDxfProfile(mesh, path), "DXF", path, mesh, settings),
            _ => throw new InvalidOperationException("فرمت ناشناخته"),
        };
    }

    // ---------- مسیر خروجی هستهٔ CAD (STEP/IGES) ----------

    /// <summary>
    /// خروجی STEP/IGES با هستهٔ CAD داخلی — B-rep تحلیلی برای خانواده‌های منشوری و
    /// وجه‌های صفحه‌ای برای بقیه؛ محدوده‌های قطعه/مونتاژ/جدا پشتیبانی می‌شوند.
    /// </summary>
    private object ExportCad(string path, MeshExportSettings settings, Stopwatch sw)
    {
        bool isStep = SelectedFormat!.Id == "STEP";
        var solids = new List<SahandGearMaker.Cad.CadSolid>();

        try
        {
            if (Scope == ExportScope.SelectedPart)
            {
                var part = _designer.SelectedPart!;
                var build = BuildForExport(part, _designer.LastBuild!, settings);
                var (solid, rep) = SahandGearMaker.Cad.CadPartBuilder.Build(part, build);
                solids.Add(solid);
                Report.Add($"راهبرد: {rep.Strategy} — {rep.FaceCount:N0} وجه تحلیلی");
                foreach (var n in rep.Notes)
                    Report.Add(n);
            }
            else
            {
                foreach (var (p, b) in _designer.BuildableParts())
                {
                    var build = BuildForExport(p, b, settings);
                    var (solid, _) = SahandGearMaker.Cad.CadPartBuilder.Build(p, build);
                    solids.Add(SahandGearMaker.Cad.CadTransform.Transformed(
                        solid, p.Placement.X, p.Placement.Y, p.Placement.Z,
                        p.Placement.Theta, p.Placement.Scale <= 0 ? 1.0 : p.Placement.Scale));
                }
                if (Scope == ExportScope.EachPart)
                {
                    // هر قطعه فایل جدا
                    string dir = System.IO.Path.GetDirectoryName(path)!;
                    string stem = System.IO.Path.GetFileNameWithoutExtension(path);
                    string ext = System.IO.Path.GetExtension(path);
                    int ok = 0;
                    for (int i = 0; i < solids.Count; i++)
                    {
                        string file = System.IO.Path.Combine(dir, $"{stem}-{MeshExporter.SanitizeName(solids[i].Name)}{ext}");
                        if (isStep)
                            SahandGearMaker.Cad.CadFile.WriteStep(solids[i], file);
                        else
                            SahandGearMaker.Cad.CadFile.WriteIges(solids[i], file);
                        Report.Add($"✓ {solids[i].Name} → {System.IO.Path.GetFileName(file)}");
                        ok++;
                    }
                    StatusText = $"خروجی CAD جدا برای {ok} قطعه نوشته شد.";
                    Report.Insert(0, $"فرمت: {SelectedFormat.Id}");
                    Report.Add($"زمان: {sw.Elapsed.TotalMilliseconds:F0} میلی‌ثانیه");
                    return true;
                }
                Report.Add($"مونتاژ: {solids.Count} جامد با جای‌گذاری صحنه در یک فایل");
            }

            var cadReport = isStep
                ? SahandGearMaker.Cad.CadFile.WriteStep2(solids, path)
                : SahandGearMaker.Cad.CadFile.WriteIges2(solids, path);

            Report.Insert(0, $"فرمت: {SelectedFormat.Id}");
            Report.Add($"فایل: {path}");
            Report.Add($"وجوه تحلیلی: {cadReport.FaceCount:N0}");
            Report.Add($"نوع هندسه: B-rep دقیق (سطوح تحلیلی — نه مش مثلثی)");
            Report.Add($"زمان: {sw.Elapsed.TotalMilliseconds:F0} میلی‌ثانیه");
            StatusText = $"خروجی {SelectedFormat.Title} با هستهٔ CAD نوشته شد.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"خروجی CAD ناموفق: {ex.Message}";
            Report.Add($"خطا: {ex.Message}");
            return false;
        }
    }

    // ---------- پوشش صادرکننده‌های بدون گزارش برای یکنواختی UI (فاز ۰٫۳) ----------

    /// <summary>پروفیل مقطع دندانه از مرز مش — خط بستهٔ بیرونی (DXF دوبعدی صادقانه).</summary>
    private static void ExportDxfProfile(TriMesh mesh, string path)
    {
        var doc = TechnicalDrawingBuilder.BuildFromMesh(mesh);
        var outline = doc.Views.SelectMany(v => v.Outlines).OrderByDescending(o => o.Count).FirstOrDefault()
            ?? throw new InvalidOperationException("پروفیلی برای DXF یافت نشد — ابتدا هندسهٔ معتبر بسازید.");
        Format3mfAmfExporter.ExportProfileDxf(outline, path, closed: true);
    }

    private static ExportReport WrapContainer(
        Action write, string formatId, string filePath, TriMesh mesh, MeshExportSettings settings)
    {
        write();
        var (min, max) = mesh.Bounds();
        double size = Math.Max(1, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
        return new ExportReport
        {
            FormatId = formatId,
            FilePath = filePath,
            TriangleCount = mesh.TriangleCount,
            VertexCount = mesh.VertexCount,
            ChordalToleranceMm = settings.EffectiveToleranceMm(size),
            Elapsed = TimeSpan.Zero,
            Success = true,
            PostWriteValidated = false,
            Warnings = new List<string> { "اعتبارسنجی خودکار پس از نوشتن برای این قالب پیاده نشده — فایل را در نرم‌افزار مقصد باز کنید." },
        };
    }
}
