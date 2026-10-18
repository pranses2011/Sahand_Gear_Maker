using System.IO;
using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.Domain;

namespace SahandGearMaker.App.Views;

public partial class HomeView : UserControl
{
    private MainWindow? _main;

    public HomeView() => InitializeComponent();

    public void Bind(MainWindow main)
    {
        _main = main;
        RefreshStatusTexts();
    }

    /// <summary>
    /// فاز ۰٫۲: متن وضعیت از CapabilityCatalog (منبع واحد) ساخته می‌شود؛
    /// متن ثابت دستی قبلی با واقعیت نسخهٔ ۰٫۲ (۱۹ خانوادهٔ پیاده‌شده) ناسازگار بود.
    /// </summary>
    private void RefreshStatusTexts()
    {
        var fams = CapabilityCatalog.GearFamilies;
        int implemented = fams.Count(f => f.Status == CapabilityStatus.Implemented);
        int limited = fams.Count(f => f.Status == CapabilityStatus.Limited);
        int roadmap = fams.Count(f => f.Status == CapabilityStatus.Roadmap);
        FamiliesStatus.Text =
            $"خانواده‌های هندسی: {fams.Count} مورد — {implemented} پیاده‌سازی‌شده" +
            (limited > 0 ? $"، {limited} محدود" : "") +
            (roadmap > 0 ? $"، {roadmap} در نقشهٔ راه" : "") +
            "؛ وضعیت هر خانواده هنگام انتخاب در طراح نمایش داده می‌شود.";

        var formats = CapabilityCatalog.ExportFormats;
        int active = formats.Count(f => f.Status != CapabilityStatus.Roadmap);
        var activeNames = string.Join("، ", formats.Where(f => f.Status != CapabilityStatus.Roadmap).Select(f => f.PersianTitle));
        var roadmapNames = string.Join("، ", formats.Where(f => f.Status == CapabilityStatus.Roadmap).Select(f => f.PersianTitle));
        FormatsStatus.Text = $"فعال ({active} فرمت): {activeNames}. نقشهٔ راه: {roadmapNames} — مش مثلثی به‌عنوان B-rep دقیق CAD ارائه نمی‌شود.";

        VersionLine.Text = $"Sahand Gear Designer v{AppInfo.Version} — {AppInfo.EnglishName}";
    }

    private void CreateSample_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var designer = _main.Designer;

        designer.NewProjectCommand.Execute(null);
        var part = new GearPart
        {
            Name = "چرخ‌دندهٔ نمونه ۲۰×۲",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { ToothCount = 20, Module = 2, FaceWidth = 20 },
            Body = new BodyFeatures { BoreDiameter = 10 },
        };
        designer.AttachPart(part);
        designer.SelectedPart = part;
        _main.SetStatus("نمونه ساخته شد — تب طراح را ببینید.");
    }

    // ---------- پروژه‌های اخیر (فاز ۵٫۳) ----------

    public void RefreshRecentFiles(IReadOnlyList<(string Path, string Title)> items)
    {
        RecentList.ItemsSource = items;
        RecentBox.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenRecent_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null || ((FrameworkElement)sender).Tag is not (string path, string _)) return;
        if (!File.Exists(path))
        {
            _main.SetStatus("فایل اخیر یافت نشد — شاید جابه‌جا یا حذف شده باشد.");
            return;
        }
        _main.OpenPath(path);
    }
}
