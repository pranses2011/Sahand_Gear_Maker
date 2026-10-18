using SahandGearMaker.Domain;

namespace SahandGearMaker.Geometry;

/// <summary>پروفیل شانهٔ مستقیم (rack) — مرز CCW بلوک با دندانه‌های ذوزنقه‌ای (سند ۰۳، خانوادهٔ رک).</summary>
public sealed class RackProfile : IProfileGenerator
{
    private readonly double _pitch;        // گام دندانه در صفحهٔ عرضی (π·mt)
    private readonly double _addendum;    // ارتفاع دندانه بالای خط گام
    private readonly double _dedendum;    // عمق پایه زیر خط گام
    private readonly double _halfToothPitchLine; // نیم‌ضخامت دندانه روی خط گام
    private readonly double _flankAngle;  // زاویهٔ پهلو از قائم (زاویهٔ فشار)
    private readonly double _length;      // طول شانه
    private readonly double _thickness;   // ضخامت بدنه زیر خط گام
    private readonly int _toothCount;

    public RackProfile(ToothParameters p, double length)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (length <= 0)
            throw new ArgumentException("طول شانه باید مثبت باشد.", nameof(length));

        _pitch = Math.PI * p.Module;
        _addendum = p.AddendumCoefficient * p.Module;
        _dedendum = p.DedendumCoefficient * p.Module;
        _halfToothPitchLine = (Math.PI * p.Module / 2.0 - p.BacklashAllowance) / 2.0;
        _flankAngle = p.PressureAngle;
        _length = length;
        _thickness = Math.Max(2.0 * p.Module, _dedendum * 2.0);
        _toothCount = Math.Max(1, (int)Math.Floor(length / _pitch));
    }

    /// <summary>تعداد دندانه‌ای که در طول مفاز جا می‌شود.</summary>
    public int ToothCount => _toothCount;

    /// <summary>خط گام در y=0 قرار دارد؛ دندانه‌ها در +y و بدنه در −y.</summary>
    public (double MinRadius, double MaxRadius) RadiusRange()
        => (0, Math.Sqrt((_length / 2.0) * (_length / 2.0) + (_addendum) * (_addendum)));

    public List<PolarPoint> BoundaryPoints(double chordToleranceMm)
    {
        // مرز مستطیل با دندانه‌ها روی y=0؛ چون IProfile قطبی است، مختصات دکارتی به قطبی تبدیل می‌شود
        // و مصرف‌کننده (RackBuilder) از X/Y مستقیم استفاده می‌کند.
        var pts = new List<PolarPoint>(_toothCount * 8 + 8);
        double half = _length / 2.0;
        double rootY = -_thickness;
        double tipY = _addendum;
        double dx = _dedendum * Math.Tan(_flankAngle); // جابه‌جایی افقی پهلو از قائم

        // CCW از گوشهٔ پایین-راست (x=+half, y=rootY):
        // راست به بالا، خط ریشه به چپ، دندانه‌ها، چپ به پایین، کف
        pts.Add(P(0, rootY));                       // گوشهٔ پایین راست (زاویهٔ مرجع)
        double x = half;
        while (x - _pitch >= -half)
        {
            double xc = x - _pitch / 2.0;           // مرکز دندانهٔ بعدی
            double rootRight = xc + _halfToothPitchLine + dx;
            double tipRight = xc + _halfToothPitchLine;
            double tipLeft = xc - _halfToothPitchLine;
            double rootLeft = xc - _halfToothPitchLine - dx;
            if (rootRight <= -half) break;
            // قوس ریشه تا پای پهلو
            pts.Add(P(Math.Min(x, half), rootY));
            if (rootRight <= half)
            {
                pts.Add(P(rootRight, rootY));
                pts.Add(P(tipRight, tipY));
                if (tipLeft > -half)
                {
                    pts.Add(P(tipLeft, tipY));
                    pts.Add(P(Math.Max(rootLeft, -half), rootY));
                }
            }
            x -= _pitch;
        }
        pts.Add(P(0, rootY)); // نشانهٔ پایان در سمت چپ؛ مصرف‌کننده rect را بازمی‌سازد

        return pts;

        PolarPoint P(double px, double py) => new(Math.Atan2(py, px), Math.Sqrt(px * px + py * py));
    }

    /// <summary>
    /// نقاط مرز دوبعدی دکارتی (برای سازندهٔ مش شانه) — CCW حول ماده.
    /// ترتیب: کف → راست → خط ریشه + دندانه‌ها (رو به چپ) → چپ.
    /// </summary>
    public List<(double X, double Y)> BoundaryXY(double backlash = 0.0)
    {
        var pts = new List<(double, double)>(_toothCount * 8 + 8);
        double half = _length / 2.0;
        double rootY = -_thickness;
        double tipY = _addendum;
        double dx = _dedendum * Math.Tan(_flankAngle);

        // شروع: پایین-راست، پادساعتگرد
        pts.Add((half, rootY));
        pts.Add((half, 0));

        double cursor = half;
        while (cursor - _pitch >= -half - 1e-9)
        {
            double xc = cursor - _pitch / 2.0;
            double rootRight = xc + _halfToothPitchLine + dx;
            double tipRight = xc + _halfToothPitchLine;
            double tipLeft = xc - _halfToothPitchLine;
            double rootLeft = xc - _halfToothPitchLine - dx;

            rootRight = Math.Min(rootRight, half);
            rootLeft = Math.Max(rootLeft, -half);

            if (rootRight < cursor - 1e-9)
                pts.Add((rootRight, rootY)); // ادامهٔ ریشه تا پای پهلو
            if (rootRight < half)
            {
                pts.Add((Math.Min(tipRight, half), tipY));
                pts.Add((Math.Max(tipLeft, -half), tipY));
                pts.Add((rootLeft, rootY));
            }
            cursor = rootLeft;
        }

        pts.Add((-half, 0));
        pts.Add((-half, rootY));
        return pts;
    }
}
