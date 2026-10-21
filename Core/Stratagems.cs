namespace SCS.Core;

public sealed record Stratagem(string Name, string Code, string Category);

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
            ("GL-52 De-Escalator", "DRULR"), ("CDC-1 One True Flag", "DLRRU"), ("PLAS-45 Epoch", "DLULR"),
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

    public static readonly IReadOnlyList<Stratagem> All;
    private static readonly Dictionary<string, Stratagem> ByName = new(StringComparer.OrdinalIgnoreCase);

    static Stratagems()
    {
        var list = new List<Stratagem>();
        foreach (var (cat, items) in Catalog)
            foreach (var (name, code) in items)
            {
                var s = new Stratagem(name, code, cat);
                list.Add(s);
                ByName[name] = s;
            }
        All = list;
    }

    public static Stratagem? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : ByName.GetValueOrDefault(name.Trim());
}
