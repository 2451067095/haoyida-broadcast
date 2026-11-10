using System.Text;

namespace Haoyida.Core;

/// <summary>
/// 中文数字口语化：这是超市播报的核心体验之一。
/// 9.9 -> 九块九；19.80 -> 十九块八；105.5 -> 一百零五块五；0.5 -> 五毛；
/// 12.00 -> 十二块（整钱不带“零零”）。
/// </summary>
public static class NumberToChinese
{
    private static readonly string[] Digits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
    private static readonly string[] Units = { "", "十", "百", "千", "万", "十万", "百万", "千万", "亿" };

    public static string Price(decimal value, bool asChinese = true)
    {
        if (!asChinese) return value.ToString("0.##");

        value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (value < 0) return "负" + Price(-value, asChinese);

        long yuan = (long)decimal.Truncate(value);
        int jiao = (int)((value - yuan) * 10);
        int fen = (int)Math.Round((value - yuan) * 100) % 10;

        var sb = new StringBuilder();
        if (yuan > 0) sb.Append(Normalize(Integer(yuan))).Append('块');
        else if (jiao == 0 && fen == 0) return "零元";

        if (jiao == 0 && fen == 0)
        {
            if (yuan > 0) sb.Append('整');
            return sb.ToString();
        }

        // 口语规则：6.98→六块九毛八；6.90→六块九；0.90→九毛；6.05→六块零五分
        if (jiao > 0)
        {
            sb.Append(Digits[jiao]);
            if (yuan == 0 || fen > 0) sb.Append('毛');
        }
        else if (yuan > 0)
        {
            sb.Append('零');
        }

        if (fen > 0) sb.Append(Digits[fen]).Append(jiao > 0 ? "" : "分");
        return sb.ToString();
    }

    public static string Integer(long value)
    {
        if (value == 0) return "零";
        if (value < 0) return "负" + Integer(-value);

        var groups = new List<int>();
        while (value > 0) { groups.Add((int)(value % 10000)); value /= 10000; }
        if (groups.Count > 2) return value.ToString(); // 亿级以上不强行口语化，交给阿拉伯数字读法

        var sb = new StringBuilder();
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            int g = groups[i];
            if (g == 0)
            {
                if (sb.Length > 0 && !sb.ToString().EndsWith('零')) sb.Append('零');
                continue;
            }

            if (sb.Length > 0 && g < 1000 && !sb.ToString().EndsWith('零')) sb.Append('零');
            sb.Append(Group(g));
            if (i == 1) sb.Append('万');
        }

        var s = sb.ToString().TrimEnd('零');
        return s.Replace("零零", "零");
    }

    private static string Group(int n)
    {
        var sb = new StringBuilder();
        int[] unitIdx = { 3, 2, 1, 0 };
        bool zeroPending = false;
        int divisor = 1000;
        for (int i = 0; i < 4; i++)
        {
            int d = n / divisor % 10;
            divisor /= 10;
            if (d == 0)
            {
                if (sb.Length > 0) zeroPending = true;
                continue;
            }
            if (zeroPending) { sb.Append('零'); zeroPending = false; }
            sb.Append(Digits[d]).Append(Units[unitIdx[i]]);
        }
        return sb.ToString();
    }

    /// <summary>把口语化数字里连续的“一十”修正为“十”（十二块，而不是一十二块）。</summary>
    public static string Normalize(string s) =>
        s.StartsWith("一十", StringComparison.Ordinal) ? s[1..] : s;

    /// <summary>折扣：0.85 -> 八五折；0.9 -> 九折。</summary>
    public static string Discount(decimal rate)
    {
        int tenths = (int)Math.Round(rate * 100);
        if (tenths >= 100) return "原价";
        int d1 = tenths / 10, d2 = tenths % 10;
        return d2 == 0 ? $"{Digits[d1]}折" : $"{Digits[d1]}{Digits[d2]}折";
    }
}
