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

    // ---------- کتابخانهٔ نمونه‌ها (فاز ۵٫۳) ----------

    /// <summary>جفت خارجی درگیر با فاصلهٔ محور درست و فاز تماس.</summary>
    private void SamplePair_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var d = _main.Designer;
        d.NewProjectCommand.Execute(null);
        d.ProjectName = "نمونهٔ جفت خارجی ۲۰×۳۰";

        var a = new GearPart
        {
            Name = "چرخ ۲۰ دندانه",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 20, FaceWidth = 16 },
            Body = new BodyFeatures { BoreDiameter = 8 },
            DisplayColor = "#2563EB",
        };
        var b = new GearPart
        {
            Name = "چرخ ۳۰ دندانه",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 30, FaceWidth = 16 },
            Body = new BodyFeatures { BoreDiameter = 10 },
            DisplayColor = "#0D9488",
            Placement = new Placement { X = 50.5, Y = 0, Theta = Math.PI / 20 },
        };
        d.AttachPart(a);
        d.AttachPart(b);
        d.AddMating(new SahandGearMaker.Domain.MatingRelation
        {
            Kind = SahandGearMaker.Domain.MatingKind.ExternalPair,
            PartAId = a.Id,
            PartBId = b.Id,
        });
        d.SelectedPart = a;
        _main.NavList.SelectedIndex = 1;
        _main.SetStatus("نمونهٔ جفت خارجی باز شد — تب مونتاژ را برای پخش حرکت ببینید.");
    }

    private void SampleInternal_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var d = _main.Designer;
        d.NewProjectCommand.Execute(null);
        d.ProjectName = "نمونهٔ جفت داخلی";

        var pinion = new GearPart
        {
            Name = "پینیون ۱۶ دندانه",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 16, FaceWidth = 14 },
            Body = new BodyFeatures { BoreDiameter = 8 },
            DisplayColor = "#DC2626",
        };
        var ring = new GearPart
        {
            Name = "حلقهٔ داخلی ۴۸ دندانه",
            Family = GearFamily.InternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 48, FaceWidth = 14 },
            DisplayColor = "#0D9488",
        };
        d.AttachPart(pinion);
        d.AttachPart(ring);
        d.AddMating(new SahandGearMaker.Domain.MatingRelation
        {
            Kind = SahandGearMaker.Domain.MatingKind.InternalPair,
            PartAId = pinion.Id,
            PartBId = ring.Id,
        });
        d.SelectedPart = ring;
        _main.NavList.SelectedIndex = 1;
        _main.SetStatus("نمونهٔ جفت داخلی باز شد.");
    }

    private void SampleHelical_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var d = _main.Designer;
        d.NewProjectCommand.Execute(null);
        d.ProjectName = "نمونهٔ مارپیچ ۲۴";
        var part = new GearPart
        {
            Name = "چرخ‌دندهٔ مارپیچ ۲۴",
            Family = GearFamily.ExternalHelical,
            Teeth = new ToothParameters { Module = 2.5, ToothCount = 24, FaceWidth = 24, HelixAngle = UnitConverter.DegToRad(22) },
            Body = new BodyFeatures { BoreDiameter = 10, LighteningHoleCount = 5, LighteningHoleDiameter = 6, LighteningHoleCircleRadius = 20 },
        };
        d.AttachPart(part);
        d.SelectedPart = part;
        _main.NavList.SelectedIndex = 1;
        _main.SetStatus("نمونهٔ مارپیچ باز شد.");
    }

    private void SampleRack_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var d = _main.Designer;
        d.NewProjectCommand.Execute(null);
        d.ProjectName = "نمونهٔ شانه و پینیون";

        var pinion = new GearPart
        {
            Name = "پینیون ۱۸ دندانه",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 18, FaceWidth = 14 },
            Body = new BodyFeatures { BoreDiameter = 8 },
            DisplayColor = "#2563EB",
        };
        var rack = new GearPart
        {
            Name = "شانهٔ ۱۲۰ میلی‌متری",
            Family = GearFamily.Rack,
            Teeth = new ToothParameters { Module = 2, ToothCount = 20, FaceWidth = 14 },
            Specific = new FamilyParameters { RackLengthMm = 120 },
            DisplayColor = "#D97706",
            Placement = new Placement { X = 0, Y = -18 },
        };
        d.AttachPart(pinion);
        d.AttachPart(rack);
        d.AddMating(new SahandGearMaker.Domain.MatingRelation
        {
            Kind = SahandGearMaker.Domain.MatingKind.RackPinion,
            PartAId = pinion.Id,
            PartBId = rack.Id,
        });
        d.SelectedPart = pinion;
        _main.NavList.SelectedIndex = 1;
        _main.SetStatus("نمونهٔ شانه و پینیون باز شد — در تب مونتاژ حرکت را پخش کنید.");
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
