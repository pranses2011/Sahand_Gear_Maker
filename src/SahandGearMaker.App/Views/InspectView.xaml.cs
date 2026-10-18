using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.App.Controls;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.App.Views;

/// <summary>
/// نمای «بررسی» (فاز ۴): ابزار اندازه‌گیری روی بوم طراح، صفحهٔ برش نمایشی،
/// گزارش تحلیل هندسی (نسبت تماس/undercut/ضخامت نوک)، بررسی برخورد تقریبی
/// و نمای دوم ارتوگرافیک (Split View).
/// </summary>
public partial class InspectView : UserControl
{
    private MainWindow? _main;
    private DesignerViewModel VM => _main!.Designer;

    public InspectView()
    {
        InitializeComponent();
    }

    public void Bind(MainWindow main)
    {
        _main = main;
        VM.Rebuilt += (_, _) => Dispatcher.Invoke(Refresh);
        var designerViewport = main.DesignerViewport;
        if (designerViewport is not null)
        {
            designerViewport.MeasureUpdated += Viewport_MeasureUpdated;
        }
        SecondViewport.PickRequested += (s, id) =>
        {
            if (id is not null) main.SelectPartById(id);
        };
        SecondViewport.ApplyMouseSettings(0.008, 1);
        Refresh();
        ApplySecondView();
    }

    public void Refresh()
    {
        if (_main is null) return;
        var names = VM.Parts.Select(p => p.Name).ToList();
        AnalysisPartCombo.ItemsSource = names.ToList();
        CollideACombo.ItemsSource = names.ToList();
        CollideBCombo.ItemsSource = names.ToList();
        if (AnalysisPartCombo.SelectedIndex < 0 && names.Count > 0) AnalysisPartCombo.SelectedIndex = 0;
        if (CollideACombo.SelectedIndex < 0 && names.Count > 0) CollideACombo.SelectedIndex = 0;
        if (CollideBCombo.SelectedIndex < 1 && names.Count > 1) CollideBCombo.SelectedIndex = 1;

        // صحنهٔ دوم همان صحنهٔ طراح است
        SecondViewport.UpdateScene(VM.SceneSnapshot(), VM.SelectedPart?.Id);

        // محدودهٔ اسلایدر برش از صحنه
        var bounds = SahandGearMaker.Scene.SceneRenderer.CombinedBounds(VM.SceneSnapshot());
        _clipMinZ = bounds.Min.Z;
        _clipMaxZ = bounds.Max.Z;
        ClipLabel.Text = $"Z از {_clipMinZ:0.#} تا {_clipMaxZ:0.#} mm";
    }

    /// <summary>تازه‌سازی عمومی — از MainWindow.RefreshDesignerScene (فاز ۳/۴).</summary>
    public void RefreshPublic() => Refresh();

    private double _clipMinZ, _clipMaxZ;

    // ---------- ۴٫۱ اندازه‌گیری ----------

    private void ToggleMeasure_Click(object sender, RoutedEventArgs e)
    {
        var vp = _main?.DesignerViewport;
        if (vp is null) return;
        vp.EnableMeasure(!vp.IsMeasuring);
        BtnMeasure.Content = vp.IsMeasuring ? "📏 غیرفعال‌سازی" : "📏 فعال‌سازی";
        if (vp.IsMeasuring)
        {
            _main.NavList.SelectedIndex = 1; // برو به بوم طراح
            _main.SetStatus("حالت اندازه‌گیری فعال — هر کلیک یک نقطه؛ دو نقطه = فاصله، سه نقطه = زاویه (M برای قطع).");
        }
    }

    private void ClearMeasure_Click(object sender, RoutedEventArgs e)
    {
        _main?.DesignerViewport?.ClearMeasure();
        MeasureResult.Text = "";
    }

    private void Viewport_MeasureUpdated(object? sender, IReadOnlyList<Vector3> points)
    {
        Dispatcher.Invoke(() =>
        {
            if (points.Count == 1)
                MeasureResult.Text = "یک نقطه ثبت شد — نقطهٔ دوم را کلیک کنید.";
            else if (points.Count == 2)
                MeasureResult.Text = $"فاصله: {(points[0] - points[1]).Length:0.###} mm";
            else if (points.Count == 3)
            {
                var v1 = points[0] - points[1];
                var v2 = points[2] - points[1];
                double ang = Math.Acos(Math.Clamp(Vector3.Dot(v1, v2) / Math.Max(1e-9, v1.Length * v2.Length), -1, 1));
                MeasureResult.Text = $"فاصله ۱-۲: {(points[0] - points[1]).Length:0.###} mm | فاصله ۲-۳: {(points[1] - points[2]).Length:0.###} mm | زاویه: {ang * 180 / Math.PI:0.##}°";
            }
        });
    }

    // ---------- ۴٫۲ صفحهٔ برش ----------

