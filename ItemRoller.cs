using Godot;

namespace Arc.Items;

/// <summary>
/// Rolls new items: a slot, a base in it that the class can wear and the item level reaches, its
/// properties, a rarity, then that rarity's affixes. Only what the game implements rolls
/// (<see cref="ItemCatalog.Rolls(ItemBase)"/>).
/// </summary>
public static class ItemRoller
{
    /// <summary>
    /// A random item for <paramref name="archetype"/> at <paramref name="level"/> (the threat when
    /// it drops). <paramref name="minRarity"/> raises the floor (a boss drops Rare or better);
    /// <paramref name="baseId"/>, <paramref name="slot"/>, <paramref name="rarityId"/> and
    /// <paramref name="uniqueId"/> pin those choices (a unique only lands on a rarity that carries
    /// one, in a slot it allows). <paramref name="maxAffixes"/> caps the affixes (drops early in a
    /// run): rarities that need more never roll, and the rest roll no more than the cap.
    /// <paramref name="affixIds"/> are rolled first (those that fit the base and class), then random
    /// ones fill the rarity's count.
    /// </summary>
    public static Item Roll(RandomNumberGenerator rng, string archetype, int level, int minRarity = 0, string? baseId = null, string? rarityId = null, string? uniqueId = null,
        int maxAffixes = int.MaxValue, IReadOnlyList<string>? affixIds = null, ItemSlot? slot = null)
    {
        level = Math.Clamp(level, 1, ItemCatalog.MaxLevel);
        var def = baseId != null ? ItemCatalog.Bases[baseId] : PickBase(rng, archetype, level, slot);
        var rarities = ItemCatalog.Rarities;
        var rarity = rarityId != null
            ? ItemCatalog.Rarity(rarityId) ?? rarities[0]
            : Pick(rng, Allowed(rarities, minRarity, maxAffixes), r => r.Weight);
        var count = rng.RandiRange(rarity.AffixMin, Math.Max(rarity.AffixMin, Math.Min(rarity.AffixMax, maxAffixes)));

        var pool = ItemCatalog.Affixes.Values.Where(a => a.Fits(def, archetype) && ItemCatalog.Rolls(a)).ToList();
        var affixes = new List<ItemAffix>();
        foreach (var id in affixIds ?? [])
            if (pool.FirstOrDefault(a => a.Id == id) is { } chosen)
            {
                pool.Remove(chosen);
                affixes.Add(new ItemAffix(chosen.Id, Value(rng, chosen.Roll, level)));
            }
        while (affixes.Count < count && pool.Count > 0)
        {
            var affix = Pick(rng, pool, a => a.Weight);
            pool.Remove(affix);
            affixes.Add(new ItemAffix(affix.Id, Value(rng, affix.Roll, level)));
        }

        string? unique = null;
        if (rarity.Unique)
        {
            var uniques = ItemCatalog.Uniques.Values.Where(u => u.Slots.Contains(def.Slot) && ItemCatalog.Rolls(u)).ToList();
            unique = uniqueId != null && uniques.Any(u => u.Id == uniqueId) ? uniqueId
                : uniques.Count > 0 ? Pick(rng, uniques, _ => 1f).Id : null;
        }

        return new Item
        {
            Id = $"{rng.Randi():x8}{rng.Randi():x8}",
            Base = def.Id,
            Rarity = rarity.Id,
            Level = level,
            Implicit = def.Implicit is { } implicitRoll ? Value(rng, implicitRoll, level) : 0f,
            Props = def.Props.ToDictionary(p => p.Stat, p => Value(rng, p, level)),
            Affixes = [.. affixes],
            Unique = unique,
        };
    }

    /// <summary>
    /// The bases <paramref name="archetype"/> could find at <paramref name="level"/>, with the chance
    /// of each: every slot equally likely, and within a slot each base by its weight, halved for every
    /// <see cref="ItemCatalog.BaseFalloff"/> levels it lies below <paramref name="level"/>. A slot with
    /// nothing in reach yet offers its lowest bases.
    /// </summary>
    public static IReadOnlyList<(ItemBase Base, float Chance)> Odds(string archetype, int level, ItemSlot? slot = null)
    {
        var bySlot = ItemCatalog.Bases.Values
            .Where(b => b.Fits(archetype) && ItemCatalog.Rolls(b) && (slot == null || b.Slot == slot))
            .GroupBy(b => b.Slot)
            .ToList();
        var odds = new List<(ItemBase, float)>();
        foreach (var group in bySlot)
        {
            var reach = group.Where(b => b.Level <= level).ToList();
            if (reach.Count == 0)
            {
                var lowest = group.Min(b => b.Level);
                reach = [.. group.Where(b => b.Level == lowest)];
            }
            var weights = reach.Select(b => b.Weight * Mathf.Pow(0.5f, Mathf.Max(0, level - b.Level) / Mathf.Max(0.01f, ItemCatalog.BaseFalloff))).ToList();
            var total = weights.Sum();
            for (var i = 0; i < reach.Count; i++)
                odds.Add((reach[i], weights[i] / total / bySlot.Count));
        }
        return odds;
    }

    private static ItemBase PickBase(RandomNumberGenerator rng, string archetype, int level, ItemSlot? slot)
    {
        var odds = Odds(archetype, level, slot);
        if (odds.Count == 0)
            throw new InvalidOperationException($"items: no base can drop for '{archetype}'{(slot != null ? $" in {slot}" : "")}");
        return Pick(rng, [.. odds], o => o.Chance).Base;
    }

    /// <summary>The rarities from <paramref name="minRarity"/> up that fit under the affix cap; the floor alone if none does.</summary>
    private static List<RarityDef> Allowed(IReadOnlyList<RarityDef> rarities, int minRarity, int maxAffixes)
    {
        var floor = rarities.Skip(Math.Min(minRarity, rarities.Count - 1)).ToList();
        var allowed = floor.Where(r => r.AffixMin <= maxAffixes).ToList();
        return allowed.Count > 0 ? allowed : [floor[0]];
    }

    private static float Value(RandomNumberGenerator rng, StatRoll roll, int level)
    {
        var range = roll.At(level);
        return roll.Snap(rng.RandfRange(range.X, range.Y));
    }

    private static T Pick<T>(RandomNumberGenerator rng, List<T> items, Func<T, float> weight)
    {
        var total = items.Sum(weight);
        var roll = rng.Randf() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0f)
                return item;
        }
        return items[^1];
    }
}
