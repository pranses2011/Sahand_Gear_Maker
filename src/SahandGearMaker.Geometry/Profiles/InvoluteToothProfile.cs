using SahandGearMaker.Domain;

namespace SahandGearMaker.Geometry;

/// <summary>یک نقطهٔ مرزی قطعه در مختصات قطبی (زاویه بر حسب رادیان، شعاع بر حسب mm).</summary>
public readonly struct PolarPoint(double theta, double radius)
{
    public readonly double Theta = theta;
    public readonly double Radius = radius;

    public double X => Radius * Math.Cos(Theta);
    public double Y => Radius * Math.Sin(Theta);
}

/// <summary>
/// ژنراتور پروفیل مرزی قطعه‌ها — رابط مشترک همهٔ خانواده‌ها (سند ۰۳، بخش ۴).
/// مرزها پادساعتگرد (CCW) در صفحهٔ XY تولید می‌شوند تا نرمال مش رو به بیرون باشد.
/// </summary>
public interface IProfileGenerator
{
    /// <summary>حداقل/حداکثر شعاع مرز (برای حلقه‌های نمونه‌برداری داخلی).</summary>
    (double MinRadius, double MaxRadius) RadiusRange();

    /// <summary>نقاط مرز؛ زاویه‌ها صعودی و پیوسته‌اند (ممکن است از 2π بگذرند؛ مصرف‌کننده با mod استفاده می‌کند).</summary>
    List<PolarPoint> BoundaryPoints(double chordToleranceMm);
}

/// <summary>پروفیل دایرهٔ ساده (ریشهٔ چرخ‌دندهٔ داخلی، لبهٔ استوانه‌ها و…).</summary>
public sealed class CircleProfile(double radius) : IProfileGenerator
{
    public double Radius { get; } = radius;

    public (double, double) RadiusRange() => (Radius, Radius);

    public List<PolarPoint> BoundaryPoints(double chordToleranceMm)
    {
        if (Radius <= 0)
            throw new InvalidOperationException("شعاع دایره باید مثبت باشد.");
        int segments = SegmentsForCircle(Radius, chordToleranceMm);
        var pts = new List<PolarPoint>(segments);
        for (int i = 0; i < segments; i++)
            pts.Add(new PolarPoint(2.0 * Math.PI * i / segments, Radius));
        return pts;
    }

    /// <summary>تعداد قطعهٔ دایره برای رسیدن به خطای وتر خواسته‌شده: n = π / acos(1 − tol/r).</summary>
    public static int SegmentsForCircle(double radius, double chordToleranceMm)
    {
        if (chordToleranceMm <= 0) chordToleranceMm = 0.02;
        double ratio = Math.Min(0.9, chordToleranceMm / Math.Max(1e-9, radius));
        double n = Math.PI / Math.Acos(1.0 - ratio);
        return Math.Clamp((int)Math.Ceiling(n), 12, 720);
    }
}

/// <summary>
/// پروفیل کامل چرخ‌دندهٔ استوانه‌ای خارجی/داخلی با دندانهٔ involute (سند ۰۳، بخش ۴).
///
/// روابط پایه در صفحهٔ عرضی (transverse):
///   rb = r·cos(αt)؛ inv(α) = tan α − α
///   ψ (نیم‌زاویهٔ دندانه روی دایرهٔ پایه) = eb/(2r) + inv(αt)
///   نقطهٔ اینولوت: θ(t) = center ± (ψ + t − tan t)، ρ(t) = rb·√(1+t²)، t = tan α
///
/// برای چرخ‌دندهٔ داخلی، «دندانهٔ» ماده در واقع فضای خالی برای پینیون است؛
/// مرز ماده شامل قوس‌های دایرهٔ ریشه (شعاع بزرگ‌تر) و پهلوهای اینولوت فضای خالی است.
/// </summary>
public sealed class InvoluteToothProfile : IProfileGenerator
{
    private readonly GearGeometryFigures _fig;
    private readonly bool _internal;
    private readonly double _filletRadius;

