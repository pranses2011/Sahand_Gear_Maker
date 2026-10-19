using SahandGearMaker.Domain;

namespace SahandGearMaker.Geometry;

/// <summary>
/// مقاطع دوبعدی سوراخ مرکزی (بور) و حفره‌های سبک‌سازی — نسخهٔ ۰٫۵.
///
/// همهٔ خروجی‌ها چندضلعیِ بسته و پادساعتگرد (CCW) حول مبدأ خودشان هستند و مستقیماً
/// به «حلقهٔ رأس در ارتفاع دلخواه» (منشور مستقیم) یا مثلث‌بندی درپوش با حفره داده می‌شوند.
/// سوراخ‌های ماشین‌کاری موازی محورند؛ بنابراین این مقاطع با پیچش مارپیچ چرخ‌دنده نمی‌چرخند.
/// </summary>
public static class BoreSection
{
    /// <summary>شکل مؤثر بور با مهاجرت قدیمی: شیار خارِ فعال یعنی نوع Keyed.</summary>
    public static BoreShapeType EffectiveShape(BodyFeatures body)
        => body.KeywayWidth > 0 && body.BoreShape == BoreShapeType.Round
            ? BoreShapeType.Keyed
            : body.BoreShape;

    /// <summary>آیا این شکل بور برای این خانواده ساخته می‌شود یا به دایره برگردد؟</summary>
    public static bool SupportedOn(GearFamily family)
        => family is GearFamily.ExternalSpur or GearFamily.ExternalHelical or GearFamily.ExternalHerringbone
            or GearFamily.InternalSpur or GearFamily.InternalHelical or GearFamily.CrossedHelical
            or GearFamily.Cycloidal or GearFamily.SplineExternal or GearFamily.SplineInternal
            or GearFamily.Worm or GearFamily.WormWheel;

