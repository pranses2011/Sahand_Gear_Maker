using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SahandGearMaker.Domain;
using SahandGearMaker.Exports;
using SahandGearMaker.Geometry;
using SahandGearMaker.Persistence;

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

/// <summary>
/// مدل نمایش طراح پارامتریک — برش عمودی اصلی برنامه (سند ۰۲، بخش ۶.۲).
/// ورودی نامعتبر، پیام قابل‌فهم می‌دهد و UI نمی‌ریزد.
/// </summary>
public partial class DesignerViewModel : ObservableObject
{
    private GearProject _project = new() { Name = "پروژهٔ بدون نام" };
    private GearPart? _selectedPart;
    private GearBuildResult? _lastBuild;

    /// <summary>
    /// نمونهٔ یکتای مجموعهٔ قطعات (فاز ۰٫۴ — رفع Q2): پیش از این هر بار خوانده‌شدن Parts
    /// یک ObservableCollection جدید ساخته می‌شد و bindingهای قدیمی از همگام‌سازی می‌افتادند.
    /// </summary>
    private readonly ObservableCollection<GearPart> _parts;

    public ObservableCollection<GearPart> Parts => _parts;

    private void SyncPartsFromProject()
    {
        _parts.Clear();
        foreach (var p in _project.Parts)
            _parts.Add(p);
        OnPropertyChanged(nameof(Parts));
    }

    [ObservableProperty]
    private string _projectName = "پروژهٔ بدون نام";

    [ObservableProperty]
    private ObservableCollection<DiagnosticViewModel> _diagnostics = new();

    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>مش آخرین ساخت موفق برای پیش‌نمایش/خروجی.</summary>
    public GearBuildResult? LastBuild => _lastBuild;

    public IReadOnlyList<GearFamily> SupportedFamilies { get; } = new[]
    {
        GearFamily.ExternalSpur,
        GearFamily.ExternalHelical,
        GearFamily.ExternalHerringbone,
        GearFamily.InternalSpur,
        GearFamily.Rack,
    };

    public DesignerViewModel()
    {
        _parts = new ObservableCollection<GearPart>(_project.Parts);
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
        _lastBuild = null;
        Diagnostics.Clear();
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

        try
        {
            _project = ProjectFile.Load(dlg.FileName);
            ProjectName = _project.Name;
            CurrentFilePath = dlg.FileName;
            SyncPartsFromProject();
            SelectedPart = _project.Parts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Diagnostics.Clear();
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-OPEN",
                Message = $"بازکردن پروژه ناموفق بود: {ex.Message}",
            });
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
            ProjectFile.Save(_project, path);
            CurrentFilePath = path;
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

    /// <summary>افزودن قطعهٔ ازپیش‌ساخته به پروژه و فهرست (فاز ۰٫۴ — همگام با نمونهٔ یکتای Parts).</summary>
    public void AttachPart(GearPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        _project.Parts.Add(part);
        _parts.Add(part);
    }

    /// <summary>بارگذاری پروژه از مسیر داده‌شده (برای فایل‌های اخیر)؛ خطا پیام امن می‌دهد.</summary>
    public bool OpenPath(string path)
    {
        try
        {
            _project = ProjectFile.Load(path);
            ProjectName = _project.Name;
            CurrentFilePath = path;
            SyncPartsFromProject();
            SelectedPart = _project.Parts.FirstOrDefault();
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
    private void AddPart()
    {
        var part = new GearPart
        {
            Name = $"چرخ‌دندهٔ {Parts.Count + 1}",
            Family = GearFamily.ExternalSpur,
        };
        _project.Parts.Add(part);
        _parts.Add(part);
        SelectedPart = part;
    }

    [RelayCommand]
    private void RemovePart(GearPart? part)
    {
        if (part is null) return;
        _project.Parts.Remove(part);
        _project.Matings.RemoveAll(m => m.PartAId == part.Id || m.PartBId == part.Id);
        _parts.Remove(part);
        if (ReferenceEquals(SelectedPart, part)) SelectedPart = _project.Parts.FirstOrDefault();
    }

    [RelayCommand]
    private void DuplicatePart(GearPart? part)
    {
        if (part is null) return;
        var copy = part.Clone(part.Name + " (کپی)");
        _project.Parts.Add(copy);
        _parts.Add(copy);
        SelectedPart = copy;
    }

    public GearPart? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (SetProperty(ref _selectedPart, value))
                Rebuild();
        }
    }

    /// <summary>بازسازی هندسهٔ قطعهٔ انتخابی و نمایش پیام‌ها — تغییر پارامتر این را صدا می‌زند.</summary>
    public void Rebuild()
    {
        Diagnostics.Clear();
        if (_selectedPart is null) { _lastBuild = null; return; }

        try
        {
            var result = GearGeometryEngine.BuildPart(_selectedPart);
            _lastBuild = result;
            _selectedPart.Validation = result.Diagnostics.ToList();
            foreach (var d in result.Diagnostics)
                Diagnostics.Add(new DiagnosticViewModel { Code = d.Code, Severity = d.Severity, Message = d.Message });
        }
        catch (GeometryBuildException gex)
        {
            _lastBuild = null;
            foreach (var d in gex.Diagnostics)
                Diagnostics.Add(new DiagnosticViewModel { Code = d.Code, Severity = d.Severity, Message = d.Message });
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-BUILD",
                Message = gex.Message,
            });
        }
        catch (Exception ex)
        {
            _lastBuild = null;
            Diagnostics.Add(new DiagnosticViewModel
            {
                Severity = Severity.Error,
                Code = "SGM-UI-BUILD",
                Message = $"ساخت هندسه با خطا متوقف شد: {ex.Message}",
            });
        }
    }

    partial void OnProjectNameChanged(string value) => _project.Name = value;
}
