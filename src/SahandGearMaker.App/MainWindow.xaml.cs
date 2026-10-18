using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SahandGearMaker.App.ViewModels;

namespace SahandGearMaker.App;

public partial class MainWindow : Window
{
    public DesignerViewModel Designer { get; } = new();
    public ExportViewModel Export { get; }
    public FeedbackViewModel Feedback { get; } = new();

    /// <summary>تایمر ذخیرهٔ خودکار/بروزرسانی‌های دوره‌ای (فاز ۲٫۴) — هر ۳۰ ثانیه.</summary>
    private DispatcherTimer? _maintenanceTimer;

    public MainWindow()
    {
        InitializeComponent();
        Export = new ExportViewModel(Designer);

        HomeView.Bind(this);
        DesignerView.Bind(this);
        AssemblyView.Bind(this);
        DrawingView.Bind(this);
        ExportView.Bind(Export);
        InspectView.Bind(this);
        SettingsView.Bind(this);
        _settings = AppSettings.Load();

        StatusText.Text = "آماده";

        // میان‌برهای سراسری (فاز ۵٫۱)
        InputBindings.Add(new KeyBinding(new RelayCommandWrapper(() => Designer.UndoCommand.Execute(null)), Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommandWrapper(() => Designer.RedoCommand.Execute(null)), Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommandWrapper(() => Designer.SaveProjectCommand.Execute(null)), Key.S, ModifierKeys.Control));

        // نگهداشت دوره‌ای: ذخیرهٔ خودکار
        _maintenanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _maintenanceTimer.Tick += (_, _) => { if (_autosaveEnabled) Designer.TickAutosave(); };
        _maintenanceTimer.Start();

        Loaded += (_, _) => PromptRecoveryIfNeeded();
        RefreshRecentOnHome();
    }

    /// <summary>پوشش سبک ICommand برای میان‌برها (بدون وابستگی به CommunityToolkit در XAML).</summary>
    private sealed class RelayCommandWrapper(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pages is null) return;
        int idx = NavList.SelectedIndex;
        if (idx < 0) return;
        if (idx == Pages.SelectedIndex) return;
        Pages.SelectedIndex = idx;
        // بازآفرینی صفحات وابسته به داده
        if (idx == 3) DrawingView.Refresh();          // نقشهٔ فنی
        if (idx == 2) AssemblyView.Refresh();         // مونتاژ
        if (idx == 5) InspectView.Refresh();          // بررسی
        if (idx == 7) SettingsView.Refresh();         // تنظیمات
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
        if (Designer.CurrentFilePath is null)
        {
            StatusText.Text = "بازکردن لغو شد.";
            return;
        }
        NavList.SelectedIndex = 1;
        StatusText.Text = $"باز شد: {Designer.CurrentFilePath}";
        Designer.EnableAutosave();
        PromptRecoveryIfNeeded();
        RefreshRecentOnHome();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        Designer.SaveProjectCommand.Execute(null);
        StatusText.Text = Designer.CurrentFilePath is null ? "ذخیره لغو شد." : $"ذخیره شد: {Designer.CurrentFilePath}";
        Designer.EnableAutosave();
        RefreshRecentOnHome();
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        NavList.SelectedIndex = 4;
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (Designer.UndoCommand.CanExecute(null))
        {
            Designer.UndoCommand.Execute(null);
            StatusText.Text = "واگرد انجام شد.";
        }
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (Designer.RedoCommand.CanExecute(null))
        {
            Designer.RedoCommand.Execute(null);
            StatusText.Text = "ازنو انجام شد.";
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>بوم طراح — برای نمای بررسی (فاز ۴) و همگام‌سازی بین صفحات.</summary>
    public Controls.ViewportControl? DesignerViewport => DesignerView?.Viewport;

    /// <summary>تازه‌سازی صحنهٔ بوم طراح از بیرون (پخش‌کنندهٔ حرکت/چیدمان خودکار — فاز ۳).</summary>
    public void RefreshDesignerScene()
    {
        DesignerView?.RefreshScenePublic();
        InspectView?.RefreshPublic();
    }

    /// <summary>انتخاب قطعه از راه دور (پیکینگ در نمای دوم — فاز ۴٫۴).</summary>
    public void SelectPartById(string id)
    {
        var part = Designer.Parts.FirstOrDefault(p => p.Id == id);
        if (part is not null) Designer.SelectedPart = part;
    }

    // ---------- تنظیمات (فاز ۵٫۲) ----------

    private AppSettings _settings = new();
    private bool _autosaveEnabled = true;

    /// <summary>پیش‌فرض‌های نمایش بوم‌ها از تنظیمات.</summary>
    public void ApplyViewportDefaults(bool grid, bool axes)
    {
        DesignerViewport?.ApplyDisplayDefaults(grid, axes);
    }

    /// <summary>روشن/خاموش کردن ذخیرهٔ خودکار از تنظیمات.</summary>
    public void SetAutosaveEnabled(bool enabled)
    {
        if (enabled) Designer.EnableAutosave();
        _autosaveEnabled = enabled;
    }

    /// <summary>بازکردن مسیر پروژه از فهرست «پروژه‌های اخیر» و پرش به تب طراح (فاز ۵٫۳).</summary>
    public void OpenPath(string path)
    {
        if (Designer.OpenPath(path))
        {
            NavList.SelectedIndex = 1;
            StatusText.Text = $"باز شد: {path}";
            Designer.EnableAutosave();
            RefreshRecentOnHome();
        }
        else
        {
            StatusText.Text = "بازکردن فایل ناموفق بود — پیام‌های طراح را ببینید.";
        }
    }

    private void RefreshRecentOnHome()
        => HomeView.RefreshRecentFiles(RecentFilesService.List().Take(6).ToList());

    /// <summary>اگر نسخهٔ بازیابی خودکار موجود بود، پیشنهاد بازیابی (فاز ۲٫۴).</summary>
    private void PromptRecoveryIfNeeded()
    {
        try
        {
            if (Designer.CurrentFilePath is null || !Designer.HasRecoveryFile(Designer.CurrentFilePath))
                return;
            var result = MessageBox.Show(this,
                "نسخهٔ بازیابی خودکار برای این پروژه پیدا شد.\nبازیابی شود؟ (نسخهٔ ذخیره‌شدهٔ اصلی دست نمی‌خورد)",
                "بازیابی نسخهٔ خودکار", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes && Designer.TryRecover(Designer.CurrentFilePath) is { } recovered)
            {
                Designer.LoadRecovered(recovered);
                StatusText.Text = "نسخهٔ خودکار بازیابی شد — تغییرات را بررسی و ذخیره کنید.";
            }
            else
            {
                Designer.DiscardRecovery(Designer.CurrentFilePath);
            }
        }
        catch
        {
            // بازیابی نباید برنامه را زمین بزند
        }
    }
}
