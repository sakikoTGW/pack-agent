using System.Windows;
using System.Windows.Controls;

namespace Pad.Views;

/// <summary>
/// 「在跑」指示灯：绿色呼吸灯（闪 + 缩放 + 轻抖），不写「在跑 / pid」这类字。
/// 绑定 <see cref="Running"/>；没在跑就整盏收起（连灰点都不留）。
/// </summary>
public partial class RunLamp : UserControl
{
    public static readonly DependencyProperty RunningProperty = DependencyProperty.Register(
        nameof(Running), typeof(bool), typeof(RunLamp),
        new PropertyMetadata(false, (d, _) => ((RunLamp)d).Sync()));

    public bool Running
    {
        get => (bool)GetValue(RunningProperty);
        set => SetValue(RunningProperty, value);
    }

    public RunLamp()
    {
        InitializeComponent();
        Sync();
    }

    void Sync() => Visibility = Running ? Visibility.Visible : Visibility.Collapsed;
}
