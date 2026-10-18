using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SahandGearMaker.App.Controls;

/// <summary>دیالوگ انتخاب رنگ بومی WPF — پالت مهندسی + ورودی هگز؛ بدون WinForms (فاز ۱٫۲۲).</summary>
public partial class ColorPickDialog : Window
{
    private static readonly string[] PaletteHex =
    {
        "#2563EB", "#0EA5E9", "#0D9488", "#16A34A", "#65A30D",
        "#D97706", "#EA580C", "#DC2626", "#DB2777", "#9333EA",
        "#475569", "#0F172A", "#78716C", "#F59E0B", "#E2E8F0",
    };

    public string SelectedHex { get; private set; } = "#2563EB";

    public ColorPickDialog(string initialHex)
    {
        InitializeComponent();
        SelectedHex = Normalize(initialHex);
        HexBox.Text = SelectedHex;

        foreach (var hex in PaletteHex)
        {
            var swatch = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Parse(hex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = hex,
                ToolTip = hex,
            };
            swatch.MouseLeftButtonUp += Swatch_Click;
            Palette.Children.Add(swatch);
        }
    }

    private static string Normalize(string hex)
        => hex.StartsWith('#') ? hex : "#" + hex;

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(Normalize(hex)); }
        catch { return Color.FromRgb(0x25, 0x63, 0xEB); }
    }

    private void Swatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string hex)
        {
            SelectedHex = hex;
            HexBox.Text = hex; // TextChanged پیش‌نمایش را به‌روز می‌کند
        }
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hex = HexBox.Text.Trim();
        if (hex.Length is 7 or 4 && hex.StartsWith('#'))
        {
            try
            {
                Preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                SelectedHex = hex;
            }
            catch { /* هگز نامعتبر — پیش‌نمایش تغییر نمی‌کند */ }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>نمایش دیالوگ؛ null اگر لغو شد.</summary>
    public static string? Show(string initialHex)
    {
        var dlg = new ColorPickDialog(initialHex);
        return dlg.ShowDialog() == true ? dlg.SelectedHex : null;
    }
}
