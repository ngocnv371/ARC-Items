using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace Arc.Items;

/// <summary>
/// What a game tells the catalog at startup (<see cref="ItemCatalog.Setup"/>).
/// </summary>
public sealed class ItemSetup
{
    /// <summary>
    /// The game's own item files, laid over the library's <c>data/items.json</c> in order: drop
    /// rules, bag sizes, its own bases and affixes, and changes to the library's. See the README.
    /// </summary>
    public IReadOnlyList<string> Files { get; init; } = ["res://data/items.json"];

    /// <summary>
    /// Whether the game gives a stat meaning. Bases, affixes and implicits on a stat the game
    /// doesn't implement never roll (items already saved with one still load). Null: every stat.
    /// </summary>
    public Func<string, bool>? Stats { get; init; }

    /// <summary>Whether the game wires up a unique effect (by id); others never roll. Null: every one.</summary>
    public Func<string, bool>? Uniques { get; init; }
}

/// <summary>
/// Every rarity, base, affix and unique: the library's <c>data/items.json</c> with the game's files
/// laid over it (<see cref="ItemSetup.Files"/>). Loads on first use; a game calls
/// <see cref="Setup"/> before that to name its files and the stats and uniques it implements.
/// </summary>
public static class ItemCatalog
{
    /// <summary>Where the library is mounted (its data file is read from here).</summary>
    public static string Root { get; set; } = "res://addons/arc_items";

    /// <summary>
    /// The path of a base's icon (<c>icons/&lt;base&gt;.png</c>, square, the art carrying its own
    /// frame; rarity-neutral, so a game tints it). For a base not drawn yet, the drawn one closest
    /// to it: a metal helm shows the Iron Helm, a leather one the Leather Cap, a mage's weapon the
    /// Oak Staff. Null for an unknown base.
    /// </summary>
    public static string? IconPath(string baseId)
    {
        var path = $"{Root.TrimEnd('/')}/icons/{baseId}.png";
        if (ResourceLoader.Exists(path))
            return path;
        return Bases.GetValueOrDefault(baseId) is { } def && IconStandIn(def) is var standIn && standIn != baseId
            ? IconPath(standIn)
            : null;
    }

    private static string IconStandIn(ItemBase def)
    {
        var metal = def.Prop(ItemStats.Armour) != null;
        return def.Slot switch
        {
            ItemSlot.Weapon => def.Classes.FirstOrDefault() switch { "archer" => "hunting_bow", "mage" => "oak_staff", _ => "great_axe" },
            ItemSlot.Helm => metal ? "iron_helm" : "leather_cap",
            ItemSlot.Armour => metal ? "chain_mail" : "jerkin",
            ItemSlot.Boots => metal ? "greaves" : "light_boots",
            ItemSlot.Amulet => "bone_charm",
            _ => "copper_ring",
        };
    }

    private static readonly string[] Lists = ["rarities", "bases", "affixes", "uniques"];

    private static ItemSetup _setup = new();
    private static bool _loaded;
    private static int _maxLevel = 40;
    private static int _bagSize = 36;
    private static int _stashSize = 36;
    private static float _baseFalloff = 8f;
    private static readonly Dictionary<string, DropRule> _drops = [];
    private static readonly List<(float Minute, int Max)> _dropAffixes = [];
    private static readonly List<RarityDef> _rarities = [];
    private static readonly Dictionary<string, ItemBase> _bases = [];
    private static readonly Dictionary<string, AffixDef> _affixes = [];
    private static readonly Dictionary<string, UniqueDef> _uniques = [];
    private static readonly Dictionary<string, string> _propFormats = [];

    /// <summary>Names the game's files and what it implements, and (re)loads the catalog.</summary>
    public static void Setup(ItemSetup setup)
    {
        _setup = setup;
        _loaded = false;
        Load();
    }

    public static int MaxLevel { get { Load(); return _maxLevel; } }

    /// <summary>Items the hero can carry unworn through a run.</summary>
    public static int BagSize { get { Load(); return _bagSize; } }

