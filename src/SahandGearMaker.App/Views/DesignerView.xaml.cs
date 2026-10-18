using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Domain;
using SahandGearMaker.Rendering;
using SahandGearMaker.Exports;
using Key = System.Windows.Input.Key;

namespace SahandGearMaker.App.Views;

/// <summary>
/// نمای طراح: فرم پارامتر، بوم سه‌بعدی و درخت پروژه (سند ۰۲، بخش ۶).
/// اعداد در فیلدهای چپ‌به‌راست نمایش می‌یابند تا با RTL مخلوط نشوند (سند ۰۵، بخش ۶).
/// </summary>
public partial class DesignerView : UserControl
{
    private MainWindow? _main;
    private DesignerViewModel VM => _main!.Designer;
    private OrbitCamera _camera = new();
    private readonly System.Timers.DispatcherTimer? _renderTimer;
    private bool _renderQueued;
    private bool _dragging;
    private Point _lastMouse;
    private SoftwareRenderer.DisplayMode _mode = SoftwareRenderer.DisplayMode.ShadedWithEdges;

    public DesignerView()
    {
        InitializeComponent();
        Loaded += (_, _) => QueueRender();
        SizeChanged += (_, _) => QueueRender();
    }

    public void Bind(MainWindow main)
    {
        _main = main;

        FamilyCombo.ItemsSource = VM.SupportedFamilies.Select(f => L.Get($"Family.{f}")).ToList();
        PartsList.ItemsSource = VM.Parts;
        DiagnosticsList.ItemsSource = VM.Diagnostics;
        VM.PropertyChanged += (_, _) => Dispatcher.Invoke(() => { SyncFromPart(); QueueRender(); });

        // انتخاب اولین قطعه و همگام‌سازی اولیه
        if (VM.Parts.Count > 0 && VM.SelectedPart is null)
            VM.SelectedPart = VM.Parts[0];
        SyncFromPart();
    }

    // ---------- فرم ↔ مدل ----------

    private void SyncFromPart()
    {
        if (_main is null) return;
        var p = VM.SelectedPart;
        if (p is null) return;

        PartName.Text = p.Name;
        int famIdx = IndexOfFamily(p.Family);
        if (famIdx >= 0 && FamilyCombo.SelectedIndex != famIdx) FamilyCombo.SelectedIndex = famIdx;

        ToothCount.Text = p.Teeth.ToothCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Module.Text = Fmt(p.Teeth.Module);
        PressureAngle.Text = Fmt(UnitConverter.RadToDeg(p.Teeth.PressureAngle));
        HelixAngle.Text = Fmt(UnitConverter.RadToDeg(p.Teeth.HelixAngle));
        FaceWidth.Text = Fmt(p.Teeth.FaceWidth);
        BoreDiameter.Text = Fmt(p.Body.BoreDiameter);
        KeywayWidth.Text = Fmt(p.Body.KeywayWidth);
        HubDiameter.Text = Fmt(p.Body.HubDiameter);

        PartsList.Items.Refresh();
        FamilyNote.Text = CapabilityNote(p.Family);
    }

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private int IndexOfFamily(GearFamily f)
    {
        var arr = VM.SupportedFamilies;
        for (int i = 0; i < arr.Count; i++)
            if (arr[i] == f) return i;
        return -1;
    }

    private static string CapabilityNote(GearFamily f)
    {
        var info = CapabilityCatalog.GearFamilies.First(x => x.Key == f.ToString());
        return info.Status switch
        {
            CapabilityStatus.Implemented => $"وضعیت: پیاده‌سازی‌شده — {info.Note}",
            CapabilityStatus.Limited => $"وضعیت: محدود — {info.Note}",
            _ => "وضعیت: در نقشهٔ راه",
        };
    }

    private bool _syncing;

    private void PushToModel()
    {
        if (_syncing || _main is null) return;
        var p = VM.SelectedPart;
        if (p is null) return;

        p.Name = PartName.Text;
        p.Teeth.ToothCount = ParseInt(ToothCount.Text, p.Teeth.ToothCount);
        p.Teeth.Module = ParseDouble(Module.Text, p.Teeth.Module);
        p.Teeth.PressureAngle = UnitConverter.DegToRad(ParseDouble(PressureAngle.Text, UnitConverter.RadToDeg(p.Teeth.PressureAngle)));
        p.Teeth.HelixAngle = UnitConverter.DegToRad(ParseDouble(HelixAngle.Text, UnitConverter.RadToDeg(p.Teeth.HelixAngle)));
        p.Teeth.FaceWidth = ParseDouble(FaceWidth.Text, p.Teeth.FaceWidth);
        p.Body.BoreDiameter = ParseDouble(BoreDiameter.Text, p.Body.BoreDiameter);
        p.Body.KeywayWidth = ParseDouble(KeywayWidth.Text, p.Body.KeywayWidth);
        p.Body.HubDiameter = ParseDouble(HubDiameter.Text, p.Body.HubDiameter);

        VM.Rebuild();
        QueueRender();
    }