    /// <summary>نیم‌زاویهٔ دندانه (خارجی) یا نیم‌زاویهٔ فضای خالی (داخلی) روی دایرهٔ پایه.</summary>
    private readonly double _baseHalfAngle;

    /// <summary>پارامتر t (=tan α) در دایرهٔ شروع مرز و دایرهٔ سر.</summary>
    private readonly double _tMin, _tMax;

    public InvoluteToothProfile(GearGeometryFigures figures, double filletRadius)
    {
        _fig = figures ?? throw new ArgumentNullException(nameof(figures));
        _internal = figures.Family is GearFamily.InternalSpur or GearFamily.InternalHelical;
        _filletRadius = Math.Max(0, filletRadius);

        double r = _fig.PitchRadius;
        double alphaT = _fig.TransversePressureAngle;
        double invA = Math.Tan(alphaT) - alphaT;
        double ebPitch = _fig.ToothThicknessAtPitch;

        double halfAngleAtPitch = _internal
            ? Math.PI / Math.Max(1, _fig.ToothCount) - ebPitch / (2.0 * r) // نیم‌فضای خالی روی گام
            : ebPitch / (2.0 * r);                                          // نیم‌ضخامت دندانه روی گام

        _baseHalfAngle = halfAngleAtPitch + invA;

        double rTip = _fig.TipRadius;
        double rStart = _internal ? _fig.RootRadius : Math.Max(_fig.BaseRadius, _fig.RootRadius);
        _tMin = TAtRadius(rStart);
        _tMax = TAtRadius(rTip);
        if (_tMax < _tMin + 1e-4)
            _tMax = _tMin + 0.05; // محافظ ورودی مرزی
    }

    public (double, double) RadiusRange()
        => (Math.Min(_fig.TipRadius, _fig.RootRadius), Math.Max(_fig.TipRadius, _fig.RootRadius));

    // ---------- روابط اینولوت ----------

    private double TAtRadius(double radius)
    {
        double rb = _fig.BaseRadius;
        return Math.Sqrt(Math.Max(0.0, radius * radius / (rb * rb) - 1.0));
    }

    /// <summary>زاویهٔ اینولوت نسبت به مرکز دندانه، مثبت (پهلوی +) — بدون center.</summary>
    private double InvTheta(double t) => _baseHalfAngle + t - Math.Tan(t);

    private double ThetaAtRadius(double radius) => InvTheta(TAtRadius(radius));

    // ---------- ساخت مرز ----------

    public List<PolarPoint> BoundaryPoints(double chordToleranceMm)
    {
        double tol = chordToleranceMm > 0 ? chordToleranceMm : 0.02;
        int z = _fig.ToothCount;
        double pitchAngle = 2.0 * Math.PI / z;
        var pts = new List<PolarPoint>(z * 40);

        for (int k = 0; k < z; k++)
        {
            double c = k * pitchAngle;
            if (_internal)
                AppendInternalTooth(pts, c, pitchAngle, tol);
            else
                AppendExternalTooth(pts, c, pitchAngle, tol);
        }
        return pts;
    }

