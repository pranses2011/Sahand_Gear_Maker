using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SahandGearMaker.App;

public partial class App : Application
{
    /// <summary>
    /// راه‌اندازی برنامه — دو مسیر:
    ///  ۱) مسیر عادی: پنجرهٔ اصلی ساخته و نمایش داده می‌شود (StartupUri حذف شد تا نسخهٔ
    ///     عنوان پنجره از AppInfo — منبع واحد MSBuild — تنظیم شود).
    ///  ۲) حالت کپچر برای مستندات/CI: اجرای exe با آرگومان «screenshot» و مسیر پوشهٔ
    ///     خروجی (ر.ک. RunUiScreenshot). پنجرهٔ اصلی بدون تعامل کاربر رندر و به
    ///     ui-main.png ذخیره می‌شود و برنامه خارج می‌شود. این حالت داخل خود برنامهٔ
    ///     واقعی اجرا می‌شود تا همهٔ منابع نسبی (فونت وزیرمتن، استایل‌ها و BAML) در
    ///     اسمبلی درست خودشان بارگذاری شوند؛ رفع ریشه‌ای خطای DeferredBinaryDeserializer
    ///     Extension که در میزبان بیرونی (exe ابزار اسکرین‌شات) به دلیل تفاوت اسمبلی
    ///     ورودی رخ می‌داد.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] is "--screenshot" or "-s")
        {
            RunUiScreenshot(e.Args[1]);
            return;
        }

        new MainWindow().Show();
    }

    /// <summary>
    /// کپچر رابط کاربری برای مستندات (بدون نمایش تعاملی).
    /// خروجی: &lt;پوشه&gt;/ui-main.png — کد خروجی غیرصفر یعنی خطا (CI متوقف می‌شود).
    /// </summary>
    private void RunUiScreenshot(string outDir)
    {
        Directory.CreateDirectory(outDir);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 1360,
            Height = 860,
            WindowState = WindowState.Normal,
        };
        window.Show();

        // پمپ دیسپچر برای چیدمان، اتصال داده‌ها و اولین فریم‌های رشتهٔ رندر بوم سه‌بعدی
        for (int i = 0; i < 48; i++)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            System.Threading.Thread.Sleep(25);
        }
        window.UpdateLayout();

        var rtb = new RenderTargetBitmap(
            (int)Math.Max(1, window.ActualWidth),
            (int)Math.Max(1, window.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        string path = Path.Combine(outDir, "ui-main.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(path, FileMode.Create);
        enc.Save(fs);

        Console.WriteLine($"[موفق] ui-main.png — {AppInfo.PersianName} v{AppInfo.Version} ({(int)window.ActualWidth}×{(int)window.ActualHeight})");

        window.Close();
        Shutdown(0);
    }
}
