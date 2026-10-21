using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SCS.Core;

/// <summary>
/// The input engine. Three threads, each with one job:
///   Hook thread      - sees every key/mouse event, does an O(1) lookup, pushes a slot index. Never blocks.
///   Executor thread  - pops slot indexes and plays the sequences with precise timing via SendInput.
///   UI thread        - only ever receives fire-and-forget notifications.
///
/// Fixes compared to v1.4:
///  * Every arrow is HELD for a real duration (v1.4 sent down+up back-to-back via pydirectinput.press).
///    Helldivers 2 samples key state once per frame; at 35 FPS a frame is ~29 ms, so a 0 ms tap that
///    lands between two samples simply never happens as far as the game is concerned.
///  * Lag Guard stretches hold/gap/menu-delay to at least one frame at your worst-case FPS.
///  * The menu key is sent as a hardware scancode too (v1.4 used pyautogui's virtual-key path for it).
///  * Executions are serialized on one thread. v1.4 started a new thread per press, so two quick presses
///    interleaved their arrows and one released Ctrl in the middle of the other.
///  * Auto-repeat from a held hotkey is ignored instead of queueing a pile of executions.
///  * Our own injected keys are tagged and ignored by the hook, so they can never re-trigger a hotkey.
///  * Every key is guaranteed to be released (try/finally), even if something throws.
/// </summary>
public sealed class MacroEngine : IDisposable
{
    private sealed class Binding
    {
        public required string Name;
        public required ushort[] Arrows; // scancodes
        public required bool[] ArrowExt;
        public required int[] ArrowVk;
    }

    public event Action<bool>? RunningChanged;          // raised on hook/UI thread; UI must marshal
    public event Action? ToggleRequested;
    public event Action<string>? ExecutionStarted;
    public event Action<string, double, double>? ExecutionFinished; // name, total ms, worst timing error ms
    public event Action<string>? ExecutionDropped;

    private readonly InputHook _hook;
    private readonly Thread _executor;
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new();

    // Hot-path state. Replaced atomically as whole arrays, never mutated in place while running.
    private volatile int[] _slotForVk = NewMap();
    private volatile Binding?[] _bindings = Array.Empty<Binding?>();
    private volatile int _toggleVk;
    private volatile bool _running;
    private volatile bool _busy;
    private volatile EngineSettings _settings = new();
    private ushort _menuScan;
    private bool _menuExt;
    private readonly bool[] _physDown = new bool[256];
    private readonly bool[] _swallowed = new bool[256];
    private readonly int[] _restoreBuf = new int[4];

    private const int MaxPending = 2;

    // Foreground-process cache so the focus check costs nothing on repeat.
    private uint _lastPid;
    private bool _lastPidIsGame;

    public MacroEngine()
    {
        _hook = new InputHook(OnInput);
        _executor = new Thread(ExecutorLoop)
        {
            IsBackground = true,
            Name = "SCS.Executor",
            Priority = ThreadPriority.Highest
        };
    }

    public bool IsRunning => _running;
    public bool IsBusy => _busy;

    public void Start()
    {
        _hook.Start();
        _executor.Start();
    }

    /// <summary>Compile the profile into flat lookup tables. Returns null on success, or an error string.</summary>
    public string? Load(Profile profile)
    {
        var s = profile.Settings.Clone();
        var map = NewMap();
        var bindings = new Binding?[Profile.SlotCount];
        var seen = new HashSet<int>();

        if (!KeyNames.TryGetVk(s.MenuKey, out var menuVk))
            return $"Unknown stratagem menu key '{s.MenuKey}'.";
        int toggleVk = 0;
        if (!string.IsNullOrWhiteSpace(s.ToggleHotkey) && !KeyNames.TryGetVk(s.ToggleHotkey, out toggleVk))
            return $"Unknown master toggle key '{s.ToggleHotkey}'.";

        int[] arrowVks = s.UseWasd
            ? new[] { (int)'W', 'S', 'A', 'D' }
            : new[] { KeyNames.VK_UP, KeyNames.VK_DOWN, KeyNames.VK_LEFT, KeyNames.VK_RIGHT };

        for (int i = 0; i < Profile.SlotCount && i < profile.Slots.Count; i++)
        {
            var slot = profile.Slots[i];
            var strat = Stratagems.Find(slot.Stratagem);
            if (strat == null || string.IsNullOrWhiteSpace(slot.Hotkey)) continue;
            if (!KeyNames.TryGetVk(slot.Hotkey, out var vk))
                return $"Slot {i + 1}: unknown key '{slot.Hotkey}'.";
            if (!seen.Add(vk)) return $"Key '{KeyNames.Display(slot.Hotkey)}' is bound more than once.";
            if (vk == toggleVk) return $"Key '{KeyNames.Display(slot.Hotkey)}' is also the master toggle.";
            if (vk == menuVk) return $"Key '{KeyNames.Display(slot.Hotkey)}' is also the stratagem menu key.";

            var n = strat.Code.Length;
            var b = new Binding { Name = strat.Name, Arrows = new ushort[n], ArrowExt = new bool[n], ArrowVk = new int[n] };
            for (int k = 0; k < n; k++)
            {
                int avk = strat.Code[k] switch { 'U' => arrowVks[0], 'D' => arrowVks[1], 'L' => arrowVks[2], _ => arrowVks[3] };
                b.ArrowVk[k] = avk;
                b.Arrows[k] = Scan(avk);
                b.ArrowExt[k] = KeyNames.IsExtended(avk);
            }
            bindings[i] = b;
            map[vk & 0xFF] = i;
        }

        _menuScan = Scan(menuVk);
        _menuExt = KeyNames.IsExtended(menuVk);
        _lastPid = 0; // process name may have changed
        _settings = s;
        _bindings = bindings;
        _slotForVk = map;
        _toggleVk = toggleVk;
        return null;
    }

