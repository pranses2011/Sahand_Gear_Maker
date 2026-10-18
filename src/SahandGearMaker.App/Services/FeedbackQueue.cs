using System.IO;
using System.Text.Json;

namespace SahandGearMaker.App.Services;

/// <summary>
/// صف محلی بازخورد/گزارش خطا (سند ۰۵، بخش ۱-۲):
/// در حالت آفلاین یا نبود سرویس، پیام‌ها محلی و قابل بازبینی ذخیره می‌شوند.
/// هیچ ارسالی بدون رضایت صریح کاربر انجام نمی‌شود و برنامهٔ ایمیل کاربر باز نمی‌شود.
/// </summary>
public sealed class FeedbackQueue
{
    public sealed class QueuedItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "suggestion";
        public string Message { get; set; } = string.Empty;
        public string? Contact { get; set; }
        public string AppVersion { get; set; } = AppInfo.Version;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string? ClientReportId { get; set; }
    }

    private readonly string _queuePath;
    private readonly object _sync = new();

    public FeedbackQueue()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SahandGearDesigner");
        Directory.CreateDirectory(baseDir);
        _queuePath = Path.Combine(baseDir, "feedback-queue.json");
    }

    public IReadOnlyList<QueuedItem> Items
    {
        get
        {
            lock (_sync)
            {
                if (!File.Exists(_queuePath)) return Array.Empty<QueuedItem>();
                try
                {
                    return JsonSerializer.Deserialize<List<QueuedItem>>(File.ReadAllText(_queuePath)) ?? new();
                }
                catch
                {
                    return Array.Empty<QueuedItem>(); // صف خراب → خواندن ایمن، بدون پاک‌سازی خاموش
                }
            }
        }
    }

    /// <summary>افزودن پیام به صف محلی.</summary>
    public QueuedItem Enqueue(string kind, string message, string? contact = null, string? clientReportId = null)
    {
        var item = new QueuedItem
        {
            Kind = kind,
            Message = message,
            Contact = contact,
            ClientReportId = clientReportId,
        };
        lock (_sync)
        {
            var list = Items.ToList();
            list.Add(item);
            AtomicWrite(list);
        }
        return item;
    }

    /// <summary>حذف یک مورد از صف (پس از ارسال موفق یا به درخواست کاربر).</summary>
    public void Remove(string id)
    {
        lock (_sync)
        {
            var list = Items.ToList();
            list.RemoveAll(i => i.Id == id);
            AtomicWrite(list);
        }
    }

    /// <summary>پاک‌سازی کل صف — فقط با اقدام روشن کاربر (الزام سند ۰۵، بخش ۴).</summary>
    public void ClearAll()
    {
        lock (_sync)
        {
            AtomicWrite(new List<QueuedItem>());
        }
    }

    private void AtomicWrite(List<QueuedItem> items)
    {
        var tmp = _queuePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _queuePath, overwrite: true);
    }
}

/// <summary>
/// ارسال بازخورد فقط از HTTPS به سرویس سمت‌سرور (سند ۰۵).
/// اگر endpoint تنظیم نشده باشد، ارسال واقعاً انجام نمی‌شود و برنامه صادقانه اعلام می‌کند.
/// </summary>
public static class FeedbackSender
{
    /// <summary>تلاش برای ارسال؛ خروجی صادقانه است و هرگز «ارسال شد» جعلی نمی‌دهد.</summary>
    public static async Task<(bool Sent, string StatusMessage)> TrySendAsync(FeedbackQueue.QueuedItem item)
    {
        if (!AppInfo.FeedbackConfigured)
            return (false, "سرویس ارسال پیکربندی نشده است؛ پیام در صف محلی باقی می‌ماند.");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var payload = JsonSerializer.Serialize(new
            {
                kind = item.Kind,
                appVersion = item.AppVersion,
                locale = "fa-IR",
                message = item.Message,
                contact = item.Contact,
                clientReportId = item.ClientReportId,
                consent = new { diagnostics = false, timestampUtc = DateTime.UtcNow },
            });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(AppInfo.FeedbackEndpoint, content);
            if (response.IsSuccessStatusCode)
                return (true, $"ارسال شد — شناسهٔ درخواست: {item.Id[..8]}");
            return (false, $"سرور پاسخ { (int)response.StatusCode } داد؛ پیام در صف محلی باقی می‌ماند.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "ارسال ممکن نشد (شبکه)؛ پیام در صف محلی باقی می‌ماند.");
        }
    }
}
