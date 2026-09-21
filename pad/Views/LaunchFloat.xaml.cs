using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// A small card that sits on the PAD window while a profile starts. Progress
/// does not live on the manage page.
/// </summary>
public partial class LaunchFloat : Window
{
    static LaunchFloat? _live;
    MainWindow? _host;
    ProfileVm? _profile;
    readonly CancellationTokenSource _cts = new();

    public LaunchFloat()
    {
        InitializeComponent();
    }

    public static void Open(MainWindow owner, ProfileVm profile)
    {
        var state = AppState.Current;
        if (LaunchPolicy.ShouldSkipDuplicateLaunch(
                state.Launcher.HarnessAlive(profile.Instance.Id, profile.Name)))
            return;
        if (_live is { IsVisible: true }) return;

        var w = new LaunchFloat
        {
            Owner = owner,
            _host = owner,
            _profile = profile,
        };
        _live = w;
        w.Show();
        w.Pin();
        owner.LocationChanged += w.OnHostMoved;
        owner.SizeChanged += w.OnHostMoved;
        owner.StateChanged += w.OnHostState;
        Fx.PulseIn(w.Card);
        w.Bounce();
        _ = w.Begin();
    }

    public async Task Begin()
    {
        var profile = _profile;
        var host = _host;
        if (profile is null || host is null) return;
        var state = AppState.Current;
        LaunchTitle.Text = $"启动 {profile.InstanceName} · {profile.Name}";
        LaunchMeter.Value = 0;
        Step(8, "核对 API Key");
        try
        {
            var key = CredentialsGate.Inspect(state.Launcher, profile.Instance, state.Settings);
            if (!key.Configured)
            {
                Step(0, "没有 DEEPSEEK_API_KEY");
                host.OpenSettingsApi();
                host.LaunchFailed(CredentialsFile.MissingError(key.Location).Render(), profile.Instance);
                Close();
                return;
            }
            Step(18, "把 API Key 写入实例");
            CredentialsGate.CopyDistributorToHome(state.Launcher, profile.Instance, state.Settings);
            Step(36, "准备 node");
            await state.Launcher.EnsureRuntimeNode(
                new Progress<string>(msg => Step(48, msg)),
                _cts.Token, profile.Instance.Dsh.Version);
            Step(58, "写启动脚本");
            UiWatchdog.QuietFor(TimeSpan.FromSeconds(LaunchPolicy.RunStartGraceSec));
            await Task.Run(() => state.Runner.Launch(profile.Instance, profile.Name, state.Settings), _cts.Token);
            Step(72, "打开终端");
            Step(86, "等待 Harness");
            await WaitAlive(profile, _cts.Token);
            Step(100, "已启动");
            LaunchTitle.Text = $"{profile.InstanceName} · {profile.Name} 在跑";
            state.SaveSelection();
            state.RefreshRunning();
            var ovAfter = profile.Instance.Launch?.AfterLaunch;
            var after = string.IsNullOrWhiteSpace(ovAfter) ? state.Settings.Launch.AfterLaunch : ovAfter!;
            switch (after)
            {
                case "minimize":
                    host.WindowState = WindowState.Minimized;
                    break;
                case "hide":
                    host.Hide();
                    break;
            }
            await Task.Delay(Fx.Animated ? 720 : 80, _cts.Token);
            Close();
        }
        catch (OperationCanceledException)
        {
            /* closed while waiting */
        }
        catch (Exception ex)
        {
            Step(LaunchMeter.Value, "启动失败");
            host.LaunchFailed(PadError.Describe(ex), profile.Instance);
            Close();
        }
    }

    void Bounce()
    {
        if (!Fx.Animated)
        {
            RingSpin.Angle = 0;
            return;
        }
        RingSpin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1100))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        Pulse(Dot0T, 0);
        Pulse(Dot1T, 110);
        Pulse(Dot2T, 220);
    }

    static void Pulse(TranslateTransform shift, int delay)
    {
        var anim = new DoubleAnimation(0, -7, TimeSpan.FromMilliseconds(380))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        shift.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    async Task WaitAlive(ProfileVm profile, CancellationToken ct)
    {
        var state = AppState.Current;
        var started = DateTimeOffset.UtcNow;
        while (!LaunchPolicy.LaunchWaitGiveUp(started, DateTimeOffset.UtcNow))
        {
            await Task.Delay(LaunchPolicy.LaunchWaitPollMs, ct);
            state.RefreshRunning();
            if (LaunchPolicy.LaunchHarnessReady(
                    state.Launcher.HarnessAlive(profile.Instance.Id, profile.Name)))
                return;
        }
        throw new PadError("PA025", "Harness did not start",
            $"instance `{profile.Instance.Id}`", profile.Name,
            ["看终端窗口", "到管理页打开日志"]);
    }

    void Step(double pct, string text)
    {
        LaunchStep.Text = text;
        LaunchPct.Text = (int)pct + "%";
        Fx.MeterTo(LaunchMeter, pct);
    }

    void Pin()
    {
        if (Owner is not Window host) return;
        Left = host.Left + (host.ActualWidth - Width) / 2;
        Top = host.Top + host.ActualHeight * 0.42 - Height / 2;
    }

    void OnHostMoved(object? sender, EventArgs e) => Pin();

    void OnHostState(object? sender, EventArgs e)
    {
        if (_host is null) return;
        Visibility = _host.WindowState == WindowState.Minimized
            ? Visibility.Collapsed : Visibility.Visible;
        if (IsVisible) Pin();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        if (_live == this) _live = null;
        if (_host is not null)
        {
            _host.LocationChanged -= OnHostMoved;
            _host.SizeChanged -= OnHostMoved;
            _host.StateChanged -= OnHostState;
        }
        base.OnClosed(e);
    }
}
