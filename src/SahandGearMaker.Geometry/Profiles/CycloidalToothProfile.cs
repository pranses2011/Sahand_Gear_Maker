using SahandGearMaker.Domain;

namespace SahandGearMaker.Geometry;

/// <summary>
/// پروفیل سیکلوئیدی چرخ‌دنده (سند ۰۳، بخش ۴ — سیستم پروفیل دوم).
///
/// ریاضیات واقعی دندانهٔ سیکلوئیدی (قرارداد ساعت‌سازی با دایرهٔ مولّد نصف شعاع گام):
///   - بالای دندانه (گام → نوک): اپیسایکلوئید — غلتش بدون لغزش دایرهٔ مولّد re = r/2
///     روی بیرون دایرهٔ گام. پارامتر t = زاویهٔ چرخش مرکز دایرهٔ مولّد حول مرکز:
///       P(t) = ((r+re)·cos t − re·cos((r+re)/re·t), (r+re)·sin t − re·sin((r+re)/re·t))
///     با re = r/2: |P(t)| = r·√(2.5 − 1.5·cos 2t)
///   - پایین دندانه (ریشه → گام): هیپوسایکلوئید با ri = r/2 که به «خط شعاعی» تبدیل می‌شود
///     (نتیجهٔ کلاسیک: هیپوسایکلوئید با دایرهٔ مولّد نصف، قطر دایرهٔ پایه است)؛
///     بنابراین پهلو از ریشه تا گام دقیقاً شعاعی است و ضخامت زاویه‌ای دندانه در این ناحیه ثابت می‌ماند.
///
/// نیم‌زاویهٔ ضخامت روی گام همان قرارداد اینولوت است (eb/2r) تا با لقی/جفت‌شدن سازگار بماند.
/// مرز خروجی CCW و اکیداً صعودی در θ است (قرارداد IProfileGenerator).
/// </summary>
public sealed class CycloidalToothProfile : IProfileGenerator
{
    private readonly GearGeometryFigures _fig;
    private readonly double _filletRadius;

    private readonly double _r;               // شعاع دایرهٔ گام
    private readonly double _rTip;
    private readonly double _rRoot;
    private readonly double _halfAngle;       // نیم‌زاویهٔ ضخامت دندانه روی گام
    private readonly double _tipHalfAngle;    // نیم‌زاویهٔ ضخامت روی نوک (محاسبه‌شده)

    public CycloidalToothProfile(GearGeometryFigures figures, double filletRadius)
    {
        _fig = figures ?? throw new ArgumentNullException(nameof(figures));
        if (figures.Family is GearFamily.InternalSpur or GearFamily.InternalHelical)
            throw new InvalidOperationException("پروفیل سیکلوئیدی داخلی در این نسخه پشتیبانی نمی‌شود.");
        if (figures.TipRadius <= figures.PitchRadius + 1e-9)
            throw new InvalidOperationException("شعاع نوک باید بزرگ‌تر از شعاع گام باشد.");

        _filletRadius = Math.Max(0, filletRadius);
        _r = figures.PitchRadius;
        _rTip = Math.Min(figures.TipRadius, 1.999 * _r); // سقف اپیسایکلوئید: 2r
        _rRoot = figures.RootRadius;
        _halfAngle = figures.ToothThicknessAtPitch / (2.0 * _r);

        // زاویهٔ غلتش متناظر با شعاع نوک — فرم بسته: cos 2t = (2.5 − (rt/r)²)/1.5
        double ratioSq = (_rTip / _r) * (_rTip / _r);
        double c2t = Math.Clamp((2.5 - ratioSq) / 1.5, -1.0, 1.0);
        double tTip = 0.5 * Math.Acos(c2t);
        _tipHalfAngle = Math.Max(1e-4, AddendumAngle(tTip));
    }

    public (double, double) RadiusRange() => (_rRoot, _rTip);

    public List<PolarPoint> BoundaryPoints(double chordToleranceMm)
    {
        double tol = chordToleranceMm > 0 ? chordToleranceMm : 0.02;
        int z = _fig.ToothCount;
        double pitch = 2.0 * Math.PI / z;
        var pts = new List<PolarPoint>(z * 48);

        for (int k = 0; k < z; k++)
            AppendTooth(pts, k * pitch, pitch, tol);

        return pts;
    }

    // ---------- روابط اپیسایکلوئید (re = r/2) ----------

    /// <summary>شعاع نقطهٔ اپیسایکلوئید در زاویهٔ غلتش t: |P| = r·√(2.5 − 1.5·cos 2t).</summary>
    private double EpiRadius(double t)
        => _r * Math.Sqrt(Math.Max(0.0, 2.5 - 1.5 * Math.Cos(2.0 * t)));

    /// <summary>زاویهٔ موقعیت نقطهٔ اپیسایکلوئید نسبت به نقطهٔ شروع (روی دایرهٔ گام).</summary>
    private double EpiTheta(double t)
    {
        double re = _r / 2.0;
        double x = 1.5 * _r * Math.Cos(t) - re * Math.Cos(3.0 * t);
        double y = 1.5 * _r * Math.Sin(t) - re * Math.Sin(3.0 * t);
        return Math.Atan2(y, x);
    }