    /// <summary>
    /// ساخت مقطع بور (CCW، مختصات دکارتی حول مرکز). اندازه از BoreDiameter؛
    /// شکل‌های تکمیلی پارامترهای خود را از Body می‌خوانند.
    /// </summary>
    public static List<Point2> Build(BodyFeatures body, double boreRadius, double chordTol)
    {
        ArgumentNullException.ThrowIfNull(body);
        double r = boreRadius;
        int seg = Math.Clamp(CircleProfile.SegmentsForCircle(r, chordTol), 8, 256);

        switch (EffectiveShape(body))
        {
            case BoreShapeType.DShape:
            {
                double d = body.BoreFlatDistanceMm > 1e-9 ? body.BoreFlatDistanceMm : 0.8 * r;
                d = Math.Clamp(d, 0.05 * r, 0.98 * r);
                double y = Math.Sqrt(Math.Max(0, r * r - d * d));
                double aTopL = Math.Atan2(y, -d);   // ربع دوم (مثبت)
                double aBotL = Math.Atan2(-y, -d);  // ربع سوم (منفی)
                // مسیر CCW: از پایین-چپ با کمان بزرگ از سمت راست به بالا-چپ، سپس وتر تخت به پایین-چپ
                double span = aTopL - aBotL; // کمان اصلی (> π) از سمت راست — بدون دور اضافه
                int arcSeg = Math.Clamp((int)Math.Ceiling(span / (2.0 * Math.PI) * seg), 6, 256);
                var pts = new List<Point2>(arcSeg + 2);
                for (int i = 0; i <= arcSeg; i++)
                {
                    double a = aBotL + span * i / arcSeg;
                    pts.Add(new Point2(r * Math.Cos(a), r * Math.Sin(a)));
                }
                // آخرین نقطه = (−d, +y)؛ وتر تا (−d, −y) (نقطهٔ اول) حلقه را می‌بندد
                return pts;
            }

            case BoreShapeType.DoubleD:
            {
                double d = body.BoreFlatDistanceMm > 1e-9 ? body.BoreFlatDistanceMm : 0.75 * r;
                d = Math.Clamp(d, 0.05 * r, 0.98 * r);
                double y = Math.Sqrt(Math.Max(0, r * r - d * d));
                double aTopL = Math.Atan2(y, -d), aBotL = Math.Atan2(-y, -d);
                double aBotR = Math.Atan2(-y, d), aTopR = Math.Atan2(y, d);
                var pts = new List<Point2>(seg + 4);
                // CCW: بالا-چپ → کمان چپ → پایین-چپ → وتر → پایین-راست → کمان راست → بالا-راست → وتر بسته
                AppendArc(pts, r, aTopL, aBotL, Math.Max(6, seg / 2));
                AppendArc(pts, r, aBotR, aTopR + 2.0 * Math.PI, Math.Max(6, seg / 2));
                return pts;
            }

            case BoreShapeType.Hexagon:
                return RegularPolygon(6, (body.BoreDiameter / 2.0) / Math.Cos(Math.PI / 6.0), Math.PI / 6.0);

            case BoreShapeType.Square:
                return RegularPolygon(4, (body.BoreDiameter / 2.0) * Math.Sqrt(2.0), Math.PI / 4.0);

            case BoreShapeType.Spline:
            {
                int n = Math.Clamp(body.BoreSplineCount, 3, 24);
                double r0 = r;
                double r1 = r0 + Math.Max(0.05, body.BoreSplineDepthMm);
                double halfWidth = 0.25 * (2.0 * Math.PI * r0 / n); // عرض خار = نصف گام ریشه
                var pts = new List<Point2>(n * 12);
                for (int k = 0; k < n; k++)
                {
                    double c = 2.0 * Math.PI * k / n;
                    double psi0 = Math.Atan2(halfWidth, r0);
                    double psi1 = Math.Atan2(halfWidth, r1);
                    // ریشه: از انتهای خار قبلی تا ابتدای این خار (نقطهٔ آغاز تکراری حذف می‌شود)
                    double rootFrom = c - 2.0 * Math.PI / n + psi0;
                    double rootTo = c - psi0;
                    int rootSeg = Math.Clamp((int)Math.Ceiling((rootTo - rootFrom) * r0 / Math.Max(1e-4, 2.0 * chordTol)), 1, 48);
                    AppendArc(pts, r0, rootFrom, rootTo, rootSeg, skipFirst: true);
                    // پهلو صعودی + قوس نوک (آغازش همان بالای پهلو است) + پهلو نزولی
                    pts.Add(new Point2(r1 * Math.Cos(c - psi1), r1 * Math.Sin(c - psi1)));
                    AppendArc(pts, r1, c - psi1, c + psi1, Math.Clamp((int)Math.Ceiling(2.0 * psi1 * r1 / Math.Max(1e-4, 2.0 * chordTol)), 2, 24), skipFirst: true);
                    pts.Add(new Point2(r0 * Math.Cos(c + psi0), r0 * Math.Sin(c + psi0)));
                }
                return pts;
            }

            case BoreShapeType.Keyed:
                return KeyedSection(body, r, chordTol);

            default:
            {
                var pts = new List<Point2>(seg);
                for (int i = 0; i < seg; i++)
                {
                    double a = 2.0 * Math.PI * i / seg;
                    pts.Add(new Point2(r * Math.Cos(a), r * Math.Sin(a)));
                }
                return pts;
            }
        }
    }

