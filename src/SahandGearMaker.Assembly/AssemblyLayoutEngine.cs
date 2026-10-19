using SahandGearMaker.Domain;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Assembly;

/// <summary>نتیجهٔ چیدمان خودکار مونتاژ.</summary>
public sealed record AutoLayoutResult
{
    public int Applied { get; init; }
    public int Failed { get; init; }
    public int UnplacedArranged { get; init; }
    public List<string> Notes { get; init; } = new();
}

/// <summary>
/// موتور چیدمان خودکار مونتاژ — نسخهٔ ۰٫۵ (رفع ایرادهای ۹ و ۱۴ کاربر).
///
/// ویژگی‌ها:
///   - پیمایش گراف روابط (BFS): هر قطعه یک‌بار جای‌گذاری می‌شود؛ رابطهٔ دوم روی همان قطعه
///     به‌جای بازنویسی جای‌گذاری، هشدار «بیش‌قید» می‌دهد (رفع «بیش از ۲ قطعه روی هم می‌افتاد»)؛
///   - فاز دندانه: دندانهٔ B دقیقاً در فضای A روی خط مراکز می‌نشیند (رفع «دندانه‌ها روی هم»)؛
///   - مونتاژ سه‌بعدی: چرخ کرم با محور عمود بر کرم، مخروطی‌ها با رأس مشترک و زاویهٔ محور،
///     متقاطع‌ها و هیپوئید با فاصله/آفست محور (Placement.ThetaX/ThetaY — جدید)؛
///   - قطعات بدون رابطه در حلقهٔ آزاد با فاصلهٔ امن چیده می‌شوند (نه روی مبدأ روی هم).
/// </summary>
public static class AssemblyLayoutEngine
{
    /// <summary>وابستگی‌های بیرونی — برای آزمون پذیری بدون WPF.</summary>
    public interface ILayoutContext
    {
        IReadOnlyList<GearPart> Parts { get; }
        IReadOnlyList<MatingRelation> Matings { get; }
        MatingCheckResult? Check(MatingKind kind, GearPart a, GearPart b);
        /// <summary>شعاع تقریبی قطعه برای چیدن قطعات آزاد (mm)؛ ۰ اگر هندسه نیست.</summary>
        double RadiusOf(GearPart part);
        /// <summary>تعداد دندانهٔ مؤثر برای فاز (کرم = رشته‌ها)؛ null = فاز نداریم.</summary>
        int? ToothCountOf(GearPart part);
        bool IsInternal(GearPart part);
        double PitchRadiusOf(GearPart part);
    }

