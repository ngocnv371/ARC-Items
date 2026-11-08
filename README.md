# ARC Items

The shared loot library for the ARC games: item bases, rarities, affixes and Legendaries, rolling
them, and the equipment a hero wears and carries. Every game's Steel Bascinet is the same helm and
rolls the same way.

Items only roll. What a stat or a Legendary *does* (how armour cuts a blow, what Emberstep burns)
is the game's: it reads the worn gear's bonuses into its own stats and wires up the effects it
supports. A mod one game has no use for never rolls there.

```csharp
// At startup, before anything touches items:
ItemCatalog.Setup(new ItemSetup
{
    Files = ["res://data/items.json"],          // the game's overlay (drops, bag size, its own bases)
    Stats = Stat.Implemented.Contains,          // stats the game gives meaning to
    Uniques = GearEffects.Implemented.Contains, // Legendaries the game wires up
});

var item = ItemRoller.Roll(rng, "warrior", level: 24, minRarity: 2);
var equipment = new Equipment("warrior", bonuses => stats.SetSource("gear", bonuses));
equipment.Equip(item);
foreach (var line in item.Lines()) GD.Print(line);   // "27 armour", "+42% armour", "+18 max health"
```

## Setting up

Mount this repo at `addons/arc_items/` (the catalog reads `data/items.json` from there; another
mount point sets `ItemCatalog.Root`). It is plain C# compiled with the game's project, like ARC FX:
no scenes, no nodes.

## Bases, tiers and properties

A base's **props** are the baseline every item of it carries: a weapon's `weapon_damage`, an
armour piece's `armour` and/or `evasion`. They roll in the base's own fixed range, so the base
matters: an Iron Sallet (17–21 armour) beats a Bronze Helm (10–13) before any affix lands.

Each base has a **level**, the item level it starts dropping at. Each armour slot runs two lines
of six tiers and three exotic hybrids:

| Line | Tiers (from item level 1, 8, 15, 22, 30, 36) | Gives |
|---|---|---|
| Metal | wood → bronze → iron → steel → dwarven steel → mithril | armour |
| Leather | hide → leather → studded → wolfskin → shadowsilk → wyrmskin | evasion |
| Exotic | bone (12), crystal (20), dragonscale (34) | both, or one, plus an implicit |

Weapons climb the same way, one line per class (axes, bows, staves). Within a slot, a base drops
half as often for every `baseFalloff` (8) levels it lies below the item level: newer tiers take
over, older ones linger. Every slot is equally likely.

**Local** affixes multiply their own item's prop: `of the Bulwark` (+15–80% armour), `of the Fox`
(evasion), `of Heft` (weapon damage). They only roll on bases that have that prop. So a high tier
with a good local roll is the best piece, and an exotic base's implicit can still beat it: there
is something to min-max.

## The data, and a game's overlay

`data/items.json` holds the shared catalog. The game's files (`ItemSetup.Files`) are laid over it:

- `rarities`, `bases`, `affixes` and `uniques` merge **by id**: a new id is added; an existing one
  has the given fields replaced (`{ "id": "thorns", "weight": 2 }`); `{ "id": "x", "remove": true }`
  drops it.
- `propFormats` merges by key; everything else (`maxLevel`, `bagSize`, `stashSize`, `drops`,
  `dropAffixes`, `baseFalloff`) is replaced whole.

What rolls is narrowed further by `ItemSetup.Stats` and `ItemSetup.Uniques`: a base, affix or
implicit on a stat the game does not implement never rolls, nor does a Legendary it does not wire
up. Items saved with them still load and show.

**Saved items.** `Item` is plain data for System.Text.Json. Keep base, affix and unique ids stable;
retire a base (`"retired": true`) instead of deleting it, so old items still load. An item saved
before its base had a prop shows the middle of the prop's range; one saved with the old weapon
`Power` keeps that as its weapon damage.

## What's here

| File | Contents |
|---|---|
| `ItemDefs.cs` | `ItemSlot`, `ItemStats`, `RarityDef`, `ItemBase`, `AffixDef`, `UniqueDef`, `StatRoll`, `DropRule` |
| `ItemCatalog.cs` | `ItemSetup`, loading and merging the data, what may roll, tooltip wording |
| `Item.cs` | `Item` (props, implicit, affixes, bonuses, tooltip lines, comparison) |
| `ItemRoller.cs` | Rolling items, and `Odds` (which bases a class finds at a level, and how often) |
| `Equipment.cs` | Worn slots, the bag, unopened chests; hands the bonuses to the game |
| `Chest.cs` | An unopened chest, rolled when opened |
| `data/items.json` | The shared catalog |

## Games using it

- [ARC Dungeons](https://github.com/ngocnv371/ARC-Dungeons): implements armour (cuts each blow by
  armour ÷ (armour + 20 × the blow)) and evasion (dodges evasion ÷ (evasion + 400) of blows).
- [ARC Rogue Guys](https://github.com/ngocnv371/ARC-Rogue-Guys): not moved over yet; its
  `src/Items` is the code this library came from.
