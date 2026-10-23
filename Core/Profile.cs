using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SCS.Core;

public sealed class SlotConfig
{
    public string Stratagem { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

/// <summary>
/// Timing + behaviour settings. Reads v1.x profiles (activation_delay / pydirectinput_pause in seconds)
/// and writes both the new millisecond fields and the legacy fields, so a profile still opens in v1.4.
/// </summary>
public sealed class EngineSettings
{
    public string MenuKey { get; set; } = "ctrl";
    public string ToggleHotkey { get; set; } = "";
    public bool UseWasd { get; set; }

    /// <summary>Wait after pressing the menu key before the first arrow (menu open animation).</summary>
    public double MenuDelayMs { get; set; } = 50;
    /// <summary>How long each arrow is physically held down. v1.x held for ~0 ms, the main cause of drops.</summary>
    public double KeyHoldMs { get; set; } = 25;
    /// <summary>Gap between releasing one arrow and pressing the next.</summary>
    public double KeyGapMs { get; set; } = 20;
    /// <summary>
    /// Lag Guard: lowest frame rate you expect in heavy fights. Hold and gap are stretched to at
    /// least one full frame at this FPS (plus margin) so the game can't miss a press between frames.
    /// 0 disables it.
    /// </summary>
    public int LagGuardFps { get; set; } = 40;

    public bool BlockTriggerKeys { get; set; }
    public bool GameFocusOnly { get; set; }
    public string GameProcess { get; set; } = "helldivers2";
    public double ReminderOpacity { get; set; } = 0.9;

    public EngineSettings Clone() => (EngineSettings)MemberwiseClone();

    /// <summary>Effective timings after Lag Guard is applied.</summary>
    public (double menu, double hold, double gap) EffectiveTimings()
    {
        double hold = Math.Max(1, KeyHoldMs), gap = Math.Max(1, KeyGapMs), menu = Math.Max(0, MenuDelayMs);
        if (LagGuardFps > 0)
        {
            double frame = 1000.0 / LagGuardFps;
            hold = Math.Max(hold, frame * 1.1);
            gap = Math.Max(gap, frame * 1.1);
            menu = Math.Max(menu, frame * 2.2); // menu open needs the game to process at least two frames
        }
        return (menu, hold, gap);
    }
}

public sealed class Profile
{
    public const int SlotCount = 12;

    public EngineSettings Settings { get; set; } = new();
    public List<SlotConfig> Slots { get; set; } = Enumerable.Range(0, SlotCount).Select(_ => new SlotConfig()).ToList();

    public static string Directory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "stratagem_profiles");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static IEnumerable<string> ListNames() =>
        System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    public static string PathFor(string name) => Path.Combine(Directory, name + ".json");

    public static Profile Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        var p = new Profile();
        var s = p.Settings;

        if (root["settings"] is JsonObject js)
        {
            s.MenuKey = Str(js, "stratagem_menu_key") ?? s.MenuKey;
            s.ToggleHotkey = Str(js, "global_toggle_hotkey") ?? "";
            s.UseWasd = Bool(js, "use_wasd_input") ?? false;
            s.ReminderOpacity = Num(js, "reminder_opacity") ?? s.ReminderOpacity;

            // New fields first, legacy seconds-based fields as fallback.
            s.MenuDelayMs = Num(js, "menu_delay_ms") ?? (Num(js, "activation_delay") * 1000) ?? s.MenuDelayMs;
            s.KeyGapMs = Num(js, "key_gap_ms") ?? (Num(js, "pydirectinput_pause") * 1000) ?? s.KeyGapMs;
            s.KeyHoldMs = Num(js, "key_hold_ms") ?? s.KeyHoldMs;
            s.LagGuardFps = (int)(Num(js, "lag_guard_fps") ?? s.LagGuardFps);
            s.BlockTriggerKeys = Bool(js, "block_trigger_keys") ?? false;
            s.GameFocusOnly = Bool(js, "game_focus_only") ?? false;
            s.GameProcess = Str(js, "game_process") ?? s.GameProcess;
        }

        if (root["hotkeys"] is JsonArray arr)
        {
            for (int i = 0; i < SlotCount && i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject o) continue;
                var name = Str(o, "stratagem") ?? "";
                // Canonicalise the name (old builds sometimes stored different casing).
                p.Slots[i].Stratagem = Stratagems.Find(name)?.Name ?? "";
                p.Slots[i].Hotkey = (Str(o, "hotkey") ?? "").Trim().ToLowerInvariant();
            }
        }
        return p;
    }

    public void Save(string path)
    {
        var s = Settings;
        var root = new JsonObject
        {
            ["settings"] = new JsonObject
            {
                ["stratagem_menu_key"] = s.MenuKey,
                ["global_toggle_hotkey"] = s.ToggleHotkey,
                ["use_wasd_input"] = s.UseWasd,
                ["menu_delay_ms"] = Math.Round(s.MenuDelayMs, 2),
                ["key_hold_ms"] = Math.Round(s.KeyHoldMs, 2),
                ["key_gap_ms"] = Math.Round(s.KeyGapMs, 2),
                ["lag_guard_fps"] = s.LagGuardFps,
                ["block_trigger_keys"] = s.BlockTriggerKeys,
                ["game_focus_only"] = s.GameFocusOnly,
                ["game_process"] = s.GameProcess,
                ["reminder_opacity"] = Math.Round(s.ReminderOpacity, 2),
                // v1.x compatibility
                ["activation_delay"] = Math.Round(s.MenuDelayMs / 1000.0, 4),
                ["pydirectinput_pause"] = Math.Round(s.KeyGapMs / 1000.0, 4),
            },
            ["hotkeys"] = new JsonArray(Slots.Select(sl => (JsonNode)new JsonObject
            {
                ["stratagem"] = sl.Stratagem,
                ["hotkey"] = sl.Hotkey,
            }).ToArray())
        };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    private static string? Str(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? Bool(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static double? Num(JsonObject o, string k)
    {
        if (o[k] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return null;
    }
}

/// <summary>Small app-level state (last profile, overlay positions) kept next to the exe.</summary>
public sealed class AppState
{
    public string? LastProfile { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public bool OverlayVisible { get; set; }
    public double? LedLeft { get; set; }
    public double? LedTop { get; set; }
    public bool LedVisible { get; set; }
    /// <summary>Check the wiki for new stratagem codes at startup (at most once a day).</summary>
    public bool WikiAutoUpdate { get; set; } = true;

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "scs_state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
        }
        catch { /* corrupt state is not worth crashing over */ }
        return new AppState();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
