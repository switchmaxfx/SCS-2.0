namespace SCS.Core;

/// <summary>
/// One stratagem. <see cref="Name"/> is the stable key saved in profiles. <see cref="Alias"/> holds the wiki's
/// title when it differs (e.g. "A/AC-8 Autocannon Sentry"), so search and old/new names both resolve.
/// </summary>
public sealed record Stratagem(string Name, string Code, string Category)
{
    public string? Alias { get; init; }
    public bool FromWiki { get; init; }
    public string SearchText => Alias == null ? Name : Name + " " + Alias;
    public string Subtitle => (Alias == null ? Category : $"{Category}   /   wiki: {Alias}").ToUpperInvariant();
}

public static class Stratagems
{
    public static readonly (string Category, (string Name, string Code)[] Items)[] Catalog =
    {
        ("Mission", new[]
        {
            ("Reinforce", "UDRLU"), ("Resupply", "DDUR"), ("SOS Beacon", "UDRU"), ("Super Earth Flag", "DUDU"),
            ("Hellbomb", "DULDURDU"), ("Seismic Probe", "UULRDD"), ("SSSD Delivery", "DDDUU"),
            ("Prospecting Drill", "DDLRDD"), ("Upload Data", "LRUUU"), ("Dark Fluid Vessel", "ULRDUU"),
            ("Tectonic Drill", "UDUDUD"), ("Hive Breaker Drill", "LUDRDD"), ("SEAF Artillery", "RUUD"),
        }),
        ("Orbital", new[]
        {
            ("Orbital Precision Strike", "RRU"), ("Orbital Gatling Barrage", "RDLUU"), ("Orbital Airburst Strike", "RRR"),
            ("Orbital 120MM HE Barrage", "RRDLRD"), ("Orbital 380MM HE Barrage", "RDUULDD"),
            ("Orbital Walking Barrage", "RDRDRD"), ("Orbital Laser", "RDURD"), ("Orbital Railcannon Strike", "RUDDR"),
            ("Orbital Gas Strike", "RRDR"), ("Orbital EMS Strike", "RRLD"), ("Orbital Smoke Strike", "RRDU"),
            ("Orbital Napalm Barrage", "RRDLRU"), ("Orbital Illumination Flare", "RRLL"),
        }),
        ("Eagle", new[]
        {
            ("Eagle Rearm", "UULUR"), ("Eagle Strafing Run", "URR"), ("Eagle Airstrike", "URDR"),
            ("Eagle Cluster Bomb", "URDDR"), ("Eagle Napalm Airstrike", "URDU"), ("Eagle Smoke Strike", "URUD"),
            ("Eagle 110MM Rocket Pods", "URUL"), ("Eagle 500kg Bomb", "URDDD"), ("Eagle Gas Airstrike", "URLR"),
        }),
        ("Support Weapon", new[]
        {
            ("MG-43 Machine Gun", "DLDUR"), ("APW-1 Anti-Materiel Rifle", "DLRUD"), ("M-105 Stalwart", "DLDUUL"),
            ("EAT-17 Expendable Anti-Tank", "DDLUR"), ("GR-8 Recoilless Rifle", "DLRRL"), ("FLAM-40 Flamethrower", "DLUDU"),
            ("AC-8 Autocannon", "DLDUUR"), ("MG-206 Heavy Machine Gun", "DLUDD"), ("RS-422 Railgun", "DRDULR"),
            ("FAF-14 Spear", "DDUDD"), ("GL-21 Grenade Launcher", "DLULD"), ("LAS-98 Laser Cannon", "DLDUL"),
            ("ARC-3 Arc Thrower", "DRDULL"), ("LAS-99 Quasar Cannon", "DDULR"), ("RL-77 Airburst Rocket Launcher", "DUULR"),
            ("MLS-4X Commando", "DLUDR"), ("SM-63 W.A.S.P. Launcher", "DDUDR"), ("TX-41 Sterilizer", "DLUDL"),
            ("GL-52 De-Escalator", "DRULR"), ("CQC-1 One True Flag", "DLRRU"), ("PLAS-45 Epoch", "DLULR"),
            ("S-11 Speargun", "DRDLUR"), ("EAT-700 Expendable Napalm", "DDLUL"), ("MS-11 Solo Silo", "DURDD"),
            ("CQC-9 Defoliation Tool", "DLRRD"), ("M-1000 Maxigun", "DLRDUU"), ("CQC-20 Breaching Hammer", "DLRLU"),
            ("EAT-411 Leveller", "DDLUD"), ("GL-28 Belt-Fed Grenade Launcher", "DLULUU"), ("B/FLAM-80 Cremator", "DDRDUU"),
            ("MGX-42 Bullet Storm", "DLDRUL"), ("40-K Meltagun", "DLULLD"), ("AMW-20 Apex", "DLLUDL"), ("LAS-44 Nova", "DLRDD"),
        }),
        ("Sentry", new[]
        {
            ("Machine Gun Sentry", "DURRU"), ("Gatling Sentry", "DURL"), ("Mortar Sentry", "DURRD"),
            ("EMS Mortar Sentry", "DURDR"), ("Autocannon Sentry", "DURULU"), ("Rocket Sentry", "DURRL"),
            ("Tesla Tower", "DURULR"), ("Flame Sentry", "DURDUU"), ("Laser Sentry", "DURDUR"),
            ("A/GM-17 Gas Mortar Sentry", "DURDL"),
        }),
        ("Emplacement", new[]
        {
            ("Anti-Personnel Minefield", "DLUR"), ("Incendiary Mines", "DLLD"), ("Anti-Tank Mines", "DLUU"),
            ("MD-B Gas Mines", "DLLR"), ("HMG Emplacement", "DULRRL"), ("Anti-Tank Emplacement", "DULRRR"),
            ("Grenadier Emplacement", "DRDLR"), ("Shield Generator Relay", "DDLRLR"),
        }),
        ("Backpack", new[]
        {
            ("LIFT-850 Jump Pack", "DUUDU"), ("LIFT-660 Hover Pack", "DUUDLR"), ("B-1 Supply Pack", "DLDUUD"),
            ("AX/AR-23 Guard Dog", "DULURD"), ("AX/LAS-5 Guard Dog", "DULURR"), ("AX/ARC-3 Guard Dog", "DULURL"),
            ("AD/TX-13 Guard Dog", "DULURU"), ("AX/FLAM-75 Hot Dog", "DULULL"), ("SH-20 Ballistic Shield Backpack", "DLDDUL"),
            ("SH-32 Shield Generator Pack", "DULRLR"), ("SH-51 Directional Shield", "DULRUU"),
            ("B-100 Portable Hellbomb", "DRUUU"), ("LIFT-182 Warp Pack", "DLRDLR"), ("B/MD C4 Pack", "DRUURU"),
        }),
        ("Vehicle", new[]
        {
            ("EXO-45 Patriot Exosuit", "LDRULDD"), ("EXO-49 Emancipator Exosuit", "LDRULDU"),
            ("M-102 Fast Recon Vehicle", "LDRDRDU"), ("M-103 Supply FRV", "LDLLDUR"), ("M-104 Incinerator FRV", "LDRLDUU"),
            ("TD-220 Bastion MK XVI", "LDRDLDUDU"), ("EXO-51 Lumberer Exosuit", "LDRURLU"),
            ("EXO-55 Breakthrough Exosuit", "LDRLRDU"), ("TD-110 Maelstrom", "LDRDLDULR"),
        }),
    };

