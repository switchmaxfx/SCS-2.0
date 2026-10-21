namespace SCS.Core;

/// <summary>
/// Global low-level keyboard + mouse hooks running on their own high-priority thread with a bare
/// Win32 message loop (no UI framework in the way).
///
/// Why this matters: Windows calls a low-level hook synchronously for EVERY input event system-wide.
/// If the callback doesn't return within LowLevelHooksTimeout (~300 ms, less on newer builds),
/// Windows skips it, and after repeated timeouts silently unhooks it. pynput runs its callback in
/// Python under the GIL, competing with the Qt thread and the sender threads, so under load presses
/// were dropped before the macro ever saw them. This callback does an array lookup and a queue push.
/// </summary>
public sealed class InputHook : IDisposable
{
    /// <summary>(vk, isDown, isAutoRepeat) -> return true to swallow the event so the game never sees it.</summary>
    public delegate bool InputHandler(int vk, bool isDown, bool isRepeat);

    private readonly InputHandler _handler;
    private readonly Native.LowLevelProc _kbProc;   // fields keep the delegates alive (GC would otherwise collect them)
    private readonly Native.LowLevelProc _msProc;
    private readonly bool[] _down = new bool[256];
    private Thread? _thread;
    private volatile uint _threadId;
    private IntPtr _kbHook, _msHook;
    private readonly ManualResetEventSlim _ready = new(false);

    private const uint WM_REARM = Native.WM_USER + 1;

    public InputHook(InputHandler handler)
    {
        _handler = handler;
        _kbProc = KeyboardProc;
        _msProc = MouseProc;
    }

    public bool IsRunning => _threadId != 0;

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "SCS.InputHook",
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
        _ready.Wait(2000);
    }

    /// <summary>Re-installs both hooks. Cheap insurance in case Windows dropped them during a hitch.</summary>
    public void Rearm()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, WM_REARM, UIntPtr.Zero, IntPtr.Zero);
    }

    private void Install()
    {
        var hMod = Native.GetModuleHandle(null);
        _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, hMod, 0);
    }

    private void Uninstall()
    {
        if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_msHook); _msHook = IntPtr.Zero; }
    }

    private void Loop()
    {
        // MMCSS "Games" class: the scheduler treats this thread like game-critical work.
        uint taskIndex = 0;
        IntPtr mmcss = IntPtr.Zero;
        try { mmcss = Native.AvSetMmThreadCharacteristics("Games", ref taskIndex); } catch { /* optional */ }

        _threadId = Native.GetCurrentThreadId();
        Install();
        _ready.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_REARM)
            {
                Uninstall();
                Array.Clear(_down);
                Install();
            }
        }

        Uninstall();
        if (mmcss != IntPtr.Zero) Native.AvRevertMmThreadCharacteristics(mmcss);
        _threadId = 0;
    }

    private unsafe IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = *(Native.KBDLLHOOKSTRUCT*)lParam;  // zero-alloc read
            // Ignore our own SendInput traffic (tagged via dwExtraInfo) so a sequence can never
            // re-trigger a hotkey, e.g. a hotkey bound to "w" while sending WASD. Injected input from
            // other software (G Hub / Synapse mouse-button remaps) is still accepted.
            if (data.dwExtraInfo != Native.ScsSignature)
            {
                int msg = (int)wParam;
                int vk = KeyNames.Normalize((int)(data.vkCode & 0xFF));
                bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                bool isUp = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    bool repeat = isDown && _down[vk];
                    _down[vk] = isDown;
                    bool swallow;
                    try { swallow = _handler(vk, isDown, repeat); } catch { swallow = false; }
                    if (swallow) return (IntPtr)1;
                }
            }
        }
        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private unsafe IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            int vk = 0;
            bool isDown = false;
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN: vk = KeyNames.VK_LBUTTON; isDown = true; break;
                case Native.WM_LBUTTONUP: vk = KeyNames.VK_LBUTTON; break;
                case Native.WM_RBUTTONDOWN: vk = KeyNames.VK_RBUTTON; isDown = true; break;
                case Native.WM_RBUTTONUP: vk = KeyNames.VK_RBUTTON; break;
                case Native.WM_MBUTTONDOWN: vk = KeyNames.VK_MBUTTON; isDown = true; break;
                case Native.WM_MBUTTONUP: vk = KeyNames.VK_MBUTTON; break;
                case Native.WM_XBUTTONDOWN:
                case Native.WM_XBUTTONUP:
                    {
                        var xdata = *(Native.MSLLHOOKSTRUCT*)lParam;
                        int which = (int)(xdata.mouseData >> 16) & 0xFFFF;
                        vk = which == 1 ? KeyNames.VK_XBUTTON1 : KeyNames.VK_XBUTTON2;
                        isDown = msg == Native.WM_XBUTTONDOWN;
                        break;
                    }
            }

            if (vk != 0)
            {
                {
                    bool repeat = isDown && _down[vk];
                    _down[vk] = isDown;
                    bool swallow;
                    try { swallow = _handler(vk, isDown, repeat); } catch { swallow = false; }
                    if (swallow) return (IntPtr)1;
                }
            }
        }
        return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
        _thread = null;
    }
}
