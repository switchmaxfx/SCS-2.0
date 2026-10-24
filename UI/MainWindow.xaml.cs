using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SCS.Core;

namespace SCS.UI;

public partial class MainWindow : Window
{
    private readonly MacroEngine _engine = new();
    private readonly ObservableCollection<SlotViewModel> _slots = new();
    private readonly AppState _state = AppState.Load();
    private EngineSettings _settings = new();
    private string? _profileName;        // null = unsaved new profile
    private bool _dirty;
    private bool _suppressProfileEvents;
    private int _sentCount, _droppedCount;
    private OverlayWindow? _overlay;
    private LedWindow? _led;
    private readonly Storyboard _pulse;
    private DateTime? _lastWikiFetch;
    private bool _wikiBusy;

    public MainWindow()
    {
        InitializeComponent();
        _pulse = (Storyboard)FindResource("PulseStoryboard");

        // Codes saved from the last wiki update, merged over the built-in list before any profile loads.
        var wikiCache = WikiStratagems.LoadCache();
        if (wikiCache != null) Stratagems.ApplyWiki(wikiCache.Entries);
        _lastWikiFetch = wikiCache?.FetchedUtc;
        Stratagems.Changed += () => Dispatcher.InvokeAsync(OnCatalogChanged);

        for (int i = 0; i < Profile.SlotCount; i++)
        {
            var vm = new SlotViewModel(i);
            vm.PropertyChanged += Slot_PropertyChanged;
            _slots.Add(vm);
        }
        SlotsList.ItemsSource = _slots;

        // Engine events arrive on the hook/executor threads. BeginInvoke = fire and forget,
        // so the input threads never wait on the UI.
        _engine.ToggleRequested += () => Dispatcher.InvokeAsync(() => ToggleArmed(fromHotkey: true));
        _engine.ExecutionStarted += name => Dispatcher.InvokeAsync(() => OnExecutionStarted(name), DispatcherPriority.Background);
        _engine.ExecutionFinished += (name, ms, jitter) => Dispatcher.InvokeAsync(() => OnExecutionFinished(name, ms, jitter), DispatcherPriority.Background);
        _engine.ExecutionDropped += name => Dispatcher.InvokeAsync(() =>
        {
            _droppedCount++;
            SetStatus("QUEUE FULL", $"skipped {name.ToUpperInvariant()} (already two queued)", "DangerBrush");
        }, DispatcherPriority.Background);
        _engine.Start();

        Loaded += OnLoaded;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshProfileList();
        if (_state.LastProfile != null && File.Exists(Profile.PathFor(_state.LastProfile)))
            SelectProfileInCombo(_state.LastProfile);
        else
            ApplyProfile(new Profile(), null);

        if (_state.OverlayVisible) Overlay_Click(this, new RoutedEventArgs());
        if (_state.LedVisible) Led_Click(this, new RoutedEventArgs());
        UpdateArmedVisuals();

        // Daily background check for new or changed stratagem codes.
        if (_state.WikiAutoUpdate && (_lastWikiFetch == null || DateTime.UtcNow - _lastWikiFetch > WikiStratagems.AutoUpdateInterval))
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += async (_, _) => { t.Stop(); await UpdateFromWikiAsync(manual: false); };
            t.Start();
        }
    }

    // ================================================================ profiles

    private void RefreshProfileList()
    {
        _suppressProfileEvents = true;
        ProfileCombo.Items.Clear();
        foreach (var n in Profile.ListNames()) ProfileCombo.Items.Add(n);
        if (_profileName == null) ProfileCombo.SelectedIndex = -1;
        else ProfileCombo.SelectedItem = ProfileCombo.Items.Cast<string>().FirstOrDefault(x => x.Equals(_profileName, StringComparison.OrdinalIgnoreCase));
        _suppressProfileEvents = false;
        UpdateTitle();
    }

    private void SelectProfileInCombo(string name)
    {
        var item = ProfileCombo.Items.Cast<string>().FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item != null) ProfileCombo.SelectedItem = item; // triggers load
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || ProfileCombo.SelectedItem is not string name) return;
        if (name.Equals(_profileName, StringComparison.OrdinalIgnoreCase)) return;

        if (_dirty && !Dialog.Confirm(this, "Unsaved changes", $"Discard changes to {_profileName ?? "this profile"} and load {name}?", "DISCARD"))
        {
            RefreshProfileList();
            return;
        }
        try
        {
            ApplyProfile(Profile.Load(Profile.PathFor(name)), name);
        }
        catch (Exception ex)
        {
            Dialog.Error(this, "Load failed", ex.Message);
            RefreshProfileList();
        }
    }

    private void ApplyProfile(Profile p, string? name)
    {
        _settings = p.Settings;
        foreach (var vm in _slots)
        {
            var sc = vm.Index < p.Slots.Count ? p.Slots[vm.Index] : new SlotConfig();
            vm.PropertyChanged -= Slot_PropertyChanged;
            vm.Stratagem = sc.Stratagem;
            vm.Hotkey = sc.Hotkey;
            vm.PropertyChanged += Slot_PropertyChanged;
        }
        _profileName = name;
        _dirty = false;
        _state.LastProfile = name;
        _state.Save();
        OnConfigChanged();
        // Deferred: this can run inside the combo's SelectionChanged, where rebuilding its items is unsafe.
        Dispatcher.InvokeAsync(RefreshProfileList);
    }

    private Profile BuildProfile()
    {
        var p = new Profile { Settings = _settings.Clone() };
        foreach (var vm in _slots)
            p.Slots[vm.Index] = new SlotConfig { Stratagem = vm.IsEmpty ? "" : vm.Stratagem, Hotkey = vm.Hotkey };
        return p;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !Dialog.Confirm(this, "Unsaved changes", "Discard current changes and start a blank profile?", "DISCARD")) return;
        ApplyProfile(new Profile(), null);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) return;
        if (_profileName == null) { SaveAs_Click(sender, e); return; }
        SaveTo(_profileName);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) return;
        var name = Dialog.Prompt(this, "Save profile", "Profile name:", _profileName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (File.Exists(Profile.PathFor(name)) && !name.Equals(_profileName, StringComparison.OrdinalIgnoreCase)
            && !Dialog.Confirm(this, "Overwrite", $"A profile named {name} already exists. Overwrite it?", "OVERWRITE", danger: true))
            return;
        SaveTo(name);
    }

    private void SaveTo(string name)
    {
        try
        {
            BuildProfile().Save(Profile.PathFor(name));
            _profileName = name;
            _dirty = false;
            _state.LastProfile = name;
            _state.Save();
            RefreshProfileList();
            SetStatus("SAVED", name.ToUpperInvariant(), "OkBrush");
        }
        catch (Exception ex) { Dialog.Error(this, "Save failed", ex.Message); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_profileName == null || _engine.IsRunning) return;
        if (!Dialog.Confirm(this, "Delete profile", $"Permanently delete {_profileName}?", "DELETE", danger: true)) return;
        try
        {
            File.Delete(Profile.PathFor(_profileName));
            ApplyProfile(new Profile(), null);
        }
        catch (Exception ex) { Dialog.Error(this, "Delete failed", ex.Message); }
    }

    private void UpdateTitle()
    {
        UnsavedHint.Visibility = ProfileCombo.SelectedIndex < 0 ? Visibility.Visible : Visibility.Collapsed;
        ModifiedText.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ slots

    private void Slot_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlotViewModel.Hotkey) or nameof(SlotViewModel.Stratagem))
        {
            _dirty = true;
            OnConfigChanged();
            UpdateTitle();
        }
    }

    private void SlotPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm }) OpenPicker(vm);
    }

    private void SlotMenuChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm }) OpenPicker(vm);
    }

    private void SlotMenuClearKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm }) vm.Hotkey = "";
    }

    private void SlotMenuClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm }) { vm.Stratagem = ""; vm.Hotkey = ""; }
    }

    private void OpenPicker(SlotViewModel vm)
    {
        if (_engine.IsRunning) return;
        var picker = new PickerWindow(vm.Stratagem, vm.Number) { Owner = this };
        if (picker.ShowDialog() == true) vm.Stratagem = picker.Selected ?? "";
    }

    /// <summary>Validates conflicts, keeps the master toggle live, refreshes overlay + timing readout.</summary>
    private void OnConfigChanged()
    {
        var counts = new Dictionary<int, int>();
        KeyNames.TryGetVk(_settings.MenuKey, out var menuVk);
        KeyNames.TryGetVk(_settings.ToggleHotkey, out var toggleVk);
        foreach (var vm in _slots)
            if (KeyNames.TryGetVk(vm.Hotkey, out var vk)) counts[vk] = counts.GetValueOrDefault(vk) + 1;
        foreach (var vm in _slots)
        {
            vm.IsConflict = KeyNames.TryGetVk(vm.Hotkey, out var vk)
                            && (counts[vk] > 1 || vk == menuVk || (toggleVk != 0 && vk == toggleVk));
        }

        if (!_engine.IsRunning) _engine.SetToggleOnly(_settings);
        _overlay?.SetRows(ActiveRows());
        _overlay?.SetOpacity(_settings.ReminderOpacity);

        var (menu, hold, gap) = _settings.EffectiveTimings();
        TimingText.Text = $"HOLD {hold:0} / GAP {gap:0} / MENU {menu:0} ms" + (_settings.LagGuardFps > 0 ? $"   GUARD {_settings.LagGuardFps} FPS" : "");
    }

    private List<OverlayRow> ActiveRows() =>
        _slots.Where(s => !s.IsEmpty && !string.IsNullOrWhiteSpace(s.Hotkey))
              .Select(s => new OverlayRow(KeyNames.Display(s.Hotkey), s.Stratagem, s.Code))
              .ToList();

    // ================================================================ arm / disarm

    private void Arm_Click(object sender, RoutedEventArgs e) => ToggleArmed(fromHotkey: false);

    private void ToggleArmed(bool fromHotkey)
    {
        if (_engine.IsRunning)
        {
            _engine.SetRunning(false);
            UpdateArmedVisuals();
            return;
        }

        var err = _engine.Load(BuildProfile());
        if (err == null && _engine.ActiveBindingCount == 0) err = "No slots have both a stratagem and a hotkey.";
        if (err != null)
        {
            SetStatus("CANNOT ARM", err, "DangerBrush");
            if (!fromHotkey) Dialog.Error(this, "Cannot arm", err);
            return;
        }
        _engine.SetRunning(true);
        UpdateArmedVisuals();
    }

    private void UpdateArmedVisuals()
    {
        bool on = _engine.IsRunning;
        ArmButton.Style = on ? (Style)FindResource("PrimaryButton") : (Style)FindResource(typeof(Button));
        ArmText.Text = on ? "DISARM" : "ARM";
        ArmDot.Fill = on ? new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10)) : (Brush)FindResource("AccentBrush");

        ProfileBar.IsEnabled = !on;
        SettingsButton.IsEnabled = !on;
        SlotsList.IsHitTestVisible = !on;
        SlotsList.Opacity = on ? 0.92 : 1;

        StatusLed.Fill = on ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x5A, 0x1A, 0x20));
        if (on) _pulse.Begin(this, true); else { _pulse.Stop(this); StatusLedGlow.Opacity = 0; }

        var label = (_profileName ?? "unsaved profile").ToUpperInvariant();
        if (on) SetStatus("ARMED", $"{label}   {_engine.ActiveBindingCount} bindings live", "AccentBrush");
        else SetStatus("STANDBY", label, "TextDimBrush");

        _led?.SetArmed(on);
        _overlay?.SetArmed(on);
    }

    private void SetStatus(string text, string detail, string brushKey)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(brushKey);
        StatusDetail.Text = detail;
    }

    // ================================================================ execution feedback

    private SlotViewModel? _flashing;
    private DispatcherTimer? _flashTimer;

    private void OnExecutionStarted(string name)
    {
        SetStatus("EXECUTING", name.ToUpperInvariant(), "AccentBrush");
        if (_flashing != null) _flashing.IsFlashing = false;
        _flashing = _slots.FirstOrDefault(s => s.Stratagem == name);
        if (_flashing != null) _flashing.IsFlashing = true;
        _overlay?.Flash(name);
    }

    private void OnExecutionFinished(string name, double ms, double jitter)
    {
        _sentCount++;
        LastText.Text = Fmt.Ms(ms);
        JitterText.Text = jitter.ToString("0.00") + " ms";
        CountText.Text = _droppedCount > 0 ? $"{_sentCount} ({_droppedCount} skipped)" : _sentCount.ToString();
        if (_engine.IsRunning)
            SetStatus("ARMED", $"{name.ToUpperInvariant()} sent in {Fmt.Ms(ms)}", "AccentBrush");

        _flashTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) =>
        {
            if (_flashing != null) _flashing.IsFlashing = false;
            _flashing = null;
            _flashTimer!.Stop();
        }, Dispatcher);
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    // ================================================================ overlays, settings, help

    private void Overlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay == null)
        {
            _overlay = new OverlayWindow(_state);
            _overlay.SetRows(ActiveRows());
            _overlay.SetOpacity(_settings.ReminderOpacity);
            _overlay.SetArmed(_engine.IsRunning);
            _overlay.Show();
        }
        else if (_overlay.IsVisible) _overlay.Hide();
        else _overlay.Show();
        _state.OverlayVisible = _overlay.IsVisible;
        _state.Save();
    }

    private void Led_Click(object sender, RoutedEventArgs e)
    {
        if (_led == null)
        {
            _led = new LedWindow(_state);
            _led.SetArmed(_engine.IsRunning);
            _led.Show();
        }
        else if (_led.IsVisible) _led.Hide();
        else _led.Show();
        _state.LedVisible = _led.IsVisible;
        _state.Save();
    }

    // ================================================================ wiki stratagem data

    public string WikiStatusText()
    {
        if (_lastWikiFetch is not { } t) return $"Using the built-in list ({Stratagems.All.Count} stratagems). Not updated from the wiki yet.";
        return $"Updated from the wiki {t.ToLocalTime():MMM d, h:mm tt}. {Stratagems.All.Count} stratagems available.";
    }

    /// <summary>Fetch codes from the wiki, save them, and merge them in. Returns a one-line result.</summary>
    public async Task<string> UpdateFromWikiAsync(bool manual)
    {
        if (_wikiBusy) return "An update is already running.";
        _wikiBusy = true;
        if (manual || !_engine.IsRunning) SetStatus("UPDATING", "getting stratagem codes from the wiki", "InfoBrush");
        try
        {
            var cache = await Task.Run(() => WikiStratagems.FetchAsync());
            try { WikiStratagems.SaveCache(cache); } catch { /* read-only folder: still use it for this session */ }
            _lastWikiFetch = cache.FetchedUtc;
            var r = Stratagems.ApplyWiki(cache.Entries);

            string msg;
            if (r.Added == 0 && r.CodesChanged == 0) msg = $"Stratagem list is up to date ({r.Total} stratagems).";
            else
            {
                var parts = new List<string>();
                if (r.Added > 0) parts.Add($"{r.Added} new: {string.Join(", ", r.AddedNames.Take(4))}{(r.Added > 4 ? "..." : "")}");
                if (r.CodesChanged > 0) parts.Add($"{r.CodesChanged} code{(r.CodesChanged == 1 ? "" : "s")} changed: {string.Join(", ", r.ChangedNames.Take(4))}{(r.CodesChanged > 4 ? "..." : "")}");
                msg = string.Join("   ", parts);
            }
            if (manual || !_engine.IsRunning || r.Added + r.CodesChanged > 0) SetStatus("WIKI SYNCED", msg, "OkBrush");

            // Icons for stratagems the exe doesn't already carry an icon for (new ones since this build).
            var needIcons = cache.Entries.Where(en => en.Icon != null && !IconStore.HasBundled(en.Title)).ToList();
            if (needIcons.Count > 0)
            {
                try
                {
                    var got = await Task.Run(() => WikiStratagems.FetchIconsAsync(needIcons));
                    if (got > 0)
                    {
                        IconStore.Invalidate();
                        msg += $"   {got} new icon{(got == 1 ? "" : "s")}.";
                    }
                }
                catch { /* icons are cosmetic; codes are already updated */ }
            }
            return msg;
        }
        catch (Exception ex)
        {
            var msg = "Couldn't update from the wiki, so the saved list is still in use. " + Describe(ex);
            if (manual) SetStatus("WIKI OFFLINE", "couldn't reach the wiki, using the saved list", "DangerBrush");
            else if (!_engine.IsRunning) UpdateArmedVisuals();
            return msg;
        }
        finally { _wikiBusy = false; }
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "(The wiki took too long to answer.)",
        System.Net.Http.HttpRequestException h => $"({h.Message})",
        _ => $"({ex.Message})"
    };

    /// <summary>The stratagem list changed: re-resolve slot names, refresh cards, and re-arm with new codes.</summary>
    private void OnCatalogChanged()
    {
        foreach (var vm in _slots)
        {
            var canonical = Stratagems.Find(vm.Stratagem)?.Name;
            if (canonical != null && canonical != vm.Stratagem)
            {
                vm.PropertyChanged -= Slot_PropertyChanged;
                vm.Stratagem = canonical;
                vm.PropertyChanged += Slot_PropertyChanged;
            }
            vm.Refresh();
        }
        OnConfigChanged();
        if (_engine.IsRunning) _engine.Load(BuildProfile()); // atomic swap, safe while armed
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) return;
        var original = _settings.Clone();
        var dlg = new SettingsWindow(_settings.Clone(), op => _overlay?.SetOpacity(op),
            _state.WikiAutoUpdate, WikiStatusText, () => UpdateFromWikiAsync(manual: true)) { Owner = this };
        var ok = dlg.ShowDialog() == true;
        if (ok && dlg.WikiAutoUpdate != _state.WikiAutoUpdate)
        {
            _state.WikiAutoUpdate = dlg.WikiAutoUpdate;
            _state.Save();
        }
        if (ok)
        {
            _settings = dlg.Result;
            _dirty = true;
            UpdateTitle();
        }
        else _settings = original;
        OnConfigChanged();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        Dialog.Info(this, "How it works",
            "1.  Click a card to choose a stratagem, then click its key box and press the key or mouse button that should trigger it.\n\n" +
            "2.  Press ARM (or your master toggle key). While armed, pressing a bound key plays that stratagem's code.\n\n" +
            "3.  In Helldivers 2, set the stratagem directions to the arrow keys (or enable WASD mode in Settings). " +
            "The stratagem menu key must match the one in Settings (default Left Ctrl).\n\n" +
            "4.  If inputs still drop in heavy fights, lower the Lag Guard FPS in Settings to the lowest frame rate you see. " +
            "Every arrow is then held for at least one full frame so the game cannot miss it.\n\n" +
            "5.  If the game runs as administrator, run S.C.S as administrator too, or Windows will block the input.\n\n" +
            "Right-click a card for more options. Ctrl+S saves the profile.");
    }

    // ================================================================ window chrome

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty && !Dialog.Confirm(this, "Unsaved changes", "Close without saving changes?", "CLOSE"))
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.SetRunning(false);
        _engine.Dispose();
        _overlay?.SaveAndClose();
        _led?.SaveAndClose();
        _state.Save();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
