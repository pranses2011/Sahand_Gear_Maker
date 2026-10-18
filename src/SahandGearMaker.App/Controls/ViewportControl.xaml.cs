using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;
using Key = System.Windows.Input.Key;

namespace SahandGearMaker.App.Controls;

/// <summary>
/// بوم سه‌بعدی حرفه‌ای (فاز ۱B/۱C نقشهٔ راه):
///  - رشتهٔ رندر اختصاصی با صف فریم coalescing و بافرهای پایدار (۱٫۱/P1/P2)؛
///  - کنترل‌های صفحه: درگ/پن/زوم-به-مکان‌نما، اینرسی، انیمیشن tween، ViewCube، پرسپکتیو/ارتو،
///    صفحه‌کلید و سرعت/جهت ماوس قابل‌تنظیم (۱٫۱۲–۱٫۱۸)؛
///  - کنترل‌های شیئ: انتخاب با کلیک (پیکینگ)، منوی زمینه‌ای، هایلایت، قاب تک‌قطعه (۱٫۱۹–۱٫۲۴)؛
///  - HUD بوم، حالت خالی حرفه‌ای، اسکرین‌شات PNG و SSAA×2 در سکون (۱٫۵/۱٫۸/۱٫۹).
/// دوربین متعلق به رشتهٔ رندر است و ورودی‌ها از طریق صف فرمان امن منتقل می‌شوند.
/// </summary>
public partial class ViewportControl : UserControl, IDisposable
{
    // ----- رشتهٔ رندر -----
    private Thread? _renderThread;
    private volatile bool _running;
    private readonly ManualResetEventSlim _wake = new(false);
    private volatile bool _dirty = true;
    private int _ss = 1;                    // SSAA فعلی
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private readonly System.Diagnostics.Stopwatch _fpsClock = new();
    private double _fpsEma = 60;

    // ----- وضعیت مشترک (قفل‌محور) -----
    private readonly object _stateLock = new();
    private SceneObject[] _snapshot = Array.Empty<SceneObject>();
    private CameraFrame _sharedFrame;
    private bool _frameValid;
    private byte[] _transport = Array.Empty<byte>();
    private int _transportW, _transportH;
    private bool _blitPending;

    // ----- گزینه‌های رندر -----
    private DisplayMode _mode = DisplayMode.ShadedWithEdges;
    private bool _showGrid = true, _showAxes = true;
    private string? _highlightId;
    private double _clipZ = double.PositiveInfinity;

    // ----- ورودی ماوس -----
    private bool _rotating, _panning;
    private Point _lastMouse;
    private DateTime _lastMoveUtc = DateTime.UtcNow;
    private bool _dragMoved;               // آیا درگ واقعی انجام شد (در مقابل کلیک)

    // ----- رویدادها برای View میزبان -----
    public event EventHandler<string?>? PickRequested;
    public event EventHandler? ToggleFullscreenRequested;

    /// <summary>فاز ۴٫۱ — نقاط اندازه‌گیری به‌روزشد (۲ نقطه = فاصله، ۳ نقطه = زاویه).</summary>
    public event EventHandler<IReadOnlyList<MathX.Vector3>>? MeasureUpdated;

    private volatile int _sizeW = 8, _sizeH = 8;

    public ViewportControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { StartRenderThread(); SyncSize(); };
        Unloaded += (_, _) => StopRenderThread();
        SizeChanged += (_, _) => { SyncSize(); _dirty = true; _wake.Set(); };
        MouseEnter += (_, _) => Keyboard.Focus(this);

