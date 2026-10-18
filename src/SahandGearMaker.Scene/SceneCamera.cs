using SahandGearMaker.MathX;

namespace SahandGearMaker.Scene;

/// <summary>
/// وضعیت فوری دوربین برای رندر — مقداردهی‌شده از SceneCamera بعد از Update.
/// همهٔ بردارها واحد؛ focal بر حسب پیکسل خروجی است.
/// </summary>
public readonly struct CameraFrame
{
    public required Vector3 Eye { get; init; }
    public required Vector3 Forward { get; init; }
    public required Vector3 Right { get; init; }
    public required Vector3 Up { get; init; }
    public required ProjectionKind Projection { get; init; }

    /// <summary>فاصلهٔ کانونی مجازی (پیکسل) برای پرسپکتیو.</summary>
    public required double Focal { get; init; }

    /// <summary>مقیاس ارتو (پیکسل بر mm) — ارتفاع دید = Height / OrthoScale.</summary>
    public required double OrthoScale { get; init; }

    public double HalfW { get; init; }
    public double HalfH { get; init; }

    /// <summary>نقطهٔ هدف دوربین — برای لنگر زوم/مقیاس گرافیکی.</summary>
    public Vector3 Target { get; init; }

    /// <summary>نقطهٔ جهان روی پرتو عبوری از پیکسل (px, py).</summary>
    public Ray RayThroughPixel(double px, double py)
    {
        if (Projection == ProjectionKind.Perspective)
        {
            var dir = (Forward + Right * ((px - HalfW) / Focal) + Up * ((HalfH - py) / Focal)).Normalized();
            return new Ray(Eye, dir);
        }
        var origin = Eye + Right * ((px - HalfW) / OrthoScale) + Up * ((HalfH - py) / OrthoScale);
        return new Ray(origin, Forward);
    }
}

public readonly record struct Ray(Vector3 Origin, Vector3 Direction);

/// <summary>نمای نام‌دار ذخیره‌شونده در پروژه (فاز ۱٫۱۶).</summary>
public sealed class SavedCameraView
{
    public string Name { get; set; } = "نما";
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public double Distance { get; set; } = 160;
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public ProjectionKind Projection { get; set; } = ProjectionKind.Perspective;

    public static SavedCameraView FromCamera(SceneCamera cam, string name) => new()
    {
        Name = name,
        Azimuth = cam.Azimuth,
        Elevation = cam.Elevation,
        Distance = cam.Distance,
        TargetX = cam.Target.X,
        TargetY = cam.Target.Y,
        TargetZ = cam.Target.Z,
        Projection = cam.Projection,
    };

    public void ApplyTo(SceneCamera cam)
    {
        cam.Projection = Projection;
        cam.SetView(Azimuth, Elevation, Distance, new Vector3(TargetX, TargetY, TargetZ), animate: true);
    }
}

/// <summary>
/// دوربین مداری حرفه‌ای (فاز ۱): پرسپکتیو/ارتو، اینرسی با میرایی، انیمیشن نرم tween،
/// زوم به سمت مکان‌نما و قاب‌کردن هموار. مقادیر «هدف» با ورودی کاربر مستقیم تغییر می‌کنند و
/// مقادیر «جاری» در هر Update به سمت آن‌ها شتاب می‌گیرند — رندر همیشه از مقادیر جاری است.
/// </summary>
public sealed class SceneCamera
{
    // ----- وضعیت هدف (ورودی کاربر این‌ها را تغییر می‌دهد) -----
    public double Azimuth { get; set; } = Math.PI / 6;
    public double Elevation { get; set; } = Math.PI / 5;
    public double Distance { get; set; } = 220;
    public Vector3 Target { get; set; } = Vector3.Zero;
    public ProjectionKind Projection { get; set; } = ProjectionKind.Perspective;

    // ----- وضعیت جاری (انیمیت‌شونده) -----
    private double _curAz, _curEl, _curDist;
    private Vector3 _curTarget = Vector3.Zero;
    private bool _initialized;

    // ----- اینرسی -----
    private double _velAz, _velEl;
    private bool _inertiaActive;

    /// <summary>سرعت چرخش با درگ (رادیان بر پیکسل) — از تنظیمات کاربر (فاز ۵٫۲).</summary>
    public double RotateSpeed { get; set; } = 0.008;

    /// <summary>ضریب جهت افقی ماوس — برای کاربران چپ‌دست (فاز ۱٫۱۸).</summary>
    public int InvertHorizontal { get; set; } = 1;

