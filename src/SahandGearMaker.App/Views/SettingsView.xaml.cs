using System.IO;
using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.Scene;

namespace SahandGearMaker.App.Views;

/// <summary>
/// صفحهٔ تنظیمات (فاز ۵٫۲) — همهٔ اثرها واقعی و بلافاصله اعمال می‌شوند:
/// سرعت/جهت ماوس روی هر دو بوم، تم بوم، گرید/محور پیش‌فرض، واحد خروجی و ذخیرهٔ خودکار.
/// </summary>
public partial class SettingsView : UserControl
{
    private MainWindow? _main;
    private AppSettings _settings = new();
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void Bind(MainWindow main)
    {
        _main = main;
        _settings = AppSettings.Load();
        LoadToUi();
    }

    public void Refresh()
    {
        _settings = AppSettings.Load();
        LoadToUi();
    }

    private void LoadToUi()
    {
        _loading = true;
        RotateSpeedSlider.Value = Math.Clamp(_settings.RotateSpeed * 1000, 2, 20);
        InvertCheck.IsChecked = _settings.InvertHorizontal < 0;
        DarkCanvasCheck.IsChecked = _settings.DarkCanvas;
        GridCheck.IsChecked = _settings.DefaultShowGrid;
        AxesCheck.IsChecked = _settings.DefaultShowAxes;
        UnitCombo.SelectedIndex = _settings.ExportUnitInch ? 1 : 0;
        AutosaveCheck.IsChecked = _settings.AutosaveEnabled;

        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SahandGearDesigner");
        PathsText.Text =
            $"پروژه‌های شما: هرجا که ذخیره کنید (پیش‌فرض پوشهٔ اسناد)\n" +
            $"تنظیمات: {appData}\\settings.json\n" +
            $"پروژه‌های اخیر: {appData}\\recent.json\n" +
            $"نسخهٔ بازیابی خودکار: کنار فایل پروژه (پسوند .autosave)\n" +
            $"فونت‌ها و منابع: داخل پوشهٔ برنامه (بدون نیاز به نصب)";

        _loading = false;
        ApplyToApp();
    }

    private void SaveAndApply()
    {
        if (_loading) return;
        _settings.Save();
        ApplyToApp();
    }

    /// <summary>اعمال تنظیمات روی بوم‌های فعال و ViewModel خروجی.</summary>
    private void ApplyToApp()
    {
        if (_main is null) return;
        _main.DesignerViewport?.ApplyMouseSettings(_settings.RotateSpeed, _settings.InvertHorizontal);

        // تم بوم: پس‌زمینهٔ تیره
        var bg = _settings.DarkCanvas
            ? new Rgb(0x1E, 0x24, 0x33)
            : new Rgb(0xEA, 0xEE, 0xF4);
        _main.DesignerViewport?.SetCanvasBackground(bg);
        _main.ApplyViewportDefaults(_settings.DefaultShowGrid, _settings.DefaultShowAxes);
        _main.Export.DefaultUnitInch = _settings.ExportUnitInch;
        _main.SetAutosaveEnabled(_settings.AutosaveEnabled);
    }

    // ---------- رویدادها ----------

    private void RotateSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.RotateSpeed = Math.Clamp(e.NewValue, 2, 20) / 1000.0;
        SaveAndApply();
    }

    private void Invert_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.InvertHorizontal = InvertCheck.IsChecked == true ? -1 : 1;
        SaveAndApply();
    }

    private void DarkCanvas_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DarkCanvas = DarkCanvasCheck.IsChecked == true;
        SaveAndApply();
    }

    private void Grid_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DefaultShowGrid = GridCheck.IsChecked == true;
        SaveAndApply();
    }

    private void Axes_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DefaultShowAxes = AxesCheck.IsChecked == true;
        SaveAndApply();
    }

    private void Unit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _settings.ExportUnitInch = UnitCombo.SelectedIndex == 1;
        SaveAndApply();
    }

    private void Autosave_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AutosaveEnabled = AutosaveCheck.IsChecked == true;
        SaveAndApply();
    }
}
