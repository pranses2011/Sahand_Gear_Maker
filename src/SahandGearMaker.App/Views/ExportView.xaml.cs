using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.App.ViewModels;

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
}
