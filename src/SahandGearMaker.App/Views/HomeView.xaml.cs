using System.Windows;
using System.Windows.Controls;
using SahandGearMaker.Domain;

namespace SahandGearMaker.App.Views;

public partial class HomeView : UserControl
{
    private MainWindow? _main;

    public HomeView() => InitializeComponent();

    public void Bind(MainWindow main) => _main = main;

    private void CreateSample_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var designer = _main.Designer;

        designer.NewProjectCommand.Execute(null);
        var part = new GearPart
        {
            Name = "چرخ‌دندهٔ نمونه ۲۰×۲",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { ToothCount = 20, Module = 2, FaceWidth = 20 },
            Body = new BodyFeatures { BoreDiameter = 10 },
        };
        designer.Parts.Add(part);
        designer.SelectedPart = part;
        _main.SetStatus("نمونه ساخته شد — تب طراح را ببینید.");
    }
}