    /// <summary>مقطع دایره + شیار خار (هم‌ارز RingWithKeyway اما مستقیم و بدون پیچش).</summary>
    private static List<Point2> KeyedSection(BodyFeatures body, double radius, double chordTol)
    {
        double keywayWidth = body.KeywayWidth;
        double keywayDepth = body.KeywayDepth;
        double endRadius = body.KeywayEndRadius;
        if (keywayWidth <= 0 || keywayDepth <= 0)
        {
            var circle = new List<Point2>(64);
            for (int i = 0; i < 64; i++)
            {
                double a = 2.0 * Math.PI * i / 64;
                circle.Add(new Point2(radius * Math.Cos(a), radius * Math.Sin(a)));
            }
            return circle;
        }

        double halfW = Math.Min(keywayWidth / 2.0, radius * Math.Sin(Math.PI / 4.0));
        double aEdge = Math.Asin(Math.Min(0.999, halfW / radius));
        double depth = Math.Min(keywayDepth, radius * 0.5);
        int arcBottom = Math.Max(8, CircleProfile.SegmentsForCircle(radius, chordTol) / 2);

        var pts = new List<Point2>(2 * arcBottom + 12);
        for (int i = 1; i <= arcBottom; i++) // کمان پایین از (π+aEdge) تا (2π−aEdge)
        {
            double a = Math.PI + aEdge + (Math.PI - 2.0 * aEdge) * i / arcBottom;
            pts.Add(new Point2(radius * Math.Cos(a), radius * Math.Sin(a)));
        }
        for (int i = 1; i < arcBottom; i++) // کمان بالا
        {
            double a = 2.0 * Math.PI - aEdge + Math.PI * i / arcBottom;
            pts.Add(new Point2(radius * Math.Cos(a), radius * Math.Sin(a)));
        }
        pts.Add(new Point2(radius * Math.Cos(Math.PI - aEdge), radius * Math.Sin(Math.PI - aEdge))); // لبهٔ چپ

        if (endRadius > 1e-9)
        {
            double rE = Math.Min(endRadius, halfW);
            double rCorner = radius + depth - rE;
            int arcSteps = Math.Clamp((int)Math.Ceiling(Math.PI * rE / Math.Max(1e-9, 4.0 * chordTol)), 4, 24);
            pts.Add(new Point2(rCorner * Math.Cos(Math.PI - aEdge), rCorner * Math.Sin(Math.PI - aEdge)));
            for (int i = 0; i <= arcSteps; i++)
            {
                double a = -Math.PI / 2.0 + Math.PI * i / arcSteps;
                double rr = rCorner + rE * Math.Sin(a);
                double aa = Math.PI + (rE * Math.Cos(a)) / Math.Max(1e-9, rCorner);
                pts.Add(new Point2(rr * Math.Cos(aa), rr * Math.Sin(aa)));
            }
            pts.Add(new Point2(rCorner * Math.Cos(Math.PI + aEdge), rCorner * Math.Sin(Math.PI + aEdge)));
        }
        else
        {
            pts.Add(new Point2((radius + depth) * Math.Cos(Math.PI - aEdge), (radius + depth) * Math.Sin(Math.PI - aEdge)));
            pts.Add(new Point2((radius + depth) * Math.Cos(Math.PI), (radius + depth) * Math.Sin(Math.PI)));
            pts.Add(new Point2((radius + depth) * Math.Cos(Math.PI + aEdge), (radius + depth) * Math.Sin(Math.PI + aEdge)));
        }

        pts.Add(new Point2(radius * Math.Cos(Math.PI + aEdge), radius * Math.Sin(Math.PI + aEdge))); // لبهٔ راست
        return pts;
    }

    private static void AppendArc(List<Point2> pts, double radius, double from, double to, int steps, bool skipFirst = false)
    {
        for (int i = skipFirst ? 1 : 0; i <= steps; i++)
        {
            double a = from + (to - from) * i / steps;
            pts.Add(new Point2(radius * Math.Cos(a), radius * Math.Sin(a)));
        }
    }

    private static List<Point2> RegularPolygon(int sides, double circumradius, double rotationRad)
    {
        var pts = new List<Point2>(sides);
        for (int i = 0; i < sides; i++)
        {
            double a = rotationRad + 2.0 * Math.PI * i / sides;
            pts.Add(new Point2(circumradius * Math.Cos(a), circumradius * Math.Sin(a)));
        }
        return pts;
    }
}

/// <summary>مقاطع حفره‌های سبک‌سازی — دایره، چندضلعی‌ها، شکاف و ستاره (CCW حول مبدأ).</summary>
public static class LighteningSection
{
    /// <summary>بیشینهٔ نیم‌گسترهٔ شکل (برای محاسبهٔ فاصلهٔ ایمن از لبهٔ تار).</summary>
    public static double MaxHalfExtent(LighteningHoleShape shape, double size)
        => shape switch
        {
            LighteningHoleShape.Slot => size,                       // نیم‌طول شکاف (طول = ۲×اندازه)
            LighteningHoleShape.Star => size / 2.0,                 // شعاع بیرونی ستاره
            LighteningHoleShape.Circle => size / 2.0,
            _ => (size / 2.0) / Math.Cos(Math.PI / SidesOf(shape)), // شعاع محیطی چندضلعی
        };

