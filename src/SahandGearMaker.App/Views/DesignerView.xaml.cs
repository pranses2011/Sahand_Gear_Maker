using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SahandGearMaker.App.Controls;
using SahandGearMaker.App.ViewModels;
using SahandGearMaker.Domain;
using SahandGearMaker.Exports;
using SahandGearMaker.Persistence;
using SahandGearMaker.Scene;

namespace SahandGearMaker.App.Views;

/// <summary>
/// نمای طراح — نسخهٔ فازهای ۱ و ۲ نقشهٔ راه:
/// بوم حرفه‌ای (ViewportControl)، درخت پروژه با چشم/قفل/رنگ، پنل جای‌گذاری عددی،
/// نماهای نام‌دار، فرم پارامترهای اختصاصی هر ۱۹ خانواده (شرطی) و کیفیت مش.
/// اعداد در فیلدهای چپ‌به‌راست نمایش می‌یابند (سند ۰۵، بخش ۶).
/// </summary>
public partial class DesignerView : UserControl
{
    private MainWindow? _main;
    private DesignerViewModel VM => _main!.Designer;
    private bool _syncing;

    public DesignerView()
    {
        InitializeComponent();
    }

    public void Bind(MainWindow main)
    {
        _main = main;

        FamilyCombo.ItemsSource = VM.SupportedFamilies.Select(f =>
        {
            var info = CapabilityCatalog.GearFamilies.First(x => x.Key == f.ToString());
            return $"{info.PersianTitle}";
        }).ToList();

        PartsList.ItemsSource = VM.PartNodes;
        DiagnosticsList.ItemsSource = VM.Diagnostics;
        VM.PropertyChanged += OnVmPropertyChanged;
        VM.Rebuilt += (_, _) => RefreshScene();

        Viewport.PickRequested += Viewport_PickRequested;
        Viewport.FrameObjectRequested += (_, id) => Viewport.FrameObject(id);
        Viewport.HideObjectRequested += (_, id) => HidePart(id);
        Viewport.ShowAllRequested += (_, _) => ShowAllParts();
        Viewport.LockObjectRequested += (_, id) => ToggleLock(id);
        Viewport.DuplicateObjectRequested += (_, id) => DuplicateById(id);
        Viewport.DeleteObjectRequested += (_, id) => DeleteById(id);
        Viewport.ColorObjectRequested += (_, id) => EditColor(id);
        Viewport.OpacityObjectRequested += (_, id) => EditOpacity(id);
        Viewport.ToggleFullscreenRequested += (_, _) => ToggleFullscreen();

        if (VM.Parts.Count > 0 && VM.SelectedPart is null)
            VM.SelectedPart = VM.Parts[0];
        SyncFromPart();
        RefreshScene();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.PropertyName is nameof(DesignerViewModel.SelectedPart) or nameof(DesignerViewModel.Variants))
                SyncFromPart();
            RefreshScene();
        });
    }

    // ---------- صحنه ----------

    private void RefreshScene()
    {
        if (_main is null) return;
        Viewport.UpdateScene(VM.SceneSnapshot(), VM.SelectedPart?.Id);
        RefreshViewsList();
        VariantsList.ItemsSource = VM.Variants.ToList();
    }

    /// <summary>تازه‌سازی عمومی صحنه — از MainWindow برای مونتاژ/بررسی (فاز ۳/۴).</summary>
    public void RefreshScenePublic() => RefreshScene();

    private void RefreshViewsList()
    {
        if (_main is null) return;
        ViewsList.ItemsSource = VM.SavedViews.ToList();
    }

    // ---------- فرم ↔ مدل ----------

    private void SyncFromPart()
    {
        if (_main is null) return;
        var p = VM.SelectedPart;
        _syncing = true;
        try
        {
            if (p is null)
            {
                PartName.Text = "";
                ToothCount.Text = Module.Text = PressureAngle.Text = HelixAngle.Text = FaceWidth.Text = "";
                BoreDiameter.Text = KeywayWidth.Text = HubDiameter.Text = LighteningHoles.Text = "";
                PlaceX.Text = PlaceY.Text = PlaceZ.Text = PlaceTheta.Text = PlaceScale.Text = PlaceOpacity.Text = "";
            }
            else
            {
                PartName.Text = p.Name;
                ToothCount.Text = p.Teeth.ToothCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Module.Text = Fmt(p.Teeth.Module);
                PressureAngle.Text = Fmt(UnitConverter.RadToDeg(p.Teeth.PressureAngle));
                HelixAngle.Text = Fmt(UnitConverter.RadToDeg(p.Teeth.HelixAngle));
                FaceWidth.Text = Fmt(p.Teeth.FaceWidth);
                BoreDiameter.Text = Fmt(p.Body.BoreDiameter);
                KeywayWidth.Text = Fmt(p.Body.KeywayWidth);
                HubDiameter.Text = Fmt(p.Body.HubDiameter);
                LighteningHoles.Text = p.Body.LighteningHoleCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ProfileTolerance.Text = Fmt(p.GeometrySettings.ProfileToleranceMm);
                AxialSlices.Text = p.GeometrySettings.AxialSliceCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

                PlaceX.Text = Fmt(p.Placement.X);
                PlaceY.Text = Fmt(p.Placement.Y);
                PlaceZ.Text = Fmt(p.Placement.Z);
                PlaceTheta.Text = Fmt(UnitConverter.RadToDeg(p.Placement.Theta));
                PlaceScale.Text = Fmt(p.Placement.Scale);
                PlaceOpacity.Text = Fmt(p.DisplayOpacity);

                int famIdx = IndexOfFamily(p.Family);
                if (famIdx >= 0 && FamilyCombo.SelectedIndex != famIdx) FamilyCombo.SelectedIndex = famIdx;
                FamilyNote.Text = CapabilityNote(p.Family);
                BuildFamilyParams(p);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private int IndexOfFamily(GearFamily f)
    {
        var arr = VM.SupportedFamilies;
        for (int i = 0; i < arr.Count; i++)
            if (arr[i] == f) return i;
        return -1;
    }

    private static string CapabilityNote(GearFamily f)
    {
        var info = CapabilityCatalog.GearFamilies.First(x => x.Key == f.ToString());
        return info.Status switch
        {
            CapabilityStatus.Implemented => $"وضعیت: پیاده‌سازی‌شده — {info.Note}",
            CapabilityStatus.Limited => $"وضعیت: محدود — {info.Note}",
            _ => "وضعیت: در نقشهٔ راه",
        };
    }

    private void PushToModel()
    {
        if (_syncing || _main is null) return;
        var p = VM.SelectedPart;
        if (p is null) return;

        VM.PushUndo();
        p.Name = PartName.Text;
        p.Teeth.ToothCount = ParseInt(ToothCount.Text, p.Teeth.ToothCount);
        p.Teeth.Module = ParseDouble(Module.Text, p.Teeth.Module);
        p.Teeth.PressureAngle = UnitConverter.DegToRad(ParseDouble(PressureAngle.Text, UnitConverter.RadToDeg(p.Teeth.PressureAngle)));
        p.Teeth.HelixAngle = UnitConverter.DegToRad(ParseDouble(HelixAngle.Text, UnitConverter.RadToDeg(p.Teeth.HelixAngle)));
        p.Teeth.FaceWidth = ParseDouble(FaceWidth.Text, p.Teeth.FaceWidth);
        p.Body.BoreDiameter = ParseDouble(BoreDiameter.Text, p.Body.BoreDiameter);
        p.Body.KeywayWidth = ParseDouble(KeywayWidth.Text, p.Body.KeywayWidth);
        p.Body.HubDiameter = ParseDouble(HubDiameter.Text, p.Body.HubDiameter);
        p.Body.LighteningHoleCount = ParseInt(LighteningHoles.Text, p.Body.LighteningHoleCount);
        p.GeometrySettings.ProfileToleranceMm = Math.Max(0.002, ParseDouble(ProfileTolerance.Text, p.GeometrySettings.ProfileToleranceMm));
        p.GeometrySettings.AxialSliceCount = Math.Clamp(ParseInt(AxialSlices.Text, p.GeometrySettings.AxialSliceCount), 2, 400);

        ReadFamilyParams(p);
        VM.ScheduleRebuild(p);
        RefreshScene();
    }

    private static double ParseDouble(string s, double fallback)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // ---------- رویدادهای فرم ----------

    private void PartName_LostFocus(object sender, RoutedEventArgs e) => PushToModel();

    private void Numeric_LostFocus(object sender, RoutedEventArgs e) => PushToModel();

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PushToModel(); e.Handled = true; }
    }

    private void FamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _main is null || FamilyCombo.SelectedIndex < 0) return;
        var fam = VM.SupportedFamilies[FamilyCombo.SelectedIndex];
        if (VM.SelectedPart is { } p && p.Family != fam)
        {
            VM.PushUndo();
            p.Family = fam;
            FamilyNote.Text = CapabilityNote(fam);

            // نرمال‌سازی پارامترها برای دامنهٔ خانوادهٔ جدید (رفع «کرم ساخته نمی‌شود» — نسخهٔ ۰٫۵)
            var changes = GearFamilyDefaults.Normalize(p);
            foreach (var msg in changes)
                VM.Diagnostics.Add(new DiagnosticViewModel { Severity = Severity.Info, Code = "SGM-I460", Message = msg });
            if (changes.Count > 0)
                _main.SetStatus($"خانواده تغییر کرد — {changes.Count} پارامتر به دامنهٔ معتبر اصلاح شد (پیام‌ها را ببینید).");

            SyncFromPart(); // فیلدها ممکن است توسط نرمال‌سازی تغییر کرده باشند
            VM.RebuildPartNow(p);
            RefreshScene();
        }
    }

    private void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedPart is { } p) VM.RebuildPartNow(p);
        RefreshScene();
        _main?.SetStatus(VM.LastBuild is null
            ? "ساخت هندسه ناموفق — پیام‌ها را ببینید."
            : $"هندسه ساخته شد: {VM.LastBuild.Mesh.TriangleCount:N0} مثلث، {VM.LastBuild.AxialSlicesUsed} برش عرضی.");
    }

    private void RebuildAll_Click(object sender, RoutedEventArgs e)
    {
        VM.RebuildAll();
        RefreshScene();
        _main?.SetStatus("همهٔ قطعات بازسازی شدند.");
    }

    // ---------- پارامترهای اختصاصی خانواده (فاز ۲٫۱) ----------

    private readonly Dictionary<string, TextBox> _familyFields = new();

    private void BuildFamilyParams(GearPart p)
    {
        FamilyParamsPanel.Children.Clear();
        _familyFields.Clear();
        var spec = p.Specific;

        void Add(string key, string label, string value)
        {
            var tb = new TextBox
            {
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 140,
                Margin = new Thickness(0, 2, 0, 8),
                Tag = key,
                Text = value,
            };
            tb.LostFocus += FamilyField_LostFocus;
            _familyFields[key] = tb;
            FamilyParamsPanel.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            FamilyParamsPanel.Children.Add(tb);
        }

        switch (p.Family)
        {
            case GearFamily.Rack or GearFamily.HelicalRack:
                Add("RackLength", "طول شانه (mm)", Fmt(spec.RackLengthMm == 0 ? 120 : spec.RackLengthMm));
                break;
            case GearFamily.Worm:
                // «تعداد رشته‌ها» همان تعداد دندانهٔ کرم است (BuildWorm از ToothCount می‌خواند)
                Add("WormStartsTeeth", "تعداد رشته‌ها (استارت)", p.Teeth.ToothCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add("WormPitchDia", "قطر گام کرم (mm)", Fmt(spec.WormPitchDiameterMm == 0 ? 20 * p.Teeth.Module : spec.WormPitchDiameterMm));
                Add("WormHand", "دست (1=راست، -1=چپ)", spec.WormIsRightHanded ? "1" : "-1");
                Add("WormThroat", "خلأ گلوگاه (mm)", Fmt(spec.WormThroatClearanceMm));
                break;
            case GearFamily.WormWheel:
                Add("WormPitchDia", "قطر گام کرم جفت (mm)", Fmt(spec.WormPitchDiameterMm == 0 ? 40 : spec.WormPitchDiameterMm));
                Add("WormStarts", "ورودی‌های کرم جفت", spec.WormStartsCount.ToString());
                break;
            case GearFamily.BevelSpur or GearFamily.BevelSpiral:
                Add("BevelShaft", "زاویهٔ محورها (درجه)", Fmt(UnitConverter.RadToDeg(spec.BevelShaftAngle == 0 ? Math.PI / 2 : spec.BevelShaftAngle)));
                Add("BevelCone", "زاویهٔ مخروط گام (درجه)", Fmt(UnitConverter.RadToDeg(spec.BevelPitchConeAngle == 0 ? Math.PI / 4 : spec.BevelPitchConeAngle)));
                if (p.Family == GearFamily.BevelSpiral)
                    Add("BevelSpiral", "زاویهٔ مارپیچ قوسی (درجه)", Fmt(UnitConverter.RadToDeg(spec.BevelSpiralAngle)));
                break;
            case GearFamily.Hypoid:
                Add("BevelShaft", "زاویهٔ محورها (درجه)", Fmt(UnitConverter.RadToDeg(spec.BevelShaftAngle == 0 ? Math.PI / 2 : spec.BevelShaftAngle)));
                Add("BevelCone", "زاویهٔ مخروط گام (درجه)", Fmt(UnitConverter.RadToDeg(spec.BevelPitchConeAngle == 0 ? Math.PI / 4 : spec.BevelPitchConeAngle)));
                Add("HypoidOffset", "آفست هیپوئید (mm)", Fmt(spec.HypoidOffsetMm));
                break;
            case GearFamily.NonCircular:
                Add("Eccentricity", "خروج از مرکز (۰-۰٫۹)", Fmt(spec.NonCircularEccentricity));
                break;
            case GearFamily.HarmonicDrive:
                Add("HarmonicDeflect", "خیز موج‌ساز (mm)", Fmt(spec.HarmonicWaveDeflectionMm == 0 ? 0.6 : spec.HarmonicWaveDeflectionMm));
                Add("HarmonicWall", "ضخامت دیوارهٔ فنجان (mm)", Fmt(spec.HarmonicCupWallThicknessMm == 0 ? 1.2 : spec.HarmonicCupWallThicknessMm));
                break;
            case GearFamily.SplineExternal or GearFamily.SplineInternal:
                Add("SplineBacklash", "لقی هزارخار (mm)", Fmt(spec.SplineBacklashMm));
                Add("SplineFit", "نوع برازش (0=قطر گام، 1=جابه‌جایی دندانه)", ((int)spec.SplineFit).ToString());
                break;
        }

        FamilyParamsBox.Visibility = _familyFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FamilyField_LostFocus(object sender, RoutedEventArgs e)
    {
        ReadFamilyParams(VM.SelectedPart);
        if (VM.SelectedPart is { } p) VM.ScheduleRebuild(p);
        RefreshScene();
    }

    private void ReadFamilyParams(GearPart? p)
    {
        if (p is null || _familyFields.Count == 0) return;
        var spec = p.Specific;

        string V(string key) => _familyFields.TryGetValue(key, out var tb) ? tb.Text : "";
        double D(string key, double fb) => ParseDouble(V(key), fb);
        int I(string key, int fb) => ParseInt(V(key), fb);

        switch (p.Family)
        {
            case GearFamily.Rack or GearFamily.HelicalRack:
                spec.RackLengthMm = D("RackLength", 120);
                break;
            case GearFamily.Worm:
            {
                if (int.TryParse(V("WormStartsTeeth"), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var starts) && starts is >= 1 and <= 12)
                {
                    if (p.Teeth.ToothCount != starts)
                        p.Teeth.ToothCount = starts;
                    spec.WormStartsCount = starts; // هم‌زمان‌سازی برای ویزارد چرخ کرم
                }
                spec.WormPitchDiameterMm = D("WormPitchDia", 0);
                spec.WormIsRightHanded = I("WormHand", 1) >= 0;
                spec.WormThroatClearanceMm = D("WormThroat", 0);
                break;
            }
            case GearFamily.WormWheel:
                spec.WormPitchDiameterMm = D("WormPitchDia", 40);
                spec.WormStartsCount = Math.Clamp(I("WormStarts", 1), 1, 12);
                break;
            case GearFamily.BevelSpur or GearFamily.BevelSpiral:
                spec.BevelShaftAngle = UnitConverter.DegToRad(D("BevelShaft", 90));
                spec.BevelPitchConeAngle = UnitConverter.DegToRad(D("BevelCone", 45));
                if (p.Family == GearFamily.BevelSpiral)
                    spec.BevelSpiralAngle = UnitConverter.DegToRad(D("BevelSpiral", 32));
                break;
            case GearFamily.Hypoid:
                spec.BevelShaftAngle = UnitConverter.DegToRad(D("BevelShaft", 90));
                spec.BevelPitchConeAngle = UnitConverter.DegToRad(D("BevelCone", 45));
                spec.HypoidOffsetMm = D("HypoidOffset", 0);
                break;
            case GearFamily.NonCircular:
                spec.NonCircularEccentricity = Math.Clamp(D("Eccentricity", 0.5), 0, 0.9);
                break;
            case GearFamily.HarmonicDrive:
                spec.HarmonicWaveDeflectionMm = D("HarmonicDeflect", 0.6);
                spec.HarmonicCupWallThicknessMm = D("HarmonicWall", 1.2);
                break;
            case GearFamily.SplineExternal or GearFamily.SplineInternal:
                spec.SplineBacklashMm = D("SplineBacklash", 0);
                spec.SplineFit = (SplineFitType)Math.Clamp(I("SplineFit", 0), 0, 1);
                break;
        }
    }

    // ---------- درخت پروژه (فاز ۱٫۲۱) ----------

    private void PartNode_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id)
        {
            var part = VM.Parts.FirstOrDefault(p => p.Id == id);
            if (part is not null && !part.DisplayLocked)
                VM.SelectedPart = part;
            else if (part is not null)
                _main?.SetStatus("این قطعه قفل است — ابتدا قفل را باز کنید.");
        }
    }

    private void Eye_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is string id)
            HidePart(id);
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is string id)
            ToggleLock(id);
    }

    private GearPart? PartById(string id) => VM.Parts.FirstOrDefault(p => p.Id == id);

    private void HidePart(string id)
    {
        if (PartById(id) is { } p)
        {
            p.DisplayVisible = !p.DisplayVisible;
            VM.SyncPartDisplay(p);
            RefreshScene();
        }
    }

    private void ShowAllParts()
    {
        foreach (var p in VM.Parts)
            p.DisplayVisible = true;
        foreach (var p in VM.Parts)
            VM.SyncPartDisplay(p);
        RefreshScene();
    }

    private void ToggleLock(string id)
    {
        if (PartById(id) is { } p)
        {
            p.DisplayLocked = !p.DisplayLocked;
            VM.SyncPartDisplay(p);
            RefreshScene();
        }
    }

    private void DuplicateById(string id)
    {
        var p = PartById(id);
        if (p is null) return;
        VM.DuplicatePartCommand.Execute(p);
        SyncFromPart();
        RefreshScene();
    }

    private void DeleteById(string id)
    {
        var p = PartById(id);
        if (p is null) return;
        VM.RemovePartCommand.Execute(p);
        SyncFromPart();
        RefreshScene();
    }

    // ---------- رنگ/شفافیت (فاز ۱٫۲۲) ----------

    private void EditColor(string id)
    {
        if (PartById(id) is not { } p) return;
        EditColorOf(p);
    }

    private void PartColor_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedPart is { } p) EditColorOf(p);
    }

    private void EditColorOf(GearPart p)
    {
        var hex = ColorPickDialog.Show(p.DisplayColor);
        if (hex is not null)
        {
            VM.SetPartColor(p, hex);
            RefreshScene();
        }
    }

    private void EditOpacity(string id)
    {
        if (PartById(id) is not { } p) return;
        var input = InputDialog.Show("شفافیت قطعه",
            "شفافیت (۰٫۰۵ تا ۱ — ۱ = کاملاً مات):",
            p.DisplayOpacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (input is not null && double.TryParse(input, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            VM.SetPartOpacity(p, v);
            SyncFromPart();
            RefreshScene();
        }
    }

    // ---------- جای‌گذاری عددی (فاز ۱٫۲۳) ----------

    private void Placement_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_syncing || VM.SelectedPart is not { } p) return;
        VM.PushUndo();
        VM.SetPartPlacement(p,
            ParseDouble(PlaceX.Text, p.Placement.X),
            ParseDouble(PlaceY.Text, p.Placement.Y),
            ParseDouble(PlaceZ.Text, p.Placement.Z),
            ParseDouble(PlaceTheta.Text, UnitConverter.RadToDeg(p.Placement.Theta)),
            ParseDouble(PlaceScale.Text, p.Placement.Scale));
        if (double.TryParse(PlaceOpacity.Text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var op))
            VM.SetPartOpacity(p, op);
        RefreshScene();
    }

    private void FramePart_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedPart is { } p)
            Viewport.FrameObject(p.Id);
    }

    // ---------- نماهای نام‌دار (فاز ۱٫۱۶) ----------

    private void SaveView_Click(object sender, RoutedEventArgs e)
    {
        string name = ViewName.Text.Trim();
        if (name.Length == 0)
        {
            _main?.SetStatus("نام نما را وارد کنید.");
            return;
        }
        var v = Viewport.CaptureCurrentView(name);
        VM.SaveCurrentView(name, v.Azimuth, v.Elevation, v.Distance,
            v.TargetX, v.TargetY, v.TargetZ, v.Projection == ProjectionKind.Perspective);
        RefreshViewsList();
        _main?.SetStatus($"نمای «{name}» ذخیره شد.");
    }

    private void ApplyView_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name)
        {
            var v = VM.SavedViews.FirstOrDefault(x => x.Name == name);
            if (v is not null)
                Viewport.ApplySavedView(new SahandGearMaker.Scene.SavedCameraView
                {
                    Name = v.Name,
                    Azimuth = v.Azimuth,
                    Elevation = v.Elevation,
                    Distance = v.Distance,
                    TargetX = v.TargetX,
                    TargetY = v.TargetY,
                    TargetZ = v.TargetZ,
                    Projection = v.Projection == ViewProjection.Orthographic
                        ? SahandGearMaker.Scene.ProjectionKind.Orthographic
                        : SahandGearMaker.Scene.ProjectionKind.Perspective,
                });
        }
    }

    private void DeleteView_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name)
        {
            VM.DeleteView(name);
            RefreshViewsList();
        }
    }

    // ---------- گونه‌ها (فاز ۲٫۴) ----------

    private void RegisterVariant_Click(object sender, RoutedEventArgs e)
    {
        string name = VariantName.Text.Trim();
        if (name.Length == 0)
        {
            _main?.SetStatus("نام گونه را وارد کنید.");
            return;
        }
        VM.RegisterVariant(name);
        VariantsList.ItemsSource = VM.Variants.ToList();
        _main?.SetStatus($"گونهٔ «{name}» ثبت شد ({VM.Variants.Count} گونه).");
    }

    private void ApplyVariant_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        var variant = VM.Variants.FirstOrDefault(v => v.Name == name);
        if (variant is null) return;
        VM.ApplyVariant(variant);
        SyncFromPart();
        RefreshScene();
        _main?.SetStatus($"گونهٔ «{name}» اعمال شد — با Ctrl+Z قابل بازگشت.");
    }

    private void CompareVariants_Click(object sender, RoutedEventArgs e)
    {
        if (VM.Variants.Count < 2)
        {
            VariantCompare.Text = "برای مقایسه حداقل دو گونه لازم است.";
            return;
        }
        var diffs = VM.CompareVariants(0, 1);
        VariantCompare.Text = diffs.Count == 0
            ? $"«{VM.Variants[0].Name}» و «{VM.Variants[1].Name}» تفاوتی ندارند."
            : string.Join("\n", diffs.Select(d => $"{d.PartName} — {d.Field}: {d.OldValue} ← {d.NewValue}"));
    }

    private void SaveVariants_Click(object sender, RoutedEventArgs e)
    {
        if (VM.Variants.Count == 0)
        {
            _main?.SetStatus("گونه‌ای برای ذخیره نیست.");
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیرهٔ گونه‌ها",
            Filter = "گونه‌های طراح چرخ‌دنده (*.sgdvariants)|*.sgdvariants",
            FileName = MeshExporter.SanitizeName(VM.ProjectName) + ".sgdvariants",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            VM.SaveVariants(dlg.FileName);
            _main?.SetStatus($"گونه‌ها ذخیره شدند: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"ذخیرهٔ گونه‌ها ناموفق بود: {ex.Message}", "گونه‌ها",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadVariants_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "بارگذاری گونه‌ها",
            Filter = "گونه‌های طراح چرخ‌دنده (*.sgdvariants)|*.sgdvariants",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            VM.LoadVariants(dlg.FileName);
            VariantsList.ItemsSource = VM.Variants.ToList();
            _main?.SetStatus($"{VM.Variants.Count} گونه بارگذاری شد.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"بارگذاری گونه‌ها ناموفق بود: {ex.Message}", "گونه‌ها",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- مدل مرجع (فاز ۲٫۳) ----------

    private void ImportReference_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ورود مدل مرجع",
            Filter = "مدل مش (*.stl;*.obj;*.ply)|*.stl;*.obj;*.ply",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string info = VM.ImportReference(dlg.FileName);
            ReferenceInfo.Text = $"آخرین مرجع: {info} — ({VM.ReferenceCount} مرجع فعال)";
            RefreshScene();
            _main?.SetStatus("مدل مرجع به‌صورت شبح شفاف اضافه شد.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"ورود مدل ناموفق بود: {ex.Message}", "مدل مرجع",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveReference_Click(object sender, RoutedEventArgs e)
    {
        VM.RemoveLastReference();
        ReferenceInfo.Text = VM.ReferenceCount == 0
            ? "مرجعی فعال نیست."
            : $"({VM.ReferenceCount} مرجع فعال باقی مانده)";
        RefreshScene();
    }

    // ---------- مدیریت قطعات ----------

    private void AddPart_Click(object sender, RoutedEventArgs e)
    {
        VM.AddPartCommand.Execute(null);
        SyncFromPart();
        RefreshScene();
    }

    private void DuplicatePart_Click(object sender, RoutedEventArgs e)
    {
        VM.DuplicatePartCommand.Execute(VM.SelectedPart);
        SyncFromPart();
        RefreshScene();
    }

    private void RemovePart_Click(object sender, RoutedEventArgs e)
    {
        VM.RemovePartCommand.Execute(VM.SelectedPart);
        SyncFromPart();
        RefreshScene();
    }

    // ---------- انتخاب از بوم (فاز ۱٫۱۹) ----------

    private void Viewport_PickRequested(object? sender, string? id)
    {
        if (id is null) return;
        var part = PartById(id);
        if (part is null) return;
        if (part.DisplayLocked)
        {
            _main?.SetStatus("قطعهٔ زیر مکان‌نما قفل است.");
            return;
        }
        VM.SelectedPart = part;
        _main?.SetStatus($"انتخاب شد: {part.Name}");
    }

    // ---------- تمام‌بوم (فاز ۱٫۱۲) ----------

    private bool _fullscreen;

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        RightCol.Width = new GridLength(_fullscreen ? 0 : 330);
        LeftCol.Width = new GridLength(_fullscreen ? 0 : 300);
        _main?.SetStatus(_fullscreen ? "حالت تمام‌بوم — دوباره دکمهٔ ⛶ را بزنید تا پنل‌ها برگردند." : "پنل‌ها برگشتند.");
    }
}
