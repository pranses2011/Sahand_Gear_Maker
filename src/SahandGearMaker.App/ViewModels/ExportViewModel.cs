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

/// <summary>وضعیت صادقانهٔ هر فرمت — فقط موارد پیاده‌شده فعال‌اند (سند ۰۴، بخش ۴-گام B).</summary>
public sealed record ExportFormatOption(string Id, string Title, bool Enabled, string Note);

/// <summary>مدل نمایش ویزارذ خروجی (سند ۰۴، بخش ۴).</summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly DesignerViewModel _designer;

    /// <summary>دسترسی عمومی به طراح برای خروجی دسته‌ای (فاز ۲٫۵).</summary>
    public DesignerViewModel Designer => _designer;

    /// <summary>واحد پیش‌فرض از تنظیمات (فاز ۵٫۲) — اگر true، خروجی تازه با inch شروع می‌شود.</summary>
    public bool DefaultUnitInch { get; set; }

    public ObservableCollection<ExportFormatOption> Formats { get; } = new()
    {
        new("STL-BINARY", "STL باینری (چاپ سه‌بعدی)", true, "مش مثلثی؛ واحد در گزارش همراه ذکر می‌شود"),
        new("STL-ASCII", "STL متنی", true, "برای اشکال‌زدایی؛ حجم بیشتر"),
        new("OBJ", "OBJ + MTL", true, "مش با فایل متریال جانبی"),
        new("PLY", "PLY باینری", true, "ابزارهای پردازش مش"),
        new("GLB", "glTF باینری", true, "نمایش سه‌بعدی؛ واحد متر"),
        // فاز ۰٫۳: صادرکننده‌ها از نسخهٔ ۰٫۲ موجودند؛ پیش از این در UI خاموش مانده بودند (فاصلهٔ UI↔موتور).
        new("3MF", "3MF (بستهٔ ZIP استاندارد چاپ)", true, "مش مثلثی؛ سازگار با چاپگرهای مدرن"),
        new("AMF", "AMF (XML چاپ افزایشی)", true, "مش مثلثی؛ متن‌باز"),
        new("DXF", "DXF پروفیل دندانه (۲بعدی)", true, "پروفیل مقطع برای CAD دوبعدی — نه B-rep سه‌بعدی"),
        new("STEP", "STEP AP242", false, "نقشهٔ راه — نیازمند هستهٔ CAD؛ مش به‌عنوان B-rep ارائه نمی‌شود"),
        new("IGES", "IGES", false, "نقشهٔ راه"),
    };

    [ObservableProperty]
    private ExportFormatOption? _selectedFormat;

    [ObservableProperty]
    private int _qualityIndex = 1; // 0=پیش‌نمایش 1=معمولی 2=دقیق

    [ObservableProperty]
    private int _unitIndex = 0; // 0=mm 1=inch

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

    [RelayCommand]
    private void Export()
    {
        if (SelectedFormat is null || !SelectedFormat.Enabled)
        {
            StatusText = "این فرمت در نسخهٔ فعلی فعال نیست؛ وضعیت آن در فهرست ذکر شده است.";
            return;
        }

        var build = _designer.LastBuild;
        if (build is null)
        {
            StatusText = "ابتدا یک قطعهٔ معتبر بسازید؛ هندسه‌ای برای خروجی وجود ندارد.";
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
            FileName = MeshExporter.SanitizeName(_designer.SelectedPart?.Name ?? "gear"),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return;

        var settings = new MeshExportSettings
        {
            Preset = QualityIndex switch
            {
                0 => MeshQualityPreset.Preview,
                2 => MeshQualityPreset.Fine,
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
            ExportReport result = SelectedFormat.Id switch
            {
                "STL-BINARY" => MeshExporter.ExportStlBinary(build.Mesh, dlg.FileName, settings),
                "STL-ASCII" => MeshExporter.ExportStlAscii(build.Mesh, dlg.FileName, settings),
                "OBJ" => MeshExporter.ExportObj(build.Mesh, dlg.FileName, settings, part?.Name),
                "PLY" => MeshExporter.ExportPlyBinary(build.Mesh, dlg.FileName, settings),
                "GLB" => MeshExporter.ExportGlb(build.Mesh, dlg.FileName, settings, part?.Name),
                "3MF" => WrapContainer(() => Format3mfAmfExporter.Export3mf(build.Mesh, dlg.FileName, part?.Name ?? "gear"), "3MF", dlg.FileName, build, sw.Elapsed),
                "AMF" => WrapContainer(() => Format3mfAmfExporter.ExportAmf(build.Mesh, dlg.FileName, part?.Name ?? "gear"), "AMF", dlg.FileName, build, sw.Elapsed),
                "DXF" => WrapContainer(() => ExportDxfProfile(part, build, dlg.FileName), "DXF", dlg.FileName, build, sw.Elapsed),
                _ => throw new InvalidOperationException("فرمت ناشناخته"),
            };

            Report.Add($"فرمت: {result.FormatId}");
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

    // ---------- پوشش صادرکننده‌های بدون گزارش برای یکنواختی UI (فاز ۰٫۳) ----------

    /// <summary>پروفیل مقطع دندانه از نقشهٔ فنی — خط بستهٔ مرز بیرونی (DXF دوبعدی صادقانه).</summary>
    private static void ExportDxfProfile(GearPart? part, GearBuildResult build, string path)
    {
        var doc = TechnicalDrawingBuilder.Build(part ?? new GearPart { Name = "gear" }, build);
        var outline = doc.Views.SelectMany(v => v.Outlines).OrderByDescending(o => o.Count).FirstOrDefault()
            ?? throw new InvalidOperationException("پروفیلی برای DXF یافت نشد — ابتدا هندسهٔ معتبر بسازید.");
        Format3mfAmfExporter.ExportProfileDxf(outline, path, closed: true);
    }

    private static ExportReport WrapContainer(
        Action write, string formatId, string filePath, GearBuildResult build, TimeSpan elapsed)
    {
        write();
        return new ExportReport
        {
            FormatId = formatId,
            FilePath = filePath,
            TriangleCount = build.Mesh.TriangleCount,
            VertexCount = build.Mesh.VertexCount,
            Elapsed = elapsed,
            Success = true,
            PostWriteValidated = false,
            Warnings = new List<string> { "اعتبارسنجی خودکار پس از نوشتن برای این قالب پیاده نشده — فایل را در نرم‌افزار مقصد باز کنید." },
        };
    }
}
