using System.Windows.Controls;

namespace SahandGearMaker.App.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        // نسخه از منبع واحد (MSBuild $(Version)) — نه رشتهٔ دستی که کهنه شود
        VersionRun.Text = $"نسخهٔ {AppInfo.Version}";
    }
}
