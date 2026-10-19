using SahandGearMaker.Assembly;
using SahandGearMaker.Domain;
using SahandGearMaker.Geometry;
using SahandGearMaker.MathX;
using SahandGearMaker.Scene;
using Xunit;

namespace SahandGearMaker.Tests;

/// <summary>
/// آزمون‌های موتور چیدمان خودکار مونتاژ — نسخهٔ ۰٫۵ (ایرادهای ۹ و ۱۴):
/// فاز دندانه (دندانه در فضا، نه روی دندانه)، گراف چندقطعه‌ای (بیش از ۲ قطعه)،
/// مونتاژ سه‌بعدی کرم/مخروطی، و چیدن قطعات آزاد دور از مبدأ.
/// </summary>
public class AssemblyLayoutTests
{
    private sealed class Ctx : AssemblyLayoutEngine.ILayoutContext
    {
        private readonly List<GearPart> _parts = new();
        private readonly List<MatingRelation> _matings = new();
        private readonly Dictionary<string, GearBuildResult> _builds = new();

        public IReadOnlyList<GearPart> Parts => _parts;
        public IReadOnlyList<MatingRelation> Matings => _matings;

        public Ctx Add(GearPart p, MatingKind? with = null, GearPart? other = null)
        {
            _parts.Add(p);
            try
            {
                GearFamilyDefaults.Normalize(p);
                _builds[p.Id] = GearGeometryEngine.BuildPart(p);
            }
            catch (GeometryBuildException)
            {
                // بدون هندسه — شعاع تقریبی
            }
            if (with is { } kind && other is not null)
                _matings.Add(new MatingRelation { PartAId = other.Id, PartBId = p.Id, Kind = kind });
            return this;
        }

        public void Relate(GearPart a, GearPart b, MatingKind kind)
            => _matings.Add(new MatingRelation { PartAId = a.Id, PartBId = b.Id, Kind = kind });

        public MatingCheckResult? Check(MatingKind kind, GearPart a, GearPart b) => DesignerCheck(kind, a, b);

        public double RadiusOf(GearPart part)
            => _builds.TryGetValue(part.Id, out var b) ? b.Figures.TipRadius : 20.0;

        public int? ToothCountOf(GearPart part)
        {
            if (part.Family is GearFamily.Rack or GearFamily.HelicalRack) return null;
            return part.Teeth.ToothCount;
        }

        public bool IsInternal(GearPart part)
            => part.Family is GearFamily.InternalSpur or GearFamily.InternalHelical;

        public double PitchRadiusOf(GearPart part)
            => _builds.TryGetValue(part.Id, out var b) && b.Figures.PitchRadius > 1e-6
                ? b.Figures.PitchRadius
                : part.Teeth.Module * Math.Max(6, part.Teeth.ToothCount) / 2.0;

        // همان جفت‌سنج UI (استاتیک DesignerViewModel بدون WPF قابل‌استفاده نیست — همین مسیر)
        private static MatingCheckResult? DesignerCheck(MatingKind kind, GearPart a, GearPart b) => kind switch
        {
            MatingKind.ExternalPair => GearPairCalculator.CheckExternalPair(a.Teeth, b.Teeth),
            MatingKind.InternalPair => GearPairCalculator.CheckInternalPair(a.Teeth, b.Teeth),
            MatingKind.RackPinion => GearPairCalculator.CheckRackPinion(a.Teeth, b.Teeth),
            MatingKind.BevelPair => AdvancedPairCalculator.CheckBevelPair(a.Teeth, b.Teeth),
            MatingKind.WormPair => AdvancedPairCalculator.CheckWormPair(a.Teeth, b.Teeth,
                b.Specific.WormPitchDiameterMm > 0 ? b.Specific.WormPitchDiameterMm : 40),
            MatingKind.CrossedHelicalPair => AdvancedPairCalculator.CheckCrossedHelicalPair(a.Teeth, b.Teeth),
            MatingKind.CrownPair => AdvancedPairCalculator.CheckCrownPair(a.Teeth, b.Teeth),
            _ => null,
        };
    }

