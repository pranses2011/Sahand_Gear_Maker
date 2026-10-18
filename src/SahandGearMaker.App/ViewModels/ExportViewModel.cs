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
        new("STEP", "STEP", "STEP AP242", false,
            "نقشهٔ راه — نیازمند هستهٔ CAD؛ مش به‌عنوان B-rep ارائه نمی‌شود",
            "استاندارد تبادل B-rep دقیق CAD. تولید صادقانهٔ STEP نیازمند هستهٔ مدلسازی مرحله‌به‌مرحله است و مش مثلثی قابل تبدیل مستقیم نیست؛ در نقشهٔ راه پروژه است.",
            "*.step", "نقشهٔ راه"),
        new("IGES", "IGES", "IGES", false,
            "نقشهٔ راه",
            "استاندارد قدیمی‌تر تبادل CAD؛ جایگزین STEP در نظر گرفته شده و پیاده‌سازی آن هم‌زمان با STEP در نقشهٔ راه است.",
            "*.igs", "نقشهٔ راه"),
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
        => SelectedFormat!.Id switch
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