    /// <summary>Items the profile keeps between runs, beside what each hero wears.</summary>
    public static int StashSize { get { Load(); return _stashSize; } }

    /// <summary>
    /// How fast older bases fade from drops: a base this many levels below the item level drops
    /// half as often as one at it (within its slot), so new tiers take over without old ones vanishing.
    /// </summary>
    public static float BaseFalloff { get { Load(); return _baseFalloff; } }

    /// <summary>The drop rule for an enemy tier ("fodder", "elite", "boss"...); never drops when absent.</summary>
    public static DropRule Drop(string tier) { Load(); return _drops.GetValueOrDefault(tier); }

    /// <summary>The most affixes an item dropped <paramref name="minutes"/> into a run may carry (no limit when the data sets none).</summary>
    public static int DropAffixCap(float minutes)
    {
        Load();
        var cap = int.MaxValue;
        foreach (var (minute, max) in _dropAffixes)
            if (minutes >= minute)
                cap = max;
        return cap;
    }

    /// <summary>Common first, Legendary last.</summary>
    public static IReadOnlyList<RarityDef> Rarities { get { Load(); return _rarities; } }

    /// <summary>Every base, including those that never roll in this game (saved items still need them).</summary>
    public static IReadOnlyDictionary<string, ItemBase> Bases { get { Load(); return _bases; } }

    /// <summary>Every affix, including those that never roll in this game.</summary>
    public static IReadOnlyDictionary<string, AffixDef> Affixes { get { Load(); return _affixes; } }

    /// <summary>Every unique, including those that never roll in this game.</summary>
    public static IReadOnlyDictionary<string, UniqueDef> Uniques { get { Load(); return _uniques; } }

    public static RarityDef? Rarity(string id) => Rarities.FirstOrDefault(r => r.Id == id);

    /// <summary>Whether the game implements <paramref name="stat"/>.</summary>
    public static bool Implements(string stat) => _setup.Stats?.Invoke(stat) ?? true;

    /// <summary>A base that may drop here: not starter or retired, and every stat it gives implemented.</summary>
    public static bool Rolls(ItemBase def) => def.Drops && def.Stats.All(Implements);

    /// <summary>An affix that may roll here: weighted and its stat implemented.</summary>
    public static bool Rolls(AffixDef def) => def.Weight > 0f && Implements(def.Roll.Stat);

    /// <summary>A unique that may roll here: the game wires it up.</summary>
    public static bool Rolls(UniqueDef def) => _setup.Uniques?.Invoke(def.Id) ?? true;

    /// <summary>The tooltip format of base property <paramref name="stat"/> ("{0:0} armour").</summary>
    public static string PropFormat(string stat)
    {
        Load();
        return _propFormats.GetValueOrDefault(stat) ?? "{0:0} " + stat.Replace('_', ' ');
    }

    /// <summary>
    /// A change of <paramref name="delta"/> in <paramref name="stat"/>, worded by the first roll
    /// that grants it: "+3% damage" when it rises, "−3% damage" when it falls.
    /// </summary>
    public static string Describe(string stat, float delta)
    {
        Load();
        var format = (_propFormats.TryGetValue(stat, out var prop) ? "+" + prop : null)
            ?? Bases.Values.Select(b => b.Implicit).Concat(Affixes.Values.Where(a => !a.Local).Select(a => a.Roll)).FirstOrDefault(r => r?.Stat == stat)?.Format
            ?? stat + " {0:+0.##;-0.##}";
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Math.Abs(delta));
        return delta >= 0f ? text : "−" + text.TrimStart('+');
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        _drops.Clear();
        _dropAffixes.Clear();
        _rarities.Clear();
        _bases.Clear();
        _affixes.Clear();
        _uniques.Clear();
        _propFormats.Clear();

        var root = Read(Root.TrimEnd('/') + "/data/items.json") ?? [];
        foreach (var path in _setup.Files)
            if (Read(path) is { } overlay)
                Merge(root, overlay);

