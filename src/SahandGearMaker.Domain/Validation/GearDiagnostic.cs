using System.Text.Json.Serialization;

namespace SahandGearMaker.Domain;

/// <summary>نتیجهٔ اعتبارسنجی/محاسبهٔ موتور هندسی با کد پایدار برای ترجمهٔ UI (سند ۰۳، بخش ۸).</summary>
public sealed class GearDiagnostic
{
    /// <summary>کد پایدار پیام؛ UI با همین شناسهٔ ثابت متن بومی‌سازی‌شده را می‌گیرد.</summary>
    public string Code { get; init; }

    public Severity Severity { get; init; }

    /// <summary>متن کاربرپسند پیش‌فرض (فارسی)؛ UI می‌تواند بر اساس کد جایگزین ترجمه‌شده بگذارد.</summary>
    public string Message { get; init; }

    /// <summary>نام پارامتر مرتبط برای هایلایت فیلد مشکل‌دار (اختیاری).</summary>
    public string? AffectedParameter { get; init; }

    [JsonConstructor]
    public GearDiagnostic(string code, Severity severity, string message, string? affectedParameter = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        AffectedParameter = affectedParameter;
    }

    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

/// <summary>کدهای پایدار پیام‌های اعتبارسنجی — تغییر نام این ثابت‌ها سازگاری پروژه‌های ذخیره‌شده را نمی‌شکند،
/// اما ترجمه‌های UI به همین کدها وصل‌اند.</summary>
public static class DiagnosticCodes
{
    // خطاها (ساخت متوقف می‌شود)
    public const string ModuleNotPositive = "SGM-E001";
    public const string ToothCountTooLow = "SGM-E002";
    public const string PressureAngleOutOfRange = "SGM-E003";
    public const string FaceWidthNotPositive = "SGM-E004";
    public const string HelixAngleOutOfRange = "SGM-E005";
    public const string NonFiniteInput = "SGM-E006";
    public const string InternalToothCountInvalid = "SGM-E007";
    public const string BoreTooLarge = "SGM-E008";
    public const string ProfileSelfIntersection = "SGM-E009";
    public const string RackLengthNotPositive = "SGM-E010";
    public const string InternalRingDifferenceTooSmall = "SGM-E011";
    public const string PlanetCountInvalid = "SGM-E012";
    public const string PlanetSpacingInvalid = "SGM-E013";

    // هشدارها (ادامه با تأیید کاربر)
    public const string UndercutRisk = "SGM-W001";
    public const string ThinRimWarning = "SGM-W002";
    public const string MeshTooHeavy = "SGM-W003";
    public const string HelicalApproximation = "SGM-W004";
    public const string ProfileShifted = "SGM-W005";
    public const string InterferenceRiskInternal = "SGM-W006";
    public const string RingThinWarning = "SGM-W007";
    public const string HerringboneGapApprox = "SGM-W008";

    // اطلاعات
    public const string UnitConverted = "SGM-I001";
    public const string PreviewMeshCoarse = "SGM-I002";

    // بررسی‌نشده — نباید با «سالم» اشتباه شود
    public const string ContactAnalysisNotRun = "SGM-U001";
    public const string StrengthNotAnalyzed = "SGM-U002";
    public const string StandardComplianceNotChecked = "SGM-U003";
}