    public static readonly string[] Categories = Catalog.Select(c => c.Category).ToArray();

    /// <summary>Old names that should still resolve (typos fixed in later versions).</summary>
    private static readonly (string Old, string Current)[] LegacyNames =
    {
        ("CDC-1 One True Flag", "CQC-1 One True Flag"),
    };

    /// <summary>Raised (on whatever thread called <see cref="ApplyWiki"/>) after the list changes.</summary>
    public static event Action? Changed;

    internal sealed class Snapshot
    {
        public required IReadOnlyList<Stratagem> All;
        public required Dictionary<string, Stratagem> ByName;
    }

    // Swapped atomically, so readers on any thread always see a complete, consistent list.
    private static volatile Snapshot _snap = Build(BuiltIns(), Array.Empty<WikiStratagems.Entry>()).snap;

    public static IReadOnlyList<Stratagem> All => _snap.All;

    public static Stratagem? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var map = _snap.ByName;
        return map.GetValueOrDefault(name.Trim()) ?? map.GetValueOrDefault(Norm(name));
    }

    private static List<Stratagem> BuiltIns()
    {
        var list = new List<Stratagem>();
        foreach (var (cat, items) in Catalog)
            foreach (var (name, code) in items)
                list.Add(new Stratagem(name, code, cat));
        return list;
    }

    public sealed record MergeResult(int Total, int Added, int CodesChanged, IReadOnlyList<string> AddedNames, IReadOnlyList<string> ChangedNames);

    /// <summary>
    /// Rebuild the list from the built-ins plus wiki data. Wiki codes win. Wiki entries that match a
    /// built-in keep the built-in name (so saved profiles keep working) and add the wiki title as an alias.
    /// Built-ins the wiki doesn't list are kept, so nothing ever disappears.
    /// </summary>
    public static MergeResult ApplyWiki(IReadOnlyList<WikiStratagems.Entry> wiki)
    {
        var (snap, result) = Build(BuiltIns(), wiki);
        _snap = snap;
        Changed?.Invoke();
        return result;
    }

    internal static (Snapshot snap, MergeResult result) Build(List<Stratagem> builtIns, IReadOnlyList<WikiStratagems.Entry> wiki)
    {
        var final = builtIns.ToList();
        var matched = new bool[builtIns.Count];
        var added = new List<string>();
        var changed = new List<string>();

        // Pass order matters: confident matches first, so fuzzy passes only see what's left.
        var pending = wiki.Where(w => !string.IsNullOrWhiteSpace(w.Title) && w.Code.Length > 0).ToList();
        var matchFor = new Dictionary<WikiStratagems.Entry, int>();
        Func<WikiStratagems.Entry, Stratagem, bool>[] passes =
        {
            (w, b) => Norm(w.Title) == Norm(b.Name),
            (w, b) => Desig(w.Title) is { } d && d == Desig(b.Name),
            (w, b) => Core(w.Title) == Core(b.Name),
            (w, b) => w.Code == b.Code,
        };
        foreach (var pass in passes)
        {
            foreach (var w in pending.ToList())
            {
                int hit = -1, hits = 0;
                for (int i = 0; i < builtIns.Count; i++)
                {
                    if (matched[i] || !pass(w, builtIns[i])) continue;
                    hit = i; hits++;
                }
                if (hits != 1) continue;     // ambiguous or none: leave for a later pass
                matched[hit] = true;
                matchFor[w] = hit;
                pending.Remove(w);
            }
        }

        foreach (var (w, i) in matchFor)
        {
            var b = builtIns[i];
            if (b.Code != w.Code) changed.Add(b.Name);
            final[i] = b with
            {
                Code = w.Code,
                Alias = Norm(w.Title) == Norm(b.Name) ? null : w.Title,
                FromWiki = true,
            };
        }
        foreach (var w in pending)
        {
            final.Add(new Stratagem(w.Title, w.Code, w.Category) { FromWiki = true });
            added.Add(w.Title);
        }

        // Group by category in catalog order, alphabetical inside each.
        var ordered = final
            .OrderBy(s => Array.IndexOf(Categories, s.Category) is var k && k < 0 ? int.MaxValue : k)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byName = new Dictionary<string, Stratagem>(StringComparer.OrdinalIgnoreCase);
        void Key(string? k, Stratagem s) { if (!string.IsNullOrWhiteSpace(k)) byName.TryAdd(k, s); }
        foreach (var s in ordered) Key(s.Name, s);                       // exact names win
        foreach (var s in ordered) { Key(s.Alias, s); Key(Norm(s.Name), s); if (s.Alias != null) Key(Norm(s.Alias), s); }
        foreach (var (old, cur) in LegacyNames)
            if (byName.TryGetValue(cur, out var s)) { Key(old, s); Key(Norm(old), s); }

        return (new Snapshot { All = ordered, ByName = byName },
                new MergeResult(ordered.Count, added.Count, changed.Count, added, changed));
    }

    // ---- name matching helpers ----

    /// <summary>Lowercase letters and digits only: "AX/AR-23 \"Guard Dog\"" -> "axar23guarddog".</summary>
    internal static string Norm(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var c in s) if (char.IsLetterOrDigit(c)) buf[n++] = char.ToLowerInvariant(c);
        return new string(buf[..n]);
    }

    /// <summary>Model designation if the name starts with one ("A/AC-8", "EXO-45", "B/MD"), normalized.</summary>
    internal static string? Desig(string name)
    {
        var first = name.Split(' ', 2)[0];
        bool looksLikeDesig = (first.Contains('-') || first.Contains('/')) && (first.Any(char.IsDigit) || first.Contains('/'));
        return looksLikeDesig && name.Contains(' ') ? Norm(first) : null;
    }

    /// <summary>Name without its designation: "A/AC-8 Autocannon Sentry" -> "autocannonsentry".</summary>
    internal static string Core(string name) =>
        Desig(name) != null ? Norm(name.Split(' ', 2)[1]) : Norm(name);
}