    private void Clip_Changed(object sender, RoutedEventArgs e)
    {
        if (_main?.DesignerViewport is null || ClipSlider is null) return;
        ClipSlider.IsEnabled = ClipEnabled.IsChecked == true;
        double clip = double.PositiveInfinity;
        if (ClipEnabled.IsChecked == true)
        {
            double t = ClipSlider.Value / 100.0;
            clip = _clipMinZ + (_clipMaxZ - _clipMinZ) * t;
        }
        _main.DesignerViewport.SetClipPlane(clip);
    }

    // ---------- ۴٫۳ گزارش تحلیل ----------

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var parts = VM.Parts.ToList();
        if (AnalysisPartCombo.SelectedIndex < 0 || AnalysisPartCombo.SelectedIndex >= parts.Count)
        {
            AnalysisResult.Text = "قطعه‌ای انتخاب نشده.";
            return;
        }
        var part = parts[AnalysisPartCombo.SelectedIndex];
        try
        {
            var report = SahandGearMaker.Assembly.GearAnalysisCalculator.Analyze(part.Teeth);
            AnalysisResult.Text =
                $"نسبت تماس عرضی: {report.ContactRatio:0.###}\n" +
                $"حداقل دندانهٔ بدون undercut: {report.MinTeethNoUndercut:0.#} (فعلی: {part.Teeth.ToothCount})\n" +
                $"احتمال undercut: {(report.UndercutLikely ? "بله ⚠" : "خیر")}\n" +
                $"ضخامت نوک دندانه: {report.TipThicknessMm:0.###} mm\n" +
                (report.Diagnostics.Count > 0
                    ? "پیام‌ها: " + string.Join("؛ ", report.Diagnostics.Select(d => d.Message))
                    : "بدون هشدار هندسی.");
            AnalysisResult.Text += "\n(گزارش هندسی است، نه گواهی استحکام/استاندارد.)";
        }
        catch (Exception ex)
        {
            AnalysisResult.Text = $"محاسبه ناموفق: {ex.Message}";
        }
    }

    private void Collide_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var parts = VM.Parts.ToList();
        if (CollideACombo.SelectedIndex < 0 || CollideBCombo.SelectedIndex < 0)
        {
            CollideResult.Text = "دو قطعه انتخاب کنید.";
            return;
        }
        var a = parts[CollideACombo.SelectedIndex];
        var b = parts[CollideBCombo.SelectedIndex];
        var buildA = VM.BuildOf(a);
        var buildB = VM.BuildOf(b);
        if (buildA is null || buildB is null)
        {
            CollideResult.Text = "هر دو قطعه باید ساخت معتبر داشته باشند.";
            return;
        }
        try
        {
            // مش‌ها در محل نمایشی برای بررسی واقعیِ مونتاژ
            var meshA = buildA.Mesh.Transformed(
                new Vector3(a.Placement.X, a.Placement.Y, a.Placement.Z), a.Placement.Theta);
            var meshB = buildB.Mesh.Transformed(
                new Vector3(b.Placement.X, b.Placement.Y, b.Placement.Z), b.Placement.Theta);
            var result = SahandGearMaker.Assembly.InterferenceChecker.Check(meshA, meshB);
            CollideResult.Text = result.HasInterference
                ? $"⛔ برخورد یافت شد — عمق نفوذ تقریبی: {result.PenetrationDepthMm:0.##} mm"
                : result.MinDistanceMm is { } d
                    ? $"✓ بدون برخورد — کمینهٔ فاصله: {d:0.##} mm"
                    : "✓ بدون برخورد";
            CollideResult.Text += "\n(تقریب گسستهٔ مثلث‌ها — برای گواهی نهایی از تحلیل CAD استفاده کنید.)";
        }
        catch (Exception ex)
        {
            CollideResult.Text = $"بررسی ناموفق: {ex.Message}";
        }
    }

    // ---------- ۴٫۴ نمای دوم ----------

    private void SecondView_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_main is null) return;
        ApplySecondView();
    }

    private void ApplySecondView()
    {
        var cam = new SahandGearMaker.Scene.SceneCamera
        {
            Projection = SahandGearMaker.Scene.ProjectionKind.Orthographic,
        };
        var bounds = SahandGearMaker.Scene.SceneRenderer.CombinedBounds(VM.SceneSnapshot());
        double size = Math.Max(20, Math.Max(
            bounds.Max.X - bounds.Min.X,
            Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z)));
        var center = new Vector3(
            (bounds.Min.X + bounds.Max.X) / 2,
            (bounds.Min.Y + bounds.Max.Y) / 2,
            (bounds.Min.Z + bounds.Max.Z) / 2);
        switch (SecondViewCombo.SelectedIndex)
        {
            case 0: cam.SetView(0, 0.0001, size * 1.6, center, animate: false); break;
            case 1: cam.SetView(0, 1.45, size * 1.6, center, animate: false); break;
            case 2: cam.SetView(-Math.PI / 2, 0.0001, size * 1.6, center, animate: false); break;
            default: cam.SetView(Math.PI / 6, Math.PI / 5, size * 2, center, animate: false); break;
        }
        SecondViewport.SetCameraDirect(cam);
        SecondViewport.UpdateScene(VM.SceneSnapshot(), VM.SelectedPart?.Id);
    }
}
