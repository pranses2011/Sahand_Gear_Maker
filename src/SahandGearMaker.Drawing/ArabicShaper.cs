using System.Text;

namespace SahandGearMaker.Drawing;

/// <summary>
/// شکل‌دهی متنون فارسی/عربی (contextual joining) برای PDF — نگاشت به فرم‌های نمایشی یونی‌کد.
/// الگوریتم استاندارد اتصال حروف فارسی با پشتیبانی ZWNJ و لیگاتور لام-الف.
/// خروجی: دنبالهٔ کدپوینت فرم‌های نمایشی (برای انتخاب گلیف uniXXXX در فونت).
/// </summary>
public static class ArabicShaper
{
    /// <summary>حروف دوچسب (به هر دو طرف متصل می‌شوند).</summary>
    private static readonly HashSet<int> DualJoining = new()
    {
        0x0628, 0x067E, 0x062A, 0x062B, 0x062C, 0x0686, 0x062D, 0x062E,
        0x0633, 0x0634, 0x0635, 0x0636, 0x0637, 0x0638, 0x0639, 0x063A,
        0x0641, 0x0642, 0x06A9, 0x06AF, 0x0644, 0x0645, 0x0646, 0x0647, 0x06CC,
        0x0640, // کشیده (tatweel)
    };

    /// <summary>حروف راست‌چسب (فقط به قبل متصل می‌شوند).</summary>
    private static readonly HashSet<int> RightJoining = new()
    {
        0x0627, 0x0622, 0x0623, 0x0625, 0x062F, 0x0630, 0x0631, 0x0632, 0x0698, 0x0648, 0x0629, 0x0621,
    };

    /// <summary>نویسه‌های شفاف (اتصال را قطع نمی‌کنند ولی خودشان شکل ندارند).</summary>
    private static readonly HashSet<int> Transparent = new()
    {
        0x200C, // ZWNJ
        0x064B, 0x064C, 0x064D, 0x064E, 0x064F, 0x0650, 0x0651, 0x0652,
    };

    /// <summary>جدول فرم‌های نمایشی: کدپوینت پایه → (ایزوله، آغازین، میانی، پایانی).</summary>
    private static readonly Dictionary<int, (int Iso, int Ini, int Med, int Fin)> Forms = new()
    {
        [0x0621] = (0xFE80, 0xFE80, 0xFE80, 0xFE80),
        [0x0622] = (0xFE81, 0xFE81, 0xFE82, 0xFE82),
        [0x0623] = (0xFE83, 0xFE83, 0xFE84, 0xFE84),
        [0x0624] = (0xFE85, 0xFE85, 0xFE86, 0xFE86),
        [0x0625] = (0xFE87, 0xFE87, 0xFE88, 0xFE88),
        [0x0626] = (0xFE89, 0xFE8B, 0xFE8C, 0xFE8A),
        [0x0627] = (0xFE8D, 0xFE8D, 0xFE8E, 0xFE8E),
        [0x0628] = (0xFE8F, 0xFE91, 0xFE92, 0xFE90),
        [0x0629] = (0xFE93, 0xFE93, 0xFE94, 0xFE94),
        [0x062A] = (0xFE95, 0xFE97, 0xFE98, 0xFE96),
        [0x062B] = (0xFE99, 0xFE9B, 0xFE9C, 0xFE9A),
        [0x062C] = (0xFE9D, 0xFE9F, 0xFEA0, 0xFE9E),
        [0x062D] = (0xFEA1, 0xFEA3, 0xFEA4, 0xFEA2),
        [0x062E] = (0xFEA5, 0xFEA7, 0xFEA8, 0xFEA6),
        [0x062F] = (0xFEA9, 0xFEA9, 0xFEAA, 0xFEA9),
        [0x0630] = (0xFEAB, 0xFEAB, 0xFEAC, 0xFEAB),
        [0x0631] = (0xFEAD, 0xFEAD, 0xFEAE, 0xFEAD),
        [0x0632] = (0xFEAF, 0xFEAF, 0xFEB0, 0xFEAF),
        [0x0633] = (0xFEB1, 0xFEB3, 0xFEB4, 0xFEB2),
        [0x0634] = (0xFEB5, 0xFEB7, 0xFEB8, 0xFEB6),
        [0x0635] = (0xFEB9, 0xFEBB, 0xFEBC, 0xFEBA),
        [0x0636] = (0xFEBD, 0xFEBF, 0xFEC0, 0xFEBE),
        [0x0637] = (0xFEC1, 0xFEC3, 0xFEC4, 0xFEC2),
        [0x0638] = (0xFEC5, 0xFEC7, 0xFEC8, 0xFEC6),
        [0x0639] = (0xFEC9, 0xFECB, 0xFECC, 0xFECA),
        [0x063A] = (0xFECD, 0xFECF, 0xFED0, 0xFECE),
        [0x0640] = (0x0640, 0x0640, 0x0640, 0x0640),
        [0x0641] = (0xFED1, 0xFED3, 0xFED4, 0xFED2),
        [0x0642] = (0xFED5, 0xFED7, 0xFED8, 0xFED6),
        [0x0643] = (0xFED9, 0xFEDB, 0xFEDC, 0xFEDA),
        [0x0644] = (0xFEDD, 0xFEDF, 0xFEE0, 0xFEDE),
        [0x0645] = (0xFEE1, 0xFEE3, 0xFEE4, 0xFEE2),
        [0x0646] = (0xFEE5, 0xFEE7, 0xFEE8, 0xFEE6),
        [0x0647] = (0xFEE9, 0xFEEB, 0xFEEC, 0xFEEA),
        [0x0648] = (0xFEED, 0xFEED, 0xFEEE, 0xFEED),
        [0x0649] = (0xFEEF, 0xFEEF, 0xFEF0, 0xFEEF),
        [0x064A] = (0xFEF1, 0xFEF3, 0xFEF4, 0xFEF2),
        [0x067E] = (0xFB56, 0xFB58, 0xFB59, 0xFB57),
        [0x0686] = (0xFB7A, 0xFB7C, 0xFB7D, 0xFB7B),
        [0x0698] = (0xFB8A, 0xFB8A, 0xFB8B, 0xFB8A),
        [0x06A9] = (0xFB8E, 0xFB90, 0xFB91, 0xFB8F),
        [0x06AF] = (0xFB92, 0xFB94, 0xFB95, 0xFB93),
        [0x06CC] = (0xFBFC, 0xFBFE, 0xFBFF, 0xFBFD),
        [0x06C0] = (0xFBA4, 0xFBA4, 0xFBA5, 0xFBA4),
        [0x06D2] = (0xFBAE, 0xFBAE, 0xFBAF, 0xFBAE),
    };