    /// <summary>Only update the toggle key / settings (used while stopped so the master toggle always works).</summary>
    public void SetToggleOnly(EngineSettings s)
    {
        _toggleVk = KeyNames.TryGetVk(s.ToggleHotkey, out var vk) ? vk : 0;
    }

    public void SetRunning(bool on)
    {
        if (on) _hook.Rearm();
        _running = on;
        if (!on) while (_queue.TryDequeue(out _)) { }
        RunningChanged?.Invoke(on);
    }

    public int ActiveBindingCount => _bindings.Count(b => b != null);

    // ------------------------------------------------------------------ hook thread

    private bool OnInput(int vk, bool isDown, bool isRepeat)
    {
        _physDown[vk] = isDown;

        if (!isDown)
        {
            // Swallow the release of anything whose press we swallowed, so the game sees neither.
            if (_swallowed[vk]) { _swallowed[vk] = false; return true; }
            return false;
        }

        bool block = _settings.BlockTriggerKeys && !(vk is KeyNames.VK_LBUTTON or KeyNames.VK_RBUTTON);

        if (vk == _toggleVk && _toggleVk != 0)
        {
            if (!isRepeat) ToggleRequested?.Invoke();
            if (block) { _swallowed[vk] = true; return true; }
            return false;
        }

        if (!_running) return false;
        int slot = _slotForVk[vk];
        if (slot < 0) return false;

        if (_settings.GameFocusOnly && !IsGameFocused()) return false;

        if (!isRepeat)
        {
            if (_queue.Count >= MaxPending)
            {
                var b = _bindings[slot];
                if (b != null) ExecutionDropped?.Invoke(b.Name);
            }
            else
            {
                _queue.Enqueue(slot);
                _signal.Release();
            }
        }

        if (block) { _swallowed[vk] = true; return true; }
        return false;
    }

