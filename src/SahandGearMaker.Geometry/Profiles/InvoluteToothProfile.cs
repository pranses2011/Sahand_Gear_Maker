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
/// زاویه‌ها اکیداً صعودی و مرز نسبت به مرکز ستاره‌ای است.
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
/// روابط دقیق اینولوت با پارامتر t = tan(α) در شعاع جاری:
///   r(t) = rb·√(1+t²)؛  inv(t) = t − atan(t)
///   پهلو (خارجی): θ = c ± [eb/(2r) + invA − inv(t)]     ← دندانه به سمت نوک باریک می‌شود
///   پهلو (داخلی):  θ = c ± [π/z − eb/(2r) − invA + inv(t)] ← دندانهٔ داخلی نیز به سمت نوک باریک می‌شود
/// که در هر دو حالت θ در جهت پیمایش CCW اکیداً صعودی می‌ماند (مرز ستاره‌ای و بدون خودتقاطع).
///
/// برای چرخ‌دندهٔ داخلی، مرزِ ترسیم‌شده «فضای خالی پینیون» است: قوس ریشهٔ بیرونی (rf)
/// و پهلوهای اینولوت دندانه‌های داخلی که تا دایرهٔ سر (ra) به مرکز نزدیک می‌شوند.
/// </summary>
public sealed class InvoluteToothProfile : IProfileGenerator
{
    private readonly GearGeometryFigures _fig;
    private readonly bool _internal;
    private readonly double _filletRadius;
    /// <summary>پخ لبهٔ نوک دندانه (mm شعاعی)؛ صفر = بدون پخ.</summary>
    private readonly double _tipChamfer;

    /// <summary>نیم‌زاویهٔ مرجع روی دایرهٔ پایه: بیرونی eb/(2r)+invA؛ داخلی π/z−eb/(2r)+invA.</summary>
    private readonly double _baseHalfAngle;

    public InvoluteToothProfile(GearGeometryFigures figures, double filletRadius, double tipChamferMm = 0.0)
    {
        _fig = figures ?? throw new ArgumentNullException(nameof(figures));
        _internal = figures.Family is GearFamily.InternalSpur or GearFamily.InternalHelical;
        _filletRadius = Math.Max(0, filletRadius);

        double radialRoom = Math.Max(0, _fig.TipRadius - Math.Max(_fig.RootRadius, _fig.BaseRadius));
        _tipChamfer = Math.Max(0, Math.Min(tipChamferMm, radialRoom * 0.45));

        double r = _fig.PitchRadius;
        double alphaT = _fig.TransversePressureAngle;
        double invA = InvoluteOf(Math.Tan(alphaT));
        double ebPitch = _fig.ToothThicknessAtPitch;

        double halfAngleAtPitch = _internal
            ? Math.PI / Math.Max(1, _fig.ToothCount) - ebPitch / (2.0 * r) // نیم‌فضای خالی روی گام
            : ebPitch / (2.0 * r);                                          // نیم‌ضخامت دندانه روی گام

        _baseHalfAngle = halfAngleAtPitch + invA;
    }

    public (double, double) RadiusRange()
        => (Math.Min(_fig.TipRadius, _fig.RootRadius), Math.Max(_fig.TipRadius, _fig.RootRadius));

    // ---------- روابط اینولوت ----------

    /// <summary>inv(t) = t − atan(t) — تابع اینولوت استاندارد با پارامتر t = tan(α).</summary>
    private static double InvoluteOf(double t) => t - Math.Atan(t);

    private double TAtRadius(double radius)
    {
        double rb = Math.Max(1e-9, _fig.BaseRadius);
        return Math.Sqrt(Math.Max(0.0, radius * radius / (rb * rb) - 1.0));
    }

    /// <summary>
    /// نیم‌زاویهٔ دندانه در پارامتر t (فاصلهٔ زاویه‌ای پهلو از محور دندانه).
    /// خارجی: با رشد t (رفتن به سمت نوک) کوچک می‌شود؛ داخلی: بزرگ می‌شود (دندانهٔ داخلی به سمت مرکز باریک می‌شود).
    /// </summary>
    private double HalfAngleAt(double t) => _internal
        ? Math.PI / Math.Max(1, _fig.ToothCount) - _baseHalfAngle + InvoluteOf(t)
        : _baseHalfAngle - InvoluteOf(t);

    private double ThetaAtRadius(double radius, int side) => side >= 0
        ? HalfAngleAt(TAtRadius(radius))
        : -HalfAngleAt(TAtRadius(radius));

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