    /// <summary>لیگاتور لام-الف: فرم‌های نمایشی ترکیبی.</summary>
    private static readonly Dictionary<(int First, int Second), (int Iso, int Fin)> Ligatures = new()
    {
        [(0x0644, 0x0627)] = (0xFEFB, 0xFEFC), // لا
        [(0x0644, 0x0622)] = (0xFEF5, 0xFEF6), // لآ
        [(0x0644, 0x0623)] = (0xFEF7, 0xFEF8), // لأ
        [(0x0644, 0x0625)] = (0xFEF9, 0xFEFA), // لإ
    };

    /// <summary>
    /// شکل‌دهی متن فارسی به دنبالهٔ فرم‌های نمایشی + نویسه‌های دیگر (بی‌تغییر).
    /// خروجی در «ترتیب منطقی» است؛ نمایشگر PDF باید RTL را خودش با مختصات x اعمال کند
    /// (ما متن را از راست به چپ با مختصات پیش‌رونده می‌نویسیم).
    /// ZWNJ حذف می‌شود (خودش در فونت عرض ندارد) اما اثرش در قطع اتصال می‌ماند.
    /// </summary>
    public static List<int> Shape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<int>();

        var logical = text.EnumerateRunes().Select(r => r.Value).ToList();
        var output = new List<int>(logical.Count);

        for (int i = 0; i < logical.Count; i++)
        {
            int ch = logical[i];

            if (Transparent.Contains(ch))
            {
                output.Add(ch); // بعداً در نوشتن حذف/عرض‌صفر می‌شود
                continue;
            }

            // لیگاتور لام-الف
            if (i + 1 < logical.Count && Ligatures.TryGetValue((ch, logical[i + 1]), out var lig))
            {
                bool joinsBefore = PrevJoins(logical, i);
                output.Add(joinsBefore ? lig.Fin : lig.Iso);
                i++; // مصرف حرف دوم
                continue;
            }

            if (!Forms.TryGetValue(ch, out var forms))
            {
                output.Add(ch); // لاتین/عدد/نماد — بی‌تغییر
                continue;
            }

            bool prev = PrevJoins(logical, i);
            bool next = NextJoinable(logical, i);

            int form = (DualJoining.Contains(ch), RightJoining.Contains(ch)) switch
            {
                (true, _) => prev ? (next ? forms.Med : forms.Fin) : (next ? forms.Ini : forms.Iso),
                (_, true) => prev ? forms.Fin : forms.Iso,
                _ => forms.Iso,
            };
            output.Add(form);
        }

        return output;

        // آیا حرف قبل (با رد کردن شفاف‌ها) دوچسب است؟
        static bool PrevJoins(List<int> s, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (Transparent.Contains(s[k]))
                    continue;
                return DualJoining.Contains(s[k]);
            }
            return false;
        }

        // آیا حرف بعد (با رد کردن شفاف‌ها) قابل اتصال از راست است؟ (دوچسب یا راست‌چسب)
        static bool NextJoinable(List<int> s, int i)
        {
            for (int k = i + 1; k < s.Count; k++)
            {
                if (Transparent.Contains(s[k]))
                    continue;
                return DualJoining.Contains(s[k]) || RightJoining.Contains(s[k]);
            }
            return false;
        }
    }
}
