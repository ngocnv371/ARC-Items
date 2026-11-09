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
armour piece's `armour`, `evasion` and/or `energy_shield`, a shield's `block` too. They roll in
the base's own fixed range, so the base matters: an Iron Sallet (12–15 armour) beats a Bronze Helm
(9–11) before any affix lands.

Each base has a **level**, the item level it starts dropping at. Item levels run 1–84
(`maxLevel`), as in Path of Exile.

**Armour** follows Path of Exile's armour ladders, on its own item levels. Body armour, helms,
boots and gloves each have three defences and every pair of them. Each of the six lines has ten
tiers from item level 1, 9, 17, 25, 35, 45, 53, 62, 73 and 84, plus one special base at level
64–71. Body armour's lines:

| Line | Tiers | Gives | Special |
|---|---|---|---|
| Plate | wooden → copper → bronze → iron → steel → tempered → dwarven → mithril → adamant → starmetal | armour 3 → 134 | Bastion Plate: less damage taken |
| Leather | hide → rawhide → leather → studded → wolfskin → bearskin → shadowsilk → wyrmskin → manticore → wyvernhide | evasion 3 → 134 | Nightstalker Garb: movement speed |
| Robe | linen → woolen → velvet → runecloth → moonsilk → spiritweave → starweave → voidsilk → aetherweave → celestial | energy shield 4 → 90 | Seer's Raiment: cooldown speed |
| Scale | patchwork → copper → bronze → iron → steel → tempered → dwarven → mithril → adamant → wyvernscale | armour + evasion | Dragonscale Mail: fire resistance, armour-heavy |
| Mail | rusty → copper → bronze → iron → runed → consecrated → dwarven → mithril → adamant → starmetal | armour + energy shield | Rimeguard Hauberk: cold resistance, shield-heavy |
| Garb | felt → embroidered → hexed → ritual → shade → mistweave → ghostleather → wraithskin → phantomweave → eclipse | evasion + energy shield | Bloodbound Raiment: max health, shield-heavy |

Like PoE's, the curve starts low and steepens: a starter chest is a token 3–4, mid-game tiers add
about a quarter each, and the last two tiers jump about 30% apiece. A hybrid carries about 58% of
a pure base's armour or evasion and 56% of its energy shield in each of its two defences, so the
two together come to about 115%. A special sits just above the tier before it, with its defence
lopsided toward one side or traded for its implicit. Energy shield runs at about two-thirds of
armour's scale. A game that doesn't implement `energy_shield` never drops the robe, mail or garb
lines.

Helms, boots and gloves run the same lines and levels at PoE's share of body armour's defence: a
helm about half (Starmetal Helm 53–66 armour), boots and gloves about 30% (Starmetal Greaves
32–40). Their pieces are named for the slot: a plate helm, greaves and gauntlets; a leather hood,
boots and bracers; a robe line's circlet, slippers and gloves; a scale barbute; a mail coif; a
garb mask, footwraps and mitts. The hybrid specials are sets across all four slots: Dragonscale
(fire resistance), Rimeguard (cold resistance) and Bloodbound (max health). The pure-line
specials differ by slot:

| Slot | Plate | Leather | Robe |
|---|---|---|---|
| Helm | Bastion Helm: max health | Nightstalker Hood: crit chance | Seer's Circlet: cooldown speed |
| Boots | Bastion Greaves: max health | Nightstalker Boots: movement speed | Seer's Slippers: cooldown speed |
| Gloves | Brawler's Gauntlets: physical damage | Nightstalker Bracers: projectile damage | Seer's Gloves: damage |

**Shields** follow PoE's shields instead: ten tiers from item level 1 to 70 (1, 8, 16, 23, 30, 38,
46, 54, 62, 70), any class may carry one, and every shield has a `block` chance (22–26%) besides
its defence. Their tiers cycle three builds rather than climbing straight. A light shield (hide,
reed, wicker) has the most block and the most of its line's implicit. A balanced one (wood) sits
in the middle. A heavy one (metal) has the most defence, the least block, and little or none of
the implicit. So a newer light shield can carry less armour than the heavy one before it.

| Line | Gives | Implicit (light → heavy) |
|---|---|---|
| Pavise | armour | max health, 8–12 / 12–16 / 4–8 |
| Buckler | evasion | movement speed, 6% / 4% / 2% |
| Ward | energy shield | damage, 8–12% / 4–8% / none |
| Targe | armour + evasion | max health/s, 0.15–0.2% / 0.08–0.12% / none |
| Heater | armour + energy shield | a resistance (fire, then cold, then lightning), 10–12% / 6–8% / none |
| Parma | evasion + energy shield | dodge chance, 4–5% / 3–4% / 2–3% |

A game that doesn't implement `block` never drops a shield. `of Deflection` adds block chance on a
shield.

Weapons climb the same way, one line per class (axes, bows, staves), from item level 1 to 75.
Within a slot, a base drops
half as often for every `baseFalloff` (17) levels it lies below the item level: newer tiers take
over, older ones linger. Every slot is equally likely.

**Local** affixes multiply their own item's prop: `of the Bulwark` (+15–80% armour), `of the Fox`
(evasion), `of the Aegis` (energy shield), `of Heft` (weapon damage). They only roll on bases that have that prop. So a high tier
with a good local roll is the best piece, and a special base's implicit can still beat it: there
is something to min-max.

**Energy shield** has a family of its own besides the bases and `of the Aegis`: `of the Veil` (+x
energy shield, jewellery), `of the Sanctum` (`energy_shield_mult`, increased maximum energy shield
over everything worn), `of Replenishing` (`energy_shield_recharge`, increased recharge rate) and
`of Renewal` (`energy_shield_delay`, the recharge starts that share sooner). How the shield soaks
and recharges is the game's: it implements those stats or they never roll.

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
| `icons/<base>.png` | Each base's icon, square, framed; `ItemCatalog.IconPath` finds it (or the closest drawn one) |
| `tools/` | The icon pipeline (below); Godot skips the folder (`.gdignore`) |

## Drawing icons

`tools/gen_icons.py` holds a prompt for every base and draws the icons with the local ComfyUI
(`D:/dev/comfy-helper`). It renders `tools/drafts/<base>.png`, which isn't committed. Then it
frames the draft under the shared ring cut from `tools/panel_frame.png` (`tools/frame_icons.py`),
writing `icons/<base>.png`. Finally it imports the icon with a windowless Godot (`--headless`, on a
throwaway project that mounts this repo at `res://addons/arc_items`), so the `.import` file is the
one a game would write.

```bash
python tools/gen_icons.py                     # every base with a prompt and no icon yet
python tools/gen_icons.py bearskin_coat       # redraw one
python tools/gen_icons.py --no-render x y     # re-frame and re-import drafts already there
```

A new base gets its icon once you add its prompt to `SUBJECTS`. Name its material and spell out
the colour, so tiers read apart at a glance.

## Games using it

- [ARC Dungeons](https://github.com/ngocnv371/ARC-Dungeons): implements armour (cuts each blow by
  armour ÷ (armour + 20 × the blow)) and evasion (dodges evasion ÷ (evasion + 400) of blows).
- [ARC Rogue Guys](https://github.com/ngocnv371/ARC-Rogue-Guys): not moved over yet; its
  `src/Items` is the code this library came from.
