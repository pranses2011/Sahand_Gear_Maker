using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Domain;

namespace SahandGearMaker.App.Views;

/// <summary>
/// نمای مونتاژ (فاز ۳): گراف روابط جفت‌شدن با نتیجهٔ جفت‌سنج‌های ۱۱گانهٔ کتابخانهٔ آزموده،
/// چیدمان خودکار، ویزارد سیاره‌ای، پخش‌کنندهٔ حرکت «سینماتیک ایدئال»، نمای انفجاری و دستیار نسبت.
/// </summary>
public partial class AssemblyView : UserControl
{
    private MainWindow? _main;
    private DesignerViewModel VM => _main!.Designer;
    private DispatcherTimer? _motionTimer;
    private double _inputAngle;
    private readonly Dictionary<string, double> _baseAngles = new();
    private readonly Dictionary<string, (double X, double Y)> _basePositions = new();

    /// <summary>مدل نمایش هر رابطه در گراف.</summary>
    public sealed record MatingRow(string Title, string Detail, string Bg, MatingRelation Relation);

    public AssemblyView()
    {
        InitializeComponent();
    }

    public void Bind(MainWindow main)
    {
        _main = main;
        VM.Rebuilt += (_, _) => Dispatcher.Invoke(Refresh);
        KindCombo.ItemsSource = Enum.GetValues<MatingKind>().Select(k => k switch
        {
            MatingKind.ExternalPair => "جفت خارجی موازی",
            MatingKind.InternalPair => "جفت داخلی (پینیون در حلقه)",
            MatingKind.RackPinion => "شانه و پینیون",
            MatingKind.CompoundTrain => "قطار مرکب",
            MatingKind.SimplePlanetary => "سیاره‌ای ساده",
            MatingKind.WormPair => "کرم و چرخ کرم",
            MatingKind.CrossedHelicalPair => "مارپیچ متقاطع",
            MatingKind.BevelPair => "مخروطی",
            MatingKind.CrownPair => "تاجی/پیشانی",
            MatingKind.NonCircularPair => "غیرمدور بیضوی",
            MatingKind.HarmonicAssembly => "هارمونیک",
            MatingKind.SplineConnection => "هزارخار",
            MatingKind.HypoidPair => "هیپوئید",
            _ => k.ToString(),
        }).ToList();
        Refresh();
    }

    public void Refresh()
    {
        if (_main is null) return;
        var names = VM.Parts.Select(p => p.Name).ToList();
        PartACombo.ItemsSource = names.ToList();
        PartBCombo.ItemsSource = names.ToList();
        InputPartCombo.ItemsSource = names.ToList();
        if (PartACombo.SelectedIndex < 0 && names.Count > 0) PartACombo.SelectedIndex = 0;
        if (PartBCombo.SelectedIndex < 1 && names.Count > 1) PartBCombo.SelectedIndex = 1;
        if (InputPartCombo.SelectedIndex < 0 && names.Count > 0) InputPartCombo.SelectedIndex = 0;
        RefreshMatings();
    }

    private void RefreshMatings()
    {
        if (_main is null) return;
        var byId = VM.Parts.ToDictionary(p => p.Id);
        var rows = new List<MatingRow>();
        foreach (var m in VM.Matings)
        {
            string a = byId.TryGetValue(m.PartAId, out var pa) ? pa.Name : "؟";
            string b = byId.TryGetValue(m.PartBId, out var pb) ? pb.Name : "؟";
            string title = $"{a} ⟷ {b}";
            string detail;
            string bg;
            var check = (byId.TryGetValue(m.PartAId, out var ga) && byId.TryGetValue(m.PartBId, out var gb))
                ? DesignerViewModel.CheckMating(m.Kind, ga, gb) : null;
            if (check is null)
            {
                detail = MatingKindLabel(m.Kind) + " — این نوع بررسی مستقیم ندارد (قطار مرکب/سیاره‌ای از ویزارد استفاده کنید).";
                bg = "#FFF1F5F9";
            }
            else if (check.IsCompatible)
            {
                detail = $"{MatingKindLabel(m.Kind)} ✓ سازگار — فاصلهٔ محور {check.CenterDistanceMm:0.##} mm، نسبت {check.Ratio:0.###}" +
                         (check.RotationDirection < 0 ? "، چرخش مخالف" : "، چرخش هم‌جهت");
                bg = "#FFECFDF5";
            }
            else
            {
                var why = string.Join("؛ ", check.Diagnostics.Select(d => d.Message));
                detail = $"{MatingKindLabel(m.Kind)} ✗ ناسازگار{(why.Length > 0 ? " — " + why : "")}";
                bg = "#FFFEF2F2";
            }
            rows.Add(new MatingRow(title, detail, bg, m));
        }
        MatingsList.ItemsSource = rows;
    }

