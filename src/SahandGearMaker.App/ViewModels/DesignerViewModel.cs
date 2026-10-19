using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SahandGearMaker.Assembly;
using SahandGearMaker.Domain;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Persistence;
using SahandGearMaker.Scene;

namespace SahandGearMaker.App.ViewModels;

/// <summary>مدل نمایش یک پیام اعتبارسنجی.</summary>
public partial class DiagnosticViewModel : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public Severity Severity { get; init; }
    public string Message { get; init; } = string.Empty;

    public string SeverityLabel => Severity switch
    {
        Severity.Error => "خطا",
        Severity.Warning => "هشدار",
        Severity.Info => "اطلاع",
        _ => "بررسی‌نشده",
    };
}

/// <summary>مدل نمایش هر قطعه در درخت پروژه (فاز ۱٫۲۱) — با وضعیت نمایش/قفل/رنگ.</summary>
public partial class PartNodeViewModel : ObservableObject
{
    public required GearPart Part { get; init; }
    public string Id => Part.Id;
    public string Name => Part.Name;

    public bool Visible
    {
        get => Part.DisplayVisible;
        set
        {
            if (Part.DisplayVisible != value)
            {
                Part.DisplayVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EyeIcon));
            }
        }
    }

    public bool Locked
    {
        get => Part.DisplayLocked;
        set
        {
            if (Part.DisplayLocked != value)
            {
                Part.DisplayLocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LockIcon));
            }
        }
    }

    public string EyeIcon => Visible ? "👁" : "🚫";
    public string LockIcon => Locked ? "🔒" : "🔓";
    public string ColorHex => Part.DisplayColor;
    public string StatusBadge => Part.Validation.Any(v => v.Severity == Severity.Error) ? "⛔"
        : Part.Validation.Any(v => v.Severity == Severity.Warning) ? "⚠" : "✓";

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// مدل نمایش طراح پارامتریک — نسخهٔ فازهای ۱ تا ۵ نقشهٔ راه:
///  - ساخت همهٔ قطعات (صحنهٔ چندقطعه‌ای) نه فقط انتخابی؛
///  - Rebuild لغوپذیر با debounce ۲۵۰ms (۱٫۱۱)؛
///  - کنترل‌های شیئ: رنگ/شفافیت/نمایش/قفل/جای‌گذاری (۱٫۲۰–۱٫۲۳)؛
///  - نماهای نام‌دار (۱٫۱۶)، مدل مرجع (۲٫۳)، گونه‌ها (۲٫۴)،
///  - واگرد/ازنو اسنپ‌شات‌محور (۵٫۱) و ذخیرهٔ خودکار (۲٫۴).
/// ورودی نامعتبر پیام قابل‌فهم می‌دهد و UI نمی‌ریزد.
/// </summary>
public partial class DesignerViewModel : ObservableObject
{
    private GearProject _project = new() { Name = "پروژهٔ بدون نام" };
    private GearPart? _selectedPart;

    /// <summary>مش/نتیجهٔ ساخت همهٔ قطعات بر اساس شناسهٔ پایدار (صحنهٔ چندقطعه‌ای — فاز ۱٫۱۰).</summary>
    private readonly Dictionary<string, GearBuildResult> _builds = new();
    private readonly Dictionary<string, SceneObject> _sceneObjects = new();
    private readonly List<SceneObject> _referenceObjects = new();

    private CancellationTokenSource? _rebuildCts;
    private DispatcherTimer? _debounceTimer;

    /// <summary>نمونهٔ یکتای مجموعهٔ قطعات (فاز ۰٫۴ — رفع Q2).</summary>
    private readonly ObservableCollection<GearPart> _parts;
    private readonly ObservableCollection<PartNodeViewModel> _partNodes;

    public ObservableCollection<GearPart> Parts => _parts;
    public ObservableCollection<PartNodeViewModel> PartNodes => _partNodes;

    /// <summary>همهٔ اشیاء صحنه: قطعات + مدل‌های مرجع (برای رندر/پیکینگ).</summary>
    public IReadOnlyList<SceneObject> SceneSnapshot()
    {
        var list = new List<SceneObject>(_sceneObjects.Count + _referenceObjects.Count);
        foreach (var kv in _sceneObjects)
            if (kv.Value is { Visible: true } or { Visible: false }) list.Add(kv.Value);
        list.AddRange(_referenceObjects);
        return list;
    }

    private void SyncPartsFromProject()
    {
        _parts.Clear();
        _partNodes.Clear();
        _sceneObjects.Clear();
        _builds.Clear();
        foreach (var p in _project.Parts)
        {
            _parts.Add(p);
            _partNodes.Add(new PartNodeViewModel { Part = p });
        }
        OnPropertyChanged(nameof(Parts));
    }