    private static GearPart Spur(string name, int z, double m = 2, double helixDeg = 0)
        => new()
        {
            Name = name,
            Family = helixDeg == 0 ? GearFamily.ExternalSpur : GearFamily.ExternalHelical,
            Teeth = new ToothParameters { Module = m, ToothCount = z, FaceWidth = 12, HelixAngle = UnitConverter.DegToRad(helixDeg) },
        };

    // ═══════════ ۹) فاز دندانه ═══════════

    [Theory]
    [InlineData(20, 30, 0.0)]
    [InlineData(13, 21, 0.4)]
    [InlineData(24, 24, 1.1)]
    public void ExternalPair_TeethDoNotOverlap_AfterLayout(int zA, int zB, double thetaADeg)
    {
        var a = Spur("A", zA);
        var b = Spur("B", zB);
        a.Placement.Theta = UnitConverter.DegToRad(thetaADeg);
        var ctx = new Ctx().Add(a).Add(b);
        ctx.Relate(a, b, MatingKind.ExternalPair);

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.Equal(1, result.Applied);

        // فاصلهٔ مرکز
        double d = Math.Sqrt(Math.Pow(b.Placement.X - a.Placement.X, 2) + Math.Pow(b.Placement.Y - a.Placement.Y, 2));
        var check = GearPairCalculator.CheckExternalPair(a.Teeth, b.Teeth);
        Assert.True(Math.Abs(d - check.CenterDistanceMm) < 0.01, $"فاصلهٔ محور {d:F2} ≠ {check.CenterDistanceMm:F2}");

        // سنجهٔ برخورد روی خط مراکز: مجموع شعاع مرز A در جهت ۰ و مرز B در جهت π
        // نباید از فاصلهٔ محور بیشتر شود (دندانه روی دندانه = برخورد مستقیم)
        var meshA = GearGeometryEngine.BuildPart(a).Mesh;
        var meshB = GearGeometryEngine.BuildPart(b).Mesh;
        var movedB = meshB.Transformed(new Vector3(b.Placement.X, b.Placement.Y, 0), b.Placement.Theta);

        double rA0 = MaxRadiusInCone(meshA, centerAngle: 0, halfWindow: 2 * Math.PI / zA / 6);
        double rBpi = MaxRadiusInConeFrom(movedB, pivotX: check.CenterDistanceMm, pivotY: 0,
            centerAngle: Math.PI, halfWindow: 2 * Math.PI / zB / 6);
        double overlap = rA0 + rBpi - check.CenterDistanceMm;
        Assert.True(overlap < 0.35,
            $"برخورد دندانه روی خط مراکز: {overlap:F2}mm — دندانه‌ها روی هم افتاده‌اند (باید < ۰٫۳۵). θA={UnitConverter.RadToDeg(a.Placement.Theta):F2}°، θB={UnitConverter.RadToDeg(b.Placement.Theta):F2}°");
    }

    /// <summary>بیشینهٔ شعاع رئوس مش در مخروط زاویه‌ای حول مرکز مبدأ.</summary>
    private static double MaxRadiusInCone(TriMesh mesh, double centerAngle, double halfWindow)
        => MaxRadiusInConeFrom(mesh, 0, 0, centerAngle, halfWindow);

