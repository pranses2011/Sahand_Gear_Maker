using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش جفت کرم — کرم استوانه‌ای ZA و چرخ کرم با گلوگاه.
///
/// کرم (ZA، پروفیل محوری ذوزنقه‌ای):
///   - قطر گام d1 = q·m (q پارامتر قطر، پیش‌فرض ۱۰) یا از ورودی صریح؛
///   - پیشروی L = π·m·z1 (z1 = تعداد رشته‌ها = ToothCount)؛
///   - سطح دندانه: در مقطع محوری، پهلوها خط مستقیم با زاویهٔ فشار αx‌اند؛
///     ضخامت محوری دندانه در شعاع r: w(r) = π·m/2 + 2·(r_p1 − r)·tan(αx)؛
///   - مقطع عمود بر محور در z: مرز هر رشته روی بازهٔ زاویه‌ای ±π·w(r)/L حول
///     c_k(z) = k·2π/z1 + 2π·z/L قرار می‌گیرد (پیچش هلیکس با گام ثابت L) —
///     این تصویرِ دقیق سطح پیچی ZA در صفحهٔ مقطع است.
///
/// چرخ کرم (با گلوگاه گلوبوئیدی):
///   - چرخ‌دندهٔ هلیکی با زاویهٔ مارپیچ β2 = زاویهٔ پیشروی کرم γ = atan(z1·m/d1)، هم‌دست با کرم؛
///   - قطر نوک در هر برش با پوششِ استوانهٔ ریشهٔ کرم بریده می‌شود:
///     r_tip2(z_w) = min(r_p2 + h_a2, a − √(max(0, r_t² − z_w²)))، r_t = r_f1 + پاککردن؛
///     این همان روش ساخت واقعی چرخ کرم با ابزار کرم‌نما است (throat/globoid)؛
///   - ماژول نرمال چرخ = ماژول محوری کرم (محورهای متقاطع ۹۰ درجه).
///
/// محدودیت‌های صادقانه: شیار خار روی کرم/چرخ کرم اعمال نمی‌شود (پیچش برش‌ها جهت شیار را مخدوش می‌کند)؛
/// پوشش نوک «ابزار کرم‌نما» تقریب هندسی استاندارد است، نه حل تماس کامل.
/// </summary>
public static class WormMeshBuilder
{
    private const int MinStarts = 1;
    private const int MaxStarts = 12;