    [ObservableProperty]
    private string _projectName = "پروژهٔ بدون نام";

    [ObservableProperty]
    private ObservableCollection<DiagnosticViewModel> _diagnostics = new();

    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>نتیجهٔ ساخت قطعهٔ انتخابی — برای سازگاری با خروجی/نقشه (نگاشت به _builds).</summary>
    public GearBuildResult? LastBuild => _selectedPart is { } p && _builds.TryGetValue(p.Id, out var b) ? b : null;

    /// <summary>آیا این قطعه ساخت معتبر دارد؟ (نقشهٔ فنی/خروجی دسته‌ای — فاز ۲)</summary>
    public bool HasBuild(GearPart part) => _builds.ContainsKey(part.Id);

    /// <summary>ساخت قطعهٔ خواسته‌شده (فاز ۲٫۲/۲٫۵).</summary>
    public GearBuildResult? BuildOf(GearPart part) => _builds.TryGetValue(part.Id, out var b) ? b : null;

    /// <summary>همهٔ قطعات + ساخت‌هایشان برای خروجی دسته‌ای (فاز ۲٫۵).</summary>
    public IReadOnlyList<(GearPart Part, GearBuildResult Build)> BuildableParts()
        => _project.Parts.Where(p => _builds.ContainsKey(p.Id)).Select(p => (p, _builds[p.Id])).ToList();

    /// <summary>آخرین وضعیت رندر برای HUD بوم (فاز ۱٫۸).</summary>
    [ObservableProperty]
    private string _hudText = string.Empty;

    /// <summary>همهٔ ۱۹ خانواده — فاز ۲٫۱: کاتالوگ منبع حقیقت است.</summary>
    public IReadOnlyList<GearFamily> SupportedFamilies { get; } =
        Enum.GetValues<GearFamily>().Where(CapabilityCatalog.BuildsGeometry).ToArray();

    // ---------- ساخت/بازآرایی ----------

    public DesignerViewModel()
    {
        _parts = new ObservableCollection<GearPart>(_project.Parts);
        _partNodes = new ObservableCollection<PartNodeViewModel>();
        AddPartCommand.Execute(null); // شروع با یک قطعهٔ نمونه
    }

    [RelayCommand]
    private void NewProject()
    {
        _project = new GearProject { Name = "پروژهٔ بدون نام" };
        ProjectName = _project.Name;
        CurrentFilePath = null;
        SyncPartsFromProject();
        SelectedPart = null;
        Diagnostics.Clear();
        _referenceObjects.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFileDialog
        {
            Title = "بازکردن پروژه",
            Filter = $"پروژهٔ طراح چرخ‌دنده (*{ProjectFile.FileExtension})|*{ProjectFile.FileExtension}",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        OpenPath(dlg.FileName);
    }

    public bool OpenPath(string path)
    {
        try
        {
            _project = ProjectFile.Load(path);
            ProjectName = _project.Name;
            CurrentFilePath = path;
            SyncPartsFromProject();
            RebuildAll();
            SelectedPart = _project.Parts.FirstOrDefault();
            RecentFilesService.Remember(path, _project.Name);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-OPEN",
                Message = $"بازکردن پروژه ناموفق بود: {ex.Message}",
            });
            return false;
        }
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (CurrentFilePath is null) { SaveProjectAs(); return; }
        TrySave(CurrentFilePath);
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        var dlg = new SaveFileDialog
        {
            Title = "ذخیرهٔ پروژه",
            Filter = $"پروژهٔ طراح چرخ‌دنده (*{ProjectFile.FileExtension})|*{ProjectFile.FileExtension}",
            FileName = MeshExporter.SanitizeName(ProjectName) + ProjectFile.FileExtension,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return;
        TrySave(dlg.FileName);
    }

