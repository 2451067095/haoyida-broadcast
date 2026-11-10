using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Haoyida.Views;

public partial class ComplianceWindow : Window
{
    public ComplianceWindow() => InitializeComponent();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开链接失败：" + ex.Message, "耗易达");
        }
    }
}
