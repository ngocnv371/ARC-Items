using System.Text.Json.Serialization;
using Godot;

namespace Arc.Items;

/// <summary>One rolled affix: which one, and the value it landed on.</summary>
public readonly record struct ItemAffix(string Id, float Value);

/// <summary>
/// One dropped item. Plain data (ids and numbers, no references), so a game can save it as-is
/// with System.Text.Json; the definitions are looked up in <see cref="ItemCatalog"/>.
/// </summary>
public sealed record Item
{
    public required string Id { get; init; }
    public required string Base { get; init; }
    public required string Rarity { get; init; }
    public int Level { get; init; } = 1;
    public float Implicit { get; init; }

    /// <summary>The base's properties as rolled (weapon damage, armour, evasion), by stat.</summary>
    public Dictionary<string, float> Props { get; init; } = [];

    /// <summary>A weapon's rolled damage from before <see cref="Props"/>; read only for items saved then.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Power { get; init; }

    public ItemAffix[] Affixes { get; init; } = [];

    /// <summary>A Legendary's effect (an id in <see cref="ItemCatalog.Uniques"/>), or null.</summary>
    public string? Unique { get; init; }

    [JsonIgnore]
    public UniqueDef? UniqueDef => Unique != null ? ItemCatalog.Uniques.GetValueOrDefault(Unique) : null;

    [JsonIgnore]
    public ItemBase Def => ItemCatalog.Bases[Base];
    [JsonIgnore]
    public ItemSlot Slot => Def.Slot;
    [JsonIgnore]
    public RarityDef RarityDef => ItemCatalog.Rarity(Rarity) ?? ItemCatalog.Rarities[0];

    /// <summary>
    /// The base property <paramref name="stat"/> as rolled, before local affixes: its roll, or for
    /// an item saved before the base had it, the middle of the base's range (an old weapon's
    /// <see cref="Power"/>). 0 when the base has no such property.
    /// </summary>
    public float BaseProp(string stat)
    {
        if (Props.TryGetValue(stat, out var rolled))
            return rolled;
        if (stat == ItemStats.WeaponDamage && Power > 0f)
            return Power;
        return Def.Prop(stat)?.Middle(Level) ?? 0f;
    }

    /// <summary>The base property <paramref name="stat"/> with this item's local affixes on it (+40% armour).</summary>
    public float Prop(string stat) =>
        BaseProp(stat) * (1f + Affixes.Where(a => ItemCatalog.Affixes[a.Id] is { Local: true } def && def.Roll.Stat == stat).Sum(a => a.Value));

    /// <summary>The weapon damage it gives (0 off weapons).</summary>
    [JsonIgnore]
    public float WeaponDamage => Prop(ItemStats.WeaponDamage);

    /// <summary>A Legendary's unique name ("Emberstep"), else the base's followed by its first affix's ("Longbow of Haste").</summary>
    [JsonIgnore]
    public string Name => UniqueDef?.Name
        ?? (Affixes.Length == 0 ? Def.Name : $"{Def.Name} {ItemCatalog.Affixes[Affixes[0].Id].Name}");

    /// <summary>Gold for breaking it down: the rarity's value, growing with item level.</summary>
    [JsonIgnore]
    public int SalvageValue => Mathf.RoundToInt(RarityDef.Salvage * (1f + (Level - 1) / 20f));

    /// <summary>What a trader asks for it: four times its salvage, half as much again for every affix.</summary>
    [JsonIgnore]
    public int BuyPrice => Mathf.RoundToInt(SalvageValue * 4 * Mathf.Pow(AffixPriceStep, Affixes.Length));

    private const float AffixPriceStep = 1.5f;

    /// <summary>What wearing it adds to the wearer's stats: its base properties (local affixes applied), its implicit, its other affixes.</summary>
    public IEnumerable<(string Stat, float Value)> Bonuses()
    {
        foreach (var prop in Def.Props)
            if (Prop(prop.Stat) is var value and > 0f)
                yield return (prop.Stat, value);
        if (Def.Implicit is { } implicitRoll)
            yield return (implicitRoll.Stat, Implicit);
        // A local affix is already in its property above.
        foreach (var affix in Affixes)
            if (ItemCatalog.Affixes[affix.Id] is { Local: false } def)
                yield return (def.Roll.Stat, affix.Value);
    }

    /// <summary>Each stat that wearing <paramref name="item"/> in place of <paramref name="worn"/> (null: an empty slot) changes, and by how much.</summary>
    public static IEnumerable<(string Stat, float Delta)> Compare(Item item, Item? worn)
    {
        var after = Sum(item);
        var before = worn != null ? Sum(worn) : [];
        foreach (var stat in after.Keys.Union(before.Keys))
        {
            var delta = after.GetValueOrDefault(stat) - before.GetValueOrDefault(stat);
            if (Mathf.Abs(delta) > 1e-4f)
                yield return (stat, delta);
        }
    }

    private static Dictionary<string, float> Sum(Item item)
    {
        var sum = new Dictionary<string, float>();
        foreach (var (stat, value) in item.Bonuses())
            sum[stat] = sum.GetValueOrDefault(stat) + value;
        return sum;
    }

    /// <summary>The tooltip: the base's properties (local affixes applied), the implicit, one line per affix, then a Legendary's effect.</summary>
    public IEnumerable<string> Lines()
    {
        foreach (var prop in Def.Props)
            yield return prop.Text(Prop(prop.Stat));
        if (Def.Implicit is { } implicitRoll)
            yield return implicitRoll.Text(Implicit);
        foreach (var affix in Affixes)
            yield return ItemCatalog.Affixes[affix.Id].Roll.Text(affix.Value);
        if (UniqueDef is { } unique)
            yield return $"◆ {unique.Describe(Level)}";
    }

    public override string ToString() =>
        $"{Name} [{RarityDef.Name} {Slot}, ilvl {Level}]: {string.Join(", ", Lines())}";
}
