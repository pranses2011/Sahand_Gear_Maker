using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.App.ViewModels;

namespace SahandGearMaker.App.Views;

public partial class FeedbackView : UserControl
{
    private readonly FeedbackViewModel _vm = new();

    public FeedbackView()
    {
        InitializeComponent();
        PrivacyText.Text = _vm.PrivacyNote;
        QueueList.ItemsSource = _vm.QueuedSummaries;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        _vm.CategoryIndex = CategoryCombo.SelectedIndex;
        _vm.Message = MessageText.Text;
        _vm.Contact = ContactText.Text;
        await _vm.SendCommand.ExecuteAsync(null);
        SendStatus.Text = _vm.Status;
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is string line && line.Length > 10)
        {
            // شناسه در انتهای خط داخل پرانتز است
            int open = line.LastIndexOf('(');
            int close = line.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                string id = line.Substring(open + 1, close - open - 1);
                _vm.RemoveQueuedCommand.Execute(id);
            }
        }
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearQueueCommand.Execute(null);
        SendStatus.Text = _vm.Status;
    }
}
