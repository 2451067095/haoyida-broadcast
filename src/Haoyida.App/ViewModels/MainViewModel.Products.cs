using System.IO;
using System.Windows;
using Haoyida.Core;

namespace Haoyida.ViewModels;

public sealed partial class MainViewModel
{
    public RelayCommand DownloadProductTemplateCommand { get; private set; } = null!;
    public RelayCommand ImportProductsCommand { get; private set; } = null!;

    private void InitProductExcel()
    {
        DownloadProductTemplateCommand = new RelayCommand(_ => DownloadTemplate());
        ImportProductsCommand = new RelayCommand(_ => ImportFromExcel());
    }

    private void DownloadTemplate()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存商品导入模板",
            FileName = "耗易达-商品导入模板.xlsx",
            Filter = "Excel 工作簿|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            ExcelKit.WriteTemplate(dialog.FileName, new[]
            {
                new[] { "红富士苹果", "水果", "斤", "6.98", "4.98", "脆甜多汁" },
                new[] { "本地小油菜", "蔬菜", "斤", "3.50", "1.99", "" }
            });

            MessageBox.Show("模板已保存：\n" + dialog.FileName +
                            "\n\n填写说明：\n• 商品名称必填，同名商品会被更新\n• 原价/促销价填数字，不要带￥\n• 单位可填 斤/袋/箱/个\n• 促销语可留空",
                "耗易达", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("生成模板失败：" + ex.Message, "耗易达");
        }
    }

    private void ImportFromExcel()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入促销商品",
            Filter = "Excel / CSV|*.xlsx;*.csv|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        int added = 0, updated = 0, skipped = 0;

        try
        {
            var rows = ExcelKit.ReadRows(dialog.FileName);
            if (rows.Count == 0)
            {
                MessageBox.Show("文件里没有内容。", "耗易达");
                return;
            }

            // 第一行是标题就跳过
            int start = 0;
            if (rows[0].Length > 0 && rows[0][0].Contains("商品"))
                start = 1;

            for (int i = start; i < rows.Count; i++)
            {
                var row = rows[i];
                string Cell(int idx) => idx < row.Length ? (row[idx] ?? "").Trim() : "";

                var name = Cell(0);
                if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

                decimal? ParsePrice(int idx) =>
                    decimal.TryParse(Cell(idx).Replace("￥", "").Replace("¥", "").Replace("元", "").Trim(),
                        out var v) ? v : null;

                var existing = Products.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    existing = new ProductItem { Name = name };
                    Products.Add(existing);
                    _store.Data.Products.Add(existing);
                    added++;
                }
                else
                {
                    updated++;
                }

                if (!string.IsNullOrWhiteSpace(Cell(1))) existing.Category = Cell(1);
                if (!string.IsNullOrWhiteSpace(Cell(2))) existing.Unit = Cell(2);
                existing.OriginalPrice = ParsePrice(3) ?? existing.OriginalPrice;
                existing.PromoPrice = ParsePrice(4) ?? existing.PromoPrice;
                if (!string.IsNullOrWhiteSpace(Cell(5))) existing.PromoText = Cell(5);
            }

            Save();
            Raise(nameof(Products));
            MessageBox.Show($"导入完成：新增 {added} 个，更新 {updated} 个，跳过空行 {skipped} 行。",
                "耗易达", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message + "\n\n请确认文件是软件下载的模板格式。", "耗易达");
        }
    }
}
