using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Exports;

namespace SahandGearMaker.App.Views;

public partial class ExportView : UserControl
{
    private ExportViewModel? _vm;

    public ExportView() => InitializeComponent();

    public void Bind(ExportViewModel vm)
    {
        _vm = vm;
        FormatList.ItemsSource = vm.Formats;
        FormatList.SelectedIndex = 0;
        ReportList.ItemsSource = vm.Report;
    }

    private void FormatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || FormatList.SelectedItem is not ExportFormatOption opt) return;
        _vm.SelectedFormat = opt;
        FormatNote.Text = opt.Enabled ? opt.Note : $"غیرفعال — {opt.Note}";
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.QualityIndex = QualityCombo.SelectedIndex;
    }

    private void UnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.UnitIndex = UnitCombo.SelectedIndex;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ExportCommand.Execute(null);
        StatusText.Text = _vm.StatusText;
    }

    // ---------- خروجی دسته‌ای (فاز ۲٫۵) ----------

    private void BatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var buildable = _vm.Designer.BuildableParts();
        if (buildable.Count == 0)
        {
            BatchResult.Text = "قطعه‌ای با ساخت معتبر وجود ندارد — ابتدا در تب طراح هندسه بسازید.";
            return;
        }

        // انتخاب پوشه بدون وابستگی به WinForms: نام یک فایل نمونه می‌گیریم و پوشهٔ آن مقصد است
        var saveDlg = new SaveFileDialog
        {
            Title = "مقصد خروجی دسته‌ای (نام فقط برای انتخاب پوشه است)",
            Filter = "پوشهٔ مقصد|batch.output",
            FileName = "batch.output",
        };
        if (saveDlg.ShowDialog() != true) return;
        string dir = Path.GetDirectoryName(saveDlg.FileName)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var format = BatchFormatCombo.SelectedIndex switch
        {
            1 => ExportFileFormat.StlAscii,
            2 => ExportFileFormat.Obj,
            3 => ExportFileFormat.Ply,
            4 => ExportFileFormat.Glb,
            5 => ExportFileFormat.ThreeMf,
            6 => ExportFileFormat.Amf,
            _ => ExportFileFormat.StlBinary,
        };
        var settings = new MeshExportSettings
        {
            Preset = _vm.QualityIndex switch
            {
                0 => MeshQualityPreset.Preview,
                2 => MeshQualityPreset.Fine,
                _ => MeshQualityPreset.Standard,
            },
            Unit = _vm.UnitIndex == 1 ? SahandGearMaker.Domain.LengthUnit.Inch : SahandGearMaker.Domain.LengthUnit.Millimeter,
        };

        try
        {
            var items = buildable
                .Select(kv => (MeshExporter.SanitizeName(kv.Part.Name), kv.Build.Mesh))
                .ToList();
            var result = BatchExporter.ExportAll(items, dir, format, settings);

            var lines = new List<string>
            {
                $"مجموع: {result.Items.Count} — موفق: {result.Succeeded} — ناموفق: {result.Failed}",
                $"پوشه: {dir}",
            };
            lines.AddRange(result.Items.Select(i =>
                i.Success ? $"✓ {Path.GetFileName(i.FilePath)} ({i.BytesWritten / 1024.0:0.#} KB)" : $"✗ {i.FilePath}: {i.Error}"));
            BatchResult.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            BatchResult.Text = $"خروجی دسته‌ای ناموفق: {ex.Message}";
        }
    }
}