    private static double ParseDouble(string s, double fallback)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // ---------- رویدادها ----------

    private void PartName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || VM.SelectedPart is { } p) PushToModel();
    }

    private void Numeric_TextChanged(object sender, TextChangedEventArgs e) => PushToModel();

    private void FamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _main is null || FamilyCombo.SelectedIndex < 0) return;
        var fam = VM.SupportedFamilies[FamilyCombo.SelectedIndex];
        if (VM.SelectedPart is { } p && p.Family != fam)
        {
            p.Family = fam;
            FamilyNote.Text = CapabilityNote(fam);
            VM.Rebuild();
            QueueRender();
        }
    }

    private void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        VM.Rebuild();
        QueueRender();
        _main?.SetStatus(VM.LastBuild is null
            ? "ساخت هندسه ناموفق — پیام‌ها را ببینید."
            : $"هندسه ساخته شد: {VM.LastBuild.Mesh.TriangleCount:N0} مثلث، {VM.LastBuild.AxialSlicesUsed} برش عرضی.");
    }

    private void AddPart_Click(object sender, RoutedEventArgs e)
    {
        VM.AddPartCommand.Execute(null);
        VM.SelectedPart = VM.Parts.LastOrDefault();
        SyncFromPart();
        QueueRender();
    }

    private void DuplicatePart_Click(object sender, RoutedEventArgs e)
    {
        VM.DuplicatePartCommand.Execute(VM.SelectedPart);
        VM.SelectedPart = VM.Parts.LastOrDefault();
        SyncFromPart();
        QueueRender();
    }

    private void RemovePart_Click(object sender, RoutedEventArgs e)
    {
        VM.RemovePartCommand.Execute(VM.SelectedPart);
        SyncFromPart();
        QueueRender();
    }

    private void PartsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing && PartsList.SelectedItem is GearPart p && !ReferenceEquals(p, VM.SelectedPart))
        {
            _syncing = true;
            VM.SelectedPart = p;
            _syncing = false;
            SyncFromPart();
            QueueRender();
        }
    }

    // ---------- بوم سه‌بعدی ----------

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _lastMouse = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ViewportHost.ReleaseMouseCapture();
    }

    private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // راست‌کلیک: تغییر حالت نمایش (سایه/سیمی/لبه‌دار)
        _mode = _mode switch
        {
            SoftwareRenderer.DisplayMode.ShadedWithEdges => SoftwareRenderer.DisplayMode.Shaded,
            SoftwareRenderer.DisplayMode.Shaded => SoftwareRenderer.DisplayMode.Wireframe,
            _ => SoftwareRenderer.DisplayMode.ShadedWithEdges,
        };
        QueueRender();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(ViewportHost);
        double dx = pos.X - _lastMouse.X;
        double dy = pos.Y - _lastMouse.Y;
        _lastMouse = pos;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _camera.Pan(-dx, -dy);
        else
            _camera.Rotate(-dx * 0.008, -dy * 0.008);
        QueueRender();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _camera.Zoom(e.Delta > 0 ? 0.9 : 1.1);
        QueueRender();
    }

    private void View_Iso(object sender, RoutedEventArgs e)
    {
        _camera = new OrbitCamera { Distance = _camera.Distance };
        QueueRender();
    }

    private void View_Front(object sender, RoutedEventArgs e)
    {
        _camera.Elevation = 0.0001; _camera.Azimuth = 0;
        QueueRender();
    }

    private void View_Top(object sender, RoutedEventArgs e)
    {
        _camera.Elevation = 1.45; _camera.Azimuth = 0;
        QueueRender();
    }

    private void View_Side(object sender, RoutedEventArgs e)
    {
        _camera.Elevation = 0.0001; _camera.Azimuth = Math.PI / 2;
        QueueRender();
    }

    private void View_Fit(object sender, RoutedEventArgs e)
    {
        var build = VM.LastBuild;
        if (build is null) return;
        var (min, max) = build.Mesh.Bounds();
        double size = Math.Max(1, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
        _camera.Target = new MathX.Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        _camera.ZoomTo(size * 2.6);
        QueueRender();
    }

    private void QueueRender()
    {
        if (_renderQueued || _main is null) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _renderQueued = false;
            RenderNow();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RenderNow()
    {
        if (_main is null || ViewportHost.ActualWidth < 10 || ViewportHost.ActualHeight < 10) return;

        int w = (int)ViewportHost.ActualWidth;
        int h = (int)ViewportHost.ActualHeight;
        var renderer = new SoftwareRenderer(w, h);
        renderer.Clear(0xEA, 0xEE, 0xF4);

        var build = VM.LastBuild;
        if (build is not null)
        {
            var color = Color.FromRgb(0x25, 0x63, 0xEB);
            renderer.Render(build.Mesh, _camera, _mode, color);
        }
        else
        {
            // نمای خالی: نماد چرخ‌دنده ساده برای شروع (سند ۰۲، بخش ۸)
        }

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        renderer.Blit(bmp);
        ViewportImage.Source = bmp;
    }
}
