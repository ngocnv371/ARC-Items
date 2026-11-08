using System.Globalization;
using System.Text.RegularExpressions;
using Godot;

namespace Arc.Items;

/// <summary>Where an item is worn. New slots go at the end, so a saved slot number keeps its meaning.</summary>
public enum ItemSlot { Weapon, Helm, Armour, Boots, Amulet, Ring, Gloves, Shield }

/// <summary>
/// The stats the library itself knows by name: a base's properties. Every other stat is just a
/// string the data names and the game gives meaning to (see <see cref="ItemSetup.Stats"/>).
/// </summary>
public static class ItemStats
{
    /// <summary>A weapon's damage, which the game's weapon skills deal a share of.</summary>
    public const string WeaponDamage = "weapon_damage";

    /// <summary>A plate (or scale, or mail) base's defence: the game decides what it stops.</summary>
    public const string Armour = "armour";

    /// <summary>A leather (or scale, or garb) base's defence: the game decides what it dodges.</summary>
    public const string Evasion = "evasion";

    /// <summary>A robe (or mail, or garb) base's defence: the game decides what it absorbs.</summary>
    public const string EnergyShield = "energy_shield";

    /// <summary>A shield's chance to block a hit (0.24 is 24%): the game decides what a block stops.</summary>
    public const string Block = "block";
}

/// <summary>
/// A rarity: how many affixes it rolls (<see cref="AffixMin"/> to <see cref="AffixMax"/>), how
/// often, its colour, the gold it salvages for at item level 1, and whether it also carries a
/// unique effect (Legendary).
/// </summary>
public sealed record RarityDef(int Index, string Id, string Name, int AffixMin, int AffixMax, float Weight, int Salvage, Color Color, bool Unique);

/// <summary>
/// A Legendary's effect, which the game wires up: its numbers, of which those named in
/// <see cref="Scales"/> grow with item level, to 4.9 times at the max level, and its text with
/// <c>{param:format}</c> holes.
/// </summary>
public sealed record UniqueDef(string Id, string Name, ItemSlot[] Slots, Dictionary<string, float> Params, string[] Scales, string Text)
{
    /// <summary>Parameter <paramref name="key"/> on an item of <paramref name="level"/>.</summary>
    public float Param(string key, int level) =>
        Params.GetValueOrDefault(key) * (Scales.Contains(key) ? 1f + 3.9f * (level - 1) / Math.Max(1, ItemCatalog.MaxLevel - 1) : 1f);

    /// <summary>The text with every hole filled at <paramref name="level"/>.</summary>
    public string Describe(int level) => Regex.Replace(Text, @"\{(\w+)(?::([^}]+))?\}", m =>
        Param(m.Groups[1].Value, level).ToString(m.Groups[2].Success ? m.Groups[2].Value : "0.##", CultureInfo.InvariantCulture));
}

/// <summary>
/// How often an enemy of a tier drops an item, and the lowest rarity it can be; and how often it
/// drops gold, and how much (<see cref="GoldMin"/> to <see cref="GoldMax"/> at level 1).
/// </summary>
public readonly record struct DropRule(float Chance, int MinRarity, float GoldChance = 0f, int GoldMin = 0, int GoldMax = 0);

/// <summary>
/// A rollable stat: drawn from <see cref="At1"/> at item level 1 and from <see cref="AtMax"/> at the
/// catalog's max level, lerped between, and snapped to <see cref="Step"/> when set. A fixed range
/// (the same at both ends) ignores the level: a tiered base's defence is its tier's, whatever level
/// it drops at.
/// </summary>
public sealed record StatRoll(string Stat, Vector2 At1, Vector2 AtMax, float Step, string Format)
{
    /// <summary>The range rolled on an item of <paramref name="level"/>.</summary>
    public Vector2 At(int level)
    {
        var max = ItemCatalog.MaxLevel;
        var t = max <= 1 ? 1f : (Math.Clamp(level, 1, max) - 1f) / (max - 1f);
        return At1.Lerp(AtMax, t);
    }

    /// <summary>The middle of the range at <paramref name="level"/>, snapped like a roll.</summary>
    public float Middle(int level)
    {
        var range = At(level);
        return Snap((range.X + range.Y) * 0.5f);
    }

    /// <summary>A value snapped to <see cref="Step"/> (never below one step), or as-is when there is none.</summary>
    public float Snap(float value) => Step > 0f ? Mathf.Max(Step, Mathf.Snapped(value, Step)) : value;

    /// <summary>Formats <paramref name="value"/> for a tooltip line.</summary>
    public string Text(float value) => string.Format(CultureInfo.InvariantCulture, Format, value);
}

/// <summary>
/// An item type. Its <see cref="Props"/> are the baseline every item of it carries, rolled within
/// the base's range: a weapon's damage, an armour piece's armour, evasion or energy shield. That baseline is what
/// a better base is better at: an Iron Sallet is a sturdier helm than a Wooden Helm before any
/// affix lands, and local affixes (+% armour) multiply it. Its <see cref="Level"/> is the item
/// level it starts dropping at; <see cref="Material"/> names its line (wood, bronze, leather,
/// dragonscale...). Its <see cref="Implicit"/> is a stat it always rolls besides.
/// A <see cref="Starter"/> base is handed out, never dropped or sold; a <see cref="Retired"/> one
/// no longer drops but still loads, so saved items keep working.
/// </summary>
public sealed record ItemBase(
    string Id,
    string Name,
    ItemSlot Slot,
    string[] Classes,
    StatRoll? Implicit,
    StatRoll[] Props,
    int Level = 1,
    string? Material = null,
    float Weight = 1f,
    bool Starter = false,
    bool Retired = false)
{
    public bool Fits(string archetype) => Classes.Length == 0 || Classes.Contains(archetype);

    /// <summary>The baseline roll for <paramref name="stat"/>, or null when this base has none.</summary>
    public StatRoll? Prop(string stat) => Props.FirstOrDefault(p => p.Stat == stat);

    /// <summary>Every stat this base gives on its own (its props and implicit).</summary>
    public IEnumerable<string> Stats => Props.Select(p => p.Stat).Concat(Implicit is { } i ? [i.Stat] : []);

    /// <summary>Can drop (not handed out, not retired).</summary>
    public bool Drops => !Starter && !Retired;
}

/// <summary>
/// A rollable affix. A <see cref="Local"/> one scales its item's own base property named by its
/// stat (+40% armour on a helm multiplies that helm's armour) and only rolls on bases that have it;
/// any other adds its stat to the wearer.
/// </summary>
public sealed record AffixDef(string Id, string Name, ItemSlot[] Slots, string[] Classes, float Weight, StatRoll Roll, bool Local = false)
{
    public bool Fits(ItemSlot slot, string archetype) => Slots.Contains(slot) && (Classes.Length == 0 || Classes.Contains(archetype));

    public bool Fits(ItemBase def, string archetype) => Fits(def.Slot, archetype) && (!Local || def.Prop(Roll.Stat) != null);
}