    public static GearBuildResult BuildWorm(
        ToothParameters teeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        var diags = GearValidator.Validate(GearFamily.Worm, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای کرم نامعتبرند.", errors);

        double m = teeth.Module;
        int z1 = Math.Clamp(teeth.ToothCount, MinStarts, MaxStarts);
        if (teeth.ToothCount is < MinStarts or > MaxStarts)
        {
            errors.Add(new GearDiagnostic("SGM-E240", Severity.Error,
                $"تعداد رشته‌های کرم باید بین {MinStarts} تا {MaxStarts} باشد (ورودی: {teeth.ToothCount}).", "ToothCount"));
            throw new GeometryBuildException("تعداد رشته‌های کرم نامعتبر است.", errors);
        }

        double alphaX = teeth.PressureAngle;
        double d1 = specific.WormPitchDiameterMm > 1e-9 ? specific.WormPitchDiameterMm : 10.0 * m;
        double q = d1 / m;
        if (q < 6.0 || q > 30.0)
        {
            errors.Add(new GearDiagnostic("SGM-E241", Severity.Error,
                $"ضریب قطر کرم q = d1/m باید بین ۶ تا ۳۰ باشد (فعلی {q:F2}).", "WormPitchDiameterMm"));
            throw new GeometryBuildException("ضریب قطر کرم خارج از دامنهٔ مهندسی است.", errors);
        }

        double rp1 = d1 / 2.0;
        double ha1 = teeth.AddendumCoefficient * m;
        double hf1 = teeth.DedendumCoefficient * m;
        double ra1 = rp1 + ha1;
        double rf1 = rp1 - hf1;
        double lead = Math.PI * m * z1;
        double leadAngle = Math.Atan(z1 * m / d1);

        // ضخامت محوری در ریشه باید از گام محوری کوچک‌تر بماند
        double pitchAxial = Math.PI * m;
        double wRoot = pitchAxial / 2.0 + 2.0 * hf1 * Math.Tan(alphaX);
        if (wRoot > 0.95 * pitchAxial)
        {
            errors.Add(new GearDiagnostic("SGM-E242", Severity.Error,
                "ضخامت دندانه در ریشهٔ کرم فضای دندانهٔ مجاور را می‌بلعد؛ زاویهٔ فشار/عمق دندانه را کم کنید.", "PressureAngle"));
            throw new GeometryBuildException("هندسهٔ کرم نامعتبر: تداخل فضای دندانه.", errors);
        }

        double length = Math.Clamp(teeth.FaceWidth, 3.0 * pitchAxial, 60.0 * m);
        if (teeth.FaceWidth < 3.0 * pitchAxial)
            diags.Add(new GearDiagnostic("SGM-W243", Severity.Warning,
                $"طول کرم به {length:F1} mm محدود شد (حداقل ۳ گام محوری) تا گام کامل تشکیل شود.", "FaceWidth"));

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);
        // بور کرم: شکل‌های D/شش‌گوش/… به‌صورت منشور مستقیم (موازی محور) ساخته می‌شوند — نسخهٔ ۰٫۵
        var boreShape = BoreSection.EffectiveShape(body);
        if (boreShape != BoreShapeType.Round && body.KeywayWidth > 0)
            diags.Add(new GearDiagnostic("SGM-I244", Severity.Info,
                "شیار خار کلاسیک روی کرم با شکل بور غیر دایره ترکیب نشد؛ مقطع انتخابی اعمال می‌شود.", "KeywayWidth"));

        bool rightHand = specific.WormIsRightHanded;

        // ---------- برش‌ها ----------
        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);
        int sliceCount = Math.Clamp(
            Math.Max(quality.AxialSliceCount, (int)Math.Ceiling(length / Math.Max(0.05, 0.3 * m))),
            16, 200);

        for (int i = 0; i <= sliceCount; i++)
        {
            double z = length * i / sliceCount;
            double twist = (rightHand ? 1.0 : -1.0) * 2.0 * Math.PI * z / lead;
            pool.AddSlice(twist, z);
            var loop = pool.ProfileLoop(i);
            AppendWormSection(mesh, loop, twist, z, z1, m, rp1, rf1, ra1, lead, alphaX, rightHand, tol);
        }

        for (int i = 0; i < sliceCount; i++)
            VertexPool.WallCCWOuter(mesh, pool.ProfileLoop(i), pool.ProfileLoop(i + 1));