    /// <summary>مرز یک دندانهٔ خارجی: ریشهٔ قبلی → فیله → پهلو − → [پخ] → سر → [پخ] → پهلو + → فیله.</summary>
    private void AppendExternalTooth(List<PolarPoint> pts, double c, double pitch, double tol)
    {
        double rRoot = _fig.RootRadius;
        double rTip = _fig.TipRadius;
        double rFlankStart = Math.Max(rRoot, _fig.BaseRadius);

        double thetaRootN = c - RootOpeningHalfAngle(pitch);  // لبهٔ ریشهٔ سمت − (پایان قوس ریشه)
        double thetaRootP = c + RootOpeningHalfAngle(pitch);  // لبهٔ ریشهٔ سمت +
        double thetaTipN = c - HalfAngleAt(TAtRadius(rTip));
        double thetaTipP = c + HalfAngleAt(TAtRadius(rTip));

        // 1) قوس ریشه از پایان دندانهٔ قبلی تا ابتدای دندانهٔ جاری
        double prevEdge = c - pitch + RootOpeningHalfAngle(pitch);
        AppendArc(pts, prevEdge, thetaRootN, rRoot, tol);

        // 2) فیلهٔ ریشه سمت − : از لبهٔ قوس ریشه تا پای پهلو
        AppendFillet(pts, thetaRootN, thetaFlankN(c), -1, tol);

        if (_tipChamfer > 1e-9)
        {
            // پخ ۴۵ درجهٔ گوشهٔ نوک: پهلو تا (rTip−c) و بعد لبهٔ مورب تا قوس کوتاه‌شدهٔ سر
            double rTipEff = rTip - _tipChamfer;
            double dTheta = _tipChamfer / Math.Max(1e-9, rTip);

            AppendFlank(pts, c, -1, rFlankStart, rTipEff, tol);
            pts.Add(new PolarPoint(thetaTipN + dTheta, rTip));
            AppendArc(pts, thetaTipN + dTheta, thetaTipP - dTheta, rTip, tol);
            pts.Add(new PolarPoint(thetaTipP - dTheta, rTip));
            AppendFlank(pts, c, +1, rTipEff, rFlankStart, tol);
        }
        else
        {
            // 3) پهلو سمت − (اینولوت صعودی به سمت سر؛ θ صعودی)
            AppendFlank(pts, c, -1, rFlankStart, rTip, tol);

            // 4) قوس سر
            AppendArc(pts, thetaTipN, thetaTipP, rTip, tol);

            // 5) پهلو سمت + (نزولی به سمت ریشه؛ θ همچنان صعودی — جهت پیمایش حفظ می‌شود)
            AppendFlank(pts, c, +1, rTip, rFlankStart, tol);
        }

        // 6) فیلهٔ ریشه سمت +
        AppendFillet(pts, thetaFlankP(c), thetaRootP, +1, tol);
    }

    /// <summary>
    /// مرز فضای خالی یک دندانهٔ داخلی (که پینیون در آن جا می‌گیرد) — CCW و اکیداً صعودی:
    /// قوس ریشهٔ بیرونی (rf) → پهلو − از ریشه به سر → قوس سر داخلی (ra) → پهلو + از سر به ریشه.
    /// </summary>
    private void AppendInternalTooth(List<PolarPoint> pts, double c, double pitch, double tol)
    {
        double rRoot = _fig.RootRadius;      // بیرونی‌ترین ماده (دایرهٔ ریشهٔ دندانهٔ داخلی)
        double rTip = _fig.TipRadius;        // داخلی‌ترین نقطهٔ نوک دندانهٔ داخلی
        double rb = Math.Max(1e-9, _fig.BaseRadius);

        // پهلوها فقط برای r ≥ rb تعریف‌اند؛ نوک مؤثر = max(ra, rb)
        double rTipEff = Math.Max(rTip, rb * 1.0001);
        double rStart = Math.Max(rRoot, rb * 1.0001);

        double thetaRootN = c - HalfAngleAt(TAtRadius(rStart));
        double thetaRootP = c + HalfAngleAt(TAtRadius(rStart));
        double thetaTipN = c - HalfAngleAt(TAtRadius(rTipEff));
        double thetaTipP = c + HalfAngleAt(TAtRadius(rTipEff));

        // قوس ریشه بین دندانه‌ها: از پهلو + دندانهٔ قبلی تا پهلو − دندانهٔ جاری
        double prevRootP = c - pitch + HalfAngleAt(TAtRadius(rStart));
        AppendArc(pts, prevRootP, thetaRootN, rRoot, tol);

        if (rTipEff < rStart - 1e-9)
        {
            // پهلو − از ریشه به سر (شعاع کاهش می‌یابد؛ θ صعودی می‌ماند)
            AppendFlank(pts, c, -1, rStart, rTipEff, tol);
            // قوس سر داخلی
            AppendArc(pts, thetaTipN, thetaTipP, rTipEff, tol);
            // پهلو + از سر به ریشه
            AppendFlank(pts, c, +1, rTipEff, rStart, tol);
        }
        else
        {
            // حالت مرزی: نوک تا پایهٔ مشترک رسیده — فقط قوس ریشه کوتاه
            AppendArc(pts, thetaRootN, thetaRootP, rStart, tol);
        }
    }

