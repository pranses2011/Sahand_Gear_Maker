using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SahandGearMaker.Domain;
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

    public ObservableCollection<ExportFormatOption> Formats { get; } = new()
    {
        new("STL-BINARY", "STL باینری (چاپ سه‌بعدی)", true, "مش مثلثی؛ واحد در گزارش همراه ذکر می‌شود"),
        new("STL-ASCII", "STL متنی", true, "برای اشکال‌زدایی؛ حجم بیشتر"),
        new("OBJ", "OBJ + MTL", true, "مش با فایل متریال جانبی"),
        new("PLY", "PLY باینری", true, "ابزارهای پردازش مش"),
        new("GLB", "glTF باینری", true, "نمایش سه‌بعدی؛ واحد متر"),
        new("STEP", "STEP AP242", false, "نقشهٔ راه — نیازمند هستهٔ CAD؛ مش به‌عنوان B-rep ارائه نمی‌شود"),
        new("IGES", "IGES", false, "نقشهٔ راه"),
        new("3MF", "3MF", false, "نقشهٔ راه"),
        new("DXF", "DXF دوبعدی", false, "نقشهٔ راه — نقشهٔ فنی"),
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
            ExportReport result = SelectedFormat.Id switch
            {
                "STL-BINARY" => MeshExporter.ExportStlBinary(build.Mesh, dlg.FileName, settings),
                "STL-ASCII" => MeshExporter.ExportStlAscii(build.Mesh, dlg.FileName, settings),
                "OBJ" => MeshExporter.ExportObj(build.Mesh, dlg.FileName, settings, _designer.SelectedPart?.Name),
                "PLY" => MeshExporter.ExportPlyBinary(build.Mesh, dlg.FileName, settings),
                "GLB" => MeshExporter.ExportGlb(build.Mesh, dlg.FileName, settings, _designer.SelectedPart?.Name),
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
}
