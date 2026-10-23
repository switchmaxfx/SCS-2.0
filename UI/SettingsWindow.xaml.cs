using System.Windows;
using System.Windows.Input;
using SCS.Core;

namespace SCS.UI;

public partial class SettingsWindow : Window
{
    public EngineSettings Result { get; private set; }
    public bool WikiAutoUpdate => WikiAutoCheck.IsChecked == true;
    private readonly Action<double> _previewOpacity;
    private readonly Func<string> _wikiStatus;
    private readonly Func<Task<string>> _wikiUpdate;
    private bool _loading;

    public SettingsWindow(EngineSettings settings, Action<double> previewOpacity,
        bool wikiAutoUpdate, Func<string> wikiStatus, Func<Task<string>> wikiUpdate)
    {
        InitializeComponent();
        Result = settings;
        _previewOpacity = previewOpacity;
        _wikiStatus = wikiStatus;
        _wikiUpdate = wikiUpdate;
        WikiAutoCheck.IsChecked = wikiAutoUpdate;
        WikiStatus.Text = wikiStatus();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Populate(settings);
    }

    private void Populate(EngineSettings s)
    {
        _loading = true;
        MenuKeyBox.Hotkey = s.MenuKey;
        ToggleKeyBox.Hotkey = s.ToggleHotkey;
        WasdCheck.IsChecked = s.UseWasd;
        BlockCheck.IsChecked = s.BlockTriggerKeys;
        FocusCheck.IsChecked = s.GameFocusOnly;
        ProcessBox.Text = s.GameProcess;
        OpacitySlider.Value = Math.Round(s.ReminderOpacity * 100);
        GuardSlider.Value = s.LagGuardFps;
        HoldSlider.Value = s.KeyHoldMs;
        GapSlider.Value = s.KeyGapMs;
        MenuSlider.Value = s.MenuDelayMs;
        _loading = false;
        UpdateReadout();
        OpacityValue.Text = $"{OpacitySlider.Value:0}%";
    }

    private EngineSettings Collect() => new()
    {
        MenuKey = string.IsNullOrWhiteSpace(MenuKeyBox.Hotkey) ? "ctrl" : MenuKeyBox.Hotkey,
        ToggleHotkey = ToggleKeyBox.Hotkey ?? "",
        UseWasd = WasdCheck.IsChecked == true,
        BlockTriggerKeys = BlockCheck.IsChecked == true,
        GameFocusOnly = FocusCheck.IsChecked == true,
        GameProcess = string.IsNullOrWhiteSpace(ProcessBox.Text) ? "helldivers2" : ProcessBox.Text.Trim(),
        ReminderOpacity = OpacitySlider.Value / 100.0,
        LagGuardFps = (int)GuardSlider.Value,
        KeyHoldMs = HoldSlider.Value,
        KeyGapMs = GapSlider.Value,
        MenuDelayMs = MenuSlider.Value,
    };

    private void Timing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading && IsInitialized) UpdateReadout();
    }

    private void UpdateReadout()
    {
        if (GuardValue == null) return;
        var s = Collect();
        GuardValue.Text = s.LagGuardFps == 0 ? "OFF" : $"{s.LagGuardFps} FPS";
        HoldValue.Text = $"{s.KeyHoldMs:0} ms";
        GapValue.Text = $"{s.KeyGapMs:0} ms";
        MenuValue.Text = $"{s.MenuDelayMs:0} ms";

        var (menu, hold, gap) = s.EffectiveTimings();
        EffectiveText.Text = $"hold {hold:0.0}   gap {gap:0.0}   menu {menu:0.0}  ms";
        double Seq(int n) => menu + n * (hold + gap);
        ExampleText.Text = $"Reinforce (5) ~{Seq(5):0} ms     Hellbomb (8) ~{Seq(8):0} ms     v1.4 held each key ~0 ms";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue == null) return;
        OpacityValue.Text = $"{e.NewValue:0}%";
        if (!_loading) _previewOpacity?.Invoke(e.NewValue / 100.0);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestResult.Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush");
        TestResult.Text = "measuring...";
        try
        {
            var (avg, worst) = await MacroEngine.MeasureTimingAsync(Collect());
            TestResult.Foreground = (System.Windows.Media.Brush)FindResource(worst < 2 ? "OkBrush" : "AccentBrush");
            TestResult.Text = $"avg error {avg:0.000} ms   worst {worst:0.000} ms";
        }
        catch (Exception ex)
        {
            TestResult.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            TestResult.Text = ex.Message;
        }
        finally { TestButton.IsEnabled = true; }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = new EngineSettings
        {
            // Keep the keys the user chose, reset only behaviour + timing.
            MenuKey = MenuKeyBox.Hotkey,
            ToggleHotkey = ToggleKeyBox.Hotkey,
        };
        Populate(d);
        _previewOpacity(d.ReminderOpacity);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var s = Collect();
        if (!string.IsNullOrEmpty(s.ToggleHotkey) && KeyNames.TryGetVk(s.ToggleHotkey, out var t)
            && KeyNames.TryGetVk(s.MenuKey, out var m) && t == m)
        {
            Dialog.Error(this, "Key conflict", "The master toggle can't be the same key as the stratagem menu key.");
            return;
        }
        Result = s;
        DialogResult = true;
    }

    private async void WikiUpdate_Click(object sender, RoutedEventArgs e)
    {
        WikiUpdateButton.IsEnabled = false;
        WikiUpdateButton.Content = "UPDATING...";
        WikiResult.Visibility = Visibility.Collapsed;
        try
        {
            var msg = await _wikiUpdate();
            WikiStatus.Text = _wikiStatus();
            WikiResult.Text = msg;
            WikiResult.Foreground = (System.Windows.Media.Brush)FindResource(msg.StartsWith("Couldn't") ? "DangerBrush" : "OkBrush");
            WikiResult.Visibility = Visibility.Visible;
        }
        finally
        {
            WikiUpdateButton.Content = "UPDATE NOW";
            WikiUpdateButton.IsEnabled = true;
        }
    }
}