        // دکمهٔ وسط ماوس: از رویداد عمومی MouseDown با بررسی ChangedButton
        InteractionLayer.MouseDown += Interaction_MouseDown;
        InteractionLayer.MouseUp += Interaction_MouseUp;
    }

    private void SyncSize()
    {
        _sizeW = Math.Max(8, (int)ActualWidth);
        _sizeH = Math.Max(8, (int)ActualHeight);
    }

    // ---------- API عمومی ----------

    /// <summary>به‌روزرسانی صحنه از ViewModel — روی رشتهٔ UI صدا زده شود.</summary>
    public void UpdateScene(IReadOnlyList<SceneObject> objects, string? highlightId)
    {
        lock (_stateLock)
        {
            // کپی سبک: مقادیر سوژهٔ پایدار (مش/نرمال مرجع) + مقادیر کپی‌شدهٔ قابل‌تغییر
            var copy = new SceneObject[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                var s = objects[i];
                copy[i] = new SceneObject
                {
                    Id = s.Id,
                    Name = s.Name,
                    Mesh = s.Mesh,
                    VertexNormals = s.VertexNormals,
                    LocalBounds = s.LocalBounds,
                    Transform = s.Transform,
                    Color = s.Color,
                    Opacity = s.Opacity,
                    Visible = s.Visible,
                    Locked = s.Locked,
                    IsReference = s.IsReference,
                };
            }
            _snapshot = copy;
            _highlightId = highlightId;
        }
        _dirty = true;
        _wake.Set();
        EmptyState.Visibility = objects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetClipPlane(double clipZ)
    {
        _clipZ = double.IsNaN(clipZ) ? double.PositiveInfinity : clipZ;
        _dirty = true;
        _wake.Set();
    }

    // ---------- اندازه‌گیری (فاز ۴٫۱) ----------

    private readonly List<MathX.Vector3> _measurePoints = new();
    private bool _measureMode;

    /// <summary>فعال/غیرفعال‌کردن حالت اندازه‌گیری — کلیک‌ها نقطه می‌گیرند نه انتخاب.</summary>
    public void EnableMeasure(bool enable)
    {
        _measureMode = enable;
        if (!enable)
        {
            lock (_stateLock) _measurePoints.Clear();
            _dirty = true;
            _wake.Set();
        }
    }

    public bool IsMeasuring => _measureMode;

    public void ClearMeasure()
    {
        lock (_stateLock) _measurePoints.Clear();
        _dirty = true;
        _wake.Set();
    }

    private void AddMeasurePoint(MathX.Vector3 p)
    {
        List<MathX.Vector3> snapshot;
        lock (_stateLock)
        {
            _measurePoints.Add(p);
            snapshot = _measurePoints.ToList();
        }
        if (snapshot.Count > 3)
            lock (_stateLock) _measurePoints.RemoveRange(0, _measurePoints.Count - 3); // فقط ۳ نقطهٔ آخر
        MeasureUpdated?.Invoke(this, snapshot.Take(3).ToArray());
        _dirty = true;
        _wake.Set();
    }

    public void ShowBusy(bool busy) => Dispatcher.BeginInvoke(() =>
        BusyBadge.Visibility = busy ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>قاب‌کردن کل صحنه (دکمهٔ F یا فراخوانی میزبان).</summary>
    public void FrameAll()
    {
        lock (_stateLock)
        {
            if (_snapshot.Length == 0) return;
            var bounds = SceneRenderer.CombinedBounds(_snapshot);
            _camera.Frame(bounds);
        }
        _dirty = true;
        _wake.Set();
    }

    /// <summary>قاب‌کردن یک شیئ خاص (فاز ۱٫۲۴).</summary>
    public void FrameObject(string id)
    {
        lock (_stateLock)
        {
            var obj = _snapshot.FirstOrDefault(o => o.Id == id);
            if (obj is null) return;
            var wb = obj.WorldBounds;
            _camera.Frame(wb, 2.4);
        }
        _dirty = true;
        _wake.Set();
    }

    public void ApplySavedView(SavedCameraView view)
    {
        lock (_stateLock) view.ApplyTo(_camera);
        _dirty = true;
        _wake.Set();
    }

    public SavedCameraView CaptureCurrentView(string name)
    {
        lock (_stateLock) return SavedCameraView.FromCamera(_camera, name);
    }

    /// <summary>نصب مستقیم دوربین (نمای دوم ارتوگرافیک — فاز ۴٫۴)؛ صرفاً روی رشتهٔ UI.</summary>
    public void SetCameraDirect(SahandGearMaker.Scene.SceneCamera cam)
    {
        lock (_stateLock)
        {
            _camera.Projection = cam.Projection;
            _camera.SetView(cam.Azimuth, cam.Elevation, cam.Distance, cam.Target, animate: false);
        }
        _dirty = true;
        _wake.Set();
    }

    /// <summary>فرمان خام رشتهٔ رندر — برای صفحه‌کلید/تنظیمات.</summary>
    private void Enqueue(Action<SceneCamera> cmd)
    {
        lock (_stateLock) _pendingCommands.Enqueue(cmd);
        _dirty = true;
        _wake.Set();
    }

    private readonly Queue<Action<SceneCamera>> _pendingCommands = new();

    // ---------- دوربین (متعلق به رشتهٔ رندر؛ فقط از داخل قفل دست می‌خورد) ----------
    private readonly SceneCamera _camera = new() { Distance = 240 };

    public double RotateSpeed { private get; set; } = 0.008;
    public int InvertHorizontal { private get; set; } = 1;

    public void ApplyMouseSettings(double rotateSpeed, int invertHorizontal)
    {
        lock (_stateLock)
        {
            _camera.RotateSpeed = rotateSpeed;
            _camera.InvertHorizontal = invertHorizontal;
        }
    }

    /// <summary>تم بوم — رنگ پس‌زمینه (فاز ۵٫۲).</summary>
    public void SetCanvasBackground(Rgb bg)
    {
        _bgColor = bg;
        _dirty = true;
        _wake.Set();
    }

    private Rgb _bgColor = new(0xEA, 0xEE, 0xF4);

    /// <summary>پیش‌فرض‌های نمایش (فاز ۵٫۲: از تنظیمات).</summary>
    public void ApplyDisplayDefaults(bool showGrid, bool showAxes)
    {
        _showGrid = showGrid;
        _showAxes = showAxes;
        _dirty = true;
        _wake.Set();
    }

    // ---------- رشتهٔ رندر ----------

    private void StartRenderThread()
    {
        if (_renderThread is not null) return;
        _running = true;
        _renderThread = new Thread(RenderLoop)
        {
            Name = "SGM-ViewportRender",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _renderThread.Start();
        _fpsClock.Start();
    }

    private void StopRenderThread()
    {
        _running = false;
        _wake.Set();
        _renderThread?.Join(500);
        _renderThread = null;
    }

    private void RenderLoop()
    {
        var renderer = new SceneRenderer();
        var lastTime = DateTime.UtcNow;

        while (_running)
        {
            // ۱) فرمان‌های ورودی و وضعیت مشترک را بردار
            SceneObject[] objects;
            CameraFrame frame = default;
            Scene.RenderOptions opt;
            lock (_stateLock)
            {
                while (_pendingCommands.Count > 0)
                    _pendingCommands.Dequeue()(_camera);

                objects = _snapshot;
                opt = new Scene.RenderOptions
                {
                    Mode = _mode,
                    ShowGrid = _showGrid,
                    ShowAxes = _showAxes,
                    HighlightId = _highlightId,
                    ClipZ = _clipZ,
                    Background = _bgColor,
                };
                // پاره‌خط‌های اندازه‌گیری (۴٫۱)
                var measureCopy = _measurePoints.ToList();
                if (measureCopy.Count >= 2)
                {
                    var ml = new List<(MathX.Vector3, MathX.Vector3)>();
                    var lbls = new List<(MathX.Vector3, string)>();
                    for (int i = 0; i + 1 < measureCopy.Count; i++)
                    {
                        var a = measureCopy[i];
                        var b = measureCopy[i + 1];
                        ml.Add((a, b));
                        double d = (a - b).Length;
                        lbls.Add(((a + b) / 2, $"{d:0.##}mm"));
                    }
                    if (measureCopy.Count == 3)
                    {
                        var v1 = measureCopy[0] - measureCopy[1];
                        var v2 = measureCopy[2] - measureCopy[1];
                        double ang = Math.Acos(Math.Clamp(
                            MathX.Vector3.Dot(v1, v2) / Math.Max(1e-9, v1.Length * v2.Length), -1, 1));
                        lbls.Add((measureCopy[1], $"{ang * 180 / Math.PI:0.#}deg"));
                    }
                    opt = opt with { MeasureLines = ml, MeasureLabels = lbls };
                }
            }

            int w = 8, h = 8;
            w = _sizeW; h = _sizeH;

            var now = DateTime.UtcNow;
            double dt = (now - lastTime).TotalSeconds;
            lastTime = now;
            bool animating = false;
            lock (_stateLock) animating = _camera.Update(dt);
            lock (_stateLock)
            {
                frame = _camera.ComputeFrame(w, h);
                _sharedFrame = frame; // برای پیکینگ/زوم از رشتهٔ UI
                _frameValid = true;
            }

            bool dirty = _dirty;
            _dirty = false;

            // ۲) تشخیص سکون برای SSAA×2 (فاز ۱٫۵)
            bool idle = (now - _lastActivityUtc).TotalMilliseconds > 700;
            int wantSs = idle ? 2 : 1;
            if (wantSs != _ss)
            {
                _ss = wantSs;
                dirty = true;
            }

            if (dirty || animating)
            {
                renderer.Resize(w, h, _ss);
                var stats = renderer.Render(objects, frame, opt, _fpsEma);

                double fpsNow = dt > 1e-4 ? 1.0 / dt : 60;
                _fpsEma = _fpsEma * 0.9 + fpsNow * 0.1;

                // انتقال خروجی با قفل
                bool shouldBlit = false;
                lock (_stateLock)
                {
                    if (!_blitPending)
                    {
                        if (_transport.Length != stats.Width * stats.Height * 4)
                            _transport = new byte[stats.Width * stats.Height * 4];
                        renderer.Output.CopyTo(_transport);
                        _transportW = stats.Width;
                        _transportH = stats.Height;
                        _blitPending = true;
                        shouldBlit = true;
                    }
                }
                if (shouldBlit)
                {
                    var statsCopy = stats;
                    Dispatcher.BeginInvoke(() => Blit(statsCopy));
                }
            }

            if (!animating && !_dirty)
            {
                _wake.Wait(30); // سکون: انتظار برای ورودی/انیمیشن با بیدارباش دوره‌ای
                _wake.Reset();
            }
            else
            {
                Thread.Sleep(1); // فریم فعال — اجازهٔ هم‌زیستی با رشتهٔ UI
            }
        }
        renderer.Dispose();
    }

    private void Blit(RenderStats stats)
    {
        byte[] data;
        int w, h;
        lock (_stateLock)
        {
            if (_transport.Length == 0) { _blitPending = false; return; }
            data = _transport;
            w = _transportW;
            h = _transportH;
        }

        if (Surface.Source is not WriteableBitmap bmp || bmp.PixelWidth != w || bmp.PixelHeight != h)
        {
            bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            Surface.Source = bmp;
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), data, w * 4, 0);
        _blitPending = false;

        // HUD (فاز ۱٫۸)
        string modeLabel = _mode switch
        {
            DisplayMode.Shaded => "سایه",
            DisplayMode.Wireframe => "سیمی",
            _ => "سایه+لبه",
        };
        HudText.Text = $"fps {_fpsEma:F0}   |   {stats.TrianglesDrawn:N0} مثلث از {stats.ObjectsDrawn} قطعه   |   mm   |   {modeLabel}   |   x{_ss}";
    }

    // ---------- ماوس ----------

    private void Interaction_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        _lastMouse = e.GetPosition(InteractionLayer);
        _dragMoved = false;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _panning = true;
        }
        else
        {
            _rotating = true;
        }
        InteractionLayer.CaptureMouse();
        MarkActivity();
    }

    private void Interaction_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _lastMouse = e.GetPosition(InteractionLayer);
        _panning = true;
        _dragMoved = true;
        InteractionLayer.CaptureMouse();
        MarkActivity();
    }

    private void Interaction_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasRotating = _rotating;
        _rotating = false;
        InteractionLayer.ReleaseMouseCapture();
        MarkActivity();

        if (wasRotating)
        {
            if (_dragMoved) Enqueue(c => c.EndDrag());  // اینرسی فقط پس از درگ واقعی
            else if (_measureMode) DoMeasurePick(e);      // فاز ۴٫۱: نقطه‌گیری
            else DoPick(e);                              // کلیک تمیز = انتخاب (فاز ۱٫۱۹)
        }
    }

    private void DoMeasurePick(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(InteractionLayer);
        CameraFrame frame;
        SceneObject[] objects;
        lock (_stateLock)
        {
            frame = _sharedFrame;
            objects = _snapshot;
        }
        if (!_frameValid) return;
        var hit = ScenePicking.PickPoint(objects, frame, pos.X, pos.Y);
        if (hit is null)
        {
            // صفحهٔ هدف به‌عنوان جایگزین
            if (ScenePicking.PlaneAnchorUnderCursor(frame, pos.X, pos.Y) is { } anchor)
                AddMeasurePoint(anchor);
            return;
        }
        AddMeasurePoint(hit.Point);
    }

    private void Interaction_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panning = false;
        InteractionLayer.ReleaseMouseCapture();
        MarkActivity();
    }

    private void Interaction_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_rotating && !_panning) return;
        var pos = e.GetPosition(InteractionLayer);
        double dx = pos.X - _lastMouse.X;
        double dy = pos.Y - _lastMouse.Y;
        _lastMouse = pos;
        if (Math.Abs(dx) + Math.Abs(dy) < 0.5) return;
        _dragMoved = true;

        var now = DateTime.UtcNow;
        double dt = (now - _lastMoveUtc).TotalSeconds;
        _lastMoveUtc = now;
        MarkActivity();

        if (_panning)
        {
            double dx2 = dx, dy2 = dy;
            Enqueue(c => c.PanDrag(-dx2, -dy2));
        }
        else
        {
            double dx2 = dx, dy2 = dy, dt2 = Math.Clamp(dt, 1.0 / 240, 0.1);
            Enqueue(c => c.RotateDrag(-dx2, -dy2, dt2));
        }
    }

    private void Interaction_MouseLeave(object sender, MouseEventArgs e)
    {
        _rotating = false;
        _panning = false;
        MarkActivity();
    }

    private void Interaction_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        MarkActivity();
        var pos = e.GetPosition(InteractionLayer);
        int w = Math.Max(8, (int)ActualWidth), h = Math.Max(8, (int)ActualHeight);
        double factor = e.Delta > 0 ? 0.88 : 1.14;
        double px = pos.X, py = pos.Y;

        // لنگر: نقطهٔ جهان زیر مکان‌نما روی صفحهٔ هدف — برای تجربهٔ زوم حرفه‌ای (۱٫۱۴)
        CameraFrame frame;
        lock (_stateLock) frame = _sharedFrame;
        var anchor = _frameValid ? ScenePicking.PlaneAnchorUnderCursor(frame, px, py) : null;
        double ax = anchor?.X ?? 0, ay = anchor?.Y ?? 0, az = anchor?.Z ?? 0;
        var anchorV = anchor;
        Enqueue(c => c.ZoomAt(factor, px, py, w, h, frame, anchorV));
    }

    private void Interaction_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // راست‌کلیک: اول انتخاب زیر مکان‌نما، سپس منوی زمینه‌ای (۱٫۲۰)
        DoPick(e, silent: true);
    }

    private void Interaction_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // اگر چیزی انتخاب نشده بود منو باز نشود
        CanvasMenu.IsEnabled = _pickTargetId is not null;
    }

    private string? _pickTargetId;

    private void DoPick(MouseButtonEventArgs e, bool silent = false)
    {
        var pos = e.GetPosition(InteractionLayer);
        CameraFrame frame;
        SceneObject[] objects;
        lock (_stateLock)
        {
            frame = _sharedFrame;
            objects = _snapshot;
        }
        if (!_frameValid) return;

        var hit = ScenePicking.Pick(objects, frame, pos.X, pos.Y);
        _pickTargetId = hit?.ObjectId;
        if (!silent)
            PickRequested?.Invoke(this, hit?.ObjectId);
    }

    private void MarkActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
        if (_ss != 1) { _ss = 1; _dirty = true; }
    }

    // ---------- صفحه‌کلید (فاز ۱٫۱۸) ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        MarkActivity();
        double step = 0.12;
        switch (e.Key)
        {
            case Key.Left: Enqueue(c => c.RotateDrag(-step * 60, 0, 1)); break;
            case Key.Right: Enqueue(c => c.RotateDrag(step * 60, 0, 1)); break;
            case Key.Up: Enqueue(c => c.RotateDrag(0, step * 60, 1)); break;
            case Key.Down: Enqueue(c => c.RotateDrag(0, -step * 60, 1)); break;
            case Key.OemPlus or Key.Add: Enqueue(c => c.Zoom(0.88)); break;
            case Key.OemMinus or Key.Subtract: Enqueue(c => c.Zoom(1.14)); break;
            case Key.F: FrameAll(); e.Handled = true; break;
            case Key.G:
                _showGrid = !_showGrid;
                BtnGrid.IsChecked = _showGrid;
                _dirty = true; _wake.Set();
                e.Handled = true;
                break;
            case Key.P:
                ToggleProjection();
                e.Handled = true;
                break;
            case Key.M:
                EnableMeasure(!_measureMode);
                e.Handled = true;
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- نوار ابزار ----------

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _mode = ModeCombo.SelectedIndex switch
        {
            1 => DisplayMode.Shaded,
            2 => DisplayMode.Wireframe,
            _ => DisplayMode.ShadedWithEdges,
        };
        _dirty = true;
        _wake.Set();
    }

    private void BtnGrid_Click(object sender, RoutedEventArgs e)
    {
        _showGrid = BtnGrid.IsChecked == true;
        _dirty = true; _wake.Set();
    }

    private void BtnAxes_Click(object sender, RoutedEventArgs e)
    {
        _showAxes = BtnAxes.IsChecked == true;
        _dirty = true; _wake.Set();
    }

    private bool _isOrtho;

    private void ToggleProjection()
    {
        _isOrtho = !_isOrtho;
        bool ortho = _isOrtho;
        Enqueue(c => c.Projection = ortho ? ProjectionKind.Orthographic : ProjectionKind.Perspective);
        BtnProjection.Content = ortho ? "ارتو" : "پرسپکتیو";
    }

    private void BtnProjection_Click(object sender, RoutedEventArgs e) => ToggleProjection();

    private void BtnFrame_Click(object sender, RoutedEventArgs e) => FrameAll();

    private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
    {
        byte[]? data = null;
        int w = 0, h = 0;
        lock (_stateLock)
        {
            if (_transport.Length > 0)
            {
                data = (byte[])_transport.Clone();
                w = _transportW;
                h = _transportH;
            }
        }
        if (data is null)
        {
            MessageBox.Show(Window.GetWindow(this), "هنوز فریمی رندر نشده است.", "عکس بوم",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیرهٔ عکس بوم",
            Filter = "PNG|*.png",
            FileName = "sahand-viewport.png",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), data, w * 4, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(dlg.FileName);
            encoder.Save(fs);
            MessageBox.Show(Window.GetWindow(this), $"ذخیره شد: {dlg.FileName}", "عکس بوم",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"ذخیرهٔ عکس ناموفق بود: {ex.Message}", "عکس بوم",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        => ToggleFullscreenRequested?.Invoke(this, EventArgs.Empty);

    // ---------- مکعب نما (۱٫۱۵) ----------

    private void SetNamedView(double az, double el)
    {
        MarkActivity();
        Enqueue(c => c.SetView(az, el, c.Distance, c.Target, animate: true));
    }

    private void Cube_Iso(object sender, RoutedEventArgs e) => SetNamedView(Math.PI / 6, Math.PI / 5);
    private void Cube_Front(object sender, RoutedEventArgs e) => SetNamedView(0, 0.0001);
    private void Cube_Back(object sender, RoutedEventArgs e) => SetNamedView(Math.PI, 0.0001);
    private void Cube_Top(object sender, RoutedEventArgs e) => SetNamedView(0, 1.45);
    private void Cube_Bottom(object sender, RoutedEventArgs e) => SetNamedView(0, -1.45);
    private void Cube_Right(object sender, RoutedEventArgs e) => SetNamedView(-Math.PI / 2, 0.0001);
    private void Cube_Left(object sender, RoutedEventArgs e) => SetNamedView(Math.PI / 2, 0.0001);

    // ---------- منوی زمینه‌ای (۱٫۲۰) — رویدادها به میزبان ----------

    public event EventHandler<string>? FrameObjectRequested;
    public event EventHandler<string>? HideObjectRequested;
    public event EventHandler? ShowAllRequested;
    public event EventHandler<string>? LockObjectRequested;
    public event EventHandler<string>? DuplicateObjectRequested;
    public event EventHandler<string>? DeleteObjectRequested;
    public event EventHandler<string>? ColorObjectRequested;
    public event EventHandler<string>? OpacityObjectRequested;

    private void MenuFrame_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) FrameObjectRequested?.Invoke(this, id); }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) HideObjectRequested?.Invoke(this, id); }

    private void MenuShowAll_Click(object sender, RoutedEventArgs e)
        => ShowAllRequested?.Invoke(this, EventArgs.Empty);

    private void MenuLock_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) LockObjectRequested?.Invoke(this, id); }

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) DuplicateObjectRequested?.Invoke(this, id); }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) DeleteObjectRequested?.Invoke(this, id); }

    private void MenuColor_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) ColorObjectRequested?.Invoke(this, id); }

    private void MenuOpacity_Click(object sender, RoutedEventArgs e)
    { if (_pickTargetId is { } id) OpacityObjectRequested?.Invoke(this, id); }

    public void Dispose()
    {
        StopRenderThread();
        _wake.Dispose();
    }
}
