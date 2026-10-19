namespace SahandGearMaker.Domain;

/// <summary>
/// پارامترهای پروفیل دندانه برای چرخ‌دندهٔ استوانه‌ای (سند ۰۳، بخش ۳).
/// همهٔ طول‌ها بر حسب میلی‌متر (واحد داخلی) و همهٔ زاویه‌ها بر حسب رادیان ذخیره می‌شوند؛
/// ورودی/نمایش فارسی در UI با <see cref="LengthUnit"/>/درجه تبدیل می‌شود.
/// مقادیر این ساختار «مقدار دقیق ورودی» هستند، نه مقدار گردشدهٔ نمایشی.
/// </summary>
public sealed class ToothParameters
{
    /// <summary>تعداد دندانه. برای داخلی حداقل قانون قید تداخل جداگانه دارد.</summary>
    public int ToothCount { get; set; } = 20;

    /// <summary>
    /// ماژول. برای خانواده‌های مارپیچ این «ماژول نرمال mn» است؛ ماژول عرضی mt = mn/cos(β) مشتق می‌شود.
    /// </summary>
    public double Module { get; set; } = 2.0;

    /// <summary>زاویهٔ فشار نرمال (رادیان). مقادیر متداول: 20°، 25°، 14.5°.</summary>
    public double PressureAngle { get; set; } = 0.3490658503988659; // 20°

    /// <summary>زاویهٔ مارپیچ (رادیان). صفر برای مستقیم؛ علامت، جهت (راست/چپ) را کد می‌کند.</summary>
    public double HelixAngle { get; set; }

    /// <summary>پهنای دندانه در راستای محور (میلی‌متر).</summary>
    public double FaceWidth { get; set; } = 20.0;

    /// <summary>ضریب اصلاح پروفیل (profile shift). صفر = بی‌اصلاح.</summary>
    public double ProfileShiftCoefficient { get; set; }

    /// <summary>لقی محوری دندانه به میلی‌متر (نصف ضخامت دندانه از حالت بدون لقی کم می‌شود).</summary>
    public double BacklashAllowance { get; set; }

    /// <summary>ضریب addendum (به‌طور پیش‌فرض 1.0 برای خارجی و 0.8 برای داخلی در محاسبه اصلاح می‌شود).</summary>
    public double AddendumCoefficient { get; set; } = 1.0;

    /// <summary>ضریب dedendum (پیش‌فرض 1.25 مطابق قرارداد متریک متداول full-depth).</summary>
    public double DedendumCoefficient { get; set; } = 1.25;

    /// <summary>ضریب شعاع فیلهٔ ریشه؛ سقف آن در موتور هندسی به فضای موجود محدود می‌شود.</summary>
    public double RootFilletCoefficient { get; set; } = 0.38;

    /// <summary>سیستم پروفیل دندانه: اینولوت (پیش‌فرض) یا سیکلوئیدی.</summary>
    public ProfileSystem ProfileSystem { get; set; } = ProfileSystem.Involute;
}

/// <summary>سیستم پروفیل دندانه (سند ۰۳، بخش ۴ — هر خانواده سیستم معتبر خود را دارد).</summary>
public enum ProfileSystem
{
    /// <summary>پروفیل اینولوت — استاندارد انتقال توان؛ فرمول‌ها و آزمون مستقل دارد.</summary>
    Involute,

    /// <summary>پروفیل سیکلوئیدی: اپیسایکلوئید در سر + هیپوسایکلوئید در ریشه؛ متداول در ساعت‌سازی.</summary>
    Cycloidal,
}

/// <summary>
/// شکل سوراخ مرکزی (بور) — نسخهٔ ۰٫۵ «همهٔ انواع سوراخ».
/// اندازه از BoreDiameter خوانده می‌شود؛ پارامترهای تکمیلی هر شکل در BodyFeatures.
/// </summary>
public enum BoreShapeType
{
    /// <summary>دایرهٔ ساده (پیش‌فرض).</summary>
    Round,

    /// <summary>سوراخ D — دایره با یک سطح تخت (برای مهار دوران؛ رایج در موتورخانه).</summary>
    DShape,

    /// <summary>سوراخ دو-D — دایره با دو سطح تخت موازی (گشتاور بیشتر، نگهداری متقارن).</summary>
    DoubleD,

    /// <summary>شش‌گوش (metric hex) — بین‌سرِ مساوی با قطر بور.</summary>
    Hexagon,

    /// <summary>چهارگوش (square) — بین‌سرِ مساوی با قطر بور.</summary>
    Square,

    /// <summary>دایره + شیار خار (پارامترهای Keyway*).</summary>
    Keyed,

    /// <summary>هزارخار مستقیم‌دیواره روی بور (اتصال شفت با خارهای محیطی).</summary>
    Spline,
}

/// <summary>شکل حفره‌های سبک‌سازی — نسخهٔ ۰٫۵ «فقط دایره نباشد».</summary>
public enum LighteningHoleShape
{
    /// <summary>دایره (کلاسیک).</summary>
    Circle,

    /// <summary>مثلث متساوی‌الاضلاع.</summary>
    Triangle,

