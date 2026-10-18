using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Geometry;

/// <summary>
/// سازندهٔ مش فِلکسی‌پلاین (فنجان دندانه‌دار درایو هارمونیک).
///
/// ساختار (حالت آزاد/قابل ساخت — تغییرشکل توسط موج‌ساز یک «حالت تنش» است، نه هندسهٔ قطعه):
///   - ناحیهٔ دندانه در دهانهٔ فنجان: دندانهٔ اینولوت خارجی مستقیم (بدون پیچش) با
///     عمق کامل روی «نوار دیواره»؛
///   - انتقال به دیوارهٔ نازک: برشِ «دایرهٔ ریشه» با قدم‌زدن ادغامی — کف دندانه‌ها و
///     ادامهٔ ریشه به‌صورت یک سطح پیوسته ساخته می‌شود؛
///   - دیوارهٔ نازک استوانه‌ای تا کف فنجان؛
///   - کف دیسکی با بور مرکزی و حلقهٔ پیچ (سوراخ‌های سبک‌سازی به‌عنوان سوراخ اتصال).
///
/// توپولوژی آب‌بند: همهٔ سطوح از استخر رأس مشترک ساخته می‌شوند؛ درپوش دهانه = نوار
/// (داخل دیواره → مرز دندانه) و کف = دیسک با حفره‌ها.
///
/// محدودیت صادقانه: حالت تغییرشکل‌یافته (بیضی‌شدن دهانه توسط موج‌ساز) مدل نمی‌شود؛
/// در مونتاج، موج‌ساز به‌صورت کام بیضوی جداگانه نمایش داده می‌شود.
/// </summary>
public static class FlexSplineMeshBuilder
{
    public static GearBuildResult Build(
        ToothParameters teeth, BodyFeatures body, GeometrySettings quality, FamilyParameters specific)
    {
        ArgumentNullException.ThrowIfNull(teeth);
        ArgumentNullException.ThrowIfNull(body);
        quality ??= new GeometrySettings();
        specific ??= new FamilyParameters();

        var diags = GearValidator.Validate(GearFamily.HarmonicDrive, teeth).ToList();
        var errors = diags.Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
            throw new GeometryBuildException("پارامترهای فِلکسی‌پلاین نامعتبرند.", errors);

        double m = teeth.Module;
        int z = teeth.ToothCount;
        if (z < 60)
        {
            errors.Add(new GearDiagnostic("SGM-E320", Severity.Error,
                "تعداد دندانهٔ فِلکسی‌پلاین حداقل ۶۰ باید باشد (درایو هارمونیک دندانهٔ ریز دارد).", "ToothCount"));
            throw new GeometryBuildException("تعداد دندانهٔ فِلکسی‌پلاین کم است.", errors);
        }

        if (Math.Abs(teeth.HelixAngle) > 1e-9)
            diags.Add(new GearDiagnostic("SGM-W321", Severity.Warning,
                "فِلکسی‌پلاین معمولاً دندانهٔ مستقیم دارد؛ زاویهٔ مارپیچ ورودی صفر در نظر گرفته شد.", "HelixAngle"));

        var t = GearGeometryEngine.CloneTeeth(teeth);
        t.HelixAngle = 0;
        t.ProfileShiftCoefficient = 0; // فِلکسی‌پلاین استاندارد بدون اصلاح ساخته می‌شود

        double tol = Math.Clamp(quality.ProfileToleranceMm, 0.002, 1.0);
        var fig = GearFiguresCalculator.Compute(GearFamily.ExternalSpur, t);

        double rp = fig.PitchRadius;
        double rf = fig.RootRadius;
        double toothWidth = Math.Clamp(t.FaceWidth, 0.6 * m, 0.35 * rp); // پهنای نوار دندانه
        double wall = specific.HarmonicCupWallThicknessMm > 0
            ? specific.HarmonicCupWallThicknessMm
            : Math.Max(0.015 * 2.0 * rp, 0.4 * m);
        double wallLen = Math.Clamp(1.2 * rp, 2.0 * toothWidth, 120.0); // طول دیوارهٔ نازک
        double floorT = Math.Max(wall, 0.8 * m);                        // ضخامت کف

        double innerWall = rf - wall; // شعاع داخلی دیوارهٔ نازک
        if (innerWall <= 0.6 * rp)
        {
            errors.Add(new GearDiagnostic("SGM-E322", Severity.Error,
                "ضخامت دیوارهٔ فنجان نسبت به قطر بزرگ است؛ مقدار کوچکتری وارد کنید.", "HarmonicCupWallThicknessMm"));
            throw new GeometryBuildException("دیوارهٔ فنجان بیش از حد ضخیم است.", errors);
        }

        double boreR = body.BoreDiameter / 2.0;
        if (boreR > 0 && boreR >= innerWall - 0.8 * m)
        {
            errors.Add(new GearDiagnostic("SGM-E323", Severity.Error,
                $"بور کف فنجان باید کوچک‌تر از {2.0 * Math.Max(0, innerWall - 0.8 * m):F1} mm باشد.", "BoreDiameter"));
            throw new GeometryBuildException("بور با دیوارهٔ فنجان تداخل دارد.", errors);
        }

        var mesh = new TriMesh();
        var pool = new VertexPool(mesh);

        // ---------- پروفیل دندانه (حالت مستقیم) ----------
        var profile = new InvoluteToothProfile(fig, fig.RootFilletRadius, body.TipChamferMm);
        var boundary = CylindricalGearMeshBuilder.CleanBoundary(profile.BoundaryPoints(tol));
        if (boundary.Count < 8)
            throw new GeometryBuildException("پروفیل فِلکسی‌پلاین ساخته نشد.", diags);

        int toothSlices = 2; // مستقیم: دو برش کافی است (کف و لبهٔ دهانه)
        double zTeeth0 = wallLen;          // پایین ناحیهٔ دندانه (اتصال به دیواره)
        double zTeeth1 = wallLen + toothWidth; // لبهٔ دهانه

        for (int i = 0; i <= toothSlices; i++)
        {
            double zs = zTeeth0 + toothWidth * i / toothSlices;
            pool.AddSlice(0, zs);
            var loop = pool.ProfileLoop(i);
            foreach (var p in boundary)
                loop.Add(mesh.AddVertex(new Vector3(p.Radius * Math.Cos(p.Theta), p.Radius * Math.Sin(p.Theta), zs)));
        }
        // دیوارهٔ دندانه + ریشه بین برش‌ها
        for (int i = 0; i < toothSlices; i++)
            VertexPool.WallCCWOuter(mesh, pool.ProfileLoop(i), pool.ProfileLoop(i + 1));

        // ---------- برش انتقال: دایرهٔ ریشه ساده ----------
        int transSlice = pool.AddSlice(0, zTeeth0);
        int rootSeg = CircleProfile.SegmentsForCircle(rf, tol);
        var rootLoop = new List<int>(rootSeg);
        for (int i = 0; i < rootSeg; i++)
        {
            double ang = 2.0 * Math.PI * i / rootSeg;
            rootLoop.Add(mesh.AddVertex(new Vector3(rf * Math.Cos(ang), rf * Math.Sin(ang), zTeeth0)));
        }
        pool.SetSpokeLoop(transSlice, rootLoop);

        // قدم‌زدن ادغامی بین مرز دندانه (برش ۰) و دایرهٔ ریشه: کف دندانه‌ها ساخته می‌شود
        VertexPool.WallCCWOuter(mesh, pool.ProfileLoop(0), rootLoop);

        // ---------- دیوارهٔ نازک: از zTeeth0 به پایین ----------
        int wallSlices = Math.Clamp((int)Math.Ceiling(wallLen / Math.Max(0.05, 0.5 * m)), 4, 60);
        var outerRings = new List<List<int>>(wallSlices + 1);
        var innerRings = new List<List<int>>(wallSlices + 1);

        for (int i = 0; i <= wallSlices; i++)
        {
            double zw = zTeeth0 - wallLen * i / wallSlices;
            int segO = i == 0 ? rootSeg : CircleProfile.SegmentsForCircle(rf, tol);
            var outer = i == 0 ? rootLoop : RingAt(mesh, rf, zw, segO);
            var inner = RingAt(mesh, innerWall, zw, CircleProfile.SegmentsForCircle(innerWall, tol));
            outerRings.Add(outer);
            innerRings.Add(inner);
            if (i > 0)
            {
                VertexPool.WallCCWOuter(mesh, outerRings[i - 1], outer);
                VertexPool.WallCCW(mesh, innerRings[i - 1], inner);
            }
        }

        double zFloorTop = zTeeth0 - wallLen;      // بالای کف
        double zFloorBottom = zFloorTop - floorT;  // پایین کف

        // ---------- درپوش دهانه: نوار (innerWall → مرز دندانه) در z = zTeeth1 ----------
        {
            var loop = pool.ProfileLoop(toothSlices);
            var pts = new List<Point2>();
            var idxMap = new List<int>();
            foreach (int idx in loop)
            {
                var v = mesh.VertexAt(idx);
                if (pts.Count > 0)
                {
                    var pv = pts[^1];
                    if (Math.Abs(pv.X - v.X) < 1e-9 && Math.Abs(pv.Y - v.Y) < 1e-9)
                        continue;
                }
                pts.Add(new Point2(v.X, v.Y));
                idxMap.Add(idx);
            }

            int segI = CircleProfile.SegmentsForCircle(innerWall, tol);
            var rimInner = new List<int>(segI);
            var hole = new List<Point2>(segI);
            for (int i = 0; i < segI; i++)
            {
                double ang = 2.0 * Math.PI * i / segI;
                rimInner.Add(mesh.AddVertex(new Vector3(innerWall * Math.Cos(ang), innerWall * Math.Sin(ang), zTeeth1)));
                hole.Add(new Point2(innerWall * Math.Cos(ang), innerWall * Math.Sin(ang)));
            }
            hole.Reverse();
            var holeIdxRev = new List<int>(rimInner);
            holeIdxRev.Reverse();

            var tris = PolygonTriangulator.Triangulate(pts, new List<List<Point2>> { hole });
            foreach (var (ta, tb, tc) in tris)
            {
                int ia = ta < idxMap.Count ? idxMap[ta] : holeIdxRev[ta - idxMap.Count];
                int ib = tb < idxMap.Count ? idxMap[tb] : holeIdxRev[tb - idxMap.Count];
                int ic = tc < idxMap.Count ? idxMap[tc] : holeIdxRev[tc - idxMap.Count];
                mesh.AddTriangle(ia, ib, ic); // نرمال +Z (رو به دهانه)
            }

            // دیوارهٔ داخلی ناحیهٔ دندانه: از دهانه به پایین تا دیوارهٔ نازک
            // (innerRings[0] همان حلقهٔ z=zTeeth0 است — رأس مشترک با دیوارهٔ نازک)
            VertexPool.WallCCW(mesh, rimInner, innerRings[0]);
        }

        // ---------- کف فنجان ----------
        // ساختار صحیح: دیوارهٔ بیرونی (rf) تا زیر کف ادامه می‌یابد؛
        // کف = دیسک از بور تا innerWall؛ لبهٔ کف = استوانهٔ innerWall؛
        // حلقهٔ پایانی = نوار rf↔innerWall در پایین.
        {
            double zFloorTopLocal = zTeeth0 - wallLen;
            double zFloorBottomLocal = zFloorTopLocal - floorT;

            // ادامهٔ دیوارهٔ بیرونی از z=0 تا زیر کف
            var outerCont = RingAt(mesh, rf, zFloorBottomLocal, CircleProfile.SegmentsForCircle(rf, tol));
            VertexPool.WallCCWOuter(mesh, outerRings[^1], outerCont);

            // لبهٔ کف وجود ندارد: دیسک کف به دیوارهٔ فنجان جوش است (اتحاد حجمی)؛
            // مرز بالای کف = حلقهٔ پایین دیوارهٔ حفره (innerRings[^1])
            var floorEdgeTop = innerRings[^1];
            var floorEdgeBottom = RingAt(mesh, innerWall, zFloorBottomLocal, CircleProfile.SegmentsForCircle(innerWall, tol));

            // بور و سوراخ‌های اتصال
            List<int>? boreTopRing = null, boreBottomRing = null;
            var boreHolePts = new List<Point2>();
            if (boreR > 0)
            {
                int segB = CircleProfile.SegmentsForCircle(boreR, tol);
                boreTopRing = new List<int>(segB);
                boreBottomRing = new List<int>(segB);
                for (int i = 0; i < segB; i++)
                {
                    double ang = 2.0 * Math.PI * i / segB;
                    double x = boreR * Math.Cos(ang), y = boreR * Math.Sin(ang);
                    boreTopRing.Add(mesh.AddVertex(new Vector3(x, y, zFloorTopLocal)));
                    boreBottomRing.Add(mesh.AddVertex(new Vector3(x, y, zFloorBottomLocal)));
                    boreHolePts.Add(new Point2(x, y));
                }
                VertexPool.WallCCW(mesh, boreTopRing, boreBottomRing);
            }

            var boltTopLoops = new List<List<int>>();
            var boltBotLoops = new List<List<int>>();
            var boltHolePts = new List<List<Point2>>();
            int boltCount = body.LighteningHoleCount;
            if (boltCount > 0)
            {
                double boltR = body.LighteningHoleDiameter / 2.0;
                double boltCircle = body.LighteningHoleCircleRadius > 0
                    ? body.LighteningHoleCircleRadius
                    : (innerWall + Math.Max(boreR, 0.0)) / 2.0;

                for (int hI = 0; hI < boltCount; hI++)
                {
                    double ang = 2.0 * Math.PI * hI / boltCount;
                    double cx = boltCircle * Math.Cos(ang), cy = boltCircle * Math.Sin(ang);
                    if (boltR <= 0 || boltCircle + boltR >= innerWall - 0.3 * m ||
                        (boreR > 0 && boltCircle - boltR <= boreR + 0.3 * m))
                        continue;

                    int segH = Math.Clamp(CircleProfile.SegmentsForCircle(boltR, tol) / 2, 8, 64);
                    var topL = new List<int>(segH);
                    var botL = new List<int>(segH);
                    var hp = new List<Point2>(segH);
                    for (int i = 0; i < segH; i++)
                    {
                        double aa = 2.0 * Math.PI * i / segH;
                        double x = cx + boltR * Math.Cos(aa), y = cy + boltR * Math.Sin(aa);
                        topL.Add(mesh.AddVertex(new Vector3(x, y, zFloorTopLocal)));
                        botL.Add(mesh.AddVertex(new Vector3(x, y, zFloorBottomLocal)));
                        hp.Add(new Point2(x, y));
                    }
                    VertexPool.WallCCW(mesh, topL, botL);
                    boltTopLoops.Add(topL);
                    boltBotLoops.Add(botL);
                    boltHolePts.Add(hp);
                }
            }

            // حفره‌ها (CW): بور سپس پیچ‌ها
            var holes2D = new List<List<Point2>>();
            var topLoops = new List<List<int>>();
            var botLoops = new List<List<int>>();
            if (boreTopRing != null)
            {
                var cwPts = new List<Point2>(boreHolePts);
                cwPts.Reverse();
                holes2D.Add(cwPts);
                var tR = new List<int>(boreTopRing);
                tR.Reverse();
                topLoops.Add(tR);
                var bR = new List<int>(boreBottomRing);
                bR.Reverse();
                botLoops.Add(bR);
            }
            for (int i = 0; i < boltHolePts.Count; i++)
            {
                var cwPts = new List<Point2>(boltHolePts[i]);
                cwPts.Reverse();
                holes2D.Add(cwPts);
                var tR = new List<int>(boltTopLoops[i]);
                tR.Reverse();
                topLoops.Add(tR);
                var bR = new List<int>(boltBotLoops[i]);
                bR.Reverse();
                botLoops.Add(bR);
            }

            // روی کف (رو به حفرهٔ فنجان): از innerWall تا بور
            var topOuterPts = new List<Point2>(floorEdgeTop.Count);
            var topOuterIdx = new List<int>(floorEdgeTop.Count);
            foreach (int idx in floorEdgeTop)
            {
                var v = mesh.VertexAt(idx);
                topOuterPts.Add(new Point2(v.X, v.Y));
                topOuterIdx.Add(idx);
            }
            var trisTop = PolygonTriangulator.Triangulate(topOuterPts, holes2D.Count > 0 ? holes2D : null);
            int oc = topOuterPts.Count;
            foreach (var (ta, tb, tc) in trisTop)
            {
                mesh.AddTriangle(
                    ta < oc ? topOuterIdx[ta] : MapHole(ta, oc, topLoops),
                    tb < oc ? topOuterIdx[tb] : MapHole(tb, oc, topLoops),
                    tc < oc ? topOuterIdx[tc] : MapHole(tc, oc, topLoops));
            }

            // زیر کف: همان نقاط با رأس‌های پایین
            var botOuterPts = new List<Point2>(floorEdgeBottom.Count);
            var botOuterIdx = new List<int>(floorEdgeBottom.Count);
            foreach (int idx in floorEdgeBottom)
            {
                var v = mesh.VertexAt(idx);
                botOuterPts.Add(new Point2(v.X, v.Y));
                botOuterIdx.Add(idx);
            }
            var trisBot = PolygonTriangulator.Triangulate(botOuterPts, holes2D.Count > 0 ? holes2D : null);
            int ocB = botOuterPts.Count;
            foreach (var (ta, tb, tc) in trisBot)
            {
                mesh.AddTriangle(
                    ta < ocB ? botOuterIdx[ta] : MapHole(ta, ocB, botLoops),
                    tc < ocB ? botOuterIdx[tc] : MapHole(tc, ocB, botLoops),
                    tb < ocB ? botOuterIdx[tb] : MapHole(tb, ocB, botLoops));
            }

            // حلقهٔ پایانی فنجان: نوار rf ↔ innerWall در پایین (نرمال −Z؛ قدم‌زدن ادغامی برای حلقه‌های ناهم‌تعداد)
            VertexPool.AnnulusNZ(mesh, floorEdgeBottom, outerCont);
        }

        var figOut = new GearGeometryFigures
        {
            Family = GearFamily.HarmonicDrive,
            TransverseModule = m,
            TransversePressureAngle = fig.TransversePressureAngle,
            PitchRadius = rp,
            BaseRadius = fig.BaseRadius,
            TipRadius = fig.TipRadius,
            RootRadius = rf,
            AngularPitch = 2.0 * Math.PI / z,
            ToothThicknessAtPitch = fig.ToothThicknessAtPitch,
            HelixAngleUsed = 0,
            RootFilletRadius = fig.RootFilletRadius,
            EstimatedVolumeMm3 = 2.0 * Math.PI * rp * wall * wallLen + Math.PI * rp * rp * floorT * 0.4,
            ToothCount = z,
        };

        diags.Add(new GearDiagnostic("SGM-I324", Severity.Info,
            $"فِلکسی‌پلاین: z={z}، دیواره={wall:F2} mm، نوار دندانه={toothWidth:F1} mm، دیسک کف={floorT:F2} mm؛ حالت آزاد (قابل ساخت) — تغییرشکل موج‌ساز هندسهٔ قطعه نیست.", "HarmonicCupWallThicknessMm"));
        diags.Add(new GearDiagnostic("SGM-I325", Severity.Info,
            "اسپلاین دایره‌ای جفت (دو دندانهٔ بیشتر) با خانوادهٔ «چرخ‌دندهٔ داخلی» ساخته می‌شود؛ موج‌ساز بیضوی در مجموعهٔ مونتاژ هارمونیک ارائه می‌گردد.", "HarmonicDrive"));

        return new GearBuildResult
        {
            Mesh = mesh,
            Figures = figOut,
            Diagnostics = diags,
            AxialSlicesUsed = toothSlices + 1 + wallSlices,
            EffectiveChordToleranceMm = tol,
        };
    }

    private static List<int> RingAt(TriMesh mesh, double radius, double z, int segments)
    {
        var ids = new List<int>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = 2.0 * Math.PI * i / segments;
            ids.Add(mesh.AddVertex(new Vector3(radius * Math.Cos(a), radius * Math.Sin(a), z)));
        }
        return ids;
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
