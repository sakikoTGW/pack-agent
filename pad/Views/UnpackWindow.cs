using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pad.Views;

/// <summary>整合包首次启动时解压依赖的小进度窗（代码构建，无 XAML）。</summary>
public sealed class UnpackWindow : Window
{
    readonly TextBlock _stage;
    readonly ProgressBar _bar;

    public UnpackWindow(int total)
    {
        Title = "PAD";
        Width = 520;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));

        _stage = new TextBlock
        {
            Text = "正在解压整合包的依赖…",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x2A, 0x33)),
            TextWrapping = TextWrapping.Wrap,
        };
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, total),
            Height = 6,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
            Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xEA)),
        };
        var panel = new StackPanel();
        panel.Children.Add(_stage);
        panel.Children.Add(_bar);
        Content = new Border
        {
            Padding = new Thickness(22, 20, 22, 20),
            Child = panel,
        };
    }

    public void Tick(string text, long done, int total)
    {
        _stage.Text = text;
        _bar.Maximum = Math.Max(1, total);
        _bar.Value = Math.Min(_bar.Maximum, done);
        Pump();
    }

    /// <summary>解压在工作线程上做，这里手动跑一帧 Dispatcher，让窗口能重绘。</summary>
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