    private static double MaxRadiusInConeFrom(TriMesh mesh, double pivotX, double pivotY, double centerAngle, double halfWindow)
    {
        double best = 0;
        foreach (var v in mesh.Vertices)
        {
            double dx = v.X - pivotX, dy = v.Y - pivotY;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r < 0.5) continue;
            double th = Math.Atan2(dy, dx);
            double d = Math.Abs(AngDiff(th, centerAngle));
            if (d <= halfWindow && r > best)
                best = r;
        }
        return best;
    }

    private static double AngDiff(double a, double b)
    {
        double d = a - b;
        while (d > Math.PI) d -= 2 * Math.PI;
        while (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    private static double Mod(double v) { double r = v % (2 * Math.PI); return r < 0 ? r + 2 * Math.PI : r; }

    [Fact]
    public void ExternalPair_BToothSitsInASpace_OnLineOfCenters()
    {
        var a = Spur("A", 20);
        var b = Spur("B", 30);
        var ctx = new Ctx().Add(a).Add(b);
        ctx.Relate(a, b, MatingKind.ExternalPair);
        AssemblyLayoutEngine.AutoLayout(ctx);

        // خط مراکز روی +X؛ دندانهٔ A نزدیک زاویهٔ ۰ و دندانهٔ B نزدیک زاویهٔ π
        double pA = 2 * Math.PI / 20, pB = 2 * Math.PI / 30;
        double deltaA = Mod(-a.Placement.Theta) % pA;          // فاز A روی خط
        double deltaB = Mod(Math.PI - b.Placement.Theta) % pB;  // فاز B روی خط

        bool aToothAtLine = deltaA < pA / 4 || deltaA > 3 * pA / 4;
        bool bToothAtLine = deltaB < pB / 4 || deltaB > 3 * pB / 4;

        Assert.False(aToothAtLine && bToothAtLine, "دندانه روی دندانه روی خط مراکز!");
        Assert.True(aToothAtLine || bToothAtLine, "باید تماس دندانه-فضا برقرار باشد");
    }

    // ═══════════ ۱۴) بیش از ۲ قطعه ═══════════

    [Fact]
    public void TrainOfThreeGears_AllPlacedWithoutStacking()
    {
        var a = Spur("A", 20);
        var b = Spur("B", 30);
        var c = Spur("C", 40);
        var ctx = new Ctx().Add(a).Add(b).Add(c);
        ctx.Relate(a, b, MatingKind.ExternalPair);
        ctx.Relate(b, c, MatingKind.ExternalPair);

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Failed);

        // قطعهٔ سوم نباید روی اولی بیفتد
        double distAC = Math.Sqrt(Math.Pow(c.Placement.X - a.Placement.X, 2) + Math.Pow(c.Placement.Y - a.Placement.Y, 2));
        Assert.True(distAC > 20, $"C روی A افتاد (فاصله {distAC:F1}mm)");

        // فاصله‌های محور درست
        var ab = GearPairCalculator.CheckExternalPair(a.Teeth, b.Teeth);
        var bc = GearPairCalculator.CheckExternalPair(b.Teeth, c.Teeth);
        double distAB = Dist(a, b), distBC = Dist(b, c);
        Assert.True(Math.Abs(distAB - ab.CenterDistanceMm) < 0.01);
        Assert.True(Math.Abs(distBC - bc.CenterDistanceMm) < 0.01);
    }

    [Fact]
    public void UnrelatedParts_ArrangedOffOrigin_NotStacked()
    {
        var a = Spur("A", 20);
        var b = Spur("B", 30);
        var c = Spur("آزاد ۱", 24);
        var d = Spur("آزاد ۲", 36);
        var ctx = new Ctx().Add(a).Add(b).Add(c).Add(d);
        ctx.Relate(a, b, MatingKind.ExternalPair);

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.Equal(1, result.Applied);
        Assert.Equal(2, result.UnplacedArranged);

        // قطعات آزاد از مبدأ دورند و روی هم نیستند
        Assert.True(Math.Sqrt(c.Placement.X * c.Placement.X + c.Placement.Y * c.Placement.Y) > 30, "C باید از مبدأ دور باشد");
        Assert.True(Math.Sqrt(d.Placement.X * d.Placement.X + d.Placement.Y * d.Placement.Y) > 30, "D باید از مبدأ دور باشد");
        Assert.True(Dist(c, d) > 30, $"قطعات آزاد روی هم افتادند ({Dist(c, d):F1}mm)");
        Assert.True(Dist(a, c) > 25, "قطعهٔ آزاد نباید داخل مونتاژ بیفتد");
    }

    [Fact]
    public void OverconstrainedPart_SecondRelationDoesNotMoveItTwice()
    {
        // A–B و B–C و C هم به A گره خورده (بیش‌قید) — B/C هر کدام یک‌بار جای‌گذاری می‌شوند
        var a = Spur("A", 20);
        var b = Spur("B", 30);
        var c = Spur("C", 40);
        var ctx = new Ctx().Add(a).Add(b).Add(c);
        ctx.Relate(a, b, MatingKind.ExternalPair);
        ctx.Relate(b, c, MatingKind.ExternalPair);
        ctx.Relate(a, c, MatingKind.ExternalPair); // بیش‌قید

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.True(result.Applied >= 2);
        Assert.Contains(result.Notes, n => n.Contains("بیش‌قید"));
    }

    // ═══════════ مونتاژ سه‌بعدی ═══════════

    [Fact]
    public void WormPair_WheelAxisPerpendicularWithCenterDistance()
    {
        var worm = GearFamilyDefaults.CreatePart(GearFamily.Worm, "کرم");
        var wheel = GearFamilyDefaults.CreatePart(GearFamily.WormWheel, "چرخ کرم");
        wheel.Teeth.ToothCount = 40;
        var ctx = new Ctx().Add(worm).Add(wheel);
        ctx.Relate(worm, wheel, MatingKind.WormPair);

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.Equal(1, result.Applied);
        Assert.Equal(0, result.Failed);

        // چرخ: محور عمود (ThetaY=90°) و در فاصلهٔ محور روی Y
        Assert.Equal(Math.PI / 2, wheel.Placement.ThetaY, 5);
        Assert.Equal(0, wheel.Placement.X, 3);
        Assert.True(wheel.Placement.Y > 20, $"فاصلهٔ محور کرم/چرخ باید مثبت و معنادار باشد ({wheel.Placement.Y:F1})");
    }

    [Fact]
    public void BevelPair_ApicesCoincident_ShaftAngleApplied()
    {
        var a = GearFamilyDefaults.CreatePart(GearFamily.BevelSpur, "مخروطی A");
        var b = GearFamilyDefaults.CreatePart(GearFamily.BevelSpur, "مخروطی B");
        var ctx = new Ctx().Add(a).Add(b);
        ctx.Relate(a, b, MatingKind.BevelPair);

        var result = AssemblyLayoutEngine.AutoLayout(ctx);
        Assert.Equal(1, result.Applied);

        Assert.Equal(a.Placement.X, b.Placement.X, 3); // رأس مشترک
        Assert.Equal(a.Placement.Y, b.Placement.Y, 3);
        Assert.Equal(Math.PI / 2, b.Placement.ThetaY, 4); // زاویهٔ محور پیش‌فرض ۹۰°
    }

    // ═══════════ تبدیل سه‌بعدی صحنه ═══════════

    [Fact]
    public void ObjectTransform_RotatesAxisAboutY()
    {
        var t = new ObjectTransform(new Vector3(10, 20, 30), 0.0, 1.0, 0.0, Math.PI / 2);
        // نقطه روی محور Z محلی باید به محور X جهانی برود
        var p = t.Apply(new Vector3(0, 0, 5));
        Assert.Equal(15, p.X, 4); // 10 + 5
        Assert.Equal(20, p.Y, 4);
        Assert.Equal(30, p.Z, 4); // Z جابه‌جایی خالص

        // اسپین محلی (Theta) هنوز حول محور خودی است
        var t2 = new ObjectTransform(Vector3.Zero, Math.PI / 2, 1.0, 0.0, Math.PI / 2);
        var q = t2.Apply(new Vector3(0, 0, 5));
        Assert.Equal(5, q.X, 4); // اسپین حول Z محلی، محور به X می‌رود — نقطهٔ محوری تغییر نمی‌کند
    }

    private static double Dist(GearPart a, GearPart b)
        => Math.Sqrt(Math.Pow(b.Placement.X - a.Placement.X, 2) + Math.Pow(b.Placement.Y - a.Placement.Y, 2));
}
