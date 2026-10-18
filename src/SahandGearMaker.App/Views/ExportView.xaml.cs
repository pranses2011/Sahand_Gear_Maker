using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        if (vm.DefaultUnitInch)
        {
            UnitCombo.SelectedIndex = 1;
            vm.UnitIndex = 1;
        }
    }

    private void FormatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || FormatList.SelectedItem is not ExportFormatOption opt) return;
        _vm.SelectedFormat = opt;

        // کارت توضیح فرمت انتخابی
        FormatTitle.Text = opt.Title;
        FormatDescription.Text = opt.Description;
        FormatExtension.Text = opt.Extension;
        FormatUseCase.Text = opt.UseCase;
        FormatNote.Text = opt.Note;
        FormatStatusText.Text = opt.Enabled ? "فعال" : "غیرفعال — نقشهٔ راه";
        FormatStatusBadge.Background = new SolidColorBrush(opt.Enabled
            ? Color.FromRgb(0x16, 0xA3, 0x4A)
            : Color.FromRgb(0x94, 0xA3, 0xB8));
    }

    private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Scope = ScopeCombo.SelectedIndex switch
        {
            1 => ExportScope.MergedAssembly,
            2 => ExportScope.EachPart,
            _ => ExportScope.SelectedPart,
        };
        ScopeHint.Text = _vm.Scope switch
        {
            ExportScope.MergedAssembly => "همهٔ قطعات با جای‌گذاری صحنه (موقعیت/چرخش/مقیاس) در یک فایل ترکیب می‌شوند — ابعاد مونتاژ واقعی.",
            ExportScope.EachPart => "هر قطعه فایل جداگانه با نام خودش می‌گیرد؛ نام پایه از دکمهٔ خروجی گرفته می‌شود.",
            _ => "فقط قطعه‌ای که در تب طراح انتخاب شده است خروجی گرفته می‌شود.",
        };
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.QualityIndex = QualityCombo.SelectedIndex;
        QualityHint.Text = QualityCombo.SelectedIndex switch
        {
            0 => "مش موجود در طراح بدون بازسازی نوشته می‌شود — سریع‌ترین حالت.",
            3 => "تلرانس وتر ۰٫۰۰۴mm و ۹۶ برش مارپیچ — حجم و زمان بیشتر، دقت بالا.",
            4 => "تلرانس وتر ۰٫۰۰۲mm و ۱۶۰ برش — سنگین‌ترین حالت؛ برای قطعات حساس و خروجی نهایی.",
            _ => "هندسه با تلرانس انتخابی دوباره ساخته می‌شود — مثلث‌های بیشتر و سطح نرم‌تر از پیش‌نمایش.",
        };
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

    // ---------- خروجی سریع دسته‌ای (فاز ۲٫۵) ----------

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
                3 => MeshQualityPreset.Ultra,
                4 => MeshQualityPreset.Precision,
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