    /// <summary>مرز یک دندانهٔ خارجی: ریشهٔ قبلی → فیله → پهلو − → سر → پهلو + → فیله.</summary>
    private void AppendExternalTooth(List<PolarPoint> pts, double c, double pitch, double tol)
    {
        double rRoot = _fig.RootRadius;
        double rTip = _fig.TipRadius;
        double rFlankStart = Math.Max(rRoot, _fig.BaseRadius);

        double thetaRootN = c - RootOpeningHalfAngle(pitch);  // لبهٔ ریشهٔ سمت −
        double thetaRootP = c + RootOpeningHalfAngle(pitch);  // لبهٔ ریشهٔ سمت +
        double thetaFlankN = c - ThetaAtRadius(rFlankStart);   // پای پهلو در شعاع شروع
        double thetaFlankP = c + ThetaAtRadius(rFlankStart);
        double thetaTipN = c - ThetaAtRadius(rTip);
        double thetaTipP = c + ThetaAtRadius(rTip);

        // 1) قوس ریشه از پایان دندانهٔ قبلی تا ابتدای دندانهٔ جاری
        double prevEdge = c - pitch + RootOpeningHalfAngle(pitch);
        AppendArc(pts, prevEdge, thetaRootN, rRoot, tol);

        // 2) فیلهٔ ریشه سمت − (CCW یعنی رو به سمت پهلو)
        AppendFillet(pts, thetaRootN, thetaFlankN, -1, tol);

        // 3) پهلو سمت − (اینولوت صعودی به سمت سر)
        AppendFlank(pts, c, -1, rFlankStart, rTip, tol);

        // 4) قوس سر
        AppendArc(pts, thetaTipN, thetaTipP, rTip, tol);

        // 5) پهلو سمت + (نزولی به سمت ریشه)
        AppendFlank(pts, c, +1, rTip, rFlankStart, tol);

        // 6) فیلهٔ ریشه سمت +
        AppendFillet(pts, thetaFlankP, thetaRootP, +1, tol);
    }

    /// <summary>
    /// مرز فضای خالی یک دندانهٔ داخلی (که پینیون در آن جا می‌گیرد):
    /// قوس ریشه (شعاع rf بیرونی) → پهلو اینولوت به سمت سر داخلی → قوس سر (ra) → پهلو بازگشت.
    /// </summary>
    private void AppendInternalTooth(List<PolarPoint> pts, double c, double pitch, double tol)
    {
        double rRoot = _fig.RootRadius; // بیرونی‌ترین ماده
        double rTip = _fig.TipRadius;   // داخلی‌ترین نقطهٔ فضای خالی

        double thetaTipN = c - ThetaAtRadius(rTip);
        double thetaTipP = c + ThetaAtRadius(rTip);
        double thetaRootN = c - ThetaAtRadius(rRoot);
        double thetaRootP = c + ThetaAtRadius(rRoot);

        // قوس ریشه بین دندانه‌ها (از پهلو + فضای قبلی تا پهلو − فضای جاری)
        double prevRootP = c - pitch + ThetaAtRadius(rRoot);
        AppendArc(pts, prevRootP, thetaRootN, rRoot, tol);

        // پهلو − از ریشه به سر (شعاع کاهش می‌یابد)
        AppendFlank(pts, c, -1, rRoot, rTip, tol);

        // قوس سر (کوچک‌ترین شعاع؛ خط گلوهٔ فضای خالی)
        AppendArc(pts, thetaTipN, thetaTipP, rTip, tol);

        // پهلو + از سر به ریشه
        AppendFlank(pts, c, +1, rTip, rRoot, tol);
    }

    /// <summary>نیم‌زاویهٔ دهانهٔ ریشه: محل تلاقی مرز ماده با دایرهٔ ریشه خارجی.</summary>
    private double RootOpeningHalfAngle(double pitch)
    {
        double rFlankStart = Math.Max(_fig.RootRadius, _fig.BaseRadius);
        double flankAtRoot = ThetaAtRadius(rFlankStart);
        // دهانهٔ ریشه را بین «تلاقی پهلو» و «نیم‌گام» انتخاب می‌کنیم تا فیله جا بگیرد
        return flankAtRoot + 0.35 * Math.Max(0, pitch / 2.0 - flankAtRoot);
    }

    private static void AppendArc(List<PolarPoint> pts, double from, double to, double radius, double tol)
    {
        if (to <= from || radius <= 0) return;
        int segments = Math.Clamp(
            (int)Math.Ceiling((to - from) / (2.0 * Math.PI) * CircleProfile.SegmentsForCircle(radius, tol)),
            1, 96);
        for (int i = 0; i <= segments; i++)
            pts.Add(new PolarPoint(from + (to - from) * i / segments, radius));
    }

