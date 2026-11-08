using Godot;

namespace Arc.Items;

/// <summary>
/// An unopened chest, carried home instead of opened on the spot when the game
/// stashes chests: its drop rule's rarity floor, and the item level (the threat) and affix cap of the moment it
/// was picked up. Its item is rolled only when it is opened, wherever the game opens it. Plain data, so
/// the profile saves it as-is. Chests with the same floor stack.
/// </summary>
public sealed record Chest
{
    public int MinRarity { get; init; }
    public int Level { get; init; } = 1;
    public int MaxAffixes { get; init; } = int.MaxValue;

    /// <summary>Rolls the chest's item for <paramref name="archetype"/>.</summary>
    public Item Open(RandomNumberGenerator rng, string archetype) =>
        ItemRoller.Roll(rng, archetype, Level, MinRarity, maxAffixes: MaxAffixes);

    /// <summary>A stack's name by its floor: "Chest" when anything can come out, else "Rare+ chest".</summary>
    public static string StackName(int minRarity) =>
        minRarity <= 0 ? "Chest" : $"{ItemCatalog.Rarities[Math.Min(minRarity, ItemCatalog.Rarities.Count - 1)].Name}+ chest";
}