    public static AutoLayoutResult AutoLayout(ILayoutContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var byId = ctx.Parts.ToDictionary(p => p.Id);
        var placed = new HashSet<string>();
        var notes = new List<string>();
        int applied = 0, failed = 0;

        // ---- گراف مجاورت ----
        var adj = new Dictionary<string, List<(MatingRelation Rel, GearPart A, GearPart B)>>();
        foreach (var m in ctx.Matings)
        {
            if (!byId.TryGetValue(m.PartAId, out var a) || !byId.TryGetValue(m.PartBId, out var b))
            {
                failed++;
                continue;
            }
            if (!adj.TryGetValue(a.Id, out var la)) adj[a.Id] = la = new();
            la.Add((m, a, b));
            if (!adj.TryGetValue(b.Id, out var lb)) adj[b.Id] = lb = new();
            lb.Add((m, b, a)); // از سمت B هم قابل‌پیمایش (نقش‌ها جابه‌جا)
        }

        // ---- BFS از اولین قطعهٔ دارای رابطه ----
        var queue = new Queue<GearPart>();
        foreach (var p in ctx.Parts)
        {
            if (adj.ContainsKey(p.Id))
            {
                queue.Enqueue(p);
                placed.Add(p.Id);
                break;
            }
        }

        var placedParts = new List<GearPart>();
        if (queue.Count > 0)
            placedParts.Add(queue.Peek());

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!adj.TryGetValue(cur.Id, out var rels)) continue;
            foreach (var (rel, a, b) in rels)
            {
                // نقش‌ها: (a,b) به‌گونه‌ای که «a» همان قطعهٔ جاری است
                if (placed.Contains(b.Id))
                {
                    if (!ReferenceEquals(a, b))
                        notes.Add($"«{b.Name}» از قبل جای‌گذاری شده — رابطهٔ {rel.Kind} به‌عنوان بیش‌قید نادیده گرفته شد (نظم گراف).");
                    continue;
                }
                if (!TryPlacePair(ctx, a, b, rel.Kind, notes, placedParts))
                {
                    failed++;
                    continue;
                }
                placed.Add(b.Id);
                placedParts.Add(b);
                queue.Enqueue(b);
                applied++;
            }
        }

        // ---- قطعات بدون رابطه: حلقهٔ آزاد با فاصلهٔ امن ----
        int arranged = 0;
        var free = ctx.Parts.Where(p => !placed.Contains(p.Id) && !adj.ContainsKey(p.Id)).ToList();
        if (free.Count > 0)
        {
            // شعاع حلقه = مجموع شعاع‌ها + فاصله؛ قطعات روی دایره با تفکیک زاویه‌ای مساوی
            double sumRadii = free.Sum(ctx.RadiusOf) + 6.0 * free.Count;
            double ringR = Math.Max(40.0, sumRadii / Math.Max(1, Math.PI));
            for (int i = 0; i < free.Count; i++)
            {
                double ang = 2.0 * Math.PI * i / free.Count;
                var p = free[i];
                // مبدأ پایین-چپِ حلقه — دور از مونتاژ اصلی
                p.Placement.X = ringR * Math.Cos(ang) + ringR;
                p.Placement.Y = ringR * Math.Sin(ang) - ringR;
                p.Placement.Z = 0;
                arranged++;
            }
            notes.Add($"{free.Count} قطعهٔ بدون رابطه روی حلقهٔ آزاد با فاصلهٔ امن چیده شدند (نه روی مبدأ).");
        }

        return new AutoLayoutResult
        {
            Applied = applied,
            Failed = failed,
            UnplacedArranged = arranged,
            Notes = notes,
        };
    }

    // ═══════════ جای‌گذاری یک جفت ═══════════

    private static bool TryPlacePair(ILayoutContext ctx, GearPart a, GearPart b, MatingKind kind,
        List<string> notes, List<GearPart> placedParts)
    {
        var check = ctx.Check(kind, a, b) ?? ctx.Check(kind, b, a);
        if (check is null || !check.IsCompatible)
        {
            notes.Add($"جفت «{a.Name}» ↔ «{b.Name}» سازگار نیست — جای‌گذاری نشد.");
            return false;
        }

        switch (kind)
        {
            case MatingKind.ExternalPair or MatingKind.InternalPair:
            {
                double d = check.CenterDistanceMm;
                // جهت ترجیحی (موقعیت فعلی B نسبت به A)؛ اگر به قطعهٔ جای‌گذاری‌شدهٔ دیگری بخورد، می‌چرخد
                double ang = ChooseClearDirection(a, b, d, DirectionTo(a, b), placedParts, ctx);
                b.Placement.X = a.Placement.X + d * Math.Cos(ang);
                b.Placement.Y = a.Placement.Y + d * Math.Sin(ang);
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaX = 0;
                b.Placement.ThetaY = 0;
                AlignCylindricalPhase(ctx, a, b, ang, internalMesh: kind == MatingKind.InternalPair);
                return true;
            }

            case MatingKind.RackPinion:
            {
                // پینیون مرجع؛ شانه با خط گام روی شعاع گام پینیون و دندانه‌ها رو به پینیون
                var pinion = a.Family is GearFamily.Rack or GearFamily.HelicalRack ? b : a;
                var rack = ReferenceEquals(pinion, a) ? b : a;
                double rp = Math.Max(1.0, ctx.PitchRadiusOf(pinion));
                double dx = rack.Placement.X - pinion.Placement.X;
                double dy = rack.Placement.Y - pinion.Placement.Y;
                double dirAng = Math.Abs(dx) + Math.Abs(dy) < 1e-6 ? -Math.PI / 2 : Math.Atan2(dy, dx);

                // خط گام شانه (y محلی = 0) روی شعاع گام پینیون؛ دندانه‌ها (＋Y محلی) رو به پینیون
                rack.Placement.X = pinion.Placement.X + Math.Cos(dirAng) * rp;
                rack.Placement.Y = pinion.Placement.Y + Math.Sin(dirAng) * rp;
                rack.Placement.Z = pinion.Placement.Z;
                rack.Placement.Theta = dirAng + Math.PI / 2; // ‏(＋Y محلی) → جهت پینیون
                rack.Placement.ThetaX = 0;
                rack.Placement.ThetaY = 0;

                AlignRackPhase(ctx, pinion, rack, dirAng);
                return true;
            }

            case MatingKind.WormPair:
            {
                // کرم: محور Z در جای خودش؛ چرخ: محور عمود (ThetaY=90°) در فاصلهٔ محور روی +Y
                var worm = a.Family == GearFamily.Worm ? a : (b.Family == GearFamily.Worm ? b : a);
                var wheel = ReferenceEquals(worm, a) ? b : a;
                double d = check.CenterDistanceMm;
                if (d <= 0) d = ctx.PitchRadiusOf(worm) + ctx.PitchRadiusOf(wheel);

                if (ReferenceEquals(wheel, b))
                {
                    wheel.Placement.X = worm.Placement.X;
                    wheel.Placement.Y = worm.Placement.Y + d;
                    wheel.Placement.Z = worm.Placement.Z;
                    wheel.Placement.ThetaY = Math.PI / 2.0; // محور Z→X (عمود بر کرم)
                    wheel.Placement.ThetaX = 0;
                    AlignWormWheelPhase(ctx, wheel);
                }
                else
                {
                    // B خودِ کرم است: چرخ مرجع است — کرم را جابه‌جا می‌کنیم
                    worm.Placement.X = wheel.Placement.X;
                    worm.Placement.Y = wheel.Placement.Y - d;
                    worm.Placement.Z = wheel.Placement.Z;
                    worm.Placement.ThetaY = 0;
                    worm.Placement.ThetaX = 0;
                }
                return true;
            }

            case MatingKind.BevelPair:
            {
                // رأس‌های مخروط منطبق؛ محور B با زاویهٔ محورها حول Y کج می‌شود
                double shaft = a.Specific.BevelShaftAngle > 0 ? a.Specific.BevelShaftAngle : Math.PI / 2;
                b.Placement.X = a.Placement.X;
                b.Placement.Y = a.Placement.Y;
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaY = shaft;
                b.Placement.ThetaX = 0;
                AlignBevelPhase(ctx, a, b, shaft);
                return true;
            }

            case MatingKind.CrossedHelicalPair:
            {
                // محورها متقاطع (پیش‌فرض ۹۰°)؛ فاصلهٔ محور روی +Y
                double d = check.CenterDistanceMm > 0 ? check.CenterDistanceMm
                    : ctx.PitchRadiusOf(a) + ctx.PitchRadiusOf(b);
                double shaft = Math.Abs(a.Teeth.HelixAngle) + Math.Abs(b.Teeth.HelixAngle);
                b.Placement.X = a.Placement.X;
                b.Placement.Y = a.Placement.Y + d;
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaY = Math.Clamp(shaft, Math.PI / 12, Math.PI / 2);
                b.Placement.ThetaX = 0;
                return true;
            }

            case MatingKind.HypoidPair:
            {
                double e = a.Specific.HypoidOffsetMm;
                double d = ctx.PitchRadiusOf(a) + ctx.PitchRadiusOf(b);
                b.Placement.X = a.Placement.X + e;
                b.Placement.Y = a.Placement.Y + d;
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaY = Math.PI / 2.0;
                b.Placement.ThetaX = 0;
                return true;
            }

            case MatingKind.CrownPair:
            {
                // تاج: صفحهٔ دندانه رو به پینیون؛ محور پینیون عمود بر محور تاج
                double d = check.CenterDistanceMm > 0 ? check.CenterDistanceMm
                    : ctx.PitchRadiusOf(a) + ctx.PitchRadiusOf(b);
                b.Placement.X = a.Placement.X;
                b.Placement.Y = a.Placement.Y + d;
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaY = Math.PI / 2.0;
                b.Placement.ThetaX = 0;
                return true;
            }

            case MatingKind.NonCircularPair:
            {
                double d = check.CenterDistanceMm;
                if (d <= 0)
                {
                    notes.Add($"فاصلهٔ مرکز بیضوی‌ها برای «{a.Name}»/«{b.Name}» در دسترس نیست — تقریب شعاعی.");
                    d = ctx.PitchRadiusOf(a) + ctx.PitchRadiusOf(b);
                }
                b.Placement.X = a.Placement.X + d;
                b.Placement.Y = a.Placement.Y;
                b.Placement.Z = a.Placement.Z;
                b.Placement.ThetaX = 0;
                b.Placement.ThetaY = 0;
                return true;
            }

            case MatingKind.HarmonicAssembly or MatingKind.SplineConnection:
            {
                // هم‌محور — روی محور A
                b.Placement.X = a.Placement.X;
                b.Placement.Y = a.Placement.Y;
                b.Placement.Z = a.Placement.Z;
                return true;
            }

            default:
                notes.Add($"نوع رابطهٔ {kind} در چیدمان خودکار پشتیبانی نمی‌شود.");
                return false;
        }
    }

    private static double DirectionTo(GearPart a, GearPart b)
    {
        double dx = b.Placement.X - a.Placement.X;
        double dy = b.Placement.Y - a.Placement.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-6)
            return 0;
        return Math.Atan2(dy, dx);
    }

    /// <summary>
    /// جهت چیدمان B حول A: جهت ترجیحی تا وقتی آزاد است حفظ می‌شود؛ وگرنه در گام‌های ۶۰°
    /// می‌چرخد تا از قطعات جای‌گذاری‌شده فاصلهٔ امن بگیرد (رفع «بیش از ۲ قطعه روی هم»).
    /// </summary>
    private static double ChooseClearDirection(GearPart a, GearPart b, double d, double preferred,
        List<GearPart> placedParts, ILayoutContext ctx)
    {
        double rB = ctx.RadiusOf(b);

        bool IsClear(double ang)
        {
            double nx = a.Placement.X + d * Math.Cos(ang);
            double ny = a.Placement.Y + d * Math.Sin(ang);
            foreach (var p in placedParts)
            {
                if (ReferenceEquals(p, a) || ReferenceEquals(p, b)) continue;
                double dx = nx - p.Placement.X, dy = ny - p.Placement.Y;
                double need = ctx.RadiusOf(p) + rB + 2.0;
                if (dx * dx + dy * dy < need * need)
                    return false;
            }
            return true;
        }

        if (IsClear(preferred))
            return preferred;
        for (int i = 1; i <= 11; i++)
        {
            double ang = preferred + i * Math.PI / 6.0;
            if (IsClear(ang))
                return ang;
        }
        return preferred; // جای خالی نبود — همان جهت ترجیحی (هشدار در یادداشت‌ها بهتر است)
    }

    // ═══════════ فاز دندانه ═══════════

    /// <summary>
    /// فاز B طوری که دندانه‌اش در فضای A روی خط مراکز بنشیند (رفع «دندانه‌ها روی هم»).
    /// دندانهٔ k در قاب محلی هر چرخ‌دنده دقیقاً روی زاویهٔ k·گام مرکزش دارد (قرارداد پروفیل).
    /// </summary>
    private static void AlignCylindricalPhase(ILayoutContext ctx, GearPart a, GearPart b, double lineAngle, bool internalMesh)
    {
        int? za = ctx.ToothCountOf(a), zb = ctx.ToothCountOf(b);
        if (za is not { } zA || zb is not { } zB || zA < 3 || zB < 3)
            return;

        double pA = 2.0 * Math.PI / zA, pB = 2.0 * Math.PI / zB;

        // موقعیت خط مراکز در چرخهٔ دندانهٔ A: ۰ = دندانه دقیقاً روی خط
        double deltaA = Mod(lineAngle - a.Placement.Theta, pA);
        bool aPresentsTooth = deltaA < pA / 4 || deltaA > 3 * pA / 4;

        // B باید در جهت خط (رو به A) نقش مکمل بدهد:
        //   A دندانه → B فضای دندانه (نیم‌گام جابه‌جا)؛ A فضای دندانه → B دندانه
        double towardA = lineAngle + Math.PI;
        b.Placement.Theta = aPresentsTooth
            ? Mod(towardA + pB / 2, pB)
            : Mod(towardA, pB);
    }

    private static void AlignRackPhase(ILayoutContext ctx, GearPart pinion, GearPart rack, double dirAng)
    {
        int? z = ctx.ToothCountOf(pinion);
        if (z is not { } zp || zp < 3)
            return;
        double rp = Math.Max(1.0, ctx.PitchRadiusOf(pinion));
        double pr = Math.PI * pinion.Teeth.Module; // گام شانه = π·m
        if (pr <= 0) return;

        // فضای دندانهٔ پینیون نزدیک نقطهٔ تماس (نقطهٔ تماس = مرکز پینیون + rp·جهت شانه)
        double p = 2.0 * Math.PI / zp;
        double alpha = Mod(dirAng - pinion.Placement.Theta, p); // موقعیت جهت در چرخهٔ پینیون
        if (alpha > p / 2) alpha -= p;                            // دندانهٔ نزدیک در dirAng+α
        double side = alpha >= 0 ? 1 : -1;
        double beta = dirAng + alpha - side * p / 2;         // فضای سمتِ تماس
        double contactX = pinion.Placement.X + rp * Math.Cos(beta);
        double contactY = pinion.Placement.Y + rp * Math.Sin(beta);

        // نقطهٔ تماس به قاب محلی شانه (معکوسِ جای‌گذاری شانه)
        double cosT = Math.Cos(-rack.Placement.Theta), sinT = Math.Sin(-rack.Placement.Theta);
        double lx = (contactX - rack.Placement.X) * cosT - (contactY - rack.Placement.Y) * sinT;

        // دندانه‌های شانه در قاب محلی: مراکز ≡ (half − pr/2) mod pr
        double length = rack.Specific.RackLengthMm > 0 ? rack.Specific.RackLengthMm : 120;
        double half = length / 2.0;
        double shift = Mod(lx - (half - pr / 2), pr);
        if (shift > pr / 2) shift -= pr; // نزدیک‌ترین جابه‌جایی

        // جابه‌جایی در راستای X محلی شانه
        double wx = Math.Cos(rack.Placement.Theta), wy = Math.Sin(rack.Placement.Theta);
        rack.Placement.X += shift * wx;
        rack.Placement.Y += shift * wy;
    }

    private static void AlignWormWheelPhase(ILayoutContext ctx, GearPart wheel)
    {
        int? z = ctx.ToothCountOf(wheel);
        if (z is not { } zw || zw < 3)
            return;
        // نقطهٔ تماس در قاب محلی چرخ (پس از ThetaY=90°): جهت −Y جهانی = −Y محلی
        double p = 2.0 * Math.PI / zw;
        wheel.Placement.Theta = Mod(-Math.PI / 2, p);
    }

    private static void AlignBevelPhase(ILayoutContext ctx, GearPart a, GearPart b, double shaft)
    {
        int? za = ctx.ToothCountOf(a), zb = ctx.ToothCountOf(b);
        if (za is not { } zA || zb is not { } zB || zA < 3 || zB < 3)
            return;
        double pA = 2.0 * Math.PI / zA, pB = 2.0 * Math.PI / zB;
        // عنصر مشترک در صفحهٔ XZ: در قاب A زاویهٔ ۰؛ در قاب B (پس از چرخش Y به اندازهٔ shaft) زاویهٔ π
        double deltaA = Mod(-a.Placement.Theta, pA);
        bool aPresentsTooth = deltaA < pA / 4 || deltaA > 3 * pA / 4;
        b.Placement.Theta = aPresentsTooth ? Mod(Math.PI + pB / 2, pB) : Mod(Math.PI, pB);
    }

    private static double Mod(double v, double m)
    {
        double r = v % m;
        return r < 0 ? r + m : r;
    }
}