        _maxLevel = root.TryGetValue("maxLevel", out var max) ? max.AsInt32() : 40;
        _bagSize = root.TryGetValue("bagSize", out var bag) ? bag.AsInt32() : 36;
        _stashSize = root.TryGetValue("stashSize", out var stash) ? stash.AsInt32() : 36;
        _baseFalloff = root.TryGetValue("baseFalloff", out var falloff) ? falloff.AsSingle() : 8f;
        if (root.TryGetValue("propFormats", out var formats))
            foreach (var (stat, format) in formats.AsGodotDictionary())
                _propFormats[stat.AsString()] = format.AsString();

        foreach (var r in Entries(root, "rarities"))
        {
            // "affixes" is a count or a [min, max] range.
            var affixes = r["affixes"];
            var (low, high) = affixes.VariantType == Variant.Type.Array
                ? (affixes.AsGodotArray()[0].AsInt32(), affixes.AsGodotArray()[1].AsInt32())
                : (affixes.AsInt32(), affixes.AsInt32());
            _rarities.Add(new RarityDef(_rarities.Count, r["id"].AsString(), r["name"].AsString(), low, high, r["weight"].AsSingle(),
                r.TryGetValue("salvage", out var salvage) ? salvage.AsInt32() : 10, Color.FromHtml(r["color"].AsString()),
                r.TryGetValue("unique", out var unique) && unique.AsBool()));
        }
        if (root.TryGetValue("dropAffixes", out var caps))
            foreach (var cap in caps.AsGodotArray())
                _dropAffixes.Add((cap.AsGodotDictionary()["from"].AsSingle(), cap.AsGodotDictionary()["max"].AsInt32()));
        _dropAffixes.Sort((a, b) => a.Minute.CompareTo(b.Minute));
        if (root.TryGetValue("drops", out var drops))
        {
            foreach (var (tier, rule) in drops.AsGodotDictionary())
            {
                var d = rule.AsGodotDictionary();
                var floor = d.TryGetValue("minRarity", out var m) ? _rarities.FindIndex(r => r.Id == m.AsString()) : 0;
                var gold = d.TryGetValue("gold", out var g) ? g.AsGodotDictionary() : null;
                _drops[tier.AsString()] = gold == null
                    ? new DropRule(d["chance"].AsSingle(), Math.Max(0, floor))
                    : new DropRule(d["chance"].AsSingle(), Math.Max(0, floor), gold["chance"].AsSingle(), gold["min"].AsInt32(), gold["max"].AsInt32());
            }
        }
        foreach (var b in Entries(root, "bases"))
        {
            var props = new List<StatRoll>();
            if (b.TryGetValue("props", out var p))
                foreach (var (stat, range) in p.AsGodotDictionary())
                    props.Add(Prop(stat.AsString(), range));
            var def = new ItemBase(b["id"].AsString(), b["name"].AsString(), Slot(b["slot"].AsString()), Strings(b, "classes"),
                b.ContainsKey("stat") ? Roll(b) : null,
                [.. props],
                b.TryGetValue("level", out var level) ? level.AsInt32() : 1,
                b.TryGetValue("material", out var material) ? material.AsString() : null,
                b.TryGetValue("weight", out var weight) ? weight.AsSingle() : 1f,
                b.TryGetValue("starter", out var starter) && starter.AsBool(),
                b.TryGetValue("retired", out var retired) && retired.AsBool());
            _bases[def.Id] = def;
        }
        foreach (var a in Entries(root, "affixes"))
        {
            var def = new AffixDef(a["id"].AsString(), a["name"].AsString(), Strings(a, "slots").Select(Slot).ToArray(), Strings(a, "classes"),
                a.TryGetValue("weight", out var w) ? w.AsSingle() : 1f, Roll(a), a.TryGetValue("local", out var local) && local.AsBool());
            _affixes[def.Id] = def;
        }
        foreach (var u in Entries(root, "uniques"))
        {
            var parameters = u.TryGetValue("params", out var p)
                ? p.AsGodotDictionary().ToDictionary(kv => kv.Key.AsString(), kv => kv.Value.AsSingle())
                : [];
            var def = new UniqueDef(u["id"].AsString(), u["name"].AsString(), Strings(u, "slots").Select(Slot).ToArray(), parameters, Strings(u, "scales"), u["text"].AsString());
            _uniques[def.Id] = def;
        }
    }

    private static GDict? Read(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushWarning($"items: {path} not found");
            return null;
        }
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(path));
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PushError($"items: {path} is not a JSON object");
            return null;
        }
        return parsed.AsGodotDictionary();
    }

    /// <summary>
    /// Lays <paramref name="overlay"/> over <paramref name="root"/>: the four lists merge by id (an
    /// entry's fields replace the existing entry's one by one; <c>"remove": true</c> drops it; a new
    /// id is added), <c>propFormats</c> merges by key, and anything else is replaced whole.
    /// </summary>
    private static void Merge(GDict root, GDict overlay)
    {
        foreach (var (key, value) in overlay)
        {
            var name = key.AsString();
            if (Lists.Contains(name) && root.TryGetValue(name, out var existing))
            {
                var list = existing.AsGodotArray();
                foreach (var item in value.AsGodotArray())
                {
                    var entry = item.AsGodotDictionary();
                    var id = entry["id"].AsString();
                    var index = IndexOf(list, id);
                    if (entry.TryGetValue("remove", out var remove) && remove.AsBool())
                    {
                        if (index >= 0)
                            list.RemoveAt(index);
                    }
                    else if (index >= 0)
                    {
                        var target = list[index].AsGodotDictionary();
                        foreach (var (field, v) in entry)
                            target[field] = v;
                    }
                    else
                        list.Add(entry);
                }
            }
            else if (name == "propFormats" && root.TryGetValue(name, out var formats))
            {
                var target = formats.AsGodotDictionary();
                foreach (var (stat, format) in value.AsGodotDictionary())
                    target[stat] = format;
            }
            else
                root[key] = value;
        }
    }

    private static int IndexOf(GArray list, string id)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].AsGodotDictionary()["id"].AsString() == id)
                return i;
        return -1;
    }

    private static IEnumerable<GDict> Entries(GDict root, string key) =>
        root.TryGetValue(key, out var list) ? list.AsGodotArray().Select(v => v.AsGodotDictionary()) : [];

    private static ItemSlot Slot(string name) => Enum.Parse<ItemSlot>(name, ignoreCase: true);

    private static string[] Strings(GDict d, string key) =>
        d.TryGetValue(key, out var v) ? v.AsGodotArray().Select(x => x.AsString()).ToArray() : [];

    private static Vector2 Range(Variant v) => new(v.AsGodotArray()[0].AsSingle(), v.AsGodotArray()[1].AsSingle());

    /// <summary>A base property: a fixed <c>[low, high]</c>, or <c>{ "at1", "atMax", "step" }</c> to grow with item level. Whole numbers unless a step says otherwise.</summary>
    private static StatRoll Prop(string stat, Variant range)
    {
        if (range.VariantType == Variant.Type.Array)
            return new StatRoll(stat, Range(range), Range(range), 1f, PropFormat(stat));
        var d = range.AsGodotDictionary();
        return new StatRoll(stat, Range(d["at1"]), Range(d["atMax"]), d.TryGetValue("step", out var step) ? step.AsSingle() : 1f, PropFormat(stat));
    }

    /// <summary>An implicit or affix roll: <c>at1</c>/<c>atMax</c> ranges, or one fixed <c>value</c> range.</summary>
    private static StatRoll Roll(GDict d)
    {
        var (at1, atMax) = d.TryGetValue("value", out var value) ? (Range(value), Range(value)) : (Range(d["at1"]), Range(d["atMax"]));
        return new StatRoll(d["stat"].AsString(), at1, atMax, d.TryGetValue("step", out var step) ? step.AsSingle() : 0f, d["format"].AsString());
    }
}