    private static string MatingKindLabel(MatingKind kind) => kind switch
    {
        MatingKind.ExternalPair => "جفت خارجی",
        MatingKind.InternalPair => "جفت داخلی",
        MatingKind.RackPinion => "شانه-پینیون",
        MatingKind.CompoundTrain => "قطار مرکب",
        MatingKind.SimplePlanetary => "سیاره‌ای",
        MatingKind.WormPair => "کرم",
        MatingKind.CrossedHelicalPair => "مارپیچ متقاطع",
        MatingKind.BevelPair => "مخروطی",
        MatingKind.CrownPair => "تاجی",
        MatingKind.NonCircularPair => "غیرمدور",
        MatingKind.HarmonicAssembly => "هارمونیک",
        MatingKind.SplineConnection => "هزارخار",
        MatingKind.HypoidPair => "هیپوئید",
        _ => kind.ToString(),
    };

    // ---------- رابطه‌ها (۳٫۱) ----------

    private void AddMating_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var parts = VM.Parts.ToList();
        if (parts.Count < 2 || PartACombo.SelectedIndex < 0 || PartBCombo.SelectedIndex < 0 || PartACombo.SelectedIndex == PartBCombo.SelectedIndex)
        {
            _main.SetStatus("دو قطعهٔ متفاوت انتخاب کنید.");
            return;
        }
        var a = parts[PartACombo.SelectedIndex];
        var b = parts[PartBCombo.SelectedIndex];
        var kind = Enum.GetValues<MatingKind>()[KindCombo.SelectedIndex];
        VM.AddMating(new MatingRelation { Kind = kind, PartAId = a.Id, PartBId = b.Id });
        RefreshMatings();
        _main.SetStatus($"رابطهٔ {MatingKindLabel(kind)} بین «{a.Name}» و «{b.Name}» ثبت شد.");
    }

    private void RemoveMating_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MatingRelation rel)
        {
            VM.RemoveMating(rel);
            RefreshMatings();
        }
    }

    private void AutoLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        string msg = VM.AutoLayoutMatings();
        RefreshMatings();
        _main.SetStatus(msg);
        _main.RefreshDesignerScene();
    }

    // ---------- ویزارد سیاره‌ای (۳٫۳) ----------

    private void CreatePlanetary_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        if (!int.TryParse(SunTeeth.Text, out int sun) || !int.TryParse(PlanetTeeth.Text, out int planets) ||
            !int.TryParse(PlanetCount.Text, out int count) ||
            !double.TryParse(PlanetaryModule.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double module))
        {
            PlanetaryResult.Text = "ورودی‌ها عددی نیستند.";
            return;
        }
        string result = VM.CreatePlanetary(sun, planets, Math.Clamp(count, 1, 6), module, 14);
        PlanetaryResult.Text = result;
        Refresh();
        _main.SetStatus(result);
        _main.RefreshDesignerScene();
    }

    // ---------- نمای انفجاری (۳٫۵) ----------

    private void ExplodeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_main is null) return;
        VM.SetExplodedFactor(e.NewValue);
        _main.RefreshDesignerScene();
    }

    // ---------- دستیار نسبت (۳٫۶) ----------

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var parts = VM.Parts.ToList();
        if (PartACombo.SelectedIndex < 0 || PartBCombo.SelectedIndex < 0)
        {
            SuggestResult.Text = "دو قطعه انتخاب کنید.";
            return;
        }
        var a = parts[PartACombo.SelectedIndex];
        var b = parts[PartBCombo.SelectedIndex];
        try
        {
            double target = b.Teeth.ToothCount > 0 && a.Teeth.ToothCount > 0
                ? (double)b.Teeth.ToothCount / a.Teeth.ToothCount : 2.0;
            var suggestions = SahandGearMaker.Assembly.SizingAssistant.SuggestExternalPair(
                target, maxCenterDistanceMm: null, minTeeth: 12, maxTeeth: 150);
            SuggestResult.Text = suggestions.Count == 0
                ? "پیشنهادی برای این نسبت نیست."
                : string.Join("\n", suggestions.Take(5).Select(s =>
                    $"{s.GearATeeth}×{s.GearBTeeth} (m={s.Module:0.##}) — نسبت {s.AchievedRatio:0.###}، فاصلهٔ محور {s.CenterDistanceMm:0.##} mm"));
        }
        catch (Exception ex)
        {
            SuggestResult.Text = $"محاسبه ناموفق: {ex.Message}";
        }
    }

    // ---------- پخش‌کنندهٔ حرکت (۳٫۴) — سینماتیک ایدئال ----------

    private void InputPart_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_main is null) return;
        _inputAngle = 0;
        CaptureBaseState();
    }

    private void CaptureBaseState()
    {
        _baseAngles.Clear();
        _basePositions.Clear();
        foreach (var p in VM.Parts)
        {
            _baseAngles[p.Id] = p.Placement.Theta;
            _basePositions[p.Id] = (p.Placement.X, p.Placement.Y);
        }
    }

    private double CurrentSpeed()
    {
        double.TryParse(SpeedBox.Text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v);
        if (v == 0) v = 60;
        return DirectionCombo.SelectedIndex == 1 ? -v : v;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        CaptureBaseState();
        _motionTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _motionTimer.Tick -= MotionTick;
        _motionTimer.Tick += MotionTick;
        _motionTimer.Start();
        _main.SetStatus("پخش حرکت فعال — سینماتیک ایدئال (لقی/انعطاف مدل نمی‌شود).");
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
        => _motionTimer?.Stop();

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        _motionTimer?.Stop();
        MotionTick(this, EventArgs.Empty);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _motionTimer?.Stop();
        _inputAngle = 0;
        CaptureBaseState();
        foreach (var p in VM.Parts)
            VM.SetPartDisplayRotation(p, _baseAngles[p.Id]);
        RatioText.Text = "نسبت لحظه‌ای: —";
        _main?.RefreshDesignerScene();
    }

    /// <summary>انتشار زاویه در گراف روابط از عضو ورودی — نسبت هندسی دندانه‌ها، جهت از نوع جفت.</summary>
    private void MotionTick(object? sender, EventArgs e)
    {
        if (_main is null) return;
        double dt = _motionTimer?.Interval.TotalSeconds ?? 0.033;
        _inputAngle += CurrentSpeed() * dt / 180.0 * Math.PI; // رادیان

        var byId = VM.Parts.ToDictionary(p => p.Id);
        var angles = new Dictionary<string, double>();
        var visited = new HashSet<string>();

        var inputPart = VM.Parts.ToList().ElementAtOrDefault(Math.Max(0, InputPartCombo.SelectedIndex));
        if (inputPart is null) return;
        angles[inputPart.Id] = _baseAngles[inputPart.Id] + _inputAngle;
        visited.Add(inputPart.Id);

        // BFS روی روابط (هر رابطه دوطرفه انتقال می‌دهد)
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var m in VM.Matings)
            {
                bool aKnown = angles.TryGetValue(m.PartAId, out var angA);
                bool bKnown = angles.TryGetValue(m.PartBId, out var angB);
                if (aKnown && bKnown) continue;
                if (!aKnown && !bKnown) continue;

                var driver = aKnown ? byId[m.PartAId] : byId[m.PartBId];
                var driven = aKnown ? byId[m.PartBId] : byId[m.PartAId];
                double driverAngle = aKnown ? angA : angB;
                double baseDriver = _baseAngles[driver.Id];
                double rel = driverAngle - baseDriver;

                // نسبت زاویه‌ای: θ_driven = −θ_driver × z_driver / z_driven (خارجی) یا + (داخلی/هارمونیک/هزارخار)
                double ratio = driven.Teeth.ToothCount == 0 ? 1 : (double)driver.Teeth.ToothCount / driven.Teeth.ToothCount;
                int sign = m.Kind is MatingKind.InternalPair or MatingKind.HarmonicAssembly or MatingKind.SplineConnection
                    ? 1 : -1;
                double drivenAngle = _baseAngles[driven.Id] + sign * rel * ratio;
                angles[driven.Id] = drivenAngle;
                changed = true;

                // شانه: جابه‌جایی خطی به‌جای چرخش
                if (m.Kind == MatingKind.RackPinion && driven.Family is GearFamily.Rack or GearFamily.HelicalRack)
                {
                    double rp = driver.Teeth.Module * driver.Teeth.ToothCount / 2.0;
                    var (bx, by) = _basePositions[driven.Id];
                    double disp = -rp * rel * (m.PartAId == driven.Id ? -1 : 1);
                    VM.SetPartDisplayPosition(driven, bx + disp, by, driven.Placement.Z);
                }
            }
        }

        double? shownRatio = null;
        foreach (var p in VM.Parts)
        {
            if (angles.TryGetValue(p.Id, out var ang))
            {
                VM.SetPartDisplayRotation(p, ang);
                if (!ReferenceEquals(p, inputPart) && p.Teeth.ToothCount > 0 && inputPart.Teeth.ToothCount > 0)
                    shownRatio ??= Math.Abs((double)p.Teeth.ToothCount / inputPart.Teeth.ToothCount);
            }
        }

        RatioText.Text = shownRatio is { } r
            ? $"نسبت لحظه‌ای (ایده‌آل): ورودی {inputPart.Teeth.ToothCount} دندانه ← خروجی {r:0.###}: 1"
            : "نسبت لحظه‌ای: فقط عضو ورودی (رابطه‌ای متصل نیست)";
        _main.RefreshDesignerScene();
    }
}
