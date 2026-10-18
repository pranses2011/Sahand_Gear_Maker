using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.App.ViewModels;

namespace SahandGearMaker.App;

public partial class MainWindow : Window
{
    public DesignerViewModel Designer { get; } = new();
    public ExportViewModel Export { get; }
    public FeedbackViewModel Feedback { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Export = new ExportViewModel(Designer);

        HomeView.Bind(this);
        DesignerView.Bind(this);
        ExportView.Bind(Export);

        StatusText.Text = "آماده";
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pages is null) return;
        int idx = NavList.SelectedIndex;
        if (idx < 0) return;
        Pages.SelectedIndex = idx;
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        Designer.NewProjectCommand.Execute(null);
        NavList.SelectedIndex = 1;
        StatusText.Text = "پروژهٔ جدید ساخته شد.";
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        Designer.OpenProjectCommand.Execute(null);
        NavList.SelectedIndex = 1;
        StatusText.Text = Designer.CurrentFilePath is null ? "بازکردن لغو شد." : $"باز شد: {Designer.CurrentFilePath}";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        Designer.SaveProjectCommand.Execute(null);
        StatusText.Text = Designer.CurrentFilePath is null ? "ذخیره لغو شد." : $"ذخیره شد: {Designer.CurrentFilePath}";
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        NavList.SelectedIndex = 2;
    }

    public void SetStatus(string text) => StatusText.Text = text;
}
