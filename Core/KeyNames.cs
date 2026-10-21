namespace SCS.Core;

/// <summary>
/// Maps the hotkey strings used in profiles (same spelling as SCS v1.x, so old profiles load)
/// to Windows virtual-key codes, and back. Mouse buttons use their real VK codes (1,2,4,5,6).
/// </summary>
public static class KeyNames
{
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> VkToName = new();
    private static readonly HashSet<int> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24,             // PgUp PgDn End Home
        0x25, 0x26, 0x27, 0x28,             // arrows
        0x2D, 0x2E,                          // Insert Delete
        0x6F,                                // Numpad divide
        VK_RCONTROL, VK_RMENU,
        0x5B, 0x5C, 0x5D,                    // Win keys, Apps
        0x90,                                // NumLock
    };

    private static void Add(int vk, string name, params string[] aliases)
    {
        NameToVk[name] = vk;
        VkToName[vk] = name;
        foreach (var a in aliases) NameToVk[a] = vk;
    }

    static KeyNames()
    {
        // Mouse
        Add(VK_LBUTTON, "mouse_left");
        Add(VK_RBUTTON, "mouse_right");
        Add(VK_MBUTTON, "mouse_middle");
        Add(VK_XBUTTON1, "mouse_xbutton1", "mouse_x1", "mouse4");
        Add(VK_XBUTTON2, "mouse_xbutton2", "mouse_x2", "mouse5");

        // Letters & digits
        for (int c = 'A'; c <= 'Z'; c++) Add(c, ((char)c).ToString().ToLowerInvariant());
        for (int c = '0'; c <= '9'; c++) Add(c, ((char)c).ToString());

        // Numpad (v1.x used num_0..num_9)
        for (int i = 0; i <= 9; i++) Add(0x60 + i, $"num_{i}", $"numpad{i}");
        Add(0x6A, "num_multiply", "num_*");
        Add(0x6B, "num_add", "num_+");
        Add(0x6D, "num_subtract", "num_-");
        Add(0x6E, "num_decimal", "num_.");
        Add(0x6F, "num_divide", "num_/");

        // Function keys
        for (int i = 1; i <= 24; i++) Add(0x70 + i - 1, $"f{i}");

        // Modifiers. "ctrl"/"shift"/"alt" mean the LEFT key (what v1.x sent).
        Add(VK_LCONTROL, "ctrl", "ctrl_l", "control", "lctrl");
        Add(VK_RCONTROL, "ctrl_r", "rctrl");
        Add(VK_LSHIFT, "shift", "shift_l", "lshift");
        Add(VK_RSHIFT, "shift_r", "rshift");
        Add(VK_LMENU, "alt", "alt_l", "lalt");
        Add(VK_RMENU, "alt_r", "alt_gr", "ralt");

        // Navigation / editing (aliases cover both Qt and pynput spellings from v1.x)
        Add(0x08, "backspace");
        Add(0x09, "tab");
        Add(0x0D, "enter", "return");
        Add(0x13, "pause");
        Add(0x14, "caps_lock", "capslock");
        Add(0x1B, "esc", "escape");
        Add(0x20, "space");
        Add(0x21, "page_up", "pgup");
        Add(0x22, "page_down", "pgdown", "pgdn");
        Add(0x23, "end");
        Add(0x24, "home");
        Add(VK_LEFT, "left");
        Add(VK_UP, "up");
        Add(VK_RIGHT, "right");
        Add(VK_DOWN, "down");
        Add(0x2D, "insert", "ins");
        Add(0x2E, "delete", "del");
        Add(0x90, "num_lock", "numlock");
        Add(0x91, "scroll_lock", "scrolllock");

        // OEM punctuation (US layout)
        Add(0xBA, ";");
        Add(0xBB, "=");
        Add(0xBC, ",");
        Add(0xBD, "-");
        Add(0xBE, ".");
        Add(0xBF, "/");
        Add(0xC0, "`");
        Add(0xDB, "[");
        Add(0xDC, "\\");
        Add(0xDD, "]");
        Add(0xDE, "'");
    }

    public static bool TryGetVk(string? name, out int vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return NameToVk.TryGetValue(name.Trim(), out vk);
    }

    public static string? GetName(int vk) => VkToName.TryGetValue(vk, out var n) ? n : null;

    public static bool IsMouse(int vk) => vk is VK_LBUTTON or VK_RBUTTON or VK_MBUTTON or VK_XBUTTON1 or VK_XBUTTON2;

    public static bool IsExtended(int vk) => Extended.Contains(vk);

    /// <summary>Turns generic modifier VKs (what some APIs report) into their left-hand variants.</summary>
    public static int Normalize(int vk) => vk switch
    {
        VK_SHIFT => VK_LSHIFT,
        VK_CONTROL => VK_LCONTROL,
        VK_MENU => VK_LMENU,
        _ => vk
    };

    /// <summary>Pretty label for the UI.</summary>
    public static string Display(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        if (!TryGetVk(name, out var vk)) return name.ToUpperInvariant();
        return vk switch
        {
            VK_LBUTTON => "MOUSE L",
            VK_RBUTTON => "MOUSE R",
            VK_MBUTTON => "MOUSE M",
            VK_XBUTTON1 => "MOUSE 4",
            VK_XBUTTON2 => "MOUSE 5",
            >= 0x60 and <= 0x69 => $"NUM {vk - 0x60}",
            VK_LCONTROL => "L-CTRL",
            VK_RCONTROL => "R-CTRL",
            VK_LSHIFT => "L-SHIFT",
            VK_RSHIFT => "R-SHIFT",
            VK_LMENU => "L-ALT",
            VK_RMENU => "R-ALT",
            0x14 => "CAPS",
            0x21 => "PG UP",
            0x22 => "PG DN",
            _ => (GetName(vk) ?? name).Replace('_', ' ').ToUpperInvariant()
        };
    }
}