    private static int SidesOf(LighteningHoleShape shape) => shape switch
    {
        LighteningHoleShape.Triangle => 3,
        LighteningHoleShape.Square => 4,
        LighteningHoleShape.Pentagon => 5,
        LighteningHoleShape.Hexagon => 6,
        LighteningHoleShape.Octagon => 8,
        _ => 32,
    };

    /// <summary>ساخت مقطع حفره با «اندازه» = قطر دایره یا بین‌سرِ چندضلعی.</summary>
    public static List<Point2> Build(LighteningHoleShape shape, double size, double chordTol)
    {
        if (size <= 1e-9)
            return new List<Point2>();
        switch (shape)
        {
            case LighteningHoleShape.Circle:
            {
                int seg = Math.Clamp(CircleProfile.SegmentsForCircle(size / 2.0, chordTol), 8, 96);
                var pts = new List<Point2>(seg);
                for (int i = 0; i < seg; i++)
                {
                    double a = 2.0 * Math.PI * i / seg;
                    pts.Add(new Point2(size / 2.0 * Math.Cos(a), size / 2.0 * Math.Sin(a)));
                }
                return pts;
            }

            case LighteningHoleShape.Triangle or LighteningHoleShape.Square
                or LighteningHoleShape.Pentagon or LighteningHoleShape.Hexagon
                or LighteningHoleShape.Octagon:
            {
                int n = shape switch
                {
                    LighteningHoleShape.Triangle => 3,
                    LighteningHoleShape.Square => 4,
                    LighteningHoleShape.Pentagon => 5,
                    LighteningHoleShape.Hexagon => 6,
                    _ => 8,
                };
                double r = (size / 2.0) / Math.Cos(Math.PI / n);
                var pts = new List<Point2>(n);
                for (int i = 0; i < n; i++)
                {
                    double a = -Math.PI / 2.0 + 2.0 * Math.PI * i / n; // رأس به بالا
                    pts.Add(new Point2(r * Math.Cos(a), r * Math.Sin(a)));
                }
                return pts;
            }

            case LighteningHoleShape.Slot:
            {
                double w = size / 2.0;         // شعاع سرها
                double halfLen = size - w;     // نیم‌فاصلهٔ مراکز (طول کل = ۲×اندازه)
                int seg = Math.Clamp(CircleProfile.SegmentsForCircle(w, chordTol), 6, 48);
                var pts = new List<Point2>(2 * seg + 2);
                // کمان راست از −۹۰° به +۹۰° (مرکز +halfLen)
                for (int i = 0; i <= seg; i++)
                {
                    double a = -Math.PI / 2.0 + Math.PI * i / seg;
                    pts.Add(new Point2(halfLen + w * Math.Cos(a), w * Math.Sin(a)));
                }
                // کمان چپ از +۹۰° به +۲۷۰° (مرکز −halfLen)
                for (int i = 0; i <= seg; i++)
                {
                    double a = Math.PI / 2.0 + Math.PI * i / seg;
                    pts.Add(new Point2(-halfLen + w * Math.Cos(a), w * Math.Sin(a)));
                }
                return pts;
            }

            case LighteningHoleShape.Star:
            {
                int points = 6;
                double rOuter = size / 2.0;
                double rInner = rOuter * 0.55;
                var pts = new List<Point2>(points * 2);
                for (int i = 0; i < points * 2; i++)
                {
                    double a = Math.PI / 2.0 + Math.PI * i / points;
                    double rr = i % 2 == 0 ? rOuter : rInner;
                    pts.Add(new Point2(rr * Math.Cos(a), rr * Math.Sin(a)));
                }
                return pts;
            }

            default:
                return Build(LighteningHoleShape.Circle, size, chordTol);
        }
    }
}