    /// <summary>پهلو با نمونه‌برداری تطبیقی از t تا خطای وتر ≤ tol (سند ۰۴، بخش ۵).</summary>
    private void AppendFlank(List<PolarPoint> pts, double center, int side, double rFrom, double rTo, double tol)
    {
        double rb = _fig.BaseRadius;
        double tFrom = TAtRadius(rFrom);
        double tTo = TAtRadius(rTo);
        if (tTo < tFrom) (tFrom, tTo) = (tTo, tFrom);

        int steps = InvoluteSteps(rb, tFrom, tTo, tol);
        for (int i = 0; i <= steps; i++)
        {
            double t = tFrom + (tTo - tFrom) * i / steps;
            double local = _baseHalfAngle + t - Math.Tan(t);
            double theta = side >= 0 ? center + local : center - local;
            pts.Add(new PolarPoint(theta, rb * Math.Sqrt(1.0 + t * t)));
        }
    }

    /// <summary>تعداد گام نمونه‌برداری اینولوت با معیار خطای وتر: k·h²/8 ≤ tol.</summary>
    private static int InvoluteSteps(double rb, double tFrom, double tTo, double tol)
    {
        if (tol <= 0) tol = 0.02;
        double tEff = Math.Max(tFrom, 0.08);          // انحنای نزدیک پایه با کران امن
        double curvature = 1.0 / (rb * tEff);
        double h = Math.Sqrt(Math.Max(1e-12, 8.0 * tol / curvature));
        int steps = (int)Math.Ceiling((tTo - tFrom) / Math.Max(h, 1e-6));
        return Math.Clamp(steps, 4, 220);
    }

    /// <summary>
    /// فیلهٔ ریشه به‌صورت کمان دایره‌ای کوچک که دایرهٔ ریشه را به ابتدای پهلو نرم می‌کند.
    /// شعاع مؤثر به نیم فضای زاویه‌ای/شعاعی موجود محدود می‌شود (بدون خودتقاطع).
    /// side=−1 یعنی پهلو در جهت زاویهٔ کمتر قرار دارد و برعکس.
    /// </summary>
    private void AppendFillet(List<PolarPoint> pts, double thetaRoot, double thetaFlank, int side, double tol)
    {
        if (_filletRadius <= 1e-9)
            return;

        double rRoot = _fig.RootRadius;
        double rTip = _fig.TipRadius;
        double rOuter = Math.Max(rRoot, rTip);
        double span = Math.Abs(thetaFlank - thetaRoot);
        if (span <= 1e-12)
            return;

        // سقف‌ها: نیم فضای زاویه‌ای، فضای شعاعی ریشه→شروع پهلو و ضریب 0.4 (محافظه‌کارانه)
        double fr = Math.Min(_filletRadius,
                     Math.Min(span * rOuter * 0.5,
                              Math.Max(0, Math.Abs(rFlankRadius() - rRoot)) * 0.5) * 0.8);
        if (fr <= 1e-9)
            return;

        // مرکز کمان روی شعاع میانه، در عمق fr از دایرهٔ ریشه
        double dir = side; // جهت حرکت زاویه‌ای از ریشه به پهلو
        double mid = (thetaRoot + thetaFlank) / 2.0;
        double cr = Math.Max(1e-9, rOuter - fr);
        double ca = mid;

        // نقطهٔ شروع روی دایرهٔ ریشه (مماس شعاعی) و پایان نزدیک پپهلو
        double startTheta = ca + dir * Math.Asin(Math.Min(1.0, fr / cr));
        double startR = Math.Sqrt(cr * cr + fr * fr);
        double endTheta = thetaFlank - dir * Math.Min(fr / Math.Max(1e-9, rOuter) * 0.5, span * 0.25);
        double endR = rFlankRadius();

        int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(endTheta - startTheta) * cr / Math.Max(1e-9, 4.0 * tol)), 2, 16);
        for (int i = 0; i <= steps; i++)
        {
            double a = startTheta + (endTheta - startTheta) * i / steps;
            double r = startR + (endR - startR) * i / steps;
            pts.Add(new PolarPoint(a, r));
        }
    }

    private double rFlankRadius() => Math.Max(_fig.RootRadius, _fig.BaseRadius);
}
