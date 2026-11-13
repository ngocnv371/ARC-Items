"""Renders ARC Items' item icons with the local ComfyUI, through D:/dev/comfy-helper's comfy-icon.mjs
(Z-Image Turbo, then BiRefNet background removal), and imports them:

  1. render  tools/drafts/<base>.png, 768 px, the object alone on a transparent background
             (drafts are not committed)
  2. place   icons/<base>.png, 256 px: the draft trimmed to the object, with a little room round it.
             No frame and no backdrop: a game lays the icon on a tile of its own.
  3. import  icons/<base>.png.import, by a windowless Godot (--headless) on a throwaway project
             that mounts this repo at res://addons/arc_items, the path the games mount it at

With no names it draws every base in data/items.json that has a prompt below and no icon yet;
named bases are redrawn, and --all redraws every one. --no-render places and imports drafts
already there. A base's seed is fixed by its id, so a redraw gives the same picture.

    python tools/gen_icons.py [base ...] [--all] [--no-render]

Set COMFY_ICON to another comfy-icon.mjs, GODOT to another Godot 4.7 .NET editor.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DRAFTS = ROOT / "tools" / "drafts"
ICONS = ROOT / "icons"
SIZE = 256

GEN = Path(os.environ.get("COMFY_ICON", "D:/dev/comfy-helper/comfy-icon.mjs"))
GODOT = os.environ.get("GODOT", "D:/installed/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/"
                       "Godot_v4.7.2-stable_mono_win64_console.exe")

FRAME = ("A painting of one object: {}.\n\n"
         "A single object, shown whole at a slight three-quarter angle, centred and filling most of the "
         "square, floating on a perfectly plain flat dark grey background. Nothing else is in the "
         "picture: no frame, no border, no ground, no cast shadow, no hands, no wearer.\n\n"
         "Polished hand-painted fantasy equipment art for the inventory of a modern anime RPG: clean confident "
         "shapes, smooth soft shading, bright crisp highlights and a thin light rim along the edges, "
         "rich saturated colours, a sharp readable silhouette.")
NEGATIVE = ("blurry ugly bad, text, letters, numbers, watermark, frame, border, card, tile, app icon, rounded square, button, multiple objects, "
            "grid, person, face, mannequin, hands, floor, scenery")

# Every tier's material is named first and its colour spelled out, so the tiers read apart at a
# glance: wood, bronze, iron, steel, dwarven steel, mithril; hide, leather, studded, wolfskin,
# shadowsilk, wyrmskin; and the bone, crystal and dragonscale hybrids.
WOOD = "made of carved pale brown oak wood planks with visible wood grain, held by rough rope lashings"
BRONZE = "made of warm golden orange bronze, polished and slightly green with verdigris in the seams"
IRON = "made of dark grey rough hammered iron, dull and pitted, with heavy round rivets"
STEEL = "made of bright polished silver steel, clean and gleaming with sharp reflections"
DWARVEN = ("made of thick dark gunmetal dwarven steel with heavy gold trim and glowing orange "
           "angular runes engraved into it")
MITHRIL = ("made of shining pale silver blue mithril that glows softly with cool white blue light, "
           "elegant elven filigree")
HIDE = "made of rough untanned shaggy brown animal hide with the fur still on, tied with sinew"
LEATHER = "made of smooth tanned brown leather with neat stitching and laces"
STUDDED = "made of dark brown hardened leather covered in rows of small round iron studs"
WOLFSKIN = "made of thick grey wolf fur pelts over dark leather, a shaggy grey wolf pelt"
SHADOWSILK = ("made of flowing midnight black and deep violet silk that seems to drink the light, "
              "wisps of shadow curling off its edges")
WYRMSKIN = ("made of iridescent emerald green reptile scale leather, glinting turquoise and gold, "
            "supple wyrm hide")
BONE = "made of pale ivory yellowed bones and a skull, lashed together with dark leather cords"
CRYSTAL = "made of translucent glowing cyan crystal facets, shining with inner light"
DRAGON = ("made of overlapping large crimson red dragon scales with dark edges, glowing faintly "
          "like embers, horned")
# ARC Items' body armour ladder (Path of Exile's): the tiers added beside those above.
COPPER = "made of shiny reddish orange copper, hammered, with a few spots of green patina"
TEMPERED = "made of blued tempered steel with a dark blue purple sheen and bright polished edges"
ADAMANT = ("made of black adamant metal with a deep dark green sheen, sharp faceted angular plates "
           "edged in bright silver")
STARMETAL = ("made of deep midnight blue starmetal speckled with tiny glowing white stars like a "
             "night sky, edged in pale gold, softly radiant")
RAWHIDE = "made of stiff pale tan rawhide, roughly cut, laced with thongs"
BEARSKIN = "made of thick shaggy dark brown bear fur over leather, with bear claws hanging at the collar"
MANTICORE = ("made of tawny lion coloured manticore hide with a dark red mane collar and black "
             "barbed spines along the shoulders")
WYVERNHIDE = ("made of deep teal and black wyvern hide with leathery wing membrane panels and small "
              "pale horn spikes, glinting")
LINEN = "made of plain undyed off white linen cloth, simple and a little rumpled, a rope belt"
WOOL = "made of thick soft dyed dark red wool with a simple woven border"
VELVET = "made of rich deep royal blue velvet with gold thread trim"
RUNECLOTH = "made of grey cloth embroidered all over with faintly glowing pale blue runes"
MOONSILK = "made of shimmering pale silver white silk that glows like moonlight, crescent moon motifs"
SPIRITWEAVE = ("made of translucent ghostly sea green fabric that drifts like mist, faint glowing "
               "spirit wisps around it")
STARWEAVE = "made of deep indigo fabric woven with glittering golden constellations"
VOIDSILK = ("made of pitch black silk with a swirling purple void inside it like a starless "
            "galaxy, its edges dissolving into dark smoke")
AETHERWEAVE = ("made of luminous white and pale gold fabric threaded with glowing prismatic rainbow "
               "light, floating hems")
CELESTIAL = ("made of radiant white and gold celestial silk with a glowing golden halo motif and "
             "sunburst embroidery, shining brightly")
FELT = "made of plain thick grey brown felt, soft and lumpy"
GHOST = "made of pale bluish white translucent ghostly leather, faintly glowing and see through"
WRAITH = "made of tattered dark grey wraith skin and black rags, cold blue light glowing through the tears"
PHANTOM = "made of shifting smoky lavender fabric that fades into nothing at its edges, phantom like"
ECLIPSE = ("made of black leather and dark silk with a glowing ring of golden sunlight around a "
           "black disc emblem on the chest, a solar eclipse")

SUBJECTS = {
    "notched_axe": "a plain old woodcutter's axe with a chipped and notched iron head on a worn "
                   "wooden handle bound with a strip of cloth, simple and battered",
    # Axes, by tier.
    "hatchet": "a small one handed hatchet with a plain little grey iron head on a short straight "
               "wooden handle, simple and light",
    "bronze_axe": "a one handed bronze battle axe with a gleaming warm golden orange bronze head, "
                  "a crescent blade with a ridge, on a wooden haft wrapped in leather",
    "iron_broadaxe": "a broad heavy iron broadaxe with a very wide flat dark grey hammered iron "
                     "blade, studded with rivets, on a thick dark wooden haft",
    "steel_greataxe": "a huge two handed double bladed greataxe of bright polished silver steel, "
                      "gleaming mirror sharp edges, on a long haft with a steel spike on top",
    "runed_dwarven_axe": "an ornate dwarven war axe of dark gunmetal steel with heavy gold trim, its "
                         "broad blade engraved with glowing orange angular runes, a gold banded haft",
    # Bows, by tier.
    "worn_shortbow": "a plain old short wooden bow, scuffed and worn, with a frayed string and a cloth grip",
    "shortbow": "a small simple short bow of pale wood with a plain string and a leather grip",
    "ashwood_bow": "a slender bow of smooth pale grey ashwood with a wrapped leather grip and carved tips",
    "composite_bow": ("a short curved composite bow layered of wood, pale horn and sinew in bands, "
                      "with a bound leather grip"),
    "horn_bow": "a sharply recurved bow made from two dark ridged ram horns joined at a bronze grip",
    "wyrmbone_bow": ("a large elegant longbow carved from pale ivory wyrm bone with emerald green "
                     "scale inlays and a fang at each tip, glowing faintly green"),
    # Staves, by tier.
    "apprentice_staff": "a plain simple wooden walking staff with a knob at the top, worn and humble",
    "willow_staff": "a thin bendy willow wood staff with a few green leaves sprouting near its top",
    "ash_staff": "a straight smooth pale ash wood staff with a carved spiral top and a small blue stone",
    "ironshod_staff": ("a sturdy dark wooden staff shod with dark grey iron bands and a heavy iron cap "
                       "studded with rivets"),
    "bone_staff": ("a staff built of pale yellowed bones with a small horned skull on top, eye sockets "
                   "glowing green"),
    "archmage_staff": ("a tall ornate staff of deep midnight blue starmetal with a golden crescent at "
                       "its top cradling a large glowing white star crystal, radiant"),
    # Helms.
    "wooden_helm": f"a crude round helmet {WOOD}, with a simple wooden nose guard",
    "bronze_helm": f"an ancient crested helmet with cheek guards {BRONZE}",
    "iron_sallet": f"a sallet helmet with a sweeping rear tail and a narrow eye slit {IRON}",
    "steel_bascinet": f"a pointed bascinet helmet with a hinged pointed visor full of breathing holes {STEEL}",
    "dwarven_greathelm": f"a massive squat dwarven greathelm with a braided beard faceplate {DWARVEN}",
    "mithril_helm": f"an elegant winged full helm {MITHRIL}",
    "hide_hood": f"a simple hood {HIDE}",
    "leather_hood": f"a leather hood with a short shoulder cape {LEATHER}",
    "studded_cap": f"an empty round close fitting leather helmet cap with ear flaps, {STUDDED}",
    "wolfskin_hood": ("an empty hooded shoulder cape of thick shaggy grey wolf fur, the hood topped with a "
                      "flat wolf pelt and two pointed wolf ears, the dark hollow inside the hood empty"),
    "shadowsilk_cowl": f"a deep cowl hiding only darkness and two faint violet glints inside, {SHADOWSILK}",
    "wyrmskin_mask": f"a sinister half face mask with a crest of small horns, {WYRMSKIN}",
    "bone_helm": ("an empty open faced war helmet built from pale ivory bone plates and ribs lashed with "
                  "dark leather cords, two curling horns on its sides, a hollow dark opening for the face"),
    "crystal_circlet": f"a slender circlet crown {CRYSTAL}, a large gem at its brow",
    "dragonscale_helm": f"a fearsome full helm with swept back horns {DRAGON}",
    # Body armour.
    "wooden_cuirass": f"a crude breastplate cuirass {WOOD}",
    "bronze_breastplate": f"a muscled breastplate cuirass {BRONZE}",
    "iron_plate": f"a heavy chest plate armour with pauldrons {IRON}",
    "steel_plate": f"a full knight's plate armour chest piece with pauldrons {STEEL}",
    "dwarven_plate": f"a massive bulky dwarven chest plate armour with huge pauldrons {DWARVEN}",
    "mithril_plate": f"an elegant fitted chest plate armour with curved pauldrons {MITHRIL}",
    "hide_vest": f"a sleeveless primitive vest {HIDE}",
    "leather_armour": f"a fitted leather jerkin armour with sleeves and a belt {LEATHER}",
    "studded_leather": f"a sleeveless armour vest with shoulder guards {STUDDED}",
    "wolfskin_coat": f"a long hooded coat with a big fur collar {WOLFSKIN}",
    "shadowsilk_robe": f"a long hooded robe {SHADOWSILK}",
    "wyrmskin_coat": f"a long fitted coat {WYRMSKIN}",
    "bone_mail": ("an empty sleeveless body armour cuirass shaped like a torso, its front a pale ivory "
                  "ribcage of bones over dark leather, bone shoulder plates, lashed with leather cords"),
    "crystal_raiment": f"a flowing robe raiment set with shards {CRYSTAL}",
    "dragonscale_mail": f"a scale mail chest armour {DRAGON}",
    # The armour ladders' specials (helm, boots, gloves); the rest compose from their material (LOOK).
    "bastion_helm": (f"an empty massive full helm with a tall crest shaped like castle battlements {MITHRIL}"),
    "nightstalker_hood": f"an empty assassin's hood with a face scarf, {SHADOWSILK}",
    "seers_circlet": ("a slender silver circlet with a large glowing violet eye shaped gem at its brow, "
                      "mystical"),
    "rimeguard_coif": ("an empty chainmail coif hood of frosted pale blue steel rings rimed with ice, small "
                       "icicles hanging from it, glowing icy blue runes"),
    "bloodbound_mask": ("an eerie empty face mask of dark crimson leather and black silk, veins of glowing "
                        "blood red running over it, a ruby set in its brow"),
    "bearskin_bracers": ("a pair of empty leather bracers and gloves trimmed with thick shaggy dark brown "
                         "bear fur, bear claws on the knuckles, no animal, no head, no face"),
    "hide_pavise": f"a tall wide rectangular wooden tower shield seen from the front, {HIDE}, stretched over it",
    "reed_parma": ("a small round shield, a solid flat disc of tightly woven green brown reeds bound with "
                   "twine, seen from the front, with one sharp iron spike sticking out of its centre"),
    "bastion_greaves": f"a pair of massive armoured greaves with knee guards shaped like battlements {MITHRIL}",
    "nightstalker_boots": f"a pair of soft silent assassin's boots with many small buckles, {SHADOWSILK}",
    "seers_slippers": ("a pair of deep violet pointed slippers embroidered with a small glowing eye on each, "
                       "mystical"),
    "rimeguard_boots": ("a pair of chainmail boots of frosted pale blue steel rimed with ice, icicles at "
                        "their cuffs, glowing icy blue runes"),
    "bloodbound_footwraps": ("a pair of boots wrapped in dark crimson leather bands, veins of glowing blood "
                             "red running over them"),
    "brawlers_gauntlets": f"a pair of heavy spiked plate gauntlets with studded knuckles {MITHRIL}",
    "nightstalker_bracers": f"a pair of sleek archer's bracers and gloves with a quiver strap buckle, {SHADOWSILK}",
    "seers_gloves": ("a pair of long deep violet gloves embroidered with a small glowing eye on each back, "
                     "mystical"),
    "dragonscale_gauntlets": f"a pair of clawed armoured gauntlets {DRAGON}",
    "rimeguard_gloves": ("a pair of chainmail gloves of frosted pale blue steel rimed with ice, icicles at "
                         "their cuffs, glowing icy blue runes"),
    "bloodbound_mitts": ("a pair of fingerless mitts of dark crimson leather, veins of glowing blood red "
                         "running over them, a ruby on each"),
    # Body armour, the rest of the ladder: plate (armour), coats (evasion), robes (energy shield),
    # scale (armour/evasion), chainmail (armour/energy shield), garb (evasion/energy shield).
    "copper_breastplate": f"a simple breastplate cuirass {COPPER}",
    "tempered_plate": f"a full plate armour chest piece with layered pauldrons {TEMPERED}",
    "bastion_plate": (f"a towering fortress like plate armour chest piece with enormous squared "
                      f"pauldrons shaped like castle battlements {MITHRIL}"),
    "adamant_plate": f"a heavy spiked full plate armour chest piece with angular pauldrons {ADAMANT}",
    "starmetal_plate": f"a majestic full plate armour chest piece with flared pauldrons {STARMETAL}",
    "rawhide_tunic": f"a simple short sleeved tunic {RAWHIDE}",
    "bearskin_coat": f"an empty heavy long coat with a big fur collar, no animal head, no face, {BEARSKIN}",
    "nightstalker_garb": ("a sleek close fitting assassin's hooded leather garb with a face scarf and "
                          f"many small belts and buckles, {SHADOWSILK}"),
    "manticore_coat": f"a long fitted coat {MANTICORE}",
    "wyvernhide_coat": f"a long flared coat with a high collar {WYVERNHIDE}",
    "linen_robe": f"a simple long robe {LINEN}",
    "woolen_robe": f"a long hooded robe {WOOL}",
    "velvet_robe": f"a long wizard's robe with wide sleeves {VELVET}",
    "runecloth_robe": f"a long hooded robe {RUNECLOTH}",
    "moonsilk_robe": f"an elegant long flowing robe {MOONSILK}",
    "spiritweave_robe": f"a long flowing hooded robe {SPIRITWEAVE}",
    "starweave_robe": f"a long wizard's robe with a high collar {STARWEAVE}",
    "voidsilk_robe": f"a long hooded robe {VOIDSILK}",
    "seers_raiment": ("a long hooded seer's raiment of deep violet cloth with a large open glowing eye "
                      "embroidered on the chest and a crystal clasp, mystical"),
    "aetherweave_robe": f"a grand flowing robe {AETHERWEAVE}",
    "celestial_vestment": f"a grand ceremonial vestment with tall shoulders {CELESTIAL}",
    "patchwork_brigandine": ("a crude brigandine vest of mismatched patches of hide and leather with a "
                             "few small scavenged metal plates sewn on"),
    "copper_scale": f"a scale armour vest of small overlapping scales {COPPER}",
    "bronze_scale_coat": f"a scale armour coat of overlapping scales over leather {BRONZE}",
    "iron_brigandine": f"a brigandine coat of leather riveted with small plates {IRON}",
    "steel_lamellar": f"a lamellar armour coat of laced rows of small plates {STEEL}",
    "tempered_lamellar": f"a lamellar armour coat of laced rows of small plates {TEMPERED}",
    "dwarven_brigandine": f"a bulky brigandine coat riveted with plates {DWARVEN}",
    "mithril_scale": f"a scale armour coat of fine overlapping scales {MITHRIL}",
    "adamant_lamellar": f"a lamellar armour coat of laced angular plates {ADAMANT}",
    "wyvernscale_brigandine": ("a brigandine coat covered in overlapping deep teal and black wyvern "
                               "scales with small pale horn spikes on the shoulders, glinting"),
    "rusty_mail_shirt": "a short sleeved chainmail shirt of rusty orange brown iron rings, old and worn",
    "copper_ringmail": f"an empty short sleeved chainmail shirt woven of thousands of tiny interlinked rings, shaped like a torso, {COPPER}",
    "bronze_chain_coat": f"a long chainmail coat of interlinked rings {BRONZE}",
    "iron_hauberk": f"an empty headless chainmail hauberk shaped like a torso with sleeves, no helmet, no head, no hood, {IRON}",
    "runed_hauberk": ("an empty headless chainmail hauberk shaped like a torso, no helmet, no head, of bright steel rings with plates engraved with faintly "
                      "glowing pale blue runes"),
    "consecrated_mail": ("an empty headless holy chainmail hauberk with long mail sleeves, no helmet, no "
                         "head, of blued steel rings, a short white tabard over its chest bearing a "
                         "glowing golden sun emblem"),
    "dwarven_runemail": f"an empty headless chainmail hauberk shaped like a torso with sleeves, no helmet, no head, no hood, with a rune plated chest {DWARVEN}",
    "mithril_chainmail": f"an empty headless chainmail hauberk shaped like a torso with sleeves, no helmet, no head, no hood, of tiny fine rings {MITHRIL}",
    "rimeguard_hauberk": ("a chainmail hauberk of frosted pale blue steel rings rimed with ice, icicles "
                          "hanging from its shoulders, glowing icy blue runes, cold mist around it"),
    "adamant_runemail": f"an empty headless chainmail hauberk shaped like a torso with sleeves, no helmet, no head, no hood, with glowing green runes on its chest plate {ADAMANT}",
    "starmetal_hauberk": f"an empty headless chainmail hauberk shaped like a torso with sleeves, no helmet, no head, no hood, long and majestic, {STARMETAL}",
    "felt_tunic": f"a simple tunic with a hood {FELT}",
    "embroidered_coat": f"a fitted coat with rich embroidery on its cuffs and collar {WOOL}",
    "hexed_leather": ("a fitted dark leather jerkin painted with glowing green witch's hex sigils, "
                      "small charms and bones tied to it"),
    "ritual_garb": f"a hooded ritual garb with a sash and tassels {RUNECLOTH}",
    "shade_leather": ("a fitted hooded leather jerkin of dark charcoal leather that fades into shadow, "
                      "a faint purple glow at the seams"),
    "mistweave_coat": ("a long hooded coat of pale grey misty fabric, its hem dissolving into curling "
                       "fog"),
    "ghostleather_coat": f"a long fitted hooded coat {GHOST}",
    "wraithskin_garb": f"a hooded garb {WRAITH}",
    "bloodbound_raiment": ("a hooded raiment of dark crimson leather and black silk, veins of glowing "
                           "blood red running over it, a ruby clasp shaped like a heart"),
    "phantomweave_coat": f"a long hooded coat {PHANTOM}",
    "eclipse_garb": f"a fitted hooded garb with a long cape {ECLIPSE}",
    # Boots: always a pair, side by side.
    "wooden_clogs": f"a pair of chunky clogs carved from pale brown oak wood with visible wood grain",
    "bronze_sabatons": f"a pair of armoured boots with plated toes {BRONZE}",
    "iron_greaves": f"a pair of armoured greaves and boots {IRON}",
    "steel_sabatons": f"a pair of pointed armoured plate sabaton boots {STEEL}",
    "dwarven_boots": f"a pair of massive heavy dwarven armoured boots {DWARVEN}",
    "mithril_greaves": f"a pair of elegant tall armoured greaves {MITHRIL}",
    "hide_wraps": f"a pair of simple foot wraps {HIDE}",
    "leather_boots": f"a pair of knee high boots {LEATHER}",
    "studded_boots": f"a pair of boots {STUDDED}",
    "wolfskin_boots": f"a pair of boots with fur cuffs {WOLFSKIN}",
    "shadowsilk_slippers": f"a pair of soft pointed slippers {SHADOWSILK}",
    "wyrmskin_boots": f"a pair of tall boots {WYRMSKIN}",
    "bone_treads": f"a pair of boots armoured with bones, {BONE}",
    "crystal_striders": f"a pair of elegant boots {CRYSTAL}",
    "dragonscale_boots": f"a pair of clawed armoured boots {DRAGON}",
}

# The rest of the armour ladders (helm, boots, gloves, shield) compose their prompt from the base:
# its slot and line give the shape, its "material" the look. SUBJECTS above wins where it names one.
LOOK = {
    "wood": WOOD, "copper": COPPER, "bronze": BRONZE, "iron": IRON, "steel": STEEL, "tempered steel": TEMPERED,
    "dwarven steel": DWARVEN, "mithril": MITHRIL, "adamant": ADAMANT, "starmetal": STARMETAL,
    "hide": HIDE, "rawhide": RAWHIDE, "leather": LEATHER, "studded leather": STUDDED, "wolfskin": WOLFSKIN,
    "bearskin": BEARSKIN, "shadowsilk": SHADOWSILK, "wyrmskin": WYRMSKIN, "manticore hide": MANTICORE,
    "wyvernhide": WYVERNHIDE, "linen": LINEN, "wool": WOOL, "velvet": VELVET, "runecloth": RUNECLOTH,
    "moonsilk": MOONSILK, "spiritweave": SPIRITWEAVE, "starweave": STARWEAVE, "voidsilk": VOIDSILK,
    "aetherweave": AETHERWEAVE, "celestial silk": CELESTIAL, "felt": FELT, "ghostleather": GHOST,
    "wraithskin": WRAITH, "phantomweave": PHANTOM, "eclipse silk": ECLIPSE, "dragonscale": DRAGON,
    "wyvernscale": "made of overlapping deep teal and black wyvern scales with small pale horn spikes, glinting",
    "starwood": ("made of deep blue black wood speckled with tiny glowing white stars like a night sky, "
                 "edged in pale gold"),
    "runed steel": "made of bright steel engraved with faintly glowing pale blue runes",
    "shade leather": "made of dark charcoal leather that fades into shadow, a faint purple glow at the seams",
    "mistweave": "made of pale grey misty fabric whose edges dissolve into curling fog",
    "ashwood": "made of smooth pale grey ashwood planks with a leather rim",
    "wolfhide": "made of wood covered in stretched grey wolf hide with the fur at its rim",
    "heartwood": "made of dense deep red brown heartwood with iron banding",
    "bearhide": "made of wood covered in thick dark brown bear hide, bear claws at its rim",
    "blackwood": "made of glossy black wood with silver banding",
    "willow": "made of woven pale willow withies, light and simple",
    "snakeskin": "covered in patterned olive and black snakeskin over wood",
    "yew": "made of warm golden yew wood with a polished grain",
    "hardened leather": "made of thick boiled dark brown hardened leather, ridged and riveted",
    "drakeskin": "covered in rough dark red drake skin with small scales",
    "elderwood": "made of ancient pale green tinged elderwood carved with leaves",
    "bone": "made of pale ivory yellowed bone lashed with dark leather cords",
    "brass": "made of polished warm yellow brass with engraved circles",
    "rowan": "made of reddish rowan wood with small red berries carved around it",
    "antler": "made of pale branching deer antler bound with sinew",
    "silver": "made of bright polished silver with fine engraving",
    "wyrmbone": "made of pale ivory wyrm bone with emerald green scale inlays",
    "wicker": "made of woven light brown wicker, simple and light",
    "pine": "made of pale yellow pine planks with an iron rim",
    "oak": "made of sturdy brown oak planks with an iron boss",
    "dragonhide": "covered in crimson red dragon hide with dark scales, glowing faintly like embers",
    "birch": "made of white and black flecked birch wood",
    "hallowed wood": "made of pale wood painted white and gold with a holy sun emblem",
    "consecrated steel": "made of blued steel with a white enamel face bearing a glowing golden sun emblem",
    "reed": "made of bound green brown reeds, light and simple",
    "hexwood": "made of dark wood painted with glowing green witch's hex sigils",
    "ghostwood": "made of pale bluish white translucent ghostly wood, faintly glowing",
    "voidsteel": "made of pitch black steel with a swirling purple void glowing in its seams",
}
# A tier whose name says more than its material (a Hexed tier is "leather"): its look, by the name's first word.
NAME_LOOK = {
    "rusty": "made of old rusty orange brown iron, worn and pitted",
    "patchwork": "made of mismatched patches of hide and leather with a few small scavenged metal plates sewn on",
    "embroidered": "made of rich dark red wool with gold embroidery on its cuffs and edges",
    "hexed": "made of dark leather painted with glowing green witch's hex sigils, small charms and bones tied to it",
    "ritual": "made of grey cloth with tassels, embroidered all over with faintly glowing pale blue runes",
    "consecrated": "made of blued steel with white cloth trim bearing a glowing golden sun emblem",
}
LINES = {("armour",): 0, ("evasion",): 1, ("energy_shield",): 2, ("armour", "evasion"): 3,
         ("armour", "energy_shield"): 4, ("energy_shield", "evasion"): 5}
SHAPE = {   # plate, leather, robe, scale, mail, garb
    "helm": ["an empty full metal helmet with a visor", "an empty hood", "a slender circlet crown with a gem at its brow",
             "an empty open faced barbute helmet trimmed with overlapping scales", "an empty chainmail coif hood",
             "an eerie empty face mask"],
    "boots": ["a pair of armoured plate greaves and sabatons", "a pair of boots", "a pair of soft pointed slippers",
              "a pair of boots covered in overlapping scales", "a pair of chainmail boots with plated toes",
              "a pair of boots wrapped in cloth bands"],
    "gloves": ["a pair of armoured plate gauntlets", "a pair of bracers and gloves", "a pair of fine long gloves",
               "a pair of gauntlets covered in overlapping scales", "a pair of chainmail gloves with plated cuffs",
               "a pair of fingerless mitts"],
    "shield": ["a tall wide rectangular tower shield seen from the front, its whole face shown",
               "a small perfectly circular buckler shield seen from the front, a flat round disc with a small "
               "metal boss, not pointed",
               "a mystical spirit shield with a glowing sigil at its centre", "a round targe shield with a boss",
               "a heater shield, shaped like a kite",
               "a small perfectly circular round shield seen from the front with one long sharp metal spike "
               "sticking out of its centre, not pointed at the bottom"],
}


# A base with no prompt of its own and no line to compose one from is drawn from its name.
PLAIN = {
    "weapon": "a fantasy weapon, a {}, shown whole from end to end on the diagonal",
    "amulet": "a fantasy necklace, a {} hanging on its cord or chain",
    "ring": "a single finger ring, a {}, the band seen at a slight angle",
    "helm": "a piece of fantasy headgear, a {}, empty, with no head in it",
    "armour": "a piece of fantasy body armour, a {}, empty, with no body in it",
    "boots": "a pair of fantasy boots, {}",
    "gloves": "a pair of fantasy gloves, {}",
    "shield": "a fantasy shield, a {}, seen from the front",
}


def subject(base: dict) -> str | None:
    """The base's prompt subject: SUBJECTS' own, else composed from its slot, line and material."""
    if base["id"] in SUBJECTS:
        return SUBJECTS[base["id"]]
    line = LINES.get(tuple(sorted(k for k in base.get("props", {}) if k != "block")))
    look = NAME_LOOK.get(base["name"].split()[0].lower()) or LOOK.get(base.get("material", ""))
    if base["slot"] not in SHAPE or line is None or look is None:
        name = base["name"].lower()
        if "staff" in name or "rod" in name:
            return f"a wizard's staff, a {name}: one long straight shaft with an ornament at its head, shown whole on the diagonal, not a sword"
        return PLAIN.get(base["slot"], "a piece of fantasy equipment called {}").format(name)
    return f"{SHAPE[base['slot']][line]} {look}"


def render(base: str, subject_text: str) -> bool:
    DRAFTS.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        ["node", str(GEN), "--raw", "--prompt", "-", "--negative", NEGATIVE, "--size", "768",
         "--seed", str(zlib.crc32(base.encode())), "--out", str(DRAFTS / f"{base}.png"), "--quiet"],
        input=FRAME.format(subject_text), capture_output=True, text=True, encoding="utf-8")
    line = (result.stdout.strip().splitlines() or ["{}"])[-1]
    try:
        return bool(json.loads(line).get("ok"))
    except json.JSONDecodeError:
        return False


def place(bases) -> None:
    """Each draft trimmed to its object and set in the middle of its square, a little room round it."""
    ICONS.mkdir(exist_ok=True)
    for base in bases:
        draft = Image.open(DRAFTS / f"{base}.png").convert("RGBA")
        box = draft.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox() or (0, 0, *draft.size)
        cut = draft.crop(box)
        side = round(max(cut.size) * 1.08)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.paste(cut, ((side - cut.width) // 2, (side - cut.height) // 2))
        square.resize((SIZE, SIZE), Image.LANCZOS).save(ICONS / f"{base}.png", optimize=True)


def godot_import() -> None:
    """Writes the icons' .import files: Godot imports this repo mounted as a game would mount it,
    headless, so no window opens. The import cache stays in the throwaway project."""
    with tempfile.TemporaryDirectory() as tmp:
        project = Path(tmp)
        (project / "project.godot").write_text(
            'config_version=5\n\n[application]\n\nconfig/name="arc_items_icons"\n')
        (project / "addons").mkdir()
        link = project / "addons" / "arc_items"
        if os.name == "nt":   # a junction needs no admin rights, unlike a symlink
            subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(ROOT)], check=True, capture_output=True)
        else:
            link.symlink_to(ROOT, target_is_directory=True)
        try:
            subprocess.run([GODOT, "--headless", "--path", str(project), "--import"],
                           check=True, capture_output=True)
        finally:
            os.rmdir(link) if os.name == "nt" else link.unlink()   # the link only, never the repo


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("bases", nargs="*")
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--all", action="store_true")
    args = parser.parse_args()
    catalog = json.loads((ROOT / "data" / "items.json").read_text(encoding="utf-8"))
    bases = {b["id"]: b for b in catalog["bases"]}
    subjects = {i: subject(b) for i, b in bases.items()}
    live = [i for i, b in bases.items() if not b.get("retired")]
    if args.bases:
        todo = args.bases
    elif args.all:
        todo = [b for b in bases if subjects[b]]   # the retired too: old saves still hold them
    else:
        todo = [b for b in live if subjects[b] and not (ICONS / f"{b}.png").exists()]
        undrawn = [b for b in live if not subjects[b] and not (ICONS / f"{b}.png").exists()]
        if undrawn:
            print("no prompt yet (shows a stand-in):", ", ".join(undrawn))
    if not args.no_render:
        unknown = [b for b in todo if not subjects.get(b)]
        if unknown:
            sys.exit(f"no prompt for: {', '.join(unknown)}")
        done = []
        for base in todo:
            ok = render(base, subjects[base])
            print(base, "rendered" if ok else "FAILED", flush=True)
            if ok:
                done.append(base)
        todo = done
    if todo:
        place(todo)
        godot_import()
        print("imported", len(todo))


if __name__ == "__main__":
    main()
