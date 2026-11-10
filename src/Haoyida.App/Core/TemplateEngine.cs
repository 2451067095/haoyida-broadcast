using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Haoyida.Core;

/// <summary>播报词生成：把商品/门店/时间等结构化数据套进模板，并做中文口语化。</summary>
public static partial class TemplateEngine
{
    [GeneratedRegex(@"\{([\u4e00-\u9fa5A-Za-z]+)\}")]
    private static partial Regex TokenRegex();

    public static string Render(string template, StoreProfile store, ProductItem? product = null, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        var t = now ?? DateTime.Now;
        var sb = new StringBuilder(template);

        sb.Replace("{店名}", store.StoreName);
        sb.Replace("{分店}", store.Branch);
        sb.Replace("{电话}", store.Phone);
        sb.Replace("{时间}", t.ToString("HH点mm分"));
        sb.Replace("{日期}", t.ToString("M月d日"));
        sb.Replace("{星期}", "星期" + "日一二三四五六"[(int)t.DayOfWeek]);

        if (product is not null)
        {
            sb.Replace("{商品}", product.Name);
            sb.Replace("{单位}", product.Unit);
            sb.Replace("{促销语}", product.PromoText);
            sb.Replace("{原价}", product.OriginalPrice is { } o ? NumberToChinese.Price(o, store.ReadPriceAsChinese) : "");
            sb.Replace("{现价}", product.PromoPrice is { } p ? NumberToChinese.Price(p, store.ReadPriceAsChinese) : "");

            var save = product.OriginalPrice is { } o2 && product.PromoPrice is { } p2 && o2 > p2 ? o2 - p2 : (decimal?)null;
            sb.Replace("{立省}", save is { } s ? NumberToChinese.Price(s, store.ReadPriceAsChinese) : "");

            var rate = product.OriginalPrice is { } o3 && product.PromoPrice is { } p3 && o3 > 0 ? p3 / o3 : (decimal?)null;
            sb.Replace("{折扣}", rate is { } r ? NumberToChinese.Discount(r) : "");
        }

        var text = TokenRegex().Replace(sb.ToString(), m =>
        {
            var key = m.Groups[1].Value;
            return key switch
            {
                "商品" or "单位" or "原价" or "现价" or "立省" or "折扣" or "促销语" => "",
                _ => m.Value
            };
        });

        return Cleanup(text);
    }

    /// <summary>把标点、空格收拾干净：模板里变量为空时不会留下“，，”。</summary>
    private static string Cleanup(string s)
    {
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        s = Regex.Replace(s, @"([，。、；：！？])\1+", "$1");
        s = Regex.Replace(s, @"[，、；]\s*[，。、；]", "，");
        s = Regex.Replace(s, @"^[，。、；：\s]+", "");
        s = Regex.Replace(s, @"，。", "。");
        return s.Trim();
    }

    /// <summary>商品促销的默认播报词（用户没写模板时用）。</summary>
    public static string DefaultProductLine(ProductItem p, StoreProfile store)
    {
        var price = p.PromoPrice is { } pp ? NumberToChinese.Price(pp, store.ReadPriceAsChinese) : "";
        var original = p.OriginalPrice is { } op ? NumberToChinese.Price(op, store.ReadPriceAsChinese) : "";
        var sb = new StringBuilder();
        sb.Append("顾客朋友们请注意，本店");
        sb.Append(p.Name);
        if (!string.IsNullOrWhiteSpace(p.PromoText)) sb.Append('，').Append(p.PromoText.TrimEnd('，', '。'));
        sb.Append("，正在特惠");
        if (!string.IsNullOrEmpty(original)) sb.Append("，原价").Append(original);
        if (!string.IsNullOrEmpty(price)) sb.Append("，现价只要").Append(price);
        sb.Append("，每").Append(p.Unit).Append("，欢迎选购。");
        return Cleanup(sb.ToString());
    }

    public static string TimeOfDayGreeting(DateTime now) => now.Hour switch
    {
        < 6 => "凌晨好",
        < 11 => "早上好",
        < 13 => "中午好",
        < 18 => "下午好",
        _ => "晚上好"
    };

    public static string Money(decimal v, bool chinese) =>
        chinese ? NumberToChinese.Price(v, true) : v.ToString("0.##", CultureInfo.InvariantCulture);
}