    /// <summary>میرایی اینرسی بر ثانیه (0.9 = سریع می‌ایستد).</summary>
    public double InertiaDamping { get; set; } = 6.0;

    /// <summary>سرعت همگرایی انیمیشن (بر ثانیه).</summary>
    public double TweenRate { get; set; } = 14.0;

    /// <summary>آیا هنوز انیمیشن/اینرسی ادامه دارد؟ — برای تصمیم «رندر ادامه یابد».</summary>
    public bool IsAnimating { get; private set; }

    public double CurrentDistance => _curDist;

    // ---------- ورودی‌ها ----------

    /// <summary>چرخش با درگ — هم‌زمان سرعت برای اینرسی ثبت می‌کند.</summary>
    public void RotateDrag(double dxPixels, double dyPixels, double dtSeconds)
    {
        double k = dtSeconds > 1e-4 ? 1.0 / dtSeconds : 60.0;
        _velAz = dxPixels * RotateSpeed * InvertHorizontal * k;
        _velEl = -dyPixels * RotateSpeed * k;
        Azimuth += dxPixels * RotateSpeed * InvertHorizontal;
        Elevation = Math.Clamp(Elevation - dyPixels * RotateSpeed, -MaxElevation, MaxElevation);
        _inertiaActive = false; // در حین درگ، اینرسی خاموش
        IsAnimating = true;
    }

    /// <summary>رهاکردن دکمه — اینرسی با آخرین سرعت ادامه می‌یابد.</summary>
    public void EndDrag() => _inertiaActive = true;

    /// <summary>پن در صفحهٔ دید — جابه‌جایی هدف، متناسب با فاصله.</summary>
    public void PanDrag(double dxPixels, double dyPixels)
    {
        double scale = (_curDist > 0 ? _curDist : Distance) * 0.0016;
        var (fw, rt, up) = RoughBasis();
        Target = Target - rt * (dxPixels * scale) + up * (dyPixels * scale);
        _curTarget = Target; // پن باید بی‌تأخیر باشد
        IsAnimating = true;
    }

    /// <summary>حداکثر زاویهٔ ارتفاع: دقیقاً قطب (±۹۰°) — نمای بالا/پایین کاملاً قائم (رفع انحراف زاویه‌ای).</summary>
    public const double MaxElevation = Math.PI / 2;

    /// <summary>زوم — نقطهٔ جهانِ زیر مکان‌نما ثابت می‌ماند (فاز ۱٫۱۴).</summary>
    public void ZoomAt(double factor, double px, double py, int width, int height, in CameraFrame frame, Vector3? anchorWorld)
    {
        double oldDist = Distance;
        double newDist = Math.Clamp(oldDist * factor, 2.0, 20000.0);
        if (anchorWorld is { } anchor && Math.Abs(newDist - oldDist) > 1e-9)
        {
            // هدف را روی خط لنگر→هدف مقیاس بده تا لنگر روی همان پیکسل بماند (تقریب استاندارد orbit).
            var rel = Target - anchor;
            double ratio = newDist / oldDist;
            Target = anchor + rel * ratio;
        }
        Distance = newDist;
        IsAnimating = true;
    }

    /// <summary>زوم ساده (صفحه‌کلید/دکمه).</summary>
    public void Zoom(double factor)
    {
        Distance = Math.Clamp(Distance * factor, 2.0, 20000.0);
        IsAnimating = true;
    }