    /// <summary>نیم‌زاویهٔ دهانهٔ ریشه: محل تلاقی مرز ماده با دایرهٔ ریشه خارجی.</summary>
    private double RootOpeningHalfAngle(double pitch)
    {
        double rFlankStart = Math.Max(_fig.RootRadius, _fig.BaseRadius);
        double flankAtRoot = HalfAngleAt(TAtRadius(rFlankStart));
        return flankAtRoot + 0.35 * Math.Max(0, pitch / 2.0 - flankAtRoot);
    }

    private double thetaFlankN(double c) => c - HalfAngleAt(TAtRadius(Math.Max(_fig.RootRadius, _fig.BaseRadius)));
    private double thetaFlankP(double c) => c + HalfAngleAt(TAtRadius(Math.Max(_fig.RootRadius, _fig.BaseRadius)));

    private static void AppendArc(List<PolarPoint> pts, double from, double to, double radius, double tol)
    {
        if (to <= from || radius <= 0) return;
        int segments = Math.Clamp(
            (int)Math.Ceiling((to - from) / (2.0 * Math.PI) * CircleProfile.SegmentsForCircle(radius, tol)),
            1, 96);
        for (int i = 0; i <= segments; i++)
            pts.Add(new PolarPoint(from + (to - from) * i / segments, radius));
    }

    /// <summary>
    /// پهلو با نمونه‌برداری تطبیقی از t تا خطای وتر ≤ tol.
    /// جهت پیمایش t حفظ می‌شود (بدون جابه‌جایی) تا ترتیب CCW مرز در هر دو پهلو برقرار بماند.
    /// </summary>
    private void AppendFlank(List<PolarPoint> pts, double center, int side, double rFrom, double rTo, double tol)
    {
        double rb = Math.Max(1e-9, _fig.BaseRadius);
        double tFrom = TAtRadius(rFrom);
        double tTo = TAtRadius(rTo);
        if (tTo < tFrom) (tFrom, tTo) = (tTo, tFrom); // نمونه‌ها همیشه صعودی در t تولید و در صورت نیاز معکوس درج می‌شوند

        int steps = InvoluteSteps(rb, tFrom, tTo, tol);
        if (side >= 0)
        {
            // پهلو + : از نوک به ریشه پیمایش می‌شود (t نزولی) تا θ صعودی بماند
            for (int i = steps; i >= 0; i--)
            {
                double t = tFrom + (tTo - tFrom) * i / steps;
                pts.Add(new PolarPoint(center + HalfAngleAt(t), rb * Math.Sqrt(1.0 + t * t)));
            }
        }
        else
        {
            for (int i = 0; i <= steps; i++)
            {
                double t = tFrom + (tTo - tFrom) * i / steps;
                pts.Add(new PolarPoint(center - HalfAngleAt(t), rb * Math.Sqrt(1.0 + t * t)));
            }
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

        double rFlankR = Math.Max(rRoot, _fig.BaseRadius);
        double fr = Math.Min(_filletRadius,
                     Math.Min(span * rOuter * 0.5,
                              Math.Max(0, Math.Abs(rFlankR - rRoot)) * 0.5) * 0.8);
        if (fr <= 1e-9)
            return;

        double dir = side;
        double mid = (thetaRoot + thetaFlank) / 2.0;
        double cr = Math.Max(1e-9, rOuter - fr);
        double ca = mid;

        double startTheta = ca + dir * Math.Asin(Math.Min(1.0, fr / cr));
        double startR = Math.Sqrt(cr * cr + fr * fr);
        double endTheta = thetaFlank - dir * Math.Min(fr / Math.Max(1e-9, rOuter) * 0.5, span * 0.25);
        double endR = rFlankR;

        int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(endTheta - startTheta) * cr / Math.Max(1e-9, 4.0 * tol)), 2, 16);
        for (int i = 0; i <= steps; i++)
        {
            double a = startTheta + (endTheta - startTheta) * i / steps;
            double r = startR + (endR - startR) * i / steps;
            pts.Add(new PolarPoint(a, r));
        }
    }
}