        // ---------- بور و درپوش‌ها ----------
        double boreR = body.BoreDiameter / 2.0;
        double boreExtra = boreShape == BoreShapeType.Spline ? Math.Max(0.05, body.BoreSplineDepthMm) : 0.0;
        if (boreR > 0 && boreR + boreExtra >= rf1 - 0.5 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E245", Severity.Error,
                $"قطر بور کرم ({body.BoreDiameter:F1} mm) باید کوچک‌تر از قطر ریشه باشد (حداکثر ≈ {2.0 * Math.Max(0, rf1 - 0.5 * m - boreExtra):F1} mm).", "BoreDiameter"));
            throw new GeometryBuildException("بور از ریشهٔ کرم بزرگ‌تر است.", errors);
        }

        if (boreR > 0)
        {
            if (boreShape == BoreShapeType.Round)
            {
                for (int i = 0; i <= sliceCount; i++)
                    pool.Ring(i, "bore", boreR, tol);
                for (int i = 0; i < sliceCount; i++)
                    VertexPool.WallCCW(mesh, pool.Ring(i, "bore", boreR, tol), pool.Ring(i + 1, "bore", boreR, tol));
            }
            else
            {
                var section = BoreSection.Build(body, boreR, tol);
                var lo = pool.ShapeLoopAtZ(0, "bore", section);
                var hi = pool.ShapeLoopAtZ(length, "bore", section);
                VertexPool.WallCCW(mesh, lo, hi);
            }
        }

        if (boreShape == BoreShapeType.Round)
        {
            var wormFrontBore = boreR > 0 ? pool.Ring(0, "bore", boreR, tol) : null;
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(0), wormFrontBore, null, negativeZ: true);
            var wormBackBore = boreR > 0 ? pool.Ring(sliceCount, "bore", boreR, tol) : null;
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(sliceCount), wormBackBore, null, negativeZ: false);
        }
        else
        {
            var section = BoreSection.Build(body, boreR, tol);
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(0), boreR > 0 ? pool.ShapeLoopAtZ(0, "bore", section) : null, null, negativeZ: true);
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(sliceCount), boreR > 0 ? pool.ShapeLoopAtZ(length, "bore", section) : null, null, negativeZ: false);
        }

        if (boreShape != BoreShapeType.Round)
            diags.Add(new GearDiagnostic("SGM-I246", Severity.Info,
                $"بور {BoreShapeTitle(boreShape)} به‌صورت منشور مستقیم موازی محور روی کرم ساخته شد.", "BoreShape"));

        var fig = new GearGeometryFigures
        {
            Family = GearFamily.Worm,
            TransverseModule = m,
            TransversePressureAngle = alphaX,
            PitchRadius = rp1,
            BaseRadius = rp1,
            TipRadius = ra1,
            RootRadius = rf1,
            AngularPitch = 2.0 * Math.PI / z1,
            ToothThicknessAtPitch = pitchAxial / 2.0,
            HelixAngleUsed = Math.PI / 2.0 - leadAngle, // زاویهٔ مارپیچ دندانهٔ کرم
            RootFilletRadius = Math.Min(0.2 * m, hf1 / 3.0),
            EstimatedVolumeMm3 = Math.PI * (ra1 * ra1 - boreR * boreR) * length * 0.72,
            ToothCount = z1,
        };

        diags.Add(new GearDiagnostic("SGM-I250", Severity.Info,
            $"کرم ZA: z1={z1}، q={q:F1}، d1={d1:F2} mm، پیشروی L={lead:F2} mm، زاویهٔ پیشروی γ={UnitConverter.RadToDeg(leadAngle):F2}°، دست: {(rightHand ? "راست" : "چپ")}.", "WormPitchDiameterMm"));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags,
            AxialSlicesUsed = sliceCount + 1,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>مقطع عمود بر محور کرم در z: مرز CCW با رشته‌های ذوزنقه‌ای پیچ‌خورده.</summary>
    private static void AppendWormSection(
        TriMesh mesh, List<int> loop, double twist, double z,
        int z1, double m, double rp1, double rf1, double ra1, double lead, double alphaX, bool rightHand, double tol)
    {
        double kFactor = Math.PI / lead; // تبدیل طول محوری به زاویه

        double HalfWidthAt(double r)
            => kFactor * (Math.PI * m / 2.0 + 2.0 * (rp1 - r) * Math.Tan(alphaX));

        double pitchAngular = 2.0 * Math.PI / z1;
        int flankSteps = Math.Clamp((int)Math.Ceiling((ra1 - rf1) / Math.Max(1e-4, 0.15 * m)), 5, 24);
        int tipSeg = Math.Clamp((int)Math.Ceiling(pitchAngular * ra1 / Math.Max(1e-4, 2.0 * tol)), 2, 24);

        for (int k = 0; k < z1; k++)
        {
            double c = k * pitchAngular + twist;

            // 1) قوس ریشه از رشتهٔ قبلی تا این رشته
            double rootHalf = HalfWidthAt(rf1);
            double prevEnd = k == 0 ? c - pitchAngular + rootHalf : c - pitchAngular + rootHalf;
            int rootSeg = Math.Clamp((int)Math.Ceiling((c - rootHalf - prevEnd) * rf1 / Math.Max(1e-4, 2.0 * tol)), 2, 40);
            for (int i = 1; i < rootSeg; i++)
            {
                double a = prevEnd + (c - rootHalf - prevEnd) * i / rootSeg;
                loop.Add(mesh.AddVertex(new Vector3(rf1 * Math.Cos(a), rf1 * Math.Sin(a), z)));
            }

            // 2) پهلو − : از ریشه به نوک (زاویه با شعاع تغییر می‌کند)
            for (int i = 0; i <= flankSteps; i++)
            {
                double r = rf1 + (ra1 - rf1) * i / flankSteps;
                double a = c - HalfWidthAt(r);
                loop.Add(mesh.AddVertex(new Vector3(r * Math.Cos(a), r * Math.Sin(a), z)));
            }

            // 3) قوس نوک (نقطهٔ اول با پایان پهلو − یکسان است؛ از i=1)
            double tipHalf = Math.Max(1e-4, HalfWidthAt(ra1));
            for (int i = 1; i < tipSeg; i++)
            {
                double a = (c - tipHalf) + (2.0 * tipHalf) * i / tipSeg;
                loop.Add(mesh.AddVertex(new Vector3(ra1 * Math.Cos(a), ra1 * Math.Sin(a), z)));
            }

            // 4) پهلو + : از نوک به ریشه
            for (int i = flankSteps; i >= 0; i--)
            {
                double r = rf1 + (ra1 - rf1) * i / flankSteps;
                double a = c + HalfWidthAt(r);
                loop.Add(mesh.AddVertex(new Vector3(r * Math.Cos(a), r * Math.Sin(a), z)));
            }
        }
    }

    private static string BoreShapeTitle(BoreShapeType s) => s switch
    {
        BoreShapeType.DShape => "D",
        BoreShapeType.DoubleD => "دو-D",
        BoreShapeType.Hexagon => "شش‌گوش",
        BoreShapeType.Square => "چهارگوش",
        BoreShapeType.Keyed => "خاردار",
        BoreShapeType.Spline => "هزارخار",
        _ => "دایره‌ای",
    };

    // ---------- چرخ کرم ----------

    public static GearBuildResult BuildWheel(
        ToothParameters teeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        // پهنای صفر = عرض خودکار چرخ کرم
        var t = GearGeometryEngine.CloneTeeth(teeth);
        int z1w = Math.Clamp(specific.WormStartsCount <= 0 ? 1 : specific.WormStartsCount, MinStarts, MaxStarts);
        double m = t.Module;
        double d1 = specific.WormPitchDiameterMm > 1e-9 ? specific.WormPitchDiameterMm : 10.0 * m;
        double rp1 = d1 / 2.0;
        double rf1 = rp1 - 1.25 * m;
        double leadAngle = Math.Atan(z1w * m / d1);

        if (t.FaceWidth <= 1e-9)
            t.FaceWidth = Math.Clamp(0.75 * d1, 4.0 * m, 30.0 * m);

        var diags = GearValidator.Validate(GearFamily.WormWheel, t).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای چرخ کرم نامعتبرند.", errors);

        if (t.ToothCount < 20)
        {
            errors.Add(new GearDiagnostic("SGM-E260", Severity.Error,
                "تعداد دندانهٔ چرخ کرم باید حداقل ۲۰ باشد (رواج مهندسی برای جفت‌شدن نرم).", "ToothCount"));
            throw new GeometryBuildException("تعداد دندانهٔ چرخ کرم کم است.", errors);
        }

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);

        // زاویهٔ مارپیچ چرخ = زاویهٔ پیشروی کرم؛ هم‌دست با کرم (محورهای ۹۰ درجه)
        double beta2 = specific.WormIsRightHanded ? leadAngle : -leadAngle;
        t.HelixAngle = beta2;

        var fig = GearFiguresCalculator.Compute(GearFamily.ExternalHelical, t);
        double rp2 = fig.PitchRadius;
        double ra2 = fig.TipRadius;
        double rf2 = fig.RootRadius;
        double ha2 = ra2 - rp2;

        double centerDist = rp1 + rp2;
        double clearance = specific.WormThroatClearanceMm > 0 ? specific.WormThroatClearanceMm : 0.25 * m;
        double rt = rf1 + clearance;

        double b2 = t.FaceWidth;

        // ---------- پروفیل پایه (تا بیشینهٔ نوک) ----------
        var baseProfile = new InvoluteToothProfile(fig, fig.RootFilletRadius, body.TipChamferMm);
        var baseBoundary = CylindricalGearMeshBuilder.CleanBoundary(baseProfile.BoundaryPoints(tol));
        if (baseBoundary.Count < 8)
            throw new GeometryBuildException("پروفیل چرخ کرم ساخته نشد.", diags);

        if (body.KeywayWidth > 0 && BoreSection.EffectiveShape(body) == BoreShapeType.Round)
            diags.Add(new GearDiagnostic("SGM-I261", Severity.Info,
                "شیار خار کلاسیک روی چرخ کرم توصیه نمی‌شود (مارپیچ دندانه)؛ برای مهار دوران، بور D یا هزارخار را انتخاب کنید.", "KeywayWidth"));

        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);
        int sliceCount = Math.Clamp(
            Math.Max(quality.AxialSliceCount, (int)Math.Ceiling(b2 / Math.Max(0.05, 0.25 * m))),
            16, 200);

        for (int i = 0; i <= sliceCount; i++)
        {
            double zw = b2 * i / sliceCount - b2 / 2.0; // مرکز گلوگاه در میانهٔ پهنا
            double twist = zw * Math.Tan(beta2) / rp2;
            pool.AddSlice(twist, b2 * i / sliceCount);

            double rTipSlice = Math.Min(ra2, centerDist - Math.Sqrt(Math.Max(0.0, rt * rt - zw * zw)));
            var clipped = ClipBoundaryToRadius(baseBoundary, rTipSlice, tol);
            var loop = pool.ProfileLoop(i);
            foreach (var p in clipped)
            {
                double a = p.Theta + twist;
                loop.Add(mesh.AddVertex(new Vector3(p.Radius * Math.Cos(a), p.Radius * Math.Sin(a), pool.Z(i))));
            }
        }

        for (int i = 0; i < sliceCount; i++)
            VertexPool.WallCCWOuter(mesh, pool.ProfileLoop(i), pool.ProfileLoop(i + 1));

        // ---------- بور ----------
        double boreR = body.BoreDiameter / 2.0;
        var wheelBoreShape = BoreSection.EffectiveShape(body);
        double wheelBoreExtra = wheelBoreShape == BoreShapeType.Spline ? Math.Max(0.05, body.BoreSplineDepthMm) : 0.0;
        if (boreR > 0 && boreR + wheelBoreExtra >= rf2 - 0.5 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E262", Severity.Error,
                $"قطر بور چرخ کرم باید کوچک‌تر از قطر ریشه باشد (حداکثر ≈ {2.0 * Math.Max(0, rf2 - 0.5 * m - wheelBoreExtra):F1} mm).", "BoreDiameter"));
            throw new GeometryBuildException("بور از ریشهٔ چرخ کرم بزرگ‌تر است.", errors);
        }

        if (boreR > 0)
        {
            if (wheelBoreShape == BoreShapeType.Round)
            {
                for (int i = 0; i <= sliceCount; i++)
                    pool.Ring(i, "bore", boreR, tol);
                for (int i = 0; i < sliceCount; i++)
                    VertexPool.WallCCW(mesh, pool.Ring(i, "bore", boreR, tol), pool.Ring(i + 1, "bore", boreR, tol));
            }
            else
            {
                var section = BoreSection.Build(body, boreR, tol);
                VertexPool.WallCCW(mesh, pool.ShapeLoopAtZ(0, "bore", section), pool.ShapeLoopAtZ(b2, "bore", section));
            }
        }

        // ---------- درپوش‌ها ----------
        double hubR = body.HubDiameter / 2.0;
        if (hubR > 0 && hubR >= rf2 - 0.4 * m)
        {
            diags.Add(new GearDiagnostic("SGM-W263", Severity.Warning,
                "قطر توپی به ریشهٔ چرخ کرم نزدیک است؛ محدود شد.", "HubDiameter"));
            hubR = Math.Max(boreR + 1.0, rf2 - 0.4 * m);
        }
        double hubLen = body.HubLength > 0 ? body.HubLength : 0.0;
        double zBack = b2;

        // درپوش جلو (نزدیک گلوگاه) و پشت
        List<int>? frontBore;
        List<int>? backBore0;
        if (boreR > 0 && wheelBoreShape != BoreShapeType.Round)
        {
            var section = BoreSection.Build(body, boreR, tol);
            frontBore = pool.ShapeLoopAtZ(0, "bore", section);
            backBore0 = pool.ShapeLoopAtZ(b2, "bore", section);
        }
        else
        {
            frontBore = boreR > 0 ? pool.Ring(0, "bore", boreR, tol) : null;
            backBore0 = boreR > 0 ? pool.Ring(sliceCount, "bore", boreR, tol) : null;
        }
        VertexPool.AnnulusCap(mesh, pool.ProfileLoop(0), frontBore, null, negativeZ: true);

        if (hubR > boreR && hubLen > 0)
        {
            var backHubRing = pool.RingAtZ(zBack, "hub-outer", hubR, tol);
            // درپوش پشت: فقط توپی↔پروفیل؛ بور از داخل توپی بدون تقاطع T عبور می‌کند
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(sliceCount), null, backHubRing, negativeZ: false);

            var farRing = pool.RingAtZ(zBack + hubLen, "hub-back", hubR, tol);
            VertexPool.WallCCWOuter(mesh, backHubRing, farRing);
            if (boreR > 0)
            {
                // حلقهٔ بور در انتهای بدنه (دایره یا شکل دلخواه) + کپی هم‌شکل در انتهای توپی
                List<int> lastBore = wheelBoreShape == BoreShapeType.Round
                    ? pool.Ring(sliceCount, "bore", boreR, tol)
                    : backBore0!;
                var farBore = new List<int>(lastBore.Count);
                foreach (int idx in lastBore)
                {
                    var v = mesh.VertexAt(idx);
                    farBore.Add(mesh.AddVertex(new Vector3(v.X, v.Y, zBack + hubLen)));
                }
                VertexPool.WallCCW(mesh, lastBore, farBore);
                VertexPool.AnnulusZ(mesh, farBore, farRing);
            }
            else
            {
                var center = pool.CenterAtZ(zBack + hubLen);
                VertexPool.DiskZ(mesh, farRing, center);
            }
        }
        else
        {
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(sliceCount), backBore0, null, negativeZ: false);
        }

        if (boreR > 0 && wheelBoreShape != BoreShapeType.Round)
            diags.Add(new GearDiagnostic("SGM-I265", Severity.Info,
                $"بور {BoreShapeTitle(wheelBoreShape)} روی چرخ کرم ساخته شد (منشور مستقیم).", "BoreShape"));

        var figOut = new GearGeometryFigures
        {
            Family = GearFamily.WormWheel,
            TransverseModule = fig.TransverseModule,
            TransversePressureAngle = fig.TransversePressureAngle,
            PitchRadius = rp2,
            BaseRadius = fig.BaseRadius,
            TipRadius = ra2,
            RootRadius = rf2,
            AngularPitch = 2.0 * Math.PI / t.ToothCount,
            ToothThicknessAtPitch = fig.ToothThicknessAtPitch,
            HelixAngleUsed = beta2,
            RootFilletRadius = fig.RootFilletRadius,
            EstimatedVolumeMm3 = Math.PI * (ra2 * ra2 - boreR * boreR) * b2 * 0.55,
            ToothCount = t.ToothCount,
        };

        diags.Add(new GearDiagnostic("SGM-I264", Severity.Info,
            $"چرخ کرم گلوگاه‌دار: z2={t.ToothCount}، β2={UnitConverter.RadToDeg(beta2):F2}° (برابر زاویهٔ پیشروی کرمِ مرجع z1={z1w}, d1={d1:F1} mm)؛ فاصلهٔ محور مرجع a={centerDist:F2} mm؛ برش نوک با پوشش استوانهٔ ریشهٔ کرم (روش ابزار کرم‌نما).", "WormStartsCount"));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = figOut,
            Diagnostics = diags,
            AxialSlicesUsed = sliceCount + 1,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>بریدن مرز در شعاع داده‌شده: نقاط بیرونی حذف و کمان روی دایرهٔ برش درج می‌شود.</summary>
    private static List<PolarPoint> ClipBoundaryToRadius(List<PolarPoint> boundary, double radius, double tol)
    {
        int n = boundary.Count;
        var result = new List<PolarPoint>(n);

        for (int i = 0; i < n; i++)
        {
            var cur = boundary[i];
            var next = boundary[(i + 1) % n];
            bool curOut = cur.Radius > radius;
            bool nextOut = next.Radius > radius;

            if (!curOut)
                result.Add(cur);

            if (curOut == nextOut)
                continue;

            double dr = next.Radius - cur.Radius;
            double t = Math.Abs(dr) < 1e-12 ? 0.5 : Math.Clamp((radius - cur.Radius) / dr, 0.0, 1.0);
            double thetaCross = cur.Theta + (next.Theta - cur.Theta) * t;
            result.Add(new PolarPoint(thetaCross, radius));

            if (!curOut)
            {
                // خروج از داخل به بیرون: کمان روی دایرهٔ برش تا تقاطع ورود بعدی
                double thetaIn = double.NaN;
                for (int j = i + 1; j <= i + n; j++)
                {
                    var a2 = boundary[j % n];
                    var b2 = boundary[(j + 1) % n];
                    if (a2.Radius > radius && b2.Radius <= radius)
                    {
                        double dr2 = b2.Radius - a2.Radius;
                        double t2 = Math.Abs(dr2) < 1e-12 ? 0.5 : Math.Clamp((radius - a2.Radius) / dr2, 0.0, 1.0);
                        thetaIn = a2.Theta + (b2.Theta - a2.Theta) * t2;
                        break;
                    }
                }

                if (!double.IsNaN(thetaIn) && thetaIn > thetaCross + 1e-7)
                {
                    int seg = Math.Clamp((int)Math.Ceiling((thetaIn - thetaCross) * radius / Math.Max(1e-4, 2.0 * tol)), 1, 64);
                    for (int s = 1; s < seg; s++)
                        result.Add(new PolarPoint(thetaCross + (thetaIn - thetaCross) * s / seg, radius));
                }
            }
        }

        return result;
    }

    /// <summary>صفحهٔ حلقه‌ای بین دو حلقهٔ هم‌مرکز.</summary>
    private static void AddAnnulusFace(TriMesh mesh, List<int> outer, List<int> inner, bool positiveZ)
    {
        int n = Math.Min(outer.Count, inner.Count);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (positiveZ)
            {
                mesh.AddTriangle(inner[i], outer[i], outer[j]);
                mesh.AddTriangle(inner[i], outer[j], inner[j]);
            }
            else
            {
                mesh.AddTriangle(inner[i], inner[j], outer[j]);
                mesh.AddTriangle(inner[i], outer[j], outer[i]);
            }
        }
    }
}