    /// <summary>مربع.</summary>
    Square,

    /// <summary>پنج‌ضلعی.</summary>
    Pentagon,

    /// <summary>شش‌ضلعی (متداول در چرخ‌دنده‌های ریخته‌گری).</summary>
    Hexagon,

    /// <summary>هشت‌ضلعی.</summary>
    Octagon,

    /// <summary>شکاف کشیده با سرهای نیم‌دایره (obround/slot) — طول = ۲× عرض.</summary>
    Slot,

    /// <summary>ستارهٔ شش‌پر (سبک‌سازی تزیینی/سورس‌رخ).</summary>
    Star,
}

/// <summary>
/// ویژگی‌های بدنه (سند ۰۳، بخش ۵) — «داخل چرخ‌دنده» به معنای حفره/جزئیات بدنه،
/// جدا از «دندانهٔ داخلی» که خودش یک خانوادهٔ هندسی است.
/// </summary>
public sealed class BodyFeatures
{
    /// <summary>قطر سوراخ مرکزی/بور؛ صفر = بدون سوراخ.</summary>
    public double BoreDiameter { get; set; } = 10.0;

    /// <summary>شکل بور — نسخهٔ ۰٫۵: گرد، D، دو-D، شش‌گوش، چهارگوش، خاردار، هزارخار.</summary>
    public BoreShapeType BoreShape { get; set; } = BoreShapeType.Round;

    /// <summary>فاصلهٔ سطح(های) تخت D/دو-D از مرکز (mm). صفر = خودکار (۹۰٪ شعاع).</summary>
    public double BoreFlatDistanceMm { get; set; }

    /// <summary>تعداد خارهای بور هزارخار (۳ تا ۲۴).</summary>
    public int BoreSplineCount { get; set; } = 6;

    /// <summary>ارتفاع خار بور هزارخار از دایرهٔ ریشه (mm).</summary>
    public double BoreSplineDepthMm { get; set; } = 1.5;

    /// <summary>تعداد سوراخ‌های سبک‌سازی روی دایرهٔ گام بدنه؛ صفر = ندارد.</summary>
    public int LighteningHoleCount { get; set; }

    /// <summary>اندازهٔ هر حفرهٔ سبک‌سازی — قطر دایره یا بین‌سرِ چندضلعی‌ها (mm).</summary>
    public double LighteningHoleDiameter { get; set; } = 8.0;

    /// <summary>شکل حفره‌های سبک‌سازی (دایره، چندضلعی‌ها، شکاف، ستاره).</summary>
    public LighteningHoleShape LighteningHoleShape { get; set; } = LighteningHoleShape.Circle;

    /// <summary>شعاع دایرهٔ مرکز سوراخ‌های سبک‌سازی.</summary>
    public double LighteningHoleCircleRadius { get; set; } = 20.0;

    /// <summary>
    /// حفره‌ها همراه با پیچش مارپیچ بچرخند؟ پیش‌فرض خیر — حفره‌ها صاف و موازی محور می‌مانند
    /// («بدون چرخش» — نسخهٔ ۰٫۵، ایراد ۵ کاربر)؛ روشن‌کردن آن الگوی پیچان ریخته‌گری را می‌سازد.
    /// </summary>
    public bool LighteningHoleFollowTwist { get; set; }

    /// <summary>چرخش یکنواخت همهٔ حفره‌ها حول محور خودشان (درجه) — برای جهت‌دادن شکاف/مثلث.</summary>
    public double LighteningHoleRotationDeg { get; set; }

    /// <summary>قطر توپی (hub)؛ صفر = بدون توپی. توپی از سطح جانبی بیرون می‌زند.</summary>
    public double HubDiameter { get; set; }

    /// <summary>طول (پهنای) توپی در راستای محور.</summary>
    public double HubLength { get; set; } = 8.0;

    /// <summary>شیار خار کلیدی روی بور: پهنا (صفر = بدون شیار).</summary>
    public double KeywayWidth { get; set; }

    /// <summary>عمق شیار خار از سطح داخلی بور.</summary>
    public double KeywayDepth { get; set; } = 3.0;

    /// <summary>شعاع گِردی ته شیار خار (mm). صفر = انتهای تخت (مستطیلی)؛ مقدار مثبت = انتهای نیم‌دایره.</summary>
    public double KeywayEndRadius { get; set; }

    /// <summary>طول شانه (mm) — فقط برای خانوادهٔ رک/رک مارپیچ. صفر = پیش‌فرض ۱۰ گام.</summary>
    public double RackLengthMm { get; set; }

    /// <summary>پهنای گپ/شیار مرکزی چرخ‌دندهٔ جناغی (mm). صفر = جناغی پیوسته بدون گپ.</summary>
    public double HerringboneCentralGapMm { get; set; }

    /// <summary>پخ لبهٔ نوک دندانه (mm در راستای شعاعی) — صفر = بدون پخ.</summary>
    public double TipChamferMm { get; set; }

    /// <summary>پخ لبهٔ دهانهٔ بور در دو سطح روی/زیر (mm، ۴۵ درجه) — صفر = بدون پخ.</summary>
    public double BoreChamferMm { get; set; }
}
