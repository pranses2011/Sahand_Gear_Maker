using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش چرخ‌دندهٔ غیرمدور بیضوی و کامِ بیضوی موج‌ساز هارمونیک.
///
/// چرخ‌دندهٔ بیضوی (خانوادهٔ NonCircular):
///   - منحنی گام: بیضی با خروج از مرکز e و نیم‌محور بزرگ a که از شرط جاگیری
///     «محیط = z·π·m» حل می‌شود: 4a·E(e) = z·π·m (E = انتگرال بیضوی کامل مرتبهٔ دوم)؛
///   - دندانه‌ها در فواصل طول-کمانی مساوی قرار می‌گیرند؛ پروفیل هر دندانه «شانه‌ای موضعی»
///     با زاویهٔ فشار α در دستگاهِ (مماس، نرمال) نقطهٔ گام است — نقاط پروفیل به‌صورت
///     منحنی آفست (مکان هندسی نقطه با فاصلهٔ نرمال v روی کمان u) روی بدنه نگاشت می‌شوند؛
///   - بور روی کانون ۱ بیضی است (محور چرخش واقعی؛ فاصلهٔ محور جفت یکسان L = 2a)؛
///   - بدنه: منشور با درپوش‌های مثلث‌بندی‌شده (حفرهٔ بور پشتیبانی می‌شود).
///
/// کام موج‌ساز (درایو هارمونیک): منشور بیضوی مماس داخلی دیوارهٔ فنجان فِلکسی‌پلاین
/// در حالت تغییرشکل (A = دیوارهٔ داخلی + دامنهٔ موج، B = A − 2×دامنه).
/// </summary>
public static class NonCircularMeshBuilder
{
    public static GearBuildResult BuildGear(
        ToothParameters teeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        var diags = GearValidator.Validate(GearFamily.NonCircular, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای چرخ غیرمدور نامعتبرند.", errors);

        double m = teeth.Module;
        int z = teeth.ToothCount;
        if (z < 12 || z > 300)
        {
            errors.Add(new GearDiagnostic("SGM-E300", Severity.Error,
                "تعداد دندانهٔ چرخ غیرمدور باید بین ۱۲ تا ۳۰۰ باشد.", "ToothCount"));
            throw new GeometryBuildException("تعداد دندانهٔ چرخ غیرمدور خارج از دامنه است.", errors);
        }

        double e = Math.Clamp(specific.NonCircularEccentricity, 0.05, 0.90);
        if (Math.Abs(specific.NonCircularEccentricity - e) > 1e-9)
            diags.Add(new GearDiagnostic("SGM-W301", Severity.Warning,
                $"خروج از مرکز به {e:F2} محدود شد (دامنهٔ معتبر ۰٫۰۵ تا ۰٫۹).", "NonCircularEccentricity"));

        double E = CompleteEllipticE(e);
        double a = z * Math.PI * m / (4.0 * E);
        double bAx = a * Math.Sqrt(1.0 - e * e);
        double cF = a * e;

        double ha = teeth.AddendumCoefficient * m;
        double hf = teeth.DedendumCoefficient * m;
        double alpha = teeth.PressureAngle;
        double width = teeth.FaceWidth;
        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);

        double rCurvMin = bAx * bAx / a;
        if (m > 0.18 * rCurvMin)
        {
            errors.Add(new GearDiagnostic("SGM-E302", Severity.Error,
                $"ماژول نسبت به انحنای بیضی بزرگ است (ρmin={rCurvMin:F2} mm). ماژول را کم کنید یا خروج از مرکز را کاهش دهید.", "Module"));
            throw new GeometryBuildException("دندانه در نوک تیز بیضی جا نمی‌شود.", errors);
        }

        double boreR = body.BoreDiameter / 2.0;
        double rootNearFocus = MinRootDistance(a, e, hf);
        if (boreR > 0 && boreR >= rootNearFocus - 0.4 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E303", Severity.Error,
                $"بور روی کانون با ریشهٔ دندانه تداخل دارد (حداکثر ≈ {2.0 * Math.Max(0, rootNearFocus - 0.4 * m):F1} mm).", "BoreDiameter"));
            throw new GeometryBuildException("بور غیرمدور با ریشه تداخل دارد.", errors);
        }

        var rawBoundary = BuildEllipticalBoundary(a, e, z, m, ha, hf, alpha, tol);
        var boundary = new List<Point2>(rawBoundary.Count);
        foreach (var p in rawBoundary)
        {
            if (boundary.Count > 0)
            {
                var q = boundary[^1];
                if (Math.Abs(p.X - q.X) < 1e-9 && Math.Abs(p.Y - q.Y) < 1e-9)
                    continue;
            }
            boundary.Add(p);
        }
        while (boundary.Count > 1)
        {
            var f0 = boundary[0];
            var fN = boundary[^1];
            if (Math.Abs(f0.X - fN.X) < 1e-9 && Math.Abs(f0.Y - fN.Y) < 1e-9)
                boundary.RemoveAt(boundary.Count - 1);
            else
                break;
        }
        if (boundary.Count < 16)
            throw new GeometryBuildException("مرز بیضوی نقاط کافی ندارد.", diags);

        var mesh = ExtrudePrism(boundary, width, boreR, tol);

        var fig = new GearGeometryFigures
        {
            Family = GearFamily.NonCircular,
            TransverseModule = m,
            TransversePressureAngle = alpha,
            PitchRadius = a,
            BaseRadius = a * Math.Cos(alpha),
            TipRadius = a * (1.0 + e) + ha,
            RootRadius = Math.Max(0.1, a * (1.0 - e) - hf),
            AngularPitch = 2.0 * Math.PI / z,
            ToothThicknessAtPitch = Math.PI * m / 2.0,
            HelixAngleUsed = 0,
            RootFilletRadius = 0.2 * m,
            EstimatedVolumeMm3 = Math.PI * a * bAx * width * 1.12,
            ToothCount = z,
        };

        diags.Add(new GearDiagnostic("SGM-I304", Severity.Info,
            $"چرخ بیضوی: e={e:F2}، a={a:F2}/b={bAx:F2} mm، کانون c={cF:F2} mm (محور چرخش)، فاصلهٔ محور جفت یکسان L=2a={2 * a:F2} mm.", "NonCircularEccentricity"));
        diags.Add(new GearDiagnostic("SGM-I305", Severity.Info,
            "پروفیل دندانه: شانهٔ موضعی با زاویهٔ فشار در دستگاه مماس-نرمال (تقریب مرتبهٔ اول طراحی غیرمدور).", "ProfileSystem"));
        diags.Add(new GearDiagnostic("SGM-U306", Severity.Unverified,
            "کنترل تداخل پروفیل جفت بیضوی (خواص نوری) به‌صورت حل عددی کامل اعتبارسنجی نشده؛ نسبت لحظه‌ای در مونتاژ از فرمول مرجع i(θ)=(1+e²+2e·cosθ)/(1−e²) محاسبه می‌شود."));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags,
            AxialSlicesUsed = 2,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>کام بیضوی موج‌ساز برای مجموعهٔ درایو هارمونیک (منشور با بور مرکزی).</summary>
    public static TriMesh BuildWaveGenerator(
        ToothParameters flexTeeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(flexTeeth);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        double m = flexTeeth.Module;
        double zF = flexTeeth.ToothCount;
        double rfFlex = m * zF / 2.0 - 1.25 * m;
        double tw = specific.HarmonicCupWallThicknessMm > 0 ? specific.HarmonicCupWallThicknessMm : 0.015 * m * zF;
        double w0 = specific.HarmonicWaveDeflectionMm > 0 ? specific.HarmonicWaveDeflectionMm : 1.8 * m;

        double innerWall = rfFlex - tw;
        double A = innerWall + w0 - 0.05 * m;
        double B = Math.Max(A - 2.0 * w0, 0.2 * A);

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);
        double width = flexTeeth.FaceWidth;

        var boundary = new List<Point2>();
        int seg = Math.Clamp((int)Math.Ceiling(2.0 * Math.PI * A / Math.Max(1e-4, 2.0 * tol)), 48, 720);
        for (int i = 0; i < seg; i++)
        {
            double t = 2.0 * Math.PI * i / seg;
            boundary.Add(new Point2(A * Math.Cos(t), B * Math.Sin(t)));
        }

        double boreR = body.BoreDiameter / 2.0;
        if (boreR >= B - 1.0 * m)
            boreR = Math.Max(0.0, B - 1.0 * m);

        return ExtrudePrism(boundary, width, boreR, tol);
    }

    // ---------- ریاضیات بیضی ----------

    /// <summary>انتگرال بیضوی کامل مرتبهٔ دوم E(e) با انتگرال‌گیری سیمپسون (دقت بالا).</summary>
    public static double CompleteEllipticE(double e)
    {
        const int N = 2048;
        double h = Math.PI / 2.0 / N;
        double sum = 0;
        for (int i = 0; i <= N; i++)
        {
            double th = h * i;
            double f = Math.Sqrt(Math.Max(0.0, 1.0 - e * e * Math.Sin(th) * Math.Sin(th)));
            double w = i == 0 || i == N ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);
            sum += w * f;
        }
        return sum * h / 3.0;
    }

    /// <summary>شعاع منحنی گام بیضی در زاویهٔ قطبی θ: r = a(1−e²)/(1+e·cos θ).</summary>
    private static double R(double a, double e, double theta)
        => a * (1.0 - e * e) / (1.0 + e * Math.Cos(theta));

    /// <summary>
    /// کمینهٔ فاصلهٔ مرز ریشه تا کانون (مبدأِ دستگاه قطبی r(θ) = a(1−e²)/(1+e·cosθ) خودِ کانون است).
    /// </summary>
    private static double MinRootDistance(double a, double e, double hf)
    {
        double best = double.MaxValue;
        for (int i = 0; i < 1440; i++)
        {
            double th = 2.0 * Math.PI * i / 1440;
            // فاصلهٔ مستقیم نقاط ریشه تا کانون؛ کمینه در نزدیک‌ترین رأس رخ می‌دهد
            double rRoot = R(a, e, th) - hf;
            best = Math.Min(best, rRoot);
        }
        return best;
    }

    /// <summary>نقطهٔ گام در طول-کمان s: موقعیت، مماس و نرمال بیرونی واحد.</summary>
    private sealed class PitchCurve
    {
        private readonly double _a;
        private readonly double _e;
        private readonly double[] _thetas;
        private readonly double[] _arcs;
        public readonly double Perimeter;

        public PitchCurve(double a, double e, int samples)
        {
            _a = a;
            _e = e;
            _thetas = new double[samples + 1];
            _arcs = new double[samples + 1];
            double total = 0;
            double px = double.NaN, py = 0;
            for (int i = 0; i <= samples; i++)
            {
                double th = 2.0 * Math.PI * i / samples;
                _thetas[i] = th;
                double r = R(a, e, th);
                double x = r * Math.Cos(th), y = r * Math.Sin(th);
                if (i > 0)
                    total += Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                _arcs[i] = total;
                px = x; py = y;
            }
            Perimeter = total;
        }

        public (double X, double Y, double Tx, double Ty, double Nx, double Ny, double Theta) At(double s)
        {
            double ss = ((s % Perimeter) + Perimeter) % Perimeter;
            int lo = 0, hi = _arcs.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_arcs[mid] <= ss) lo = mid; else hi = mid;
            }
            double t = _arcs[hi] - _arcs[lo] < 1e-12 ? 0 : (ss - _arcs[lo]) / (_arcs[hi] - _arcs[lo]);
            double th = _thetas[lo] + (_thetas[hi] - _thetas[lo]) * t;

            const double d = 1e-6;
            double rp = R(_a, _e, th + d), rm = R(_a, _e, th - d);
            double dx = (rp * Math.Cos(th + d) - rm * Math.Cos(th - d)) / (2 * d);
            double dy = (rp * Math.Sin(th + d) - rm * Math.Sin(th - d)) / (2 * d);
            double len = Math.Sqrt(dx * dx + dy * dy);
            dx /= len; dy /= len;
            double nx = dy, ny = -dx; // نرمال بیرونی برای پیمایش CCW
            if (nx * Math.Cos(th) + ny * Math.Sin(th) < 0)
            {
                nx = -nx; ny = -ny;
            }
            double r = R(_a, _e, th);
            return (r * Math.Cos(th), r * Math.Sin(th), dx, dy, nx, ny, th);
        }
    }

    /// <summary>مرز کامل چرخ بیضوی؛ مبدأ = کانون ۱.</summary>
    private static List<Point2> BuildEllipticalBoundary(
        double a, double e, int z, double m, double ha, double hf, double alpha, double tol)
    {
        int baseSeg = Math.Clamp((int)Math.Ceiling(4.0 * a / Math.Max(1e-4, 3.0 * tol)), 240, 3000);
        var curve = new PitchCurve(a, e, baseSeg);
        double pitchArc = curve.Perimeter / z;
        // توجه: r(θ) = a(1−e²)/(1+e·cosθ) از «کانون» اندازه‌گیری می‌شود؛
        // نقاط خروجی بدون شیفت اضافه، مستقیماً کانون‌مرکزند (بور روی کانون = مبدأ).

        double halfT = Math.PI * m / 4.0;
        double tipHalf = Math.Max(halfT - ha * Math.Tan(alpha), 0.2 * halfT);
        double rootHalf = halfT + hf * Math.Tan(alpha);

        var pts = new List<Point2>(z * 24 + 128);

        // نقطهٔ مرز: کمان s + آفست نرمال v (نسبت به کانون)
        void Emit(double s, double v)
        {
            var (x, y, _, _, nx, ny, _) = curve.At(s);
            pts.Add(new Point2(x + v * nx, y + v * ny));
        }

        for (int k = 0; k < z; k++)
        {
            double s0 = k * pitchArc;

            // 1) ریشه از دندانهٔ قبلی تا ابتدای پهلوی − این دندانه
            int rootSeg = Math.Clamp((int)Math.Ceiling((pitchArc - 2 * rootHalf) / Math.Max(1e-4, 2.5 * tol)), 2, 48);
            double rFrom = s0 - pitchArc + rootHalf;
            for (int i = 0; i < rootSeg; i++)
                Emit(rFrom + (s0 - rootHalf - rFrom) * i / rootSeg, -hf);

            // 2) پهلو − : صعود از ریشه به نوک (u منفی، v از −hf به +ha)
            int flankSeg = Math.Clamp((int)Math.Ceiling((ha + hf) / Math.Max(1e-4, 1.2 * tol)), 6, 32);
            for (int i = 0; i <= flankSeg; i++)
            {
                double v = -hf + (ha + hf) * i / flankSeg;
                double u = -(halfT - v * Math.Tan(alpha));
                Emit(s0 + u, v);
            }

            // 3) نوک: کمان در v = +ha از −tipHalf تا +tipHalf
            int tipSeg = Math.Clamp((int)Math.Ceiling(2.0 * tipHalf / Math.Max(1e-4, 1.2 * tol)), 2, 24);
            for (int i = 0; i <= tipSeg; i++)
                Emit(s0 - tipHalf + 2.0 * tipHalf * i / tipSeg, ha);

            // 4) پهلو + : نزول از نوک به ریشه
            for (int i = flankSeg; i >= 0; i--)
            {
                double v = -hf + (ha + hf) * i / flankSeg;
                double u = +(halfT - v * Math.Tan(alpha));
                Emit(s0 + u, v);
            }
        }

        return pts;
    }

    // ---------- منشورسازی ----------

    private static TriMesh ExtrudePrism(List<Point2> boundary, double width, double boreR, double tol)
    {
        var mesh = new TriMesh();
        int n = boundary.Count;
        var bottom = new List<int>(n);
        var top = new List<int>(n);

        foreach (var p in boundary)
        {
            bottom.Add(mesh.AddVertex(new Vector3(p.X, p.Y, 0)));
            top.Add(mesh.AddVertex(new Vector3(p.X, p.Y, width)));
        }

        // دیوارهٔ جانبی
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddTriangle(bottom[i], bottom[j], top[j]);
            mesh.AddTriangle(bottom[i], top[j], top[i]);
        }

        // حفرهٔ بور
        var holes = new List<List<Point2>>();
        var holeTopIdx = new List<List<int>>();
        var holeBottomIdx = new List<List<int>>();

        if (boreR > 0)
        {
            int seg = CircleProfile.SegmentsForCircle(boreR, tol);
            var hTop = new List<int>(seg);
            var hBot = new List<int>(seg);
            var hPts = new List<Point2>(seg);
            for (int i = 0; i < seg; i++)
            {
                double ang = 2.0 * Math.PI * i / seg;
                double x = boreR * Math.Cos(ang), y = boreR * Math.Sin(ang);
                hTop.Add(mesh.AddVertex(new Vector3(x, y, width)));
                hBot.Add(mesh.AddVertex(new Vector3(x, y, 0)));
                hPts.Add(new Point2(x, y));
            }
            var cwPts = new List<Point2>(hPts);
            cwPts.Reverse();
            var cwTop = new List<int>(hTop);
            cwTop.Reverse();
            var cwBot = new List<int>(hBot);
            cwBot.Reverse();
            holes.Add(cwPts);
            holeTopIdx.Add(cwTop);
            holeBottomIdx.Add(cwBot);

            // دیوارهٔ بور
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                mesh.AddTriangle(hTop[i], hTop[j], hBot[j]);
                mesh.AddTriangle(hTop[i], hBot[j], hBot[i]);
            }
        }

        var tris = PolygonTriangulator.Triangulate(boundary, holes.Count > 0 ? holes : null);
        foreach (var (x0, y0, w0) in tris)
        {
            int xt = x0 < n ? top[x0] : MapHole(x0, n, holeTopIdx);
            int yt = y0 < n ? top[y0] : MapHole(y0, n, holeTopIdx);
            int wt = w0 < n ? top[w0] : MapHole(w0, n, holeTopIdx);
            mesh.AddTriangle(xt, yt, wt); // نرمال +Z

            int xb = x0 < n ? bottom[x0] : MapHole(x0, n, holeBottomIdx);
            int yb = y0 < n ? bottom[y0] : MapHole(y0, n, holeBottomIdx);
            int wb = w0 < n ? bottom[w0] : MapHole(w0, n, holeBottomIdx);
            mesh.AddTriangle(xb, wb, yb); // نرمال −Z
        }

        return mesh;
    }

    private static int MapHole(int virtualIndex, int outerCount, List<List<int>> holeLoops)
    {
        int k = virtualIndex - outerCount;
        foreach (var h in holeLoops)
        {
            if (k < h.Count)
                return h[k];
            k -= h.Count;
        }
        throw new InvalidOperationException("شاخص حفره خارج از محدوده است.");
    }
}
