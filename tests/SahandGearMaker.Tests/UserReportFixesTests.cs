using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;
using SahandGearMaker.Assembly;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های بستهٔ رفع ۱۲ ایراد گزارش‌شدهٔ کاربر (نسخهٔ ۰٫۴٫۰):
/// زائدهٔ بین دندانه‌ها، فاز سیاره‌ای، پرش گرید، نمای بالا دقیق، لبه‌های ویژه،
/// خروجی تجمیعی و کیفیت‌های Ultra/Precision.
/// </summary>
public class UserReportFixesTests
{
    // ═══════════ ۳) زائدهٔ بین دندانه‌ها — فیلهٔ ریشه ═══════════

    [Theory]
    [InlineData(20, 2.0)]
    [InlineData(12, 5.0)]
    [InlineData(33, 1.5)]
    [InlineData(50, 1.0)]
    public void RootFillet_NoRadialSpikesBetweenTeeth(int z, double m)
    {
        var teeth = new ToothParameters { Module = m, ToothCount = z, FaceWidth = 10 };
        var fig = GearFiguresCalculator.Compute(GearFamily.ExternalSpur, teeth);
        var profile = new InvoluteToothProfile(fig, fig.RootFilletRadius);
        var pts = profile.BoundaryPoints(0.02);
        Assert.True(pts.Count > z * 8, "پروفیل باید نقاط کافی داشته باشد.");

        // آستانهٔ پرش مجاز: نصف دهانهٔ پهلو + ۰٫۳ — پخش‌های مجاز نمونه‌برداری اینولوت از آن
        // کوچک‌ترند؛ باگ فیلهٔ قدیمی پرشی به اندازهٔ کل ارتفاع دندانه+ می‌داد.
        double rFlankStart = Math.Max(fig.RootRadius, fig.BaseRadius);
        double allowedJump = Math.Abs(fig.TipRadius - rFlankStart) / 2.0 + 0.3;
        double maxJump = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Assert.True(pts[i + 1].Theta > pts[i].Theta - 1e-12, "زاویهٔ مرز باید اکیداً صعودی باشد (CCW).");
            double jump = Math.Abs(pts[i + 1].Radius - pts[i].Radius);
            maxJump = Math.Max(maxJump, jump);
        }
        Assert.True(maxJump <= allowedJump,
            $"پرش شعاعی بین نقاط متوالی مرز ({maxJump:0.000}mm) از سقف مجاز ({allowedJump:0.000}mm) بیشتر است — زائده در پروفیل.");
        Assert.True(pts.Max(p => p.Radius) <= fig.TipRadius + 1e-6, "مرز نباید از دایرهٔ سر بیرون بزند.");
    }

    [Fact]
    public void RootFillet_InternalGear_NoSpikes()
    {
        var teeth = new ToothParameters { Module = 2, ToothCount = 40, FaceWidth = 10 };
        var fig = GearFiguresCalculator.Compute(GearFamily.InternalSpur, teeth);
        var profile = new InvoluteToothProfile(fig, fig.RootFilletRadius);
        var pts = profile.BoundaryPoints(0.02);
        double rTipEff = Math.Max(fig.TipRadius, fig.BaseRadius);
        double allowedJump = Math.Abs(fig.RootRadius - rTipEff) / 2.0 + 0.3;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Assert.True(pts[i + 1].Theta > pts[i].Theta - 1e-12, "زاویهٔ مرز داخلی باید صعودی باشد.");
            double jump = Math.Abs(pts[i + 1].Radius - pts[i].Radius);
            Assert.True(jump <= allowedJump,
                $"پرش شعاعی {jump:0.000}mm در چرخ‌دندهٔ داخلی — زائده یا ترتیب معکوس پهلو.");
        }
    }

    [Fact]
    public void InternalChamfer_ShortensTipArc_WithoutSpikes()
    {
        var teeth = new ToothParameters { Module = 2, ToothCount = 40, FaceWidth = 10 };
        var fig = GearFiguresCalculator.Compute(GearFamily.InternalSpur, teeth);
        var profile = new InvoluteToothProfile(fig, fig.RootFilletRadius, tipChamferMm: 0.3);
        var pts = profile.BoundaryPoints(0.02);
        Assert.True(pts.Count > 40 * 8);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Assert.True(pts[i + 1].Theta > pts[i].Theta - 1e-12);
            Assert.True(Math.Abs(pts[i + 1].Radius - pts[i].Radius) < 1.2,
                $"پرش {Math.Abs(pts[i + 1].Radius - pts[i].Radius):0.000}mm با پخ داخلی.");
        }
    }

    // ═══════════ ۱) چینش سیاره‌ای — فاز دندانه و درگیری ═══════════

    /// <summary>شعاع مرز ستاره‌ای در زاویهٔ دلخواه — درون‌یابی روی نقاط مرز.</summary>
    private static double BoundaryRadiusAt(List<PolarPoint> b, double theta)
    {
        double span = b[^1].Theta - b[0].Theta;
        double local = theta;
        while (local < b[0].Theta) local += span;
        while (local > b[^1].Theta) local -= span;
        int lo = 0, hi = b.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (b[mid].Theta <= local) lo = mid; else hi = mid;
        }
        if (b[hi].Theta - b[lo].Theta < 1e-12) return b[lo].Radius;
        double k = (local - b[lo].Theta) / (b[hi].Theta - b[lo].Theta);
        return b[lo].Radius + (b[hi].Radius - b[lo].Radius) * k;
    }

    private static List<PolarPoint> ProfileOf(GearFamily family, int z, double m, double chamfer = 0)
    {
        var teeth = new ToothParameters { Module = m, ToothCount = z, FaceWidth = 10 };
        var fig = GearFiguresCalculator.Compute(family, teeth);
        return new InvoluteToothProfile(fig, fig.RootFilletRadius, chamfer).BoundaryPoints(0.004);
    }

    /// <summary>
    /// بررسی عدم هم‌پوشانی مادهٔ سیاره با خورشیدی و حلقه در چیدمان واقعی ویزارد
    /// (با پخ نوک ۰٫۱۵m مثل خود ویزارد) — تلرانس ۰٫۰۶mm برای خطای گسسته‌سازی.
    /// </summary>
    private static void AssertPlanetaryMeshes(int sunTeeth, int planetTeeth, int n, double m, bool expectExactRing)
    {
        var design = PlanetaryGearsetCalculator.Design(sunTeeth, planetTeeth, n, m, PlanetaryFixedMember.Ring);
        Assert.True(design.GeometryFeasible);
        Assert.Equal(n, design.PlanetPhases.Length);
        Assert.Equal(expectExactRing, design.RingMeshExact);

        double chamfer = 0.15 * m; // مطابق ویزارد — پخ استاندارد گوشهٔ نوک
        var sunB = ProfileOf(GearFamily.ExternalSpur, sunTeeth, m, chamfer);
        var ringB = ProfileOf(GearFamily.InternalSpur, design.RingTeeth, m, chamfer);
        var planetB = ProfileOf(GearFamily.ExternalSpur, planetTeeth, m, chamfer);

        double a = design.PlanetOrbitRadiusMm;
        const double tol = 0.06; // خطای نمونه‌برداری/درون‌یابی + پاکみ نزدیک-تداخل زوج‌های مرزی

        for (int i = 0; i < n; i++)
        {
            double phi = design.PlanetAngles[i];
            double psi = design.PlanetPhases[i];
            double cx = a * Math.Cos(phi), cy = a * Math.Sin(phi);
            double cos = Math.Cos(psi), sin = Math.Sin(psi);

            foreach (var p in planetB)
            {
                double lx = p.Radius * Math.Cos(p.Theta), ly = p.Radius * Math.Sin(p.Theta);
                double wx = cx + lx * cos - ly * sin;
                double wy = cy + lx * sin + ly * cos;
                double wr = Math.Sqrt(wx * wx + wy * wy);
                double wt = Math.Atan2(wy, wx);

                // درگیری با خورشیدی: نقطهٔ سیاره نباید داخل مادهٔ خورشیدی باشد
                double sunGap = wr - BoundaryRadiusAt(sunB, wt);
                Assert.True(sunGap >= -tol,
                    $"هم‌پوشانی خورشیدی-سیاره {i + 1}: نفوذ {(-sunGap):0.000}mm در زاویهٔ {wt * 180 / Math.PI:0.#}°");

                if (expectExactRing)
                {
                    // درگیری با حلقه: نقطهٔ سیاره باید داخل فضای حلقه بماند (مرز داخلی حلقه)
                    double ringGap = BoundaryRadiusAt(ringB, wt - design.RingRotationRad) - wr;
                    Assert.True(ringGap >= -tol,
                        $"هم‌پوشانی حلقه-سیاره {i + 1}: نفوذ {(-ringGap):0.000}mm در زاویهٔ {wt * 180 / Math.PI:0.#}°");
                }
            }
        }
    }

    [Fact]
    public void Planetary_WizardDefault_AllTeethMeshWithoutOverlap()
        => AssertPlanetaryMeshes(18, 24, 3, 2.0, expectExactRing: true);

    [Fact]
    public void Planetary_OddPlanetTeeth_RingRotationHalfPitch()
        => AssertPlanetaryMeshes(21, 15, 3, 2.0, expectExactRing: true);

    [Fact]
    public void Planetary_FivePlanets_ClassicCondition_MeshesExactly()
        => AssertPlanetaryMeshes(20, 15, 5, 1.5, expectExactRing: true);

    [Fact]
    public void Planetary_InfeasibleCombo_SunStillMeshes_AndWarns()
    {
        // (21,15,n=4): هیچ چرخش واحدی حلقه را با هر ۴ سیاره هم‌فاز نمی‌کند (4 ∤ 2·zₚ) —
        // خورشیدی همچنان بی‌هم‌پوشانی است، حلقه هشدار می‌دهد و روی فاز سیارهٔ ۱ تنظیم می‌شود.
        AssertPlanetaryMeshes(21, 15, 4, 2.0, expectExactRing: false);
        var d = PlanetaryGearsetCalculator.Design(21, 15, 4, 2.0, PlanetaryFixedMember.Ring);
        Assert.Contains(d.Diagnostics, x => x.Severity == Severity.Warning);
    }

    [Fact]
    public void Planetary_PhasesAreNotAllZero()
    {
        var d = PlanetaryGearsetCalculator.Design(18, 24, 3, 2.0, PlanetaryFixedMember.Ring);
        Assert.True(d.PlanetPhases.Any(p => Math.Abs(p) > 1e-6),
            "فاز سیاره‌ها نباید همگی صفر باشند (صفر = دندانه روی دندانه).");
    }

    // ═══════════ ۹) نمای بالا دقیقاً از بالا ═══════════

    [Fact]
    public void TopView_IsExactlyNadir_NoTilt()
    {
        var cam = new SceneCamera();
        cam.SetView(0, Math.PI / 2, 200, Vector3.Zero, animate: false);
        var f = cam.ComputeFrame(400, 300);
        Assert.True(Math.Abs(f.Forward.X) < 1e-9 && Math.Abs(f.Forward.Y) < 1e-9 && Math.Abs(f.Forward.Z + 1) < 1e-9,
            $"نمای بالا باید دقیقاً رو به پایین باشد؛ forward=({f.Forward.X:0.####}, {f.Forward.Y:0.####}, {f.Forward.Z:0.####})");
        Assert.True(Math.Abs(f.Right.Length - 1) < 1e-9, "بردار راست باید واحد باشد (نه NaN).");
        Assert.True(Math.Abs(f.Up.Length - 1) < 1e-9, "بردار بالا باید واحد باشد (نه NaN).");
        Assert.True(Math.Abs(Vector3.Dot(f.Right, f.Up)) < 1e-9, "پایه باید متعامد باشد.");
        Assert.True(Math.Abs(Vector3.Dot(f.Right, Vector3.UnitZ)) < 1e-9, "راست باید در صفحهٔ افقی بماند.");
    }

    [Fact]
    public void BottomView_IsExactlyZenith()
    {
        var cam = new SceneCamera();
        cam.SetView(0.7, -Math.PI / 2, 150, Vector3.Zero, animate: false);
        var f = cam.ComputeFrame(200, 200);
        Assert.True(Math.Abs(f.Forward.Z - 1) < 1e-9, "نمای پایین باید دقیقاً رو به بالا باشد.");
        Assert.True(Math.Abs(f.Right.Length - 1) < 1e-9 && Math.Abs(f.Up.Length - 1) < 1e-9);
    }

    // ═══════════ ۵) پایداری گرید ═══════════

    [Fact]
    public void Grid_DoesNotShift_WhenContentBoundsChange()
    {
        // گام/گسترهٔ گرید از نمای دوربین می‌آید نه از ابعاد قطعات:
        // با ثابت‌ماندن دوربین، تغییر bounds نباید هیچ پیکسل گرید را جابه‌جا کند.
        var cam = new SceneCamera { Distance = 260, Elevation = 0.55, Azimuth = 0.4 };
        cam.Snap();
        var frame = cam.ComputeFrame(300, 220);

        byte[] RenderWith((Vector3 Min, Vector3 Max) bounds)
        {
            var r = new SceneRenderer();
            r.Resize(300, 220, 1);
            var opt = new RenderOptions { ShowGrid = true, ShowAxes = false };
            r.Render(Array.Empty<SceneObject>(), frame, opt, 0);
            return r.Output.ToArray();
        }

        var a = RenderWith((new Vector3(-30, -30, -10), new Vector3(30, 30, 10)));
        var b = RenderWith((new Vector3(-60, -45, -10), new Vector3(60, 45, 10)));
        Assert.Equal(a.Length, b.Length);
        int diff = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 12)
                diff++;
        Assert.True(diff == 0, $"گرید با تغییر bounds قطعات جابه‌جا شد ({diff} پیکسل تفاوت).");
    }

    [Fact]
    public void Grid_SamePosition_AtSsaa1AndSsaa2()
    {
        // روکش باید در فضای پیکسلیِ همان بافر SS رسم شود؛ فعال‌کردن SSAA نباید گرید را جابه‌جا کند.
        var cam = new SceneCamera { Distance = 240, Elevation = 0.5, Azimuth = 0.3 };
        cam.Snap();
        var opt = new RenderOptions { ShowGrid = true, ShowAxes = true };

        var r1 = new SceneRenderer();
        r1.Resize(240, 180, 1);
        r1.Render(Array.Empty<SceneObject>(), cam.ComputeFrame(240, 180), opt, 0);

        var r2 = new SceneRenderer();
        r2.Resize(240, 180, 2);
        r2.Render(Array.Empty<SceneObject>(), cam.ComputeFrame(240, 180), opt, 0);

        // مقایسهٔ نمونه‌ای: سطر میانی — خطوط گرید باید تقریباً هم‌مکان باشند
        int w = 240, probeRow = 90, hits1 = 0, hits2 = 0;
        for (int x = 0; x < w; x++)
        {
            bool g1 = IsGridPixel(r1.Output, w, probeRow, x);
            bool g2 = IsGridPixel(r2.Output, w, probeRow, x);
            if (g1) hits1++;
            if (g2) hits2++;
        }
        Assert.True(hits1 > 3, "گرید در حالت عادی باید در سطر میانی دیده شود.");
        Assert.True(Math.Abs(hits1 - hits2) <= hits1 / 2 + 2,
            $"تعداد خطوط گرید در SSAA×2 ({hits2}) نباید به‌طور چشمگیری با SSAA×1 ({hits1}) فرق کند (جابه‌جایی/مقیاس غلط).");
    }

    private static bool IsGridPixel(ReadOnlySpan<byte> rgba, int w, int row, int x)
    {
        int o = (row * w + x) * 4;
        // رنگ پس‌زمینه EA EE F4 — پیکسل گرید تیره‌تر است
        return rgba[o + 2] < 0xE0 && rgba[o + 1] < 0xE6;
    }

    // ═══════════ ۴) لبه‌های ویژه به‌جای همهٔ یال‌ها ═══════════

    [Fact]
    public void ShadedWithEdges_DrawsOnlySparseFeatureEdges()
    {
        var part = new GearPart
        {
            Name = "edge-test",
            Family = GearFamily.ExternalHelical,
            Teeth = new ToothParameters { Module = 2, ToothCount = 24, FaceWidth = 16, HelixAngle = 0.3 },
            Body = new BodyFeatures { BoreDiameter = 8 },
        };
        var mesh = GearGeometryEngine.BuildPart(part).Mesh;
        var obj = new SceneObject
        {
            Id = "g",
            Name = "gear",
            Mesh = mesh,
            LocalBounds = mesh.Bounds(),
        };

        var cam = new SceneCamera { Distance = 170, Elevation = 0.5, Azimuth = 0.6 };
        cam.Snap();
        var frame = cam.ComputeFrame(320, 240);
        var r = new SceneRenderer();
        r.Resize(320, 240, 1);

        var statsEdges = r.Render(new[] { obj }, frame, new RenderOptions { Mode = DisplayMode.ShadedWithEdges }, 0);
        Assert.True(statsEdges.TrianglesDrawn > 100);
        int edgePixels = CountEdgePixels(r.Output);
        int total = 320 * 240;
        // لبه‌های ویژه (شکست‌های واقعی سطح) کم‌تر از ۵٪ پیکسل‌ها را می‌گیرند؛
        // رسم همهٔ یال‌های مثلث چند برابر این مقدار بود (نمای مش‌طور).
        Assert.True(edgePixels > 30, "باید برخی لبه‌های ویژه رسم شوند (لبهٔ دندانه/بور).");
        Assert.True(edgePixels < total * 0.05,
            $"تراکم لبه‌ها خیلی زیاد است ({100.0 * edgePixels / total:0.#}٪) — هنوز همهٔ مثلث‌ها رسم می‌شوند؟");
    }

    private static int CountEdgePixels(ReadOnlySpan<byte> rgba)
    {
        int count = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            // رنگ لبهٔ تیره (36,48,72) با تلرانس
            if (Math.Abs(rgba[i + 2] - 36) < 45 && Math.Abs(rgba[i + 1] - 48) < 45 && Math.Abs(rgba[i] - 72) < 45)
                count++;
        }
        return count;
    }

    // ═══════════ ۱۰) رنگ قطعه ═══════════

    [Theory]
    [InlineData("#DC2626", 0xDC, 0x26, 0x26)]
    [InlineData("DC2626", 0xDC, 0x26, 0x26)]
    [InlineData("#F59", 0xFF, 0x55, 0x99)] // مختصر سه‌رقمی
    [InlineData("#FFDC2626", 0xDC, 0x26, 0x26)] // ARGB هشت‌رقمی
    public void FromHex_AcceptsShorthandAndArgb(string hex, byte r, byte g, byte b)
    {
        var rgb = Rgb.FromHex(hex);
        Assert.Equal(new Rgb(r, g, b), rgb);
    }

    // ═══════════ ۱۱) خروجی تجمیعی چندقطعه‌ای ═══════════

    [Fact]
    public void MergedExport_CombinesAllPartsWithPlacement()
    {
        var partA = new GearPart
        {
            Name = "A",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 20, FaceWidth = 8 },
            Body = new BodyFeatures(),
        };
        var partB = new GearPart
        {
            Name = "B",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 12, FaceWidth = 8 },
            Body = new BodyFeatures(),
        };
        var meshA = GearGeometryEngine.BuildPart(partA).Mesh;
        var meshB = GearGeometryEngine.BuildPart(partB).Mesh;

        var merged = new TriMesh();
        merged.AddMesh(meshA, new Vector3(0, 0, 0), 0.0, 1.0);
        merged.AddMesh(meshB, new Vector3(60, 0, 0), Math.PI / 3, 0.5); // جای‌گذاری + چرخش + مقیاس

        Assert.Equal(meshA.TriangleCount + meshB.TriangleCount, merged.TriangleCount);
        Assert.Equal(meshA.VertexCount + meshB.VertexCount, merged.VertexCount);
        var bounds = merged.Bounds();
        Assert.True(bounds.Max.X > 50, "قطعهٔ دوم باید با جای‌گذاری در فاصلهٔ ۶۰mm قرار بگیرد.");
        Assert.True(bounds.Max.X < 95, "مقیاس ۰٫۵ باید رعایت شود.");
    }

    // ═══════════ ۷) کیفیت‌های خروجی Ultra/Precision ═══════════

    [Fact]
    public void QualityPresets_UltraAndPrecision_TighterTolerance()
    {
        double Tol(SahandGearMaker.Exports.MeshQualityPreset p)
            => new SahandGearMaker.Exports.MeshExportSettings { Preset = p }.ToGeometrySettings().ProfileToleranceMm;
        int Slices(SahandGearMaker.Exports.MeshQualityPreset p)
            => new SahandGearMaker.Exports.MeshExportSettings { Preset = p }.ToGeometrySettings().AxialSliceCount;
        Assert.Equal(0.10, Tol(SahandGearMaker.Exports.MeshQualityPreset.Preview));
        Assert.Equal(0.02, Tol(SahandGearMaker.Exports.MeshQualityPreset.Standard));
        Assert.Equal(0.008, Tol(SahandGearMaker.Exports.MeshQualityPreset.Fine));
        Assert.Equal(0.004, Tol(SahandGearMaker.Exports.MeshQualityPreset.Ultra));
        Assert.Equal(0.002, Tol(SahandGearMaker.Exports.MeshQualityPreset.Precision));
        Assert.True(Slices(SahandGearMaker.Exports.MeshQualityPreset.Ultra) > Slices(SahandGearMaker.Exports.MeshQualityPreset.Fine));
    }

    [Fact]
    public void HighQualityRebuild_ProducesMoreTriangles_ThanPreview()
    {
        var part = new GearPart
        {
            Name = "q",
            Family = GearFamily.ExternalHelical,
            Teeth = new ToothParameters { Module = 2, ToothCount = 18, FaceWidth = 14, HelixAngle = 0.28 },
            Body = new BodyFeatures { BoreDiameter = 6 },
        };
        var light = GearGeometryEngine.Build(part.Family, part.Teeth, part.Body,
            new GeometrySettings { ProfileToleranceMm = 0.10, AxialSliceCount = 12 });
        var ultra = GearGeometryEngine.Build(part.Family, part.Teeth, part.Body,
            new GeometrySettings { ProfileToleranceMm = 0.004, AxialSliceCount = 96 });
        Assert.True(ultra.Mesh.TriangleCount > light.Mesh.TriangleCount * 1.8,
            $"کیفیت بالا باید مثلث‌های بیشتری بسازد: ultra={ultra.Mesh.TriangleCount:N0} در برابر preview={light.Mesh.TriangleCount:N0}");
    }

    // ═══════════ ۶) نقشهٔ فنی دو نما ═══════════

    [Fact]
    public void TechnicalDrawing_HasPlanAndSideViews()
    {
        var part = new GearPart
        {
            Name = "drawing",
            Family = GearFamily.ExternalSpur,
            Teeth = new ToothParameters { Module = 2, ToothCount = 24, FaceWidth = 12 },
            Body = new BodyFeatures { BoreDiameter = 8, HubDiameter = 24, HubLength = 6 },
        };
        var build = GearGeometryEngine.BuildPart(part);
        var doc = SahandGearMaker.Drawing.TechnicalDrawingBuilder.Build(part, build);
        Assert.True(doc.Views.Count >= 2, "نقشه باید حداقل دو نما (دایره‌ای + جانبی) داشته باشد.");
        Assert.Contains(doc.Views, v => v.Title.Contains("جانبی"));
        var side = doc.Views.First(v => v.Title.Contains("جانبی"));
        Assert.NotEmpty(side.Outlines);
        Assert.True(side.Bounds.MaxX - side.Bounds.MinX > 5, "نمای جانبی باید عرض معنادار داشته باشد.");
    }
}