    /// <summary>قاب‌کردن کادر با انیمیشن (فاز ۱٫۱۴ tween).</summary>
    public void Frame((Vector3 Min, Vector3 Max) bounds, double margin = 1.9)
    {
        var center = new Vector3(
            (bounds.Min.X + bounds.Max.X) / 2,
            (bounds.Min.Y + bounds.Max.Y) / 2,
            (bounds.Min.Z + bounds.Max.Z) / 2);
        double size = Math.Max(1, Math.Max(
            bounds.Max.X - bounds.Min.X,
            Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z)));
        SetView(Azimuth, Elevation, size * margin, center, animate: true);
    }

    /// <summary>ست مستقیم نما (دکمه‌های نما/ViewCube/نمای ذخیره) — با یا بدون انیمیشن.</summary>
    public void SetView(double azimuth, double elevation, double distance, Vector3 target, bool animate)
    {
        Azimuth = azimuth;
        Elevation = Math.Clamp(elevation, -MaxElevation, MaxElevation);
        Distance = Math.Clamp(distance, 2.0, 20000.0);
        Target = target;
        _velAz = _velEl = 0;
        _inertiaActive = false;
        if (!animate) Snap();
        IsAnimating = true;
    }

    public void Snap()
    {
        _curAz = Azimuth; _curEl = Elevation; _curDist = Distance; _curTarget = Target;
        _velAz = _velEl = 0; _inertiaActive = false; IsAnimating = false;
        _initialized = true;
    }

    private void EnsureInit()
    {
        if (_initialized) return;
        Snap();
    }

    // ---------- پردازش انیمیشن ----------

    /// <summary>پیشبردن انیمیشن/اینرسی. dt بر حسب ثانیه. true = نیاز به فریم بیشتر.</summary>
    public bool Update(double dt)
    {
        EnsureInit();
        if (dt <= 0) dt = 1.0 / 60.0;

        // اینرسی پس از رهاکردن
        if (_inertiaActive && (Math.Abs(_velAz) > 0.02 || Math.Abs(_velEl) > 0.02))
        {
            Azimuth += _velAz * dt;
            Elevation = Math.Clamp(Elevation + _velEl * dt, -MaxElevation, MaxElevation);
            double damp = Math.Exp(-InertiaDamping * dt);
            _velAz *= damp;
            _velEl *= damp;
        }
        else
        {
            _inertiaActive = false;
        }

        // tween نمایی به سمت هدف
        double k = 1.0 - Math.Exp(-TweenRate * dt);
        _curAz += (Azimuth - _curAz) * k;
        _curEl += (Elevation - _curEl) * k;
        _curDist += (Distance - _curDist) * k;
        _curTarget = Vector3.Lerp(_curTarget, Target, k);

        bool azDone = Math.Abs(Azimuth - _curAz) < 1e-4;
        bool elDone = Math.Abs(Elevation - _curEl) < 1e-4;
        bool distDone = Math.Abs(Distance - _curDist) < 1e-3;
        bool tgtDone = (Target - _curTarget).LengthSquared < 1e-6;
        IsAnimating = !azDone || !elDone || !distDone || !tgtDone || _inertiaActive;
        return IsAnimating;
    }

    // ---------- ساخت فریم ----------

    private static double FocalOf(int height) => height / 2.0 * 1.8;

    public CameraFrame ComputeFrame(int width, int height)
    {
        EnsureInit();
        var (eye, forward, right, up) = ExactBasis(_curAz, _curEl, _curDist, _curTarget);
        double focal = FocalOf(height);
        // مقیاس ارتو: دید عمودی ≈ فاصله × 1.1/1.8 تا با پرسپکتیو هم‌قد دیده شود
        double ortho = height / (_curDist * 1.25);
        return new CameraFrame
        {
            Eye = eye,
            Forward = forward,
            Right = right,
            Up = up,
            Projection = Projection,
            Focal = focal,
            OrthoScale = ortho,
            HalfW = width / 2.0,
            HalfH = height / 2.0,
            Target = _curTarget,
        };
    }

    private (Vector3 F, Vector3 R, Vector3 U) RoughBasis()
    {
        var (eye, f, r, u) = ExactBasis(Azimuth, Elevation, Distance, Target);
        return (f, r, u);
    }

    private static (Vector3 Eye, Vector3 F, Vector3 R, Vector3 U) ExactBasis(
        double az, double el, double dist, Vector3 target)
    {
        // همان قرارداد OrbitCamera قدیمی — تا نماهای ذخیره‌شده و عادت کاربر تغییر نکند.
        double ce = Math.Cos(el), se = Math.Sin(el);
        var forward = new Vector3(-ce * Math.Cos(az), -ce * Math.Sin(az), -se).Normalized();
        var eye = target - forward * dist;
        // راست تحلیلی (مستقل از ارتفاع) — در قطب‌ها (نمای بالا/پایین دقیق) بی‌تقابل می‌ماند؛
        // فرمول متقاطعِ worldUp×forward در el=±90° صفر و NaN می‌شد؛ به‌همین دلیل پیش‌تر نما ۷° کج می‌ماند.
        var right = new Vector3(Math.Sin(az), -Math.Cos(az), 0);
        var rr = right.LengthSquared > 1e-12 ? right.Normalized() : new Vector3(1, 0, 0);
        var up = Vector3.Cross(forward, rr).Normalized();
        return (eye, forward, rr, up);
    }
}
