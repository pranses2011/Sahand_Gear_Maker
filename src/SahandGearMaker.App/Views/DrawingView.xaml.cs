using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Domain;
using SahandGearMaker.Drawing;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;

namespace SahandGearMaker.App.Views;

/// <summary>
/// نمای «نقشهٔ فنی» (فاز ۲٫۲): پیش‌نمایش زندهٔ نقشهٔ فنی فارسی از موتور Drawing
/// + خروجی SVG/PDF — پیش‌تر فقط CLI به این موتور دسترسی داشت.
/// </summary>
public partial class DrawingView : UserControl
{
    private MainWindow? _main;
    private DesignerViewModel VM => _main!.Designer;
    private TechnicalDrawingDocument? _doc;

    // ----- زوم پیش‌نمایش (ورق در Canvas با LayoutTransform مقیاس می‌شود) -----
    private double _zoom = 1.0;
    private readonly System.Windows.Media.ScaleTransform _sheetZoom = new();

    public DrawingView()
    {
        InitializeComponent();
        SheetCanvas.LayoutTransform = _sheetZoom;
        ApplyZoom();
    }

    public void Bind(MainWindow main)
    {
        _main = main;
        VM.Rebuilt += (_, _) => Dispatcher.Invoke(Refresh);
        Refresh();
    }

    public void Refresh()
    {
        if (_main is null) return;
        var parts = VM.Parts.Where(p => VM.HasBuild(p)).ToList();
        PartCombo.ItemsSource = parts.Select(p => p.Name).ToList();
        if (PartCombo.SelectedIndex < 0 && parts.Count > 0)
        {
            var sel = VM.SelectedPart is { } s && parts.Contains(s) ? s : parts[0];
            PartCombo.SelectedIndex = parts.IndexOf(sel);
        }
        else if (parts.Count == 0)
        {
            _doc = null;
            SheetCanvas.Children.Clear();
            EmptyHint.Visibility = Visibility.Visible;
        }
        RenderDrawing();
    }