    /// <summary>زاویهٔ موقعیت نسبت به محور دندانه هنگام حرکت از گام به نوک (مثبت = به سمت محور).</summary>
    private double AddendumAngle(double t)
        => _halfAngle - EpiTheta(t);

    /// <summary>زاویهٔ غلتش تا رسیدن به شعاع داده‌شده (برای نمونه‌برداری یکنواخت شعاعی).</summary>
    private double RollAngleAtRadius(double radius)
    {
        double ratioSq = Math.Clamp((radius / _r) * (radius / _r), 1.0, 4.0);
        double c2t = Math.Clamp((2.5 - ratioSq) / 1.5, -1.0, 1.0);
        return 0.5 * Math.Acos(c2t);
    }

    // ---------- ساخت مرز ----------

    private void AppendTooth(List<PolarPoint> pts, double c, double pitch, double tol)
    {
        double rootHalf = Math.Min(_halfAngle * 1.35 + 0.02, pitch * 0.42);
        double flankAngle = _halfAngle; // پهلوی شعاعی: θ ثابت از ریشه تا گام
        double f = Math.Min(_filletRadius, Math.Max(0, _r - _rRoot) * 0.5); // عمق مؤثر فیله

        // 1) قوس ریشه از پایان دندانهٔ قبلی تا ابتدای فیلهٔ دندانهٔ جاری
        AppendArc(pts, c - pitch + rootHalf, c - rootHalf, _rRoot, tol);

        // 2) فیلهٔ ریشه سمت − : از دایرهٔ ریشه به پهلوی شعاعی (پایان روی rRoot + f)
        AppendRootFillet(pts, c - rootHalf, c - flankAngle, f);

        // 3) پهلوی شعاعی سمت − : ریشه → گام (θ ثابت) — نقطهٔ گام از ابتدای بخش ۴ می‌آید
        // 4) اپیسایکلوئید سمت − : گام → نوک
        AppendAddendum(pts, c, -1, tol);

        // 5) قوس نوک
        AppendArc(pts, c - _tipHalfAngle, c + _tipHalfAngle, _rTip, tol);

        // 6) اپیسایکلوئید سمت + : نوک → گام
        AppendAddendum(pts, c, +1, tol);

        // 7) پهلوی شعاعی سمت + : گام → ریشه
        pts.Add(new PolarPoint(c + flankAngle, _rRoot + f));

        // 8) فیلهٔ ریشه سمت + : به دایرهٔ ریشه
        AppendRootFillet(pts, c + flankAngle, c + rootHalf, -f);
    }

    /// <summary>اپیسایکلوئید یک طرف: نمونه‌برداری یکنواخت در شعاع از گام تا نوک.</summary>
    private void AppendAddendum(List<PolarPoint> pts, double c, int side, double tol)
    {
        int steps = Math.Clamp((int)Math.Ceiling((_rTip - _r) / Math.Max(1e-4, 2.0 * tol)), 8, 48);
        if (side < 0)
        {
            // صعود از گام به نوک روی پهلوی − : θ از (c − نیم‌گام) به (c − نیم‌نوک) رشد می‌کند
            for (int i = 0; i <= steps; i++)
            {
                double radius = _r + (_rTip - _r) * i / steps;
                double t = RollAngleAtRadius(radius);
                pts.Add(new PolarPoint(c - _halfAngle + EpiTheta(t), radius));
            }
        }
        else
        {
            // نزول از نوک به گام روی پهلوی + : θ از (c + نیم‌نوک) به (c + نیم‌گام) رشد می‌کند
            for (int i = steps; i >= 0; i--)
            {
                double radius = _r + (_rTip - _r) * i / steps;
                double t = RollAngleAtRadius(radius);
                pts.Add(new PolarPoint(c + _halfAngle - EpiTheta(t), radius));
            }
        }
    }

    /// <summary>
    /// فیلهٔ ریشه: گذر نرم بین دایرهٔ ریشه و پهلوی شعاعی.
    /// sign>0: از (θ۱، rRoot) به (θ۲، rRoot+f)؛ sign<0: مسیر معکوس (نزولی به rRoot).
    /// </summary>
    private void AppendRootFillet(List<PolarPoint> pts, double theta1, double theta2, double f)
    {
        if (Math.Abs(theta2 - theta1) < 1e-9)
            return;
        int steps = 4;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double theta = theta1 + (theta2 - theta1) * t;
            double radius = f >= 0
                ? _rRoot + f * Math.Sin(Math.PI / 2.0 * t)
                : _rRoot + (-f) * Math.Cos(Math.PI / 2.0 * t);
            pts.Add(new PolarPoint(theta, Math.Max(_rRoot, radius)));
        }
    }

    private static void AppendArc(List<PolarPoint> pts, double from, double to, double radius, double tol)
    {
        if (to <= from)
        {
            pts.Add(new PolarPoint(from, radius));
            return;
        }
        int seg = Math.Clamp((int)Math.Ceiling((to - from) * radius / Math.Max(1e-4, 2.0 * tol)), 1, 64);
        for (int i = 0; i < seg; i++)
            pts.Add(new PolarPoint(from + (to - from) * i / seg, radius));
    }
}
