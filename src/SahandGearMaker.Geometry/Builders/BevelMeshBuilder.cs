using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش چرخ‌دندهٔ مخروطی — مستقیم (BevelSpur) و مارپیچ/قوسی (BevelSpiral).
///
/// روش مهندسی (رویکرد مرجع Tredgold):
///   - پروفیل دندانه روی «مخروط پشتی» با چرخ‌دندهٔ معادل استوانه‌ای تولید می‌شود:
///     شعاع گام معادل Re = مخروط‌فاصله R، تعداد دندانهٔ مجازی zve = z/sin δ، ماژول همان m (پاشنه)؛
///   - نگاشت نقطهٔ معادل (θ, r_eq) به مقطع عمود بر محور در پاشنه:
///     r_cyl = rp + (r_eq − Re)·cos δ  (تقریب استاندارد پروژکشن مخروط پشتی)؛
///   - دندانه‌های مستقیم: همهٔ عناصر دندانه به رأس مخروط همگرا می‌شوند؛ بنابراین مقطع در
///     مخروط‌فاصله s همان مقطع پاشنه با مقیاس s/R حول محور است (تناسب دقیق خطوط شعاعی)؛
///   - دندانهٔ مارپیچ: پیچش برش‌ها روی مخروطِ بازشده با مارپیچ لگاریتمی انجام می‌شود:
///     ψ(s) = tan(β)·ln(s/R) که زاویهٔ مارپیچ ثابت β نسبت به خطوط مولد مخروط می‌دهد
///     (روش مرجع «زاویهٔ مارپیچ ثابت»؛ سازگار با تعریف β در شعاع میانگین)؛
///   - بدنه: صفحهٔ پاشنه و پنجه تخت، بور استوانه‌ای در طول محور، توپی پشت پاشنه؛
///     همهٔ وجوه از استخر رأس مشترک ساخته می‌شوند تا مش آب‌بند بماند.
///
/// محدودیت‌های صادقانه: پروفیل معادل تقریب Tredgold است (خطای مرتبهٔ 1/R)؛
/// شیار خار فقط روی مخروطیِ مستقیم (بدون پیچش) اعمال می‌شود؛ سوراخ سبک‌سازی پشتیبانی نمی‌شود.
/// </summary>
public static class BevelMeshBuilder
{
    public static GearBuildResult Build(
        GearFamily family,
        ToothParameters teeth,
        BodyFeatures body,
        GeometrySettings quality,
        FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(specific);
        quality ??= new GeometrySettings();

        var diags = GearValidator.Validate(family, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترها نامعتبرند؛ هندسهٔ مخروطی ساخته نشد.", errors);

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);

        // ---------- هندسهٔ مخروط گام ----------
        double m = teeth.Module;
        int z = teeth.ToothCount;
        double shaft = specific.BevelShaftAngle > 1e-9 ? specific.BevelShaftAngle : Math.PI / 2.0;
        double delta = specific.BevelPitchConeAngle > 1e-9
            ? specific.BevelPitchConeAngle
            : shaft / 2.0; // جفت هم‌اندازهٔ فرضی: δ = Σ/2

        if (delta <= UnitConverter.DegToRad(5) || delta >= UnitConverter.DegToRad(85))
        {
            errors.Add(new GearDiagnostic("SGM-E210", Severity.Error,
                $"زاویهٔ نیم‌مخروط گام باید بین ۵ تا ۸۵ درجه باشد (فعلی: {UnitConverter.RadToDeg(delta):F1}°).", "BevelPitchConeAngle"));
            throw new GeometryBuildException("زاویهٔ نیم‌مخروط گام خارج از دامنهٔ مهندسی است.", errors);
        }

        double spiral = family == GearFamily.BevelSpiral ? Math.Abs(specific.BevelSpiralAngle) : 0.0;
        if (spiral >= UnitConverter.DegToRad(60))
        {
            errors.Add(new GearDiagnostic("SGM-E211", Severity.Error,
                "زاویهٔ مارپیچ مخروطی باید کوچک‌تر از ۶۰ درجه باشد.", "BevelSpiralAngle"));
            throw new GeometryBuildException("زاویهٔ مارپیچ مخروطی نامعتبر است.", errors);
        }

        double rp = m * z / 2.0;                 // شعاع گام در پاشنه (مقطای بزرگ)
        double Re = rp / Math.Sin(delta);        // مخروط‌فاصله = شعاع گام معادل Tredgold
        double b = Math.Min(teeth.FaceWidth, 0.45 * Re);
        if (teeth.FaceWidth > Re / 3.0)
            diags.Add(new GearDiagnostic("SGM-W212", Severity.Warning,
                $"پهنای دندانهٔ {teeth.FaceWidth:F1} میلی‌متر از حد متداول مخروطی (R/3 ≈ {Re / 3.0:F1}) بیشتر است؛ پروفیل معادل Tredgold در پنجه خطا می‌گیرد.", "FaceWidth"));
        if (teeth.FaceWidth > 0.45 * Re)
            diags.Add(new GearDiagnostic("SGM-I213", Severity.Info,
                $"پهنای دندانه به {b:F1} میلی‌متر محدود شد (۰٫۴۵×مخروط‌فاصله).", "FaceWidth"));

        double ha = teeth.AddendumCoefficient * m;
        double hf = teeth.DedendumCoefficient * m;

        // ---------- ارقام معادل Tredgold ----------
        double alphaT = GearValidator.TransversePressureAngle(teeth.PressureAngle, 0);
        double eqTip = Re + ha;
        double eqRoot = Math.Max(Re - hf, 1e-6);
        double fillet = Math.Max(0, Math.Min(teeth.RootFilletCoefficient * m, (Re - eqRoot) / 2.0));
        double thickness = Math.PI * m / 2.0 + 2.0 * teeth.ProfileShiftCoefficient * m * Math.Tan(alphaT) - teeth.BacklashAllowance;

        var equiv = new GearGeometryFigures
        {
            Family = GearFamily.ExternalSpur,
            TransverseModule = 2.0 * Re * Math.Sin(delta) / z,
            TransversePressureAngle = alphaT,
            PitchRadius = Re,
            BaseRadius = Re * Math.Cos(alphaT),
            TipRadius = eqTip,
            RootRadius = eqRoot,
            AngularPitch = 2.0 * Math.PI / z,
            ToothThicknessAtPitch = thickness,
            HelixAngleUsed = 0,
            RootFilletRadius = fillet,
            ToothCount = z,
        };

        // ---------- مرز پاشنه: پروفیل معادل + نگاشت مخروط پشتی ----------
        var profile = new InvoluteToothProfile(equiv, fillet, body.TipChamferMm);
        var eqBoundary = CylindricalGearMeshBuilder.CleanBoundary(profile.BoundaryPoints(tol));
        if (eqBoundary.Count < 8)
            throw new GeometryBuildException("پروفیل معادل مخروطی نقاط کافی ندارد.", diags);

        double cosD = Math.Cos(delta);
        var heelPts = new List<PolarPoint>(eqBoundary.Count);
        foreach (var p in eqBoundary)
        {
            double rCyl = rp + (p.Radius - Re) * cosD;
            if (rCyl <= 1e-6)
                throw new GeometryBuildException("نگاشت مخروط پشتی شعاع نامعتبر می‌سازد؛ فشار/تعداد دندانه را بررسی کنید.", diags);
            heelPts.Add(new PolarPoint(p.Theta, rCyl));
        }

        double rootHeel = rp - hf * cosD;
        double rootToe = rootHeel * (Re - b) / Re;
        double boreR = body.BoreDiameter / 2.0;
        if (boreR > 0 && boreR >= rootToe - 0.5 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E214", Severity.Error,
                $"قطر بور ({body.BoreDiameter:F1} mm) در پنجه جا نمی‌شود (حداکثر ≈ {2.0 * Math.Max(0, rootToe - 0.5 * m):F1} mm).", "BoreDiameter"));
            throw new GeometryBuildException("بور از ریشهٔ مخروط در پنجه بزرگ‌تر است.", errors);
        }

        double hubR = body.HubDiameter / 2.0;
        if (hubR > 0 && hubR >= rootHeel - 0.2 * m)
        {
            diags.Add(new GearDiagnostic("SGM-W215", Severity.Warning,
                "قطر توپی به ریشهٔ پاشنه نزدیک/بزرگ است؛ به قطر ریشه محدود شد.", "HubDiameter"));
            hubR = Math.Max(boreR + 1.0, rootHeel - 0.2 * m);
        }
        double hubLen = body.HubLength > 0 ? body.HubLength : 0.0;

        bool keyway = body.KeywayWidth > 0 && body.KeywayDepth > 0 && spiral < 1e-9;
        if (body.KeywayWidth > 0 && spiral >= 1e-9)
            diags.Add(new GearDiagnostic("SGM-I216", Severity.Info,
                "شیار خار روی مخروطی مارپیچ اعمال نمی‌شود (چرخش برش‌ها جهت شیار را مخدوش می‌کند).", "KeywayWidth"));

        // ---------- برش‌ها ----------
        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);
        int sliceCount = Math.Clamp(quality.AxialSliceCount, 8, 96);
        if (spiral > 1e-9)
            sliceCount = Math.Max(sliceCount, 24);

        for (int i = 0; i <= sliceCount; i++)
        {
            double s = Re - b * i / sliceCount;               // مخروط‌فاصلهٔ برش
            double twist = spiral > 1e-9 ? Math.Tan(spiral) * Math.Log(s / Re) : 0.0;
            pool.AddSlice(twist, s * cosD);
            var loop = pool.ProfileLoop(i);
            foreach (var p in heelPts)
            {
                double scale = s / Re;
                int idx = mesh.AddVertex(new Vector3(
                    p.Radius * scale * Math.Cos(p.Theta + twist),
                    p.Radius * scale * Math.Sin(p.Theta + twist),
                    s * cosD));
                loop.Add(idx);
            }
        }

        // دیوارهٔ دندانه + مخروط ریشه بین برش‌ها (مرز شامل قوس ریشه است)
        for (int i = 0; i < sliceCount; i++)
            VertexPool.WallCCWOuter(mesh, pool.ProfileLoop(i), pool.ProfileLoop(i + 1));

        double zHeel = Re * cosD;
        double zToe = (Re - b) * cosD;

        // ---------- بور ----------
        List<int>?[] boreLoops = new List<int>?[sliceCount + 1];
        if (boreR > 0)
        {
            for (int i = 0; i <= sliceCount; i++)
                boreLoops[i] = pool.RingWithKeyway(i, boreR, keyway ? body.KeywayWidth : 0, keyway ? body.KeywayDepth : 0, tol,
                    keyway ? body.KeywayEndRadius : 0);
            for (int i = 0; i < sliceCount; i++)
                VertexPool.WallCCW(mesh, boreLoops[i]!, boreLoops[i + 1]!);
        }

        // ---------- صفحهٔ پنجه (جلو) ----------
        VertexPool.AnnulusCap(mesh, pool.ProfileLoop(sliceCount), boreLoops[sliceCount], null, negativeZ: true);

        // ---------- صفحهٔ پاشنه (عقب) + توپی ----------
        if (hubR > boreR && hubLen > 0)
        {
            var heelRing = pool.RingAtZ(zHeel, "hub-outer", hubR, tol);
            // درپوش پاشنه: فقط توپی↔پروفیل؛ بور از داخل توپی بدون تقاطع T عبور می‌کند
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(0), null, heelRing, negativeZ: false);

            var backRing = pool.RingAtZ(zHeel + hubLen, "hub-back", hubR, tol);
            VertexPool.WallCCWOuter(mesh, heelRing, backRing);
            // کلاهک پشت توپی: حلقه از بور تا توپی با نرمال +Z
            if (boreR > 0)
            {
                // دیوارهٔ بور داخل طول توپی: از برش پاشنه تا بالای توپی (بدون حلقهٔ میانی)
                var heelBore = boreLoops[0]!;
                var backBore = new List<int>(heelBore.Count);
                foreach (int idx in heelBore)
                {
                    var v = mesh.VertexAt(idx);
                    backBore.Add(mesh.AddVertex(new Vector3(v.X, v.Y, zHeel + hubLen)));
                }
                VertexPool.WallCCW(mesh, heelBore, backBore);
                VertexPool.AnnulusZ(mesh, backBore, backRing);
            }
            else
            {
                var c = pool.CenterAtZ(zHeel + hubLen);
                VertexPool.DiskZ(mesh, backRing, c);
            }
        }
        else
        {
            VertexPool.AnnulusCap(mesh, pool.ProfileLoop(0), boreLoops[0], null, negativeZ: false);
        }

        if (mesh.TriangleCount == 0)
            throw new GeometryBuildException("مش مخروطی خالی ساخته شد.", diags);

        var fig = new GearGeometryFigures
        {
            Family = family,
            TransverseModule = m,
            TransversePressureAngle = alphaT,
            PitchRadius = rp,
            BaseRadius = rp * Math.Cos(alphaT),
            TipRadius = rp + ha * cosD,
            RootRadius = rootHeel,
            AngularPitch = 2.0 * Math.PI / z,
            ToothThicknessAtPitch = thickness,
            HelixAngleUsed = spiral,
            RootFilletRadius = fillet,
            EstimatedVolumeMm3 = Math.PI * (Math.Pow(rp + ha * cosD, 2) - Math.Pow(rootToe, 2)) * b * cosD * 0.55,
            ToothCount = z,
        };

        diags.Add(new GearDiagnostic("SGM-I220", Severity.Info,
            $"مخروطی: δ={UnitConverter.RadToDeg(delta):F1}°، مخروط‌فاصله R={Re:F2} mm، پهنای مؤثر b={b:F2} mm؛ پروفیل معادل Tredgold (تقریب استاندارد دندانهٔ مخروطی).", "BevelPitchConeAngle"));
        if (spiral > 1e-9)
            diags.Add(new GearDiagnostic("SGM-I221", Severity.Info,
                $"مارپیچ ثابت β={UnitConverter.RadToDeg(spiral):F1}° روی مخروط بازشده (مارپیچ لگاریتمی).", "BevelSpiralAngle"));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = fig,
            Diagnostics = diags,
            AxialSlicesUsed = sliceCount + 1,
            EffectiveChordToleranceMm = tol,
        };
    }

    /// <summary>صفحهٔ حلقه‌ای بین دو حلقهٔ هم‌مرکز (کلاهک پشت توپی).</summary>
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