    private void PartCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderDrawing();
    }

    private GearPart? CurrentPart()
    {
        var parts = VM.Parts.Where(p => VM.HasBuild(p)).ToList();
        int idx = PartCombo.SelectedIndex;
        return idx >= 0 && idx < parts.Count ? parts[idx] : null;
    }

    private void RenderDrawing()
    {
        if (_main is null) return;
        SheetCanvas.Children.Clear();

        var part = CurrentPart();
        if (part is null || VM.BuildOf(part) is not { } build)
        {
            EmptyHint.Visibility = Visibility.Visible;
            _doc = null;
            PartSummary.Text = "";
            FiguresList.ItemsSource = null;
            return;
        }

        try
        {
            _doc = TechnicalDrawingBuilder.Build(part, build, VM.ProjectName);
        }
        catch (Exception ex)
        {
            _doc = null;
            EmptyHint.Visibility = Visibility.Visible;
            _main.SetStatus($"ساخت نقشهٔ فنی ناموفق بود: {ex.Message}");
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        DrawSheet(_doc);

        PartSummary.Text = $"{part.Name} — {build.Mesh.TriangleCount:N0} مثلث، تلرانس وتر {build.EffectiveChordToleranceMm:0.###} mm";
        FiguresList.ItemsSource = new[]
        {
            new Kv("تعداد دندانه", part.Teeth.ToothCount.ToString(CultureInfo.InvariantCulture)),
            new Kv("ماژول (mm)", part.Teeth.Module.ToString("0.###", CultureInfo.InvariantCulture)),
            new Kv("زاویهٔ فشار", $"{UnitConverter.RadToDeg(part.Teeth.PressureAngle):0.##}°"),
            new Kv("زاویهٔ مارپیچ", $"{UnitConverter.RadToDeg(part.Teeth.HelixAngle):0.##}°"),
            new Kv("پهنای دندانه (mm)", part.Teeth.FaceWidth.ToString("0.##", CultureInfo.InvariantCulture)),
            new Kv("قطر گام (mm)", (part.Teeth.Module * part.Teeth.ToothCount).ToString("0.##", CultureInfo.InvariantCulture)),
            new Kv("قطر نوک (mm)", (part.Teeth.Module * (part.Teeth.ToothCount + 2)).ToString("0.##", CultureInfo.InvariantCulture)),
            new Kv("حجم تقریبی (cm³)", (build.Figures.EstimatedVolumeMm3 / 1000.0).ToString("0.##", CultureInfo.InvariantCulture)),
        };
    }

    private sealed record Kv(string Key, string Value);

    // ---------- زوم ورق (رفع «نقشهٔ فنی زوم ندارد») ----------

    private const double ZoomMin = 0.2, ZoomMax = 8.0;

    private void ApplyZoom()
    {
        _sheetZoom.ScaleX = _zoom;
        _sheetZoom.ScaleY = _zoom;
        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{_zoom * 100:0}%";
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, ZoomMin, ZoomMax);
        ApplyZoom();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);

    private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        double sw = Math.Max(50, SheetScroll.ViewportWidth - 30);
        double sh = Math.Max(50, SheetScroll.ViewportHeight - 30);
        double cw = Math.Max(1, SheetCanvas.Width + 24);
        double ch = Math.Max(1, SheetCanvas.Height + 24);
        SetZoom(Math.Min(sw / cw, sh / ch));
    }

    /// <summary>چرخ ماوس روی ورق = زوم (مستقیم و بدون نیاز به Ctrl).</summary>
    private void SheetHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(_zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12));
        e.Handled = true;
    }

    /// <summary>رسم ورق A4 روی Canvas — مرزها، خطوط، متن و جدول.</summary>
    private void DrawSheet(TechnicalDrawingDocument doc)
    {
        double pxPerMm = Math.Min(SheetCanvas.Width / doc.SheetWidthMm, SheetCanvas.Height / doc.SheetHeightMm);
        var canvas = SheetCanvas;
        canvas.Children.Clear();
        canvas.Width = doc.SheetWidthMm * pxPerMm;
        canvas.Height = doc.SheetHeightMm * pxPerMm;

        // کادر ورق + کادر عنوان
        AddRect(canvas, 4 * pxPerMm, 4 * pxPerMm,
            (doc.SheetWidthMm - 8) * pxPerMm, (doc.SheetHeightMm - 8) * pxPerMm, 1.2, Colors.Black);
        AddRect(canvas, 4 * pxPerMm, (doc.SheetHeightMm - 30) * pxPerMm,
            (doc.SheetWidthMm - 8) * pxPerMm, 26 * pxPerMm, 0.8, Colors.Black);

        AddText(canvas, 8 * pxPerMm, 8 * pxPerMm, doc.Title, 4.2, true, Colors.Black);
        AddText(canvas, 8 * pxPerMm, 14 * pxPerMm, $"پروژه: {doc.ProjectName}", 3.0, false, Color.FromRgb(0x33, 0x44, 0x55));
        AddText(canvas, 8 * pxPerMm, (doc.SheetHeightMm - 26) * pxPerMm, $"قطعه: {doc.PartName}", 3.2, true, Colors.Black);
        AddText(canvas, 8 * pxPerMm, (doc.SheetHeightMm - 21) * pxPerMm, doc.UnitsNote, 2.6, false, Color.FromRgb(0x44, 0x55, 0x66));
        AddText(canvas, 8 * pxPerMm, (doc.SheetHeightMm - 16) * pxPerMm, $"تاریخ: {doc.Date}", 2.6, false, Color.FromRgb(0x44, 0x55, 0x66));

        double y = 24 * pxPerMm;

        // نماها — نمای اول سهم ارتفاع بیشتر، نماهای بعدی فشرده‌تر (ورق A4 با چند نما سرریز نکند)
        int viewIndex = 0;
        foreach (var view in doc.Views)
        {
            var b = view.Bounds;
            double vw = Math.Max(1, b.MaxX - b.MinX), vh = Math.Max(1, b.MaxY - b.MinY);
            AddText(canvas, 8 * pxPerMm, y, view.Title, 3.0, true, Color.FromRgb(0x22, 0x33, 0x44));
            y += 5 * pxPerMm;

            double maxH = viewIndex++ == 0 ? 86.0 : 48.0;
            double scale = Math.Min((doc.SheetWidthMm - 20) / vw, maxH / vh) * pxPerMm;
            double ox = 8 * pxPerMm, oy = y;

            foreach (var outline in view.Outlines)
            {
                if (outline.Count < 2) continue;
                var poly = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.9,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                foreach (var (x, yy) in outline)
                    poly.Points.Add(new Point(ox + (x - b.MinX) * scale, oy + (yy - b.MinY) * scale));
                if (outline.Count > 2)
                    poly.Points.Add(poly.Points[0]);
                canvas.Children.Add(poly);
            }

            foreach (var line in view.Lines)
            {
                Brush stroke = line.Style switch
                {
                    "center" => Brushes.Gray,
                    "pitch" => Brushes.DodgerBlue,
                    _ => Brushes.Black, // لبه‌های ایزومتریک و کانتورهای اصلی — سیاه مهندسی
                };
                var ln = new Line
                {
                    X1 = ox + (line.X1 - b.MinX) * scale,
                    Y1 = oy + (line.Y1 - b.MinY) * scale,
                    X2 = ox + (line.X2 - b.MinX) * scale,
                    Y2 = oy + (line.Y2 - b.MinY) * scale,
                    Stroke = stroke,
                    StrokeThickness = Math.Clamp(line.WidthMm * pxPerMm * 1.6, 0.45, 1.4),
                    StrokeDashArray = line.Style is "center" or "pitch" ? new DoubleCollection { 4, 3 } : null,
                };
                canvas.Children.Add(ln);
            }

            foreach (var txt in view.Texts)
            {
                AddText(canvas,
                    ox + (txt.X - b.MinX) * scale,
                    oy + (txt.Y - b.MinY) * scale,
                    txt.Text, txt.SizeMm, txt.Bold, Colors.Black);
            }

            y += vh * scale + 6 * pxPerMm;
        }

        // جدول پارامترها
        AddText(canvas, 8 * pxPerMm, y, "جدول مشخصات", 3.0, true, Color.FromRgb(0x22, 0x33, 0x44));
        y += 4 * pxPerMm;
        foreach (var row in doc.Parameters)
        {
            AddText(canvas, 8 * pxPerMm, y, $"{row.Label}:", 2.6, false, Colors.Black);
            var v = new TextBlock
            {
                Text = $"{row.Value} {row.Unit}",
                FontSize = 2.7 * pxPerMm,
                FlowDirection = FlowDirection.LeftToRight,
                Foreground = Brushes.Black,
            };
            Canvas.SetLeft(v, (doc.SheetWidthMm - 70) * pxPerMm);
            Canvas.SetTop(v, y);
            canvas.Children.Add(v);
            y += 3.6 * pxPerMm;
        }

        // یادداشت‌ها
        foreach (var note in doc.Notes)
        {
            AddText(canvas, 8 * pxPerMm, y, "• " + note, 2.4, false, Color.FromRgb(0x55, 0x55, 0x55));
            y += 3.2 * pxPerMm;
        }
    }

    private static void AddRect(Canvas c, double x, double y, double w, double h, double thickness, Color color)
    {
        var r = new Rectangle
        {
            Width = w,
            Height = h,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
        };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        c.Children.Add(r);
    }

    private static void AddText(Canvas c, double x, double y, string text, double sizeMm, bool bold, Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = sizeMm * 3.4,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(color),
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        c.Children.Add(tb);
    }

    // ---------- خروجی ----------

    private void ExportSvg_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null) { _main?.SetStatus("ابتدا قطعهٔ معتبری انتخاب کنید."); return; }
        var dlg = new SaveFileDialog
        {
            Title = "خروجی SVG",
            Filter = "SVG|*.svg",
            FileName = MeshExporter.SanitizeName(_doc.PartName) + ".svg",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            SvgDrawingWriter.WriteToFile(_doc, dlg.FileName);
            _main?.SetStatus($"نقشهٔ SVG ذخیره شد: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"خروجی SVG ناموفق بود: {ex.Message}", "نقشهٔ فنی",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null) { _main?.SetStatus("ابتدا قطعهٔ معتبری انتخاب کنید."); return; }
        var dlg = new SaveFileDialog
        {
            Title = "خروجی PDF",
            Filter = "PDF|*.pdf",
            FileName = MeshExporter.SanitizeName(_doc.PartName) + ".pdf",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            PdfDrawingWriter.Write(fs, _doc);
            _main?.SetStatus($"نقشهٔ PDF ذخیره شد: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"خروجی PDF ناموفق بود: {ex.Message}", "نقشهٔ فنی",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
