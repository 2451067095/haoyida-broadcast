using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Haoyida.Core;

/// <summary>极简 XLSX 读写（不依赖任何第三方库）：模板导出用 inlineStr，导入兼容 inlineStr / sharedStrings / 数字。</summary>
public static class ExcelKit
{
    public static readonly string[] Headers = { "商品名称", "分类", "单位", "原价", "促销价", "促销语" };

    public static void WriteTemplate(string path, IEnumerable<string[]> rows)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        Write(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");

        Write(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

        Write(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"" +
            " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
            "<sheet name=\"商品清单\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        Write(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        int rowIndex = 1;
        foreach (var row in new[] { Headers }.Concat(rows))
        {
            sb.Append($"<row r=\"{rowIndex}\">");
            for (int c = 0; c < row.Length; c++)
            {
                var cellRef = ColumnName(c) + rowIndex;
                var value = row[c] ?? "";
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                    sb.Append($"<c r=\"{cellRef}\"><v>{num.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(value)}</t></is></c>");
            }
            sb.Append("</row>");
            rowIndex++;
        }

        sb.Append("</sheetData></worksheet>");
        Write(zip, "xl/worksheets/sheet1.xml", sb.ToString());
    }

    /// <summary>读取表格：返回每行的单元格文本（跳过标题行）。</summary>
    public static List<string[]> ReadRows(string path)
    {
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ReadCsv(path);

        using var zip = ZipFile.OpenRead(path);
        var shared = ReadSharedStrings(zip);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase));

        if (sheet is null) throw new InvalidDataException("不是有效的 Excel 文件（缺少工作表）");

        var rows = new List<string[]>();
        using var stream = sheet.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true });

        var current = new SortedDictionary<int, string>();
        int maxCol = -1;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                current = new SortedDictionary<int, string>();
                maxCol = -1;
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                var cellRef = reader.GetAttribute("r") ?? "";
                var type = reader.GetAttribute("t") ?? "";
                int col = ColumnIndex(cellRef);
                string value = "";

                using (var sub = reader.ReadSubtree())
                {
                    string? v = null, inline = null;
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element) continue;
                        if (sub.LocalName == "v") v = sub.ReadElementContentAsString();
                        else if (sub.LocalName == "t") inline = sub.ReadElementContentAsString();
                    }

                    if (type == "inlineStr") value = inline ?? "";
                    else if (type == "s" && int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count) value = shared[idx];
                    else value = v ?? inline ?? "";
                }

                if (col >= 0)
                {
                    current[col] = value.Trim();
                    if (col > maxCol) maxCol = col;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
            {
                if (maxCol >= 0)
                {
                    var arr = new string[maxCol + 1];
                    for (int i = 0; i <= maxCol; i++) arr[i] = current.TryGetValue(i, out var s) ? s : "";
                    rows.Add(arr);
                }
            }
        }

        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                using var sub = reader.ReadSubtree();
                var sb = new StringBuilder();
                while (sub.Read())
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                        sb.Append(sub.ReadElementContentAsString());
                list.Add(sb.ToString());
            }
        }
        return list;
    }

    private static List<string[]> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        return lines.Select(l => l.Split(',')).ToList();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string ColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            name = (char)('A' + rem) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static int ColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (var ch in cellRef)
        {
            if (ch < 'A' || ch > 'Z') break;
            col = col * 26 + (ch - 'A' + 1);
        }
        return col - 1;
    }
}
