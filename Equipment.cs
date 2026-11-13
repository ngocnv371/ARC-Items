namespace Arc.Items;

/// <summary>
/// What the hero wears, one item per <see cref="ItemSlot"/>, and a bag of unworn items: the finds
/// it carries through a run, or (on a stash screen) the profile's stash. Beside the bag, unopened
/// <see cref="Chests"/> take no pockets: they stack by rarity floor. Every change of what is worn
/// hands the gear's bonuses to the game (<c>apply</c>), which pushes them into its stats.
/// </summary>
public sealed class Equipment
{
    private readonly Dictionary<ItemSlot, Item> _worn = [];
    private List<Item> _bag;
    private readonly Action<IEnumerable<(string Stat, float Value)>>? _apply;

    /// <summary>
    /// <paramref name="bag"/> is used as-is (a stash screen hands in the stash so changes land in the
    /// profile); without one the bag starts empty. <paramref name="capacity"/> defaults to the run bag's.
    /// <paramref name="chests"/> likewise (a stash screen hands in the hero's unopened chests).
    /// <paramref name="apply"/> receives every worn item's bonuses each time what is worn changes
    /// (stats may repeat; the game sums them); null when nothing needs them (a stash screen).
    /// </summary>
    public Equipment(string archetype, Action<IEnumerable<(string Stat, float Value)>>? apply, List<Item>? bag = null, int? capacity = null, List<Chest>? chests = null)
    {
        Archetype = archetype;
        _apply = apply;
        _bag = bag ?? [];
        Capacity = capacity ?? ItemCatalog.BagSize;
        Chests = chests ?? [];
    }

    public string Archetype { get; }

    /// <summary>
    /// Lets the hero wear any class's items (a game whose classes only steer what drops and which
    /// affixes roll, not what may be worn).
    /// </summary>
    public bool AnyClass { get; init; }

    public IEnumerable<Item> Worn => _worn.Values;

    /// <summary>Everything the worn items add, item by item (stats may repeat).</summary>
    public IEnumerable<(string Stat, float Value)> Bonuses => _worn.Values.SelectMany(i => i.Bonuses());

    public IReadOnlyList<Item> Bag => _bag;

    public int Capacity { get; }

    /// <summary>Unopened chests, in the order picked up.</summary>
    public List<Chest> Chests { get; }

    /// <summary>The chests by rarity floor, lowest first: each floor's count.</summary>
    public IEnumerable<(int MinRarity, int Count)> ChestStacks =>
        Chests.GroupBy(c => c.MinRarity).OrderBy(g => g.Key).Select(g => (g.Key, g.Count()));

    public bool BagFull => _bag.Count >= Capacity;

    public event Action? Changed;

    public Item? In(ItemSlot slot) => _worn.GetValueOrDefault(slot);

    public bool CanWear(Item item) => AnyClass || item.Def.Fits(Archetype);

    /// <summary>Puts <paramref name="item"/> on and returns what it replaced (null if the slot was empty or the class can't wear it).</summary>
    public Item? Equip(Item item)
    {
        if (!CanWear(item))
            return null;
        var old = In(item.Slot);
        _worn[item.Slot] = item;
        Apply();
        return old;
    }

    /// <summary>Takes off what is in <paramref name="slot"/> and returns it.</summary>
    public Item? Unequip(ItemSlot slot)
    {
        if (!_worn.Remove(slot, out var old))
            return null;
        Apply();
        return old;
    }

    /// <summary>Works on another bag from now on (a stash tab), as-is like the one given at construction.</summary>
    public void SwitchBag(List<Item> bag)
    {
        _bag = bag;
        Changed?.Invoke();
    }

    /// <summary>Puts <paramref name="item"/> in the bag; false when it is full.</summary>
    public bool Keep(Item item)
    {
        if (BagFull)
            return false;
        _bag.Add(item);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Stows an unopened chest; it needs no pocket.</summary>
    public void AddChest(Chest chest)
    {
        Chests.Add(chest);
        Changed?.Invoke();
    }

    /// <summary>Takes out the first chest with rarity floor <paramref name="minRarity"/> (to open it), or null if none is left.</summary>
    public Chest? TakeChest(int minRarity)
    {
        var index = Chests.FindIndex(c => c.MinRarity == minRarity);
        if (index < 0)
            return null;
        var chest = Chests[index];
        Chests.RemoveAt(index);
        return chest;
    }

    /// <summary>Wears the bag's item at <paramref name="index"/>; what it replaces takes its place in the bag.</summary>
    public void EquipFromBag(int index)
    {
        var item = _bag[index];
        if (!CanWear(item))
            return;
        _bag.RemoveAt(index);
        if (Equip(item) is { } old)
            _bag.Insert(index, old);
        Changed?.Invoke();
    }

    /// <summary>Moves what is worn in <paramref name="slot"/> to the bag; false when there is no room or nothing worn.</summary>
    public bool Stow(ItemSlot slot)
    {
        if (BagFull || In(slot) == null)
            return false;
        Keep(Unequip(slot)!);
        return true;
    }

    /// <summary>Takes the bag's item at <paramref name="index"/> out (to salvage it).</summary>
    public Item TakeFromBag(int index)
    {
        var item = _bag[index];
        _bag.RemoveAt(index);
        Changed?.Invoke();
        return item;
    }

    private void Apply()
    {
        _apply?.Invoke(Bonuses);
        Changed?.Invoke();
    }
}
