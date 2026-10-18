using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SahandGearMaker.App.Services;

namespace SahandGearMaker.App.ViewModels;

/// <summary>مدل نمایش بازخورد — فرم درون‌برنامه‌ای؛ mailto ممنوع (سند ۰۵).</summary>
public partial class FeedbackViewModel : ObservableObject
{
    private readonly FeedbackQueue _queue = new();

    [ObservableProperty]
    private int _categoryIndex; // 0=پیشنهاد 1=مشکل 2=سایر

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _contact;

    [ObservableProperty]
    private string _status = string.Empty;

    public ObservableCollection<string> QueuedSummaries { get; } = new();

    public bool ServiceConfigured => AppInfo.FeedbackConfigured;

    public string PrivacyNote =>
        "این فرم داخل برنامه است و از برنامهٔ ایمیل شما استفاده نمی‌کند. " +
        (ServiceConfigured
            ? "ارسال فقط با دکمهٔ ارسال و از مسیر امن HTTPS انجام می‌شود."
            : "سرویس ارسال پیکربندی نشده است؛ پیام شما فقط روی همین دستگاه ذخیره می‌شود و می‌توانید آن را حذف کنید.");

    [RelayCommand]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(Message))
        {
            Status = "متن پیام خالی است.";
            return;
        }

        string kind = CategoryIndex switch
        {
            1 => "bug-report",
            2 => "other",
            _ => "suggestion",
        };

        var item = _queue.Enqueue(kind, Message.Trim(), string.IsNullOrWhiteSpace(Contact) ? null : Contact.Trim());
        RefreshQueue();
        Status = $"در صف محلی ذخیره شد (شناسه: {item.Id[..8]}).";

        // فقط با سرویس پیکربندی‌شده تلاش برای ارسال — پاسخ صادقانه
        if (ServiceConfigured)
        {
            var (sent, msg) = await FeedbackSender.TrySendAsync(item);
            if (sent)
            {
                _queue.Remove(item.Id);
                RefreshQueue();
            }
            Status = msg;
        }

        Message = string.Empty;
    }

    [RelayCommand]
    private void RemoveQueued(string? id)
    {
        if (id is null) return;
        _queue.Remove(id);
        RefreshQueue();
        Status = "از صف حذف شد.";
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _queue.ClearAll();
        RefreshQueue();
        Status = "صف پاک شد.";
    }

    private void RefreshQueue()
    {
        QueuedSummaries.Clear();
        foreach (var i in _queue.Items)
            QueuedSummaries.Add($"[{i.CreatedUtc:yyyy-MM-dd HH:mm}] {i.Kind}: {Truncate(i.Message, 60)} … ({i.Id[..8]})");
    }

    private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len];
}