    private void TrySave(string path)
    {
        try
        {
            _project.Name = ProjectName;
            _project.ModifiedUtc = DateTime.UtcNow;
            ProjectFile.Save(_project, path);
            CurrentFilePath = path;
            RecentFilesService.Remember(path, _project.Name);
        }
        catch (Exception ex)
        {
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-SAVE",
                Message = $"ذخیره‌سازی ناموفق بود: {ex.Message} — فایل قبلی (در صورت وجود) دست‌نخورده است.",
            });
        }
    }

    // ---------- مدیریت قطعات ----------

    [RelayCommand]
    private void AddPart()
    {
        PushUndo();
        var part = new GearPart
        {
            Name = $"چرخ‌دندهٔ {Parts.Count + 1}",
            Family = GearFamily.ExternalSpur,
        };
        _project.Parts.Add(part);
        _parts.Add(part);
        _partNodes.Add(new PartNodeViewModel { Part = part });
        SelectedPart = part;
    }

    /// <summary>افزودن قطعهٔ ازپیش‌ساخته (نمونه‌ها/ویزارذ مونتاژ).</summary>
    public void AttachPart(GearPart part, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(part);
        _project.Parts.Add(part);
        _parts.Add(part);
        _partNodes.Add(new PartNodeViewModel { Part = part });
        if (select) SelectedPart = part;
    }

    [RelayCommand]
    private void RemovePart(GearPart? part)
    {
        if (part is null) return;
        PushUndo();
        _project.Parts.Remove(part);
        _project.Matings.RemoveAll(m => m.PartAId == part.Id || m.PartBId == part.Id);
        _parts.Remove(part);
        var node = _partNodes.FirstOrDefault(n => n.Id == part.Id);
        if (node is not null) _partNodes.Remove(node);
        _builds.Remove(part.Id);
        _sceneObjects.Remove(part.Id);
        if (ReferenceEquals(SelectedPart, part)) SelectedPart = _project.Parts.FirstOrDefault();
    }

    [RelayCommand]
    private void DuplicatePart(GearPart? part)
    {
        if (part is null) return;
        PushUndo();
        // کپی کنار اصلی — جابه‌جایی کوچک برای دیده‌شدن
        var copy = part.Clone(part.Name + " (کپی)");
        copy.Placement = new Placement
        {
            X = part.Placement.X + 60,
            Y = part.Placement.Y,
            Z = part.Placement.Z,
            Theta = part.Placement.Theta,
            ThetaX = part.Placement.ThetaX,
            ThetaY = part.Placement.ThetaY,
            Scale = part.Placement.Scale,
        };
        _project.Parts.Add(copy);
        _parts.Add(copy);
        _partNodes.Add(new PartNodeViewModel { Part = copy });
        SelectedPart = copy;
    }

    public GearPart? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (SetProperty(ref _selectedPart, value))
            {
                OnPropertyChanged(nameof(LastBuild));
                if (value is not null) ScheduleRebuild(value);
            }
        }
    }

    // ---------- ساخت هندسه (لغوپذیر + debounce — فاز ۱٫۱۱) ----------

    /// <summary>بازسازی هندسهٔ قطعهٔ انتخابی — با debounce ۲۵۰ms؛ تغییرات سریع پشت‌سرهم یک ساخت می‌شوند.</summary>
    public void Rebuild()
    {
        if (_selectedPart is { } p) ScheduleRebuild(p);
    }

    public void ScheduleRebuild(GearPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        _debounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Stop();
        _debounceTimer.Tick -= DebounceTick;
        _debounceTimer.Tick += DebounceTick;
        _pendingRebuild = part;
        _debounceTimer.Start();
    }

    private GearPart? _pendingRebuild;

    private void DebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer!.Stop();
        if (_pendingRebuild is { } part && _project.Parts.Contains(part))
            RebuildPartNow(part);
    }

    /// <summary>ساخت فوری یک قطعه (روی رشتهٔ UI — موتور هندسی سریع است؛ سقف ایمنی برای مش‌های سنگین).</summary>
    public void RebuildPartNow(GearPart part)
    {
        try
        {
            var result = GearGeometryEngine.BuildPart(part);
            _builds[part.Id] = result;
            part.Validation = result.Diagnostics.ToList();
            var node = _partNodes.FirstOrDefault(n => n.Id == part.Id);
            node?.Refresh();
            UpdateSceneObject(part, result.Mesh);
        }
        catch (GeometryBuildException gex)
        {
            _builds.Remove(part.Id);
            _sceneObjects.Remove(part.Id);
            part.Validation = gex.Diagnostics.ToList();
            var node = _partNodes.FirstOrDefault(n => n.Id == part.Id);
            node?.Refresh();
            foreach (var d in gex.Diagnostics)
                Diagnostics.Add(new DiagnosticViewModel { Code = d.Code, Severity = d.Severity, Message = d.Message });
            Diagnostics.Add(new DiagnosticViewModel { Severity = Severity.Error, Code = "SGM-UI-BUILD", Message = gex.Message });
        }
        catch (Exception ex)
        {
            _builds.Remove(part.Id);
            _sceneObjects.Remove(part.Id);
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-BUILD",
                Message = $"ساخت هندسه با خطا متوقف شد: {ex.Message}",
            });
        }
        OnPropertyChanged(nameof(LastBuild));
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>رخداد «صحنه تغییر کرد» — بوم/مونتاژ گوش می‌دهند و دوباره رندر می‌کنند.</summary>
    public event EventHandler? Rebuilt;

    /// <summary>بازسازی همهٔ قطعات (بارگذاری پروژه/الحاق دسته‌ای).</summary>
    public void RebuildAll()
    {
        Diagnostics.Clear();
        foreach (var p in _project.Parts.ToList())
        {
            // نرمال‌سازی پارامترهای خارج از دامنه (پروژه‌های ذخیره‌شدهٔ قدیمی/دستی) — نسخهٔ ۰٫۵
            foreach (var msg in GearFamilyDefaults.Normalize(p))
                Diagnostics.Add(new DiagnosticViewModel { Severity = Severity.Info, Code = "SGM-I460", Message = $"{p.Name}: {msg}" });
            RebuildPartNow(p);
        }
    }

    private void UpdateSceneObject(GearPart part, TriMesh mesh)
    {
        if (!_sceneObjects.TryGetValue(part.Id, out var obj))
        {
            obj = new SceneObject
            {
                Id = part.Id,
                Name = part.Name,
                Mesh = mesh,
                LocalBounds = mesh.Bounds(),
            };
            _sceneObjects[part.Id] = obj;
        }
        else if (!ReferenceEquals(obj.Mesh, mesh))
        {
            obj.Mesh = mesh;
            obj.LocalBounds = mesh.Bounds();
            obj.VertexNormals = null;
        }
        obj.Name = part.Name;
        obj.Transform = new ObjectTransform(
            new MathX.Vector3(part.Placement.X, part.Placement.Y, part.Placement.Z),
            part.Placement.Theta,
            part.Placement.Scale <= 0 ? 1.0 : part.Placement.Scale,
            part.Placement.ThetaX,
            part.Placement.ThetaY);
        obj.Color = Rgb.FromHex(part.DisplayColor);
        obj.Opacity = part.DisplayOpacity;
        obj.Visible = part.DisplayVisible;
        obj.Locked = part.DisplayLocked;
        obj.EnsureVertexNormals();
    }

    /// <summary>همگام‌سازی خواص نمایشی یک قطعه با شیء صحنه‌اش (پس از ویرایش در پنل شیئ).</summary>
    public void SyncPartDisplay(GearPart part)
    {
        if (_sceneObjects.TryGetValue(part.Id, out var obj))
        {
            obj.Name = part.Name;
            obj.Transform = new ObjectTransform(
                new MathX.Vector3(part.Placement.X, part.Placement.Y, part.Placement.Z),
                part.Placement.Theta,
                part.Placement.Scale <= 0 ? 1.0 : part.Placement.Scale);
            obj.Color = Rgb.FromHex(part.DisplayColor);
            obj.Opacity = part.DisplayOpacity;
            obj.Visible = part.DisplayVisible;
            obj.Locked = part.DisplayLocked;
        }
        var node = _partNodes.FirstOrDefault(n => n.Id == part.Id);
        node?.Refresh();
    }

    // ---------- کنترل‌های شیئ (فاز ۱C) ----------

    public void SetPartPlacement(GearPart part, double x, double y, double z, double thetaDeg, double scale,
        double thetaXDeg = double.NaN, double thetaYDeg = double.NaN)
    {
        var old = part.Placement;
        part.Placement = new Placement
        {
            X = x,
            Y = y,
            Z = z,
            Theta = UnitConverter.DegToRad(thetaDeg),
            ThetaX = double.IsNaN(thetaXDeg) ? old.ThetaX : UnitConverter.DegToRad(thetaXDeg),
            ThetaY = double.IsNaN(thetaYDeg) ? old.ThetaY : UnitConverter.DegToRad(thetaYDeg),
            Scale = Math.Clamp(scale, 0.05, 20),
        };
        SyncPartDisplay(part);
    }

    public void SetPartColor(GearPart part, string hex)
    {
        part.DisplayColor = hex.StartsWith('#') ? hex : "#" + hex;
        SyncPartDisplay(part);
    }

    public void SetPartOpacity(GearPart part, double opacity)
    {
        part.DisplayOpacity = Math.Clamp(opacity, 0.05, 1.0);
        SyncPartDisplay(part);
    }

    // ---------- نماهای نام‌دار (فاز ۱٫۱۶) ----------

    public IReadOnlyList<SavedView> SavedViews => _project.SavedViews;

    public void SaveCurrentView(string name, double az, double el, double dist,
        double tx, double ty, double tz, bool perspective)
    {
        _project.SavedViews.RemoveAll(v => v.Name == name);
        _project.SavedViews.Add(new SavedView
        {
            Name = name,
            Azimuth = az,
            Elevation = el,
            Distance = dist,
            TargetX = tx,
            TargetY = ty,
            TargetZ = tz,
            Projection = perspective ? ViewProjection.Perspective : ViewProjection.Orthographic,
        });
        OnPropertyChanged(nameof(SavedViews));
    }

    public bool DeleteView(string name)
    {
        var removed = _project.SavedViews.RemoveAll(v => v.Name == name) > 0;
        if (removed) OnPropertyChanged(nameof(SavedViews));
        return removed;
    }

    // ---------- مدل مرجع (فاز ۲٫۳) ----------

    /// <summary>ورود مدل مرجع STL/OBJ/PLY — به‌صورت شبح شفاف؛ هرگز چرخ‌دندهٔ پارامتریک تفسیر نمی‌شود.</summary>
    public string ImportReference(string path)
    {
        var refMesh = MeshImporter.Import(path);
        var obj = new SceneObject
        {
            Id = "ref-" + Guid.NewGuid().ToString("N"),
            Name = $"مرجع: {refMesh.SourceFileName}",
            Mesh = refMesh.Mesh,
            LocalBounds = refMesh.Mesh.Bounds(),
            IsReference = true,
            Color = new Rgb(0x94, 0xA3, 0xB8),
            Opacity = 0.35,
        };
        obj.EnsureVertexNormals();
        _referenceObjects.Add(obj);
        Rebuilt?.Invoke(this, EventArgs.Empty);
        return refMesh.SourceFileName + (refMesh.Notes.Count > 0
            ? $" — توجه: {string.Join("؛ ", refMesh.Notes)}" : "");
    }

    public void RemoveLastReference()
    {
        if (_referenceObjects.Count > 0)
        {
            _referenceObjects.RemoveAt(_referenceObjects.Count - 1);
            Rebuilt?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ReferenceCount => _referenceObjects.Count;

    // ---------- واگرد/ازنو (فاز ۵٫۱) ----------

    private readonly List<string> _undoStack = new();
    private readonly List<string> _redoStack = new();
    private const int UndoLimit = 50;
    private static readonly JsonSerializerOptions SnapshotOptions = new() { WriteIndented = false };

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>اسنپ‌شات وضعیت فعلی روی پشتهٔ واگرد — پیش از هر جهش ساختاری/پارامتری.</summary>
    public void PushUndo()
    {
        _undoStack.Add(SerializeSnapshot());
        if (_undoStack.Count > UndoLimit) _undoStack.RemoveAt(0);
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private string SerializeSnapshot() => JsonSerializer.Serialize(_project, SnapshotOptions);

    private void RestoreSnapshot(string json)
    {
        var restored = JsonSerializer.Deserialize<GearProject>(json, SnapshotOptions)
            ?? throw new InvalidOperationException("بازیابی اسنپ‌شات ناموفق بود.");
        _project = restored;
        ProjectName = restored.Name;
        SyncPartsFromProject();
        RebuildAll();
        SelectedPart = restored.Parts.FirstOrDefault();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Add(SerializeSnapshot());
        RestoreSnapshot(_undoStack[^1]);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Add(SerializeSnapshot());
        RestoreSnapshot(_redoStack[^1]);
        _redoStack.RemoveAt(_redoStack.Count - 1);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ---------- گونه‌ها (فاز ۲٫۴) ----------

    private List<GearVariant> _variants = new();

    public IReadOnlyList<GearVariant> Variants => _variants;

    public void RegisterVariant(string name, string? notes = null)
    {
        _variants.Add(VariantManager.Capture(_project, name, notes));
        OnPropertyChanged(nameof(Variants));
    }

    public void ApplyVariant(GearVariant variant)
    {
        PushUndo();
        VariantManager.Apply(_project, variant);
        ProjectName = _project.Name;
        SyncPartsFromProject();
        RebuildAll();
        OnPropertyChanged(nameof(Variants));
    }

    public IReadOnlyList<VariantDiff> CompareVariants(int indexA, int indexB)
        => VariantManager.Compare(_variants[indexA], _variants[indexB]);

    public void SaveVariants(string path)
    {
        VariantFile.Save(_variants, path);
    }

    public void LoadVariants(string path)
    {
        _variants = VariantFile.Load(path);
        OnPropertyChanged(nameof(Variants));
    }

    // ---------- مونتاژ زنده (فاز ۳) ----------

    /// <summary>چرخش نمایشی یک قطعه حول محور خودش — پخش‌کنندهٔ حرکت (۳٫۴). فقط صحنه را جابه‌جا می‌کند؛ دادهٔ پارامتریک ثابت است.</summary>
    public void SetPartDisplayRotation(GearPart part, double thetaRad)
    {
        part.Placement.Theta = thetaRad;
        if (_sceneObjects.TryGetValue(part.Id, out var obj))
            obj.Transform = new ObjectTransform(
                new MathX.Vector3(part.Placement.X, part.Placement.Y, part.Placement.Z),
                thetaRad,
                part.Placement.Scale <= 0 ? 1.0 : part.Placement.Scale);
    }

    /// <summary>جابه‌جایی نمایشی یک قطعه (۳٫۲ چیدمان خودکار / حرکت شانه).</summary>
    public void SetPartDisplayPosition(GearPart part, double x, double y, double z)
    {
        part.Placement.X = x;
        part.Placement.Y = y;
        part.Placement.Z = z;
        SyncPartDisplay(part);
    }

    private readonly Dictionary<string, (double X, double Y, double Z)> _preExplodePositions = new();
    private double _explodedFactor = -1;

    /// <summary>نمای انفجاری (۳٫۵): کشش شعاعی قطعات از مرکز مونتاژ — ۰ = واقعی.</summary>
    public void SetExplodedFactor(double factor)
    {
        if (_explodedFactor < 0 || Math.Abs(factor) < 1e-9)
            foreach (var p in _project.Parts)
                _preExplodePositions[p.Id] = (p.Placement.X, p.Placement.Y, p.Placement.Z);
        _explodedFactor = Math.Clamp(factor, 0, 1);
        foreach (var p in _project.Parts)
        {
            if (!_preExplodePositions.TryGetValue(p.Id, out var orig)) continue;
            double r = Math.Sqrt(orig.X * orig.X + orig.Y * orig.Y);
            if (r < 1e-6) continue; // قطعات مرکزی (خورشیدی) جابه‌جا نمی‌شوند
            double k = 1 + _explodedFactor * (1.5 + 30.0 / Math.Max(10, r));
            SetPartDisplayPosition(p, orig.X * k, orig.Y * k, orig.Z);
        }
    }

    /// <summary>چیدمان خودکار جفت‌های تعریف‌شده (۳٫۲): فاصلهٔ محور از محاسبهٔ جفت‌سنج.</summary>
    public string AutoLayoutMatings()
    {
        // موتور گراف مونتاژ (نسخهٔ ۰٫۵ — رفع «بیش از ۲ قطعه روی هم» و «دندانه‌ها روی هم»)
        var result = SahandGearMaker.Assembly.AssemblyLayoutEngine.AutoLayout(new LayoutContext(this));
        foreach (var p in _project.Parts)
            UpdateSceneObjectPlacement(p);
        return result.Failed == 0
            ? $"چیدمان خودکار: {result.Applied} رابطه با فاز دندانهٔ درست اعمال شد."
            : $"چیدمان خودکار: {result.Applied} اعمال، {result.Failed} ناسازگار؛ {string.Join(" ", result.Notes)}";
    }

    /// <summary>به‌روزرسانی تبدیل صحنهٔ یک قطعه پس از تغییر جای‌گذاری (چیدمان/نمای انفجاری).</summary>
    public void UpdateSceneObjectPlacement(GearPart part)
    {
        if (_sceneObjects.TryGetValue(part.Id, out var obj))
        {
            obj.Transform = new ObjectTransform(
                new MathX.Vector3(part.Placement.X, part.Placement.Y, part.Placement.Z),
                part.Placement.Theta,
                part.Placement.Scale <= 0 ? 1.0 : part.Placement.Scale,
                part.Placement.ThetaX,
                part.Placement.ThetaY);
        }
    }

    /// <summary>پارالِز موتور چیدمان به داده‌های طراح — بدون WPF و قابل‌آزمون.</summary>
    private sealed class LayoutContext(DesignerViewModel vm) : SahandGearMaker.Assembly.AssemblyLayoutEngine.ILayoutContext
    {
        public IReadOnlyList<GearPart> Parts => vm._project.Parts;
        public IReadOnlyList<MatingRelation> Matings => vm._project.Matings;

        public MatingCheckResult? Check(MatingKind kind, GearPart a, GearPart b) => DesignerViewModel.CheckMating(kind, a, b);

        public double RadiusOf(GearPart part)
            => vm._builds.TryGetValue(part.Id, out var b) ? b.Figures.TipRadius : 20.0;

        public int? ToothCountOf(GearPart part)
        {
            if (part.Family is GearFamily.Rack or GearFamily.HelicalRack)
                return null;
            if (!vm._builds.TryGetValue(part.Id, out var b))
                return part.Teeth.ToothCount;
            return b.Figures.ToothCount > 0 ? b.Figures.ToothCount : part.Teeth.ToothCount;
        }

        public bool IsInternal(GearPart part)
            => part.Family is GearFamily.InternalSpur or GearFamily.InternalHelical or GearFamily.SplineInternal;

        public double PitchRadiusOf(GearPart part)
            => vm._builds.TryGetValue(part.Id, out var b) && b.Figures.PitchRadius > 1e-6
                ? b.Figures.PitchRadius
                : part.Teeth.Module * Math.Max(6, part.Teeth.ToothCount) / 2.0;
    }

    /// <summary>جفت‌سنجی ۱۱گانه از کتابخانهٔ آزموده (۳٫۱) — UI فقط نمایش‌گر است.</summary>
    public static MatingCheckResult? CheckMating(MatingKind kind, GearPart a, GearPart b)
        => kind switch
        {
            MatingKind.ExternalPair => SahandGearMaker.Assembly.GearPairCalculator.CheckExternalPair(a.Teeth, b.Teeth),
            MatingKind.InternalPair => SahandGearMaker.Assembly.GearPairCalculator.CheckInternalPair(a.Teeth, b.Teeth),
            MatingKind.RackPinion => SahandGearMaker.Assembly.GearPairCalculator.CheckRackPinion(a.Teeth, b.Teeth),
            MatingKind.BevelPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckBevelPair(a.Teeth, b.Teeth),
            MatingKind.WormPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckWormPair(a.Teeth, b.Teeth, b.Specific.WormPitchDiameterMm > 0 ? b.Specific.WormPitchDiameterMm : 40),
            MatingKind.CrossedHelicalPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckCrossedHelicalPair(a.Teeth, b.Teeth),
            MatingKind.CrownPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckCrownPair(a.Teeth, b.Teeth),
            MatingKind.NonCircularPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckNonCircularPair(a.Teeth, b.Teeth, a.Specific.NonCircularEccentricity),
            MatingKind.HarmonicAssembly => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckHarmonicAssembly(a.Teeth, b.Teeth),
            MatingKind.SplineConnection => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckSplineConnection(a.Teeth, b.Teeth),
            MatingKind.HypoidPair => SahandGearMaker.Assembly.AdvancedPairCalculator.CheckHypoidPair(a.Teeth, b.Teeth, a.Specific.HypoidOffsetMm, a.Specific.BevelSpiralAngle, -a.Specific.BevelSpiralAngle),
            _ => null,
        };

    public IReadOnlyList<MatingRelation> Matings => _project.Matings;

    public void AddMating(MatingRelation relation)
    {
        _project.Matings.RemoveAll(m => (m.PartAId == relation.PartAId && m.PartBId == relation.PartBId)
                                     || (m.PartAId == relation.PartBId && m.PartBId == relation.PartAId));
        _project.Matings.Add(relation);
        OnPropertyChanged(nameof(Matings));
    }

    public void RemoveMating(MatingRelation relation)
    {
        _project.Matings.Remove(relation);
        OnPropertyChanged(nameof(Matings));
    }

    // ---------- ویزارد سیاره‌ای (۳٫۳) ----------

    /// <summary>ساخت گیربکس سیاره‌ای از پارامترها — قطعات + رابطه‌ها + جای‌گذاری. null اگر هندسه ناسازگار باشد.</summary>
    public string CreatePlanetary(int sunTeeth, int planetTeeth, int planetCount, double module, double faceWidth)
    {
        var design = SahandGearMaker.Assembly.PlanetaryGearsetCalculator.Design(sunTeeth, planetTeeth, planetCount,
            module, SahandGearMaker.Assembly.PlanetaryFixedMember.Ring);
        if (!design.GeometryFeasible)
            return "طراحی سیاره‌ای ناسازگار است: " + string.Join("؛ ", design.Diagnostics.Select(d => d.Message));

        PushUndo();
        // پخ نوک استاندارد (۰٫۱۵ ماژول) برای هر سه عضو — روش متداول ساخت؛
        // گوشه‌های تیز نوک را بریده و تداخل گوشه-به-فیله در درگیری نزدیک را رفع می‌کند
        double tipChamfer = Math.Max(0.05, 0.15 * module);

        // خورشیدی
        var sun = new GearPart
        {
            Name = "خورشیدی",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = module, ToothCount = sunTeeth, FaceWidth = faceWidth },
            Body = new BodyFeatures { BoreDiameter = Math.Max(4, module * 4), TipChamferMm = tipChamfer },
            DisplayColor = "#DC2626",
        };
        AttachPart(sun, select: false);

        // سیاره‌ها — فاز دندانهٔ هر سیاره (ψᵢ) برای درگیری صحیح با خورشیدی (رفع روی‌هم‌افتادن دندانه‌ها)
        for (int i = 0; i < planetCount; i++)
        {
            double ang = design.PlanetAngles.Length > i ? design.PlanetAngles[i] : 2 * Math.PI * i / planetCount;
            double phase = design.PlanetPhases.Length > i ? design.PlanetPhases[i] : 0;
            var planet = new GearPart
            {
                Name = $"سیارهٔ {i + 1}",
                Family = GearFamily.ExternalSpur,
                Teeth = new ToothParameters { Module = module, ToothCount = planetTeeth, FaceWidth = faceWidth },
                Body = new BodyFeatures { BoreDiameter = Math.Max(4, module * 4), TipChamferMm = tipChamfer },
                DisplayColor = "#2563EB",
                Placement = new Placement
                {
                    X = design.PlanetOrbitRadiusMm * Math.Cos(ang),
                    Y = design.PlanetOrbitRadiusMm * Math.Sin(ang),
                    Theta = phase,
                },
            };
            AttachPart(planet, select: false);
            _project.Matings.Add(new MatingRelation { Kind = MatingKind.ExternalPair, PartAId = sun.Id, PartBId = planet.Id, Note = "جفت سیاره‌ای" });
        }

        // حلقهٔ داخلی — چرخش θᵣ برای هم‌فازی با سیاره‌ها
        var ring = new GearPart
        {
            Name = "حلقهٔ داخلی",
            Family = GearFamily.InternalSpur,
            Teeth = new ToothParameters { Module = module, ToothCount = design.RingTeeth, FaceWidth = faceWidth },
            Body = new BodyFeatures { TipChamferMm = tipChamfer },
            DisplayColor = "#0D9488",
            Placement = new Placement { Theta = design.RingRotationRad },
        };
        AttachPart(ring, select: false);
        _project.Matings.Add(new MatingRelation { Kind = MatingKind.InternalPair, PartAId = sun.Id, PartBId = ring.Id, Note = "خورشیدی داخل حلقه" });

        RebuildAll();
        OnPropertyChanged(nameof(Matings));
        SelectedPart = sun;
        return $"گیربکس سیاره‌ای ساخته شد — نسبت {design.OverallRatio:0.###}: 1، {planetCount} سیاره در شعاع {design.PlanetOrbitRadiusMm:0.#}mm.";
    }

    // ---------- ذخیرهٔ خودکار (فاز ۲٫۴) ----------

    private AutosaveService? _autosave;

    /// <summary>فعال‌سازی ذخیرهٔ خودکار برای مسیر پروژهٔ فعلی (هر ۲ دقیقه؛ فایل اصلی دست نمی‌خورد).</summary>
    public void EnableAutosave()
    {
        _autosave = CurrentFilePath is null ? null : new AutosaveService(CurrentFilePath);
    }

    /// <summary>اگر زمانش رسیده باشد ذخیرهٔ خودکار می‌کند — از تایمر پنجرهٔ اصلی صدا زده می‌شود.</summary>
    public void TickAutosave()
    {
        if (_autosave is null) return;
        _project.Name = ProjectName;
        _autosave.TryAutosave(_project, DateTime.UtcNow);
    }

    public bool HasRecoveryFile(string projectPath) => new AutosaveService(projectPath).HasRecovery;

    public GearProject? TryRecover(string projectPath) => new AutosaveService(projectPath).TryRecover();

    public void DiscardRecovery(string projectPath) => new AutosaveService(projectPath).DiscardRecovery();

    /// <summary>بارگذاری پروژهٔ بازیابی‌شده در رابط (فاز ۲٫۴).</summary>
    public void LoadRecovered(GearProject recovered)
    {
        _project = recovered;
        ProjectName = recovered.Name;
        SyncPartsFromProject();
        RebuildAll();
        SelectedPart = recovered.Parts.FirstOrDefault();
    }

    partial void OnProjectNameChanged(string value) => _project.Name = value;
}

/// <summary>
/// فایل‌های اخیر (فاز ۵٫۳) — در %APPDATA%/SahandGearDesigner/recent.json؛
/// فقط مسیر و نام — هیچ دادهٔ طراحی ارسال/ذخیرهٔ بیرونی نمی‌شود.
/// </summary>
public static class RecentFilesService
{
    private sealed record RecentEntry(string Path, string Title, DateTime Utc);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SahandGearDesigner", "recent.json");

    public static void Remember(string path, string title)
    {
        try
        {
            var list = LoadInternal();
            list.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentEntry(path, title, DateTime.UtcNow));
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list));
        }
        catch (IOException) { /* دیسک در دسترس نیست — بی‌صدا رد می‌شود */ }
        catch (UnauthorizedAccessException) { }
    }

    public static IReadOnlyList<(string Path, string Title)> List()
        => LoadInternal().Select(r => (r.Path, r.Title)).ToList();

    private static List<RecentEntry> LoadInternal()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(StorePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new List<RecentEntry>();
    }
}
