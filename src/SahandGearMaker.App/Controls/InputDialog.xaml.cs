using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SahandGearMaker.App.Controls;

/// <summary>دیالوگ ورودی متن سادهٔ بومی WPF — بدون وابستگی به WinForms/VB (آفلاین کامل).</summary>
public partial class InputDialog : Window
{
    public string Value => ValueBox.Text;

    public InputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }

    /// <summary>نمایش و دریافت مقدار؛ null اگر لغو شد.</summary>
    public static string? Show(string title, string prompt, string initial = "")
    {
        var dlg = new InputDialog(title, prompt, initial);
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