    private bool IsGameFocused()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _lastPid) return _lastPidIsGame;
        _lastPid = pid;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            _lastPidIsGame = string.Equals(p.ProcessName, _settings.GameProcess.Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { _lastPidIsGame = false; }
        return _lastPidIsGame;
    }

    // ------------------------------------------------------------------ executor thread

    private readonly Native.INPUT[] _one = new Native.INPUT[1];
    private static readonly int InputSize = Marshal.SizeOf<Native.INPUT>();

    private void ExecutorLoop()
    {
        Native.timeBeginPeriod(1);
        uint taskIndex = 0;
        IntPtr mmcss = IntPtr.Zero;
        try { mmcss = Native.AvSetMmThreadCharacteristics("Games", ref taskIndex); } catch { }

        using var timer = new PreciseTimer();
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _signal.Wait(ct);
                if (!_queue.TryDequeue(out var slot)) continue;
                if (!_running) continue;
                var b = _bindings[slot];
                if (b == null) continue;
                Execute(b, timer);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (mmcss != IntPtr.Zero) Native.AvRevertMmThreadCharacteristics(mmcss);
            Native.timeEndPeriod(1);
        }
    }

    private void Execute(Binding b, PreciseTimer timer)
    {
        var s = _settings;
        var (menuMs, holdMs, gapMs) = s.EffectiveTimings();
        long menuT = PreciseTimer.FromMs(menuMs), holdT = PreciseTimer.FromMs(holdMs), gapT = PreciseTimer.FromMs(gapMs);
        ushort menuScan = _menuScan;
        bool menuExt = _menuExt;

        _busy = true;
        ExecutionStarted?.Invoke(b.Name);

        long start = PreciseTimer.Now;
        long worstErr = 0;
        int pressedArrow = -1;
        bool menuDown = false;
        try
        {
            Send(menuScan, menuExt, down: true);
            menuDown = true;
            long next = PreciseTimer.Now + menuT;
            timer.WaitUntil(next);

            for (int i = 0; i < b.Arrows.Length; i++)
            {
                worstErr = Math.Max(worstErr, PreciseTimer.Now - next);
                Send(b.Arrows[i], b.ArrowExt[i], down: true);
                pressedArrow = i;
                next = PreciseTimer.Now + holdT;
                timer.WaitUntil(next);

                worstErr = Math.Max(worstErr, PreciseTimer.Now - next);
                Send(b.Arrows[i], b.ArrowExt[i], down: false);
                pressedArrow = -1;
                next = PreciseTimer.Now + gapT;
                timer.WaitUntil(next);
            }
        }
        finally
        {
            if (pressedArrow >= 0) Send(b.Arrows[pressedArrow], b.ArrowExt[pressedArrow], down: false);
            if (menuDown) Send(menuScan, menuExt, down: false);

            // If you were holding a movement key we touched (WASD mode), give it back to you.
            var restored = _restoreBuf;
            int nRestored = 0;
            for (int i = 0; i < b.ArrowVk.Length; i++)
            {
                int vk = b.ArrowVk[i];
                if (!_physDown[vk] || Array.IndexOf(restored, vk, 0, nRestored) >= 0 || nRestored == 4) continue;
                restored[nRestored++] = vk;
                Send(b.Arrows[i], b.ArrowExt[i], down: true);
            }
            _busy = false;
        }

        double total = PreciseTimer.ToMs(PreciseTimer.Now - start);
        ExecutionFinished?.Invoke(b.Name, total, PreciseTimer.ToMs(worstErr));
    }

    private void Send(ushort scan, bool extended, bool down)
    {
        uint flags = Native.KEYEVENTF_SCANCODE;
        if (extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= Native.KEYEVENTF_KEYUP;
        _one[0].type = Native.INPUT_KEYBOARD;
        _one[0].U.ki = new Native.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = Native.ScsSignature };
        Native.SendInput(1, _one, InputSize);
    }

    private static ushort Scan(int vk) => (ushort)Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC);

    private static int[] NewMap()
    {
        var m = new int[256];
        Array.Fill(m, -1);
        return m;
    }

    /// <summary>
    /// Timing self-test: performs the waits of a full 9-input sequence without sending any keys,
    /// on a thread configured exactly like the executor. Returns (average error, worst error) in ms.
    /// </summary>
    public static Task<(double avg, double worst)> MeasureTimingAsync(EngineSettings s, int rounds = 5)
    {
        var tcs = new TaskCompletionSource<(double, double)>();
        var t = new Thread(() =>
        {
            Native.timeBeginPeriod(1);
            uint idx = 0; IntPtr mm = IntPtr.Zero;
            try { mm = Native.AvSetMmThreadCharacteristics("Games", ref idx); } catch { }
            try
            {
                using var timer = new PreciseTimer();
                var (menu, hold, gap) = s.EffectiveTimings();
                double sum = 0, worst = 0; int n = 0;
                for (int r = 0; r < rounds; r++)
                {
                    foreach (var ms in new[] { menu }.Concat(Enumerable.Repeat(new[] { hold, gap }, 9).SelectMany(x => x)))
                    {
                        long target = PreciseTimer.Now + PreciseTimer.FromMs(ms);
                        timer.WaitUntil(target);
                        double err = Math.Abs(PreciseTimer.ToMs(PreciseTimer.Now - target));
                        sum += err; worst = Math.Max(worst, err); n++;
                    }
                }
                tcs.SetResult((sum / n, worst));
            }
            catch (Exception ex) { tcs.SetException(ex); }
            finally
            {
                if (mm != IntPtr.Zero) Native.AvRevertMmThreadCharacteristics(mm);
                Native.timeEndPeriod(1);
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest };
        t.Start();
        return tcs.Task;
    }

    public void Dispose()
    {
        _running = false;
        _cts.Cancel();
        _hook.Dispose();
        _executor.Join(500);
    }
}
