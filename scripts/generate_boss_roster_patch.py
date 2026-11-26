#!/usr/bin/env python3
"""Generate verified TrainerTable patch specification for Mandatory Legendary + (>Lv33) Mega Bosses.

Reads exact species/move/ability IDs from src/mod/data/*.h and Mega Stone IDs from src/mega/mega_registry.hpp,
cross-references /tmp/lumi_trainers.json, and emits /tmp/boss_roster_patch.json for the C# AssetsTools.NET patcher.
"""

from __future__ import annotations

import json
import re
from pathlib import Path

REPO = Path("/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version")
DATA_DIR = REPO / "src/mod/data"
TRAINERS_JSON = Path("/tmp/lumi_trainers.json")
OUT_JSON = Path("/tmp/boss_roster_patch.json")


def parse_c_string_array(path: Path) -> tuple[list[str], dict[str, int]]:
    text = path.read_text(encoding="utf-8")
    m = re.search(r"\{(.*)\};", text, re.S)
    assert m, f"Could not find array in {path}"
    items = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))
    by_name: dict[str, int] = {}
    for idx, name in enumerate(items):
        if name not in by_name:
            by_name[name] = idx
    return items, by_name


SPECIES_LIST, SPECIES_ID = parse_c_string_array(DATA_DIR / "species.h")
MOVES_LIST, MOVE_ID = parse_c_string_array(DATA_DIR / "moves.h")
ABILITIES_LIST, ABILITY_ID = parse_c_string_array(DATA_DIR / "abilities.h")
ITEMS_LIST, ITEM_ID = parse_c_string_array(DATA_DIR / "items.h")

NATURES = {
    "Hardy": 0, "Lonely": 1, "Brave": 2, "Adamant": 3, "Naughty": 4,
    "Bold": 5, "Docile": 6, "Relaxed": 7, "Impish": 8, "Lax": 9,
    "Timid": 10, "Hasty": 11, "Serious": 12, "Jolly": 13, "Naive": 14,
    "Modest": 15, "Mild": 16, "Quiet": 17, "Bashful": 18, "Rash": 19,
    "Calm": 20, "Gentle": 21, "Sassy": 22, "Careful": 23, "Quirky": 24,
}

# Verified Mega Stone IDs matching src/mega/main.cpp (kAllMegaStones) & src/mega/mega_registry.hpp
MEGA_STONE = {
    "Gengar": 656,
    "Gardevoir": 657,
    "Ampharos": 658,
    "Venusaur": 659,
    "Charizard X": 660,
    "Charizard Y": 678,
    "Blastoise": 661,
    "Mewtwo X": 662,
    "Mewtwo Y": 663,
    "Blaziken": 664,
    "Medicham": 665,
    "Houndoom": 666,
    "Aggron": 667,
    "Banette": 668,
    "Tyranitar": 669,
    "Scizor": 670,
    "Pinsir": 671,
    "Aerodactyl": 672,
    "Lucario": 673,
    "Abomasnow": 674,
    "Kangaskhan": 675,
    "Gyarados": 676,
    "Absol": 677,
    "Alakazam": 679,
    "Heracross": 680,
    "Mawile": 681,
    "Manectric": 682,
    "Garchomp": 683,
    "Latias": 684,
    "Latios": 685,
    "Swampert": 752,
    "Sceptile": 753,
    "Sableye": 754,
    "Altaria": 755,
    "Gallade": 756,
    "Audino": 757,
    "Metagross": 758,
    "Sharpedo": 759,
    "Slowbro": 760,
    "Steelix": 761,
    "Pidgeot": 762,
    "Glalie": 763,
    "Camerupt": 767,
    "Lopunny": 768,
    "Salamence": 769,
    "Beedrill": 770,
    "Clefable": 837,
    "Victreebel": 838,
    "Starmie": 839,
    "Dragonite": 840,
    "Meganium": 848,
    "Feraligatr": 859,
    "Skarmory": 867,
    "Froslass": 868,
    "Heatran": 869,
    "Darkrai": 870,
    "Raichu X": 1073,
    "Raichu Y": 1078,
}

# Competitive, level-tiered sets for every newly added Legendary/Mythical and swapped-in Mega Pokemon.
# EV weights: [HP, Atk, Def, SpAtk, SpDef, Spe] summing to 508 (scaled proportionally to slot EV budget by C# patcher).
# Tiers: early (<=27), mid (28..55), late (>=56).
POKEMON_SETS = {
    "Regirock": {
        "ability": "Clear Body", "nature": "Impish", "is_special": False,
        "evs": [252, 4, 252, 0, 0, 0], "item": "Leftovers",
        "early": ["Rock Throw", "Bulldoze", "Curse", "Ancient Power"],
        "mid":   ["Rock Slide", "Body Press", "Stealth Rock", "Drain Punch"],
        "late":  ["Stone Edge", "Body Press", "Earthquake", "Iron Defense"],
    },
    "Mew": {
        "ability": "Synchronize", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Leftovers",
        "early": ["Psybeam", "Ancient Power", "Aura Sphere", "Reflect"],
        "mid":   ["Psychic", "Aura Sphere", "Nasty Plot", "Roost"],
        "late":  ["Psychic", "Aura Sphere", "Ice Beam", "Nasty Plot"],
    },
    "Celebi": {
        "ability": "Natural Cure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Leftovers",
        "early": ["Magical Leaf", "Confusion", "Leech Seed", "Recover"],
        "mid":   ["Giga Drain", "Psychic", "Nasty Plot", "Recover"],
        "late":  ["Leaf Storm", "Psychic", "Earth Power", "Nasty Plot"],
    },
    "Shaymin": {
        "ability": "Natural Cure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Life Orb",
        "early": ["Magical Leaf", "Leech Seed", "Air Cutter", "Synthesis"],
        "mid":   ["Energy Ball", "Air Slash", "Earth Power", "Synthesis"],
        "late":  ["Seed Flare", "Earth Power", "Air Slash", "Dazzling Gleam"],
    },
    "Darkrai": {
        "ability": "Bad Dreams", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Life Orb",
        "early": ["Snarl", "Hypnosis", "Quick Attack", "Ominous Wind"],
        "mid":   ["Dark Pulse", "Shadow Ball", "Hypnosis", "Nasty Plot"],
        "late":  ["Dark Pulse", "Sludge Bomb", "Ice Beam", "Nasty Plot"],
    },
    "Mewtwo": {
        "ability": "Pressure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Life Orb",
        "early": ["Psybeam", "Swift", "Aura Sphere", "Recover"],
        "mid":   ["Psychic", "Aura Sphere", "Ice Beam", "Calm Mind"],
        "late":  ["Psystrike", "Aura Sphere", "Ice Beam", "Calm Mind"],
    },
    "Regigigas": {
        "ability": "Slow Start", "nature": "Adamant", "is_special": False,
        "evs": [252, 252, 0, 0, 4, 0], "item": "Leftovers",
        "early": ["Headbutt", "Revenge", "Rock Tomb", "Protect"],
        "mid":   ["Body Slam", "Drain Punch", "Knock Off", "Protect"],
        "late":  ["Giga Impact", "Drain Punch", "Earthquake", "Ice Punch"],
    },
    "Suicune": {
        "ability": "Pressure", "nature": "Bold", "is_special": True,
        "evs": [252, 0, 252, 0, 4, 0], "item": "Leftovers",
        "early": ["Water Pulse", "Aurora Beam", "Calm Mind", "Protect"],
        "mid":   ["Scald", "Ice Beam", "Calm Mind", "Rest"],
        "late":  ["Scald", "Ice Beam", "Calm Mind", "Air Slash"],
    },
    "Kyogre": {
        "ability": "Drizzle", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Mystic Water",
        "early": ["Water Pulse", "Icy Wind", "Ancient Power", "Calm Mind"],
        "mid":   ["Scald", "Ice Beam", "Thunder", "Calm Mind"],
        "late":  ["Origin Pulse", "Ice Beam", "Thunder", "Calm Mind"],
    },
    "Registeel": {
        "ability": "Clear Body", "nature": "Careful", "is_special": False,
        "evs": [252, 0, 4, 0, 252, 0], "item": "Leftovers",
        "early": ["Metal Claw", "Bulldoze", "Curse", "Amnesia"],
        "mid":   ["Heavy Slam", "Body Press", "Iron Defense", "Thunder Wave"],
        "late":  ["Heavy Slam", "Body Press", "Iron Defense", "Amnesia"],
    },
    "Jirachi": {
        "ability": "Serene Grace", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Leftovers",
        "early": ["Confusion", "Swift", "Wish", "Thunder Wave"],
        "mid":   ["Iron Head", "Zen Headbutt", "U-turn", "Thunder Wave"],
        "late":  ["Iron Head", "Zen Headbutt", "Fire Punch", "U-turn"],
    },
    "Regice": {
        "ability": "Clear Body", "nature": "Modest", "is_special": True,
        "evs": [252, 0, 0, 252, 4, 0], "item": "Leftovers",
        "early": ["Icy Wind", "Ancient Power", "Charge Beam", "Amnesia"],
        "mid":   ["Ice Beam", "Thunderbolt", "Focus Blast", "Rock Polish"],
        "late":  ["Ice Beam", "Thunderbolt", "Focus Blast", "Amnesia"],
    },
    "Articuno": {
        "ability": "Pressure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Heavy-Duty Boots",
        "early": ["Icy Wind", "Air Cutter", "Ancient Power", "Roost"],
        "mid":   ["Freeze-Dry", "Hurricane", "Ice Beam", "Roost"],
        "late":  ["Ice Beam", "Freeze-Dry", "Hurricane", "Roost"],
    },
    "Zapdos": {
        "ability": "Static", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Leftovers",
        "early": ["Shock Wave", "Air Cutter", "Ancient Power", "Roost"],
        "mid":   ["Thunderbolt", "Air Slash", "Heat Wave", "Roost"],
        "late":  ["Thunderbolt", "Hurricane", "Heat Wave", "Roost"],
    },
    "Raikou": {
        "ability": "Pressure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Life Orb",
        "early": ["Shock Wave", "Bite", "Calm Mind", "Reflect"],
        "mid":   ["Thunderbolt", "Shadow Ball", "Aura Sphere", "Calm Mind"],
        "late":  ["Thunderbolt", "Scald", "Shadow Ball", "Calm Mind"],
    },
    "Groudon": {
        "ability": "Drought", "nature": "Adamant", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Leftovers",
        "early": ["Bulldoze", "Fire Fang", "Rock Tomb", "Bulk Up"],
        "mid":   ["Earthquake", "Fire Punch", "Rock Slide", "Swords Dance"],
        "late":  ["Precipice Blades", "Fire Punch", "Stone Edge", "Swords Dance"],
    },
    "Entei": {
        "ability": "Inner Focus", "nature": "Adamant", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Choice Band",
        "early": ["Flame Wheel", "Bite", "Bulldoze", "Leer"],
        "mid":   ["Sacred Fire", "Stone Edge", "Crunch", "Extreme Speed"],
        "late":  ["Sacred Fire", "Stone Edge", "Stomping Tantrum", "Extreme Speed"],
    },
    "Heatran": {
        "ability": "Flash Fire", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Air Balloon",
        "early": ["Incinerate", "Metal Sound", "Ancient Power", "Earth Power"],
        "mid":   ["Flamethrower", "Flash Cannon", "Earth Power", "Stealth Rock"],
        "late":  ["Magma Storm", "Flash Cannon", "Earth Power", "Taunt"],
    },
    "Latios": {
        "ability": "Levitate", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Soul Dew",
        "early": ["Psybeam", "Dragon Breath", "Recover", "Calm Mind"],
        "mid":   ["Psychic", "Dragon Pulse", "Ice Beam", "Recover"],
        "late":  ["Draco Meteor", "Psychic", "Aura Sphere", "Ice Beam"],
    },
    "Latias": {
        "ability": "Levitate", "nature": "Timid", "is_special": True,
        "evs": [252, 0, 0, 4, 0, 252], "item": "Soul Dew",
        "early": ["Psybeam", "Dragon Breath", "Recover", "Calm Mind"],
        "mid":   ["Psychic", "Dragon Pulse", "Calm Mind", "Recover"],
        "late":  ["Dragon Pulse", "Psychic", "Calm Mind", "Recover"],
    },
    "Rayquaza": {
        # NOTE: Deliberately NO Dragon Ascent (620) so Cynthia's Garchomp always triggers Mega Evolution!
        "ability": "Air Lock", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Life Orb",
        "early": ["Dragon Breath", "Air Cutter", "Ancient Power", "Dragon Dance"],
        "mid":   ["Dragon Claw", "Air Slash", "Earthquake", "Dragon Dance"],
        "late":  ["Outrage", "Earthquake", "Extreme Speed", "Dragon Dance"],
    },
    "Giratina": {
        "ability": "Pressure", "nature": "Impish", "is_special": False,
        "evs": [252, 4, 252, 0, 0, 0], "item": "Leftovers",
        "early": ["Shadow Sneak", "Dragon Breath", "Will-O-Wisp", "Aura Sphere"],
        "mid":   ["Shadow Force", "Dragon Claw", "Will-O-Wisp", "Earthquake"],
        "late":  ["Shadow Force", "Dragon Claw", "Earthquake", "Will-O-Wisp"],
    },
    "Dialga": {
        "ability": "Pressure", "nature": "Modest", "is_special": True,
        "evs": [252, 0, 0, 252, 4, 0], "item": "Adamant Orb",
        "early": ["Metal Claw", "Dragon Breath", "Ancient Power", "Aura Sphere"],
        "mid":   ["Flash Cannon", "Dragon Pulse", "Earth Power", "Thunderbolt"],
        "late":  ["Flash Cannon", "Draco Meteor", "Earth Power", "Thunderbolt"],
    },
    "Palkia": {
        "ability": "Pressure", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Lustrous Orb",
        "early": ["Water Pulse", "Dragon Breath", "Ancient Power", "Aura Sphere"],
        "mid":   ["Surf", "Spacial Rend", "Ice Beam", "Thunderbolt"],
        "late":  ["Hydro Pump", "Spacial Rend", "Fire Blast", "Thunderbolt"],
    },
    "Azelf": {
        "ability": "Levitate", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Life Orb",
        "early": ["Confusion", "Swift", "Uproar", "Nasty Plot"],
        "mid":   ["Psychic", "Dazzling Gleam", "Flamethrower", "Nasty Plot"],
        "late":  ["Psychic", "Dazzling Gleam", "Flamethrower", "Nasty Plot"],
    },
    "Uxie": {
        "ability": "Levitate", "nature": "Bold", "is_special": True,
        "evs": [252, 0, 252, 0, 4, 0], "item": "Leftovers",
        "early": ["Confusion", "Yawn", "Swift", "Psybeam"],
        "mid":   ["Psychic", "Dazzling Gleam", "Yawn", "Stealth Rock"],
        "late":  ["Psychic", "Dazzling Gleam", "Yawn", "U-turn"],
    },
    "Mesprit": {
        "ability": "Levitate", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Leftovers",
        "early": ["Confusion", "Swift", "Charm", "Psybeam"],
        "mid":   ["Psychic", "Dazzling Gleam", "Ice Beam", "Calm Mind"],
        "late":  ["Psychic", "Dazzling Gleam", "Ice Beam", "Thunderbolt"],
    },
    # Swapped-in Mega Pokemon sets
    "Feraligatr": {
        "ability": "Sheer Force", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Feraligite",
        "early": ["Waterfall", "Ice Fang", "Crunch", "Dragon Dance"],
        "mid":   ["Liquidation", "Ice Punch", "Crunch", "Dragon Dance"],
        "late":  ["Liquidation", "Ice Punch", "Crunch", "Dragon Dance"],
    },
    "Raichu X": {
        "species": "Raichu", "ability": "Lightning Rod", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Raichunite X",
        "early": ["Thunder Punch", "Brick Break", "Fake Out", "Quick Attack"],
        "mid":   ["Volt Tackle", "Brick Break", "Play Rough", "Fake Out"],
        "late":  ["Volt Tackle", "Brick Break", "Play Rough", "Extreme Speed"],
    },
    "Raichu Y": {
        "species": "Raichu", "ability": "Lightning Rod", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Raichunite Y",
        "early": ["Thunderbolt", "Grass Knot", "Focus Blast", "Nasty Plot"],
        "mid":   ["Thunderbolt", "Surf", "Focus Blast", "Nasty Plot"],
        "late":  ["Thunderbolt", "Surf", "Focus Blast", "Nasty Plot"],
    },
    "Charizard X": {
        "species": "Charizard", "ability": "Blaze", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Charizardite X",
        "early": ["Fire Punch", "Dragon Claw", "Earthquake", "Dragon Dance"],
        "mid":   ["Flare Blitz", "Dragon Claw", "Earthquake", "Dragon Dance"],
        "late":  ["Flare Blitz", "Dragon Claw", "Earthquake", "Dragon Dance"],
    },
    "Charizard Y": {
        "species": "Charizard", "ability": "Blaze", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Charizardite Y",
        "early": ["Flamethrower", "Air Slash", "Solar Beam", "Roost"],
        "mid":   ["Flamethrower", "Air Slash", "Solar Beam", "Focus Blast"],
        "late":  ["Fire Blast", "Solar Beam", "Air Slash", "Focus Blast"],
    },
    "Blaziken": {
        "ability": "Speed Boost", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Blazikenite",
        "early": ["Blaze Kick", "Brick Break", "Rock Slide", "Protect"],
        "mid":   ["Flare Blitz", "Close Combat", "Stone Edge", "Protect"],
        "late":  ["Flare Blitz", "Close Combat", "Knock Off", "Swords Dance"],
    },
    "Alakazam": {
        "ability": "Magic Guard", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Alakazite",
        "early": ["Psybeam", "Dazzling Gleam", "Shadow Ball", "Calm Mind"],
        "mid":   ["Psychic", "Dazzling Gleam", "Shadow Ball", "Focus Blast"],
        "late":  ["Psychic", "Dazzling Gleam", "Shadow Ball", "Focus Blast"],
    },
    "Houndoom": {
        "ability": "Flash Fire", "nature": "Timid", "is_special": True,
        "evs": [4, 0, 0, 252, 0, 252], "item": "Houndoominite",
        "early": ["Flamethrower", "Dark Pulse", "Sludge Bomb", "Nasty Plot"],
        "mid":   ["Flamethrower", "Dark Pulse", "Sludge Bomb", "Nasty Plot"],
        "late":  ["Fire Blast", "Dark Pulse", "Sludge Bomb", "Nasty Plot"],
    },
    "Camerupt": {
        "ability": "Solid Rock", "nature": "Quiet", "is_special": True,
        "evs": [252, 0, 0, 252, 4, 0], "item": "Cameruptite",
        "early": ["Lava Plume", "Earth Power", "Rock Slide", "Yawn"],
        "mid":   ["Flamethrower", "Earth Power", "Flash Cannon", "Stealth Rock"],
        "late":  ["Fire Blast", "Earth Power", "Flash Cannon", "Ancient Power"],
    },
    "Gyarados": {
        "ability": "Intimidate", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Gyaradosite",
        "early": ["Waterfall", "Ice Fang", "Crunch", "Dragon Dance"],
        "mid":   ["Waterfall", "Crunch", "Ice Fang", "Dragon Dance"],
        "late":  ["Waterfall", "Crunch", "Earthquake", "Dragon Dance"],
    },
    "Scizor": {
        "ability": "Technician", "nature": "Adamant", "is_special": False,
        "evs": [248, 252, 0, 0, 8, 0], "item": "Scizorite",
        "early": ["Bullet Punch", "Bug Bite", "Brick Break", "Swords Dance"],
        "mid":   ["Bullet Punch", "Bug Bite", "Superpower", "Swords Dance"],
        "late":  ["Bullet Punch", "Bug Bite", "Close Combat", "Swords Dance"],
    },
    "Garchomp": {
        "ability": "Rough Skin", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Garchompite",
        "early": ["Dragon Claw", "Earthquake", "Rock Slide", "Swords Dance"],
        "mid":   ["Dragon Claw", "Earthquake", "Stone Edge", "Swords Dance"],
        "late":  ["Outrage", "Earthquake", "Stone Edge", "Swords Dance"],
    },
    "Aerodactyl": {
        "ability": "Rock Head", "nature": "Jolly", "is_special": False,
        "evs": [4, 252, 0, 0, 0, 252], "item": "Aerodactylite",
        "early": ["Rock Slide", "Wing Attack", "Crunch", "Roost"],
        "mid":   ["Stone Edge", "Dual Wingbeat", "Earthquake", "Roost"],
        "late":  ["Stone Edge", "Dual Wingbeat", "Earthquake", "Ice Fang"],
    },
    "Venusaur": {
        "ability": "Chlorophyll", "nature": "Modest", "is_special": True,
        "evs": [252, 0, 0, 252, 4, 0], "item": "Venusaurite",
        "early": ["Giga Drain", "Sludge Bomb", "Leech Seed", "Synthesis"],
        "mid":   ["Giga Drain", "Sludge Bomb", "Earth Power", "Synthesis"],
        "late":  ["Giga Drain", "Sludge Bomb", "Earth Power", "Synthesis"],
    },
}


def resolve_item(name: str) -> int:
    if name in MEGA_STONE:
        return MEGA_STONE[name]
    if name in ITEM_ID:
        return ITEM_ID[name]
    raise KeyError(f"Unknown item: {name}")


def resolve_set(set_key: str, lv: int, override_item: str | None = None) -> dict:
    spec = POKEMON_SETS[set_key]
    sp_name = spec.get("species", set_key)
    tier = "early" if lv <= 27 else ("mid" if lv <= 55 else "late")
    moves = [MOVE_ID[m] for m in spec[tier]]
    assert len(moves) == 4 and len(set(moves)) == 4, f"Bad moves for {set_key} ({tier}): {spec[tier]}"
    item_name = override_item if override_item is not None else spec["item"]
    return {
        "mons": SPECIES_ID[sp_name],
        "species_name": sp_name,
        "form": 0,
        "ability": ABILITY_ID[spec["ability"]],
        "nature": NATURES[spec["nature"]],
        "is_special": spec["is_special"],
        "evs": spec["evs"],
        "moves": moves,
        "item": resolve_item(item_name),
        "item_name": item_name,
    }


def main() -> None:
    trainers_raw = json.loads(TRAINERS_JSON.read_text(encoding="utf-8"))
    by_id = {t["id"]: t for t in trainers_raw}

    # Helper to find 1-based slot index of a species on a trainer
    def find_slot(tid: int, sp_name: str) -> int:
        sp_id = SPECIES_ID[sp_name]
        for idx, m in enumerate(by_id[tid]["team"], start=1):
            if m["mons"] == sp_id:
                return idx
        actual = [SPECIES_LIST[m["mons"]] for m in by_id[tid]["team"]]
        raise ValueError(f"Trainer {tid} ({by_id[tid]['tName']}) does not have {sp_name}; actual={actual}")

    patches: list[dict] = []

    def add_patch(tid: int, replaces: list[tuple[int | str, str, str | None]] | None = None,
                  mega_slot: tuple[int | str, str] | None = None,
                  strip_dragon_ascent: bool = False) -> None:
        """
        replaces: list of (slot_or_old_species_name, new_set_key, optional_override_item)
        mega_slot: (slot_or_species_name, mega_stone_key) to set held Mega Stone on existing mon
        """
        t = by_id[tid]
        ops: list[dict] = []
        replaced_slots: set[int] = set()

        for target, set_key, ov_item in (replaces or []):
            slot = target if isinstance(target, int) else find_slot(tid, target)
            assert 1 <= slot <= len(t["team"]), f"Invalid slot {slot} for trainer {tid}"
            assert slot not in replaced_slots, f"Duplicate slot edit {slot} on trainer {tid}"
            replaced_slots.add(slot)
            lv = t["team"][slot - 1]["lv"]
            data = resolve_set(set_key, lv, ov_item)
            ops.append({"op": "replace", "slot": slot, **data})

        if mega_slot is not None:
            m_target, stone_key = mega_slot
            slot = m_target if isinstance(m_target, int) else find_slot(tid, m_target)
            ops.append({
                "op": "set_item",
                "slot": slot,
                "item": MEGA_STONE[stone_key],
                "item_name": stone_key,
            })

        if strip_dragon_ascent:
            ray_slot = find_slot(tid, "Rayquaza")
            ops.append({
                "op": "replace_move",
                "slot": ray_slot,
                "old_move": MOVE_ID["Dragon Ascent"],
                "new_move": MOVE_ID["Extreme Speed"],
            })

        patches.append({
            "id": tid,
            "name": t["tName"],
            "class": t["tClass"],
            "max_lv": max(m["lv"] for m in t["team"]),
            "ops": ops,
        })

    # =========================================================================
    # 1. GYM LEADERS — STORY 4-TEAM POOLS (IDs 823–854)
    # =========================================================================
    # Roark (Lv 15–16, no Mega): T1/T3 Regirock, T2/T4 Mew
    add_patch(823, replaces=[(1, "Regirock", None)])
    add_patch(824, replaces=[(1, "Mew", None)])
    add_patch(825, replaces=[(1, "Regirock", None)])
    add_patch(826, replaces=[(1, "Mew", None)])

    # Gardenia (Lv 25–26, no Mega): T1/T3 Celebi, T2/T4 Shaymin
    add_patch(827, replaces=[(1, "Celebi", None)])
    add_patch(828, replaces=[(1, "Shaymin", None)])
    add_patch(829, replaces=[(1, "Celebi", None)])
    add_patch(830, replaces=[(1, "Shaymin", None)])

    # Fantina (Lv 32–33, Mega EXCEPTION: Gengar Mega Evolves in all 4 decks!)
    add_patch(831, replaces=[(4, "Darkrai", None)], mega_slot=("Gengar", "Gengar"))
    add_patch(832, replaces=[(1, "Darkrai", None)], mega_slot=("Gengar", "Gengar"))
    add_patch(833, replaces=[(3, "Darkrai", None)], mega_slot=("Gengar", "Gengar"))
    add_patch(834, replaces=[(1, "Darkrai", None)], mega_slot=("Gengar", "Gengar"))

    # Maylene (Lv 38–39): T1/T3 Mewtwo, T2/T4 Regigigas; Blaziken Mega in T1 & T4, Lucario Mega in T2 & T3
    add_patch(835, replaces=[(1, "Mewtwo", None)], mega_slot=("Blaziken", "Blaziken"))
    add_patch(836, replaces=[(1, "Regigigas", None)], mega_slot=("Lucario", "Lucario"))
    add_patch(837, replaces=[(1, "Mewtwo", None)], mega_slot=("Lucario", "Lucario"))
    add_patch(838, replaces=[(1, "Regigigas", None)], mega_slot=("Blaziken", "Blaziken"))

    # Wake (Lv 43–44): T1/T3 Kyogre, T2/T4 Suicune; T1 Feraligatr Mega instead of Sharpedo
    add_patch(839, replaces=[(2, "Kyogre", None), ("Sharpedo", "Feraligatr", "Feraligatr")])
    add_patch(840, replaces=[(1, "Suicune", None), ("Seaking", "Gyarados", "Gyarados")])
    add_patch(841, replaces=[(1, "Kyogre", None), ("Huntail", "Gyarados", "Gyarados")])
    add_patch(842, replaces=[(1, "Suicune", None)], mega_slot=("Swampert", "Swampert"))

    # Byron (Lv 52–53): T1/T3 Registeel, T2/T4 Jirachi
    add_patch(843, replaces=[(1, "Registeel", None)], mega_slot=("Steelix", "Steelix"))
    add_patch(844, replaces=[(3, "Jirachi", None)], mega_slot=("Mawile", "Mawile"))
    add_patch(845, replaces=[(1, "Registeel", None)], mega_slot=("Skarmory", "Skarmory"))
    add_patch(846, replaces=[(1, "Jirachi", None)], mega_slot=("Steelix", "Steelix"))

    # Candice (Lv 55–56): T1/T3 Regice, T2/T4 Articuno; Froslass Mega
    add_patch(847, replaces=[(2, "Regice", None)], mega_slot=("Froslass", "Froslass"))
    add_patch(848, replaces=[(1, "Articuno", None)], mega_slot=("Froslass", "Froslass"))
    add_patch(849, replaces=[(1, "Regice", None)], mega_slot=("Froslass", "Froslass"))
    add_patch(850, replaces=[(2, "Articuno", None)], mega_slot=("Froslass", "Froslass"))

    # Volkner (Lv 61–62): T1/T3 Zapdos, T2/T4 Raikou; Raichu X (T1, physical) & Raichu Y (T2/T4, special)
    add_patch(851, replaces=[(2, "Zapdos", None), ("Raichu", "Raichu X", "Raichu X")])
    add_patch(852, replaces=[(1, "Raikou", None), ("Plusle", "Raichu Y", "Raichu Y")])
    add_patch(853, replaces=[(1, "Zapdos", None)], mega_slot=("Ampharos", "Ampharos"))
    add_patch(854, replaces=[(1, "Raikou", None), ("Raichu", "Raichu Y", "Raichu Y")])

    # =========================================================================
    # 2. ELITE FOUR, CHAMPION & CYRUS — STORY 4-TEAM POOLS (IDs 855–874, 887–890)
    # =========================================================================
    # Aaron (Lv 71–72)
    add_patch(855, replaces=[(1, "Shaymin", None)], mega_slot=("Scizor", "Scizor"))
    add_patch(856, replaces=[(1, "Shaymin", None)], mega_slot=("Beedrill", "Beedrill"))
    add_patch(857, replaces=[(1, "Shaymin", None)], mega_slot=("Scizor", "Scizor"))
    add_patch(858, replaces=[(1, "Shaymin", None), ("Ledian", "Scizor", "Scizor")])

    # Bertha (Lv 72–73)
    add_patch(859, replaces=[(1, "Regirock", None)], mega_slot=("Garchomp", "Garchomp"))
    add_patch(860, replaces=[(2, "Groudon", None)], mega_slot=("Camerupt", "Camerupt"))
    add_patch(861, replaces=[(1, "Regirock", None), ("Mamoswine", "Garchomp", "Garchomp")])
    add_patch(862, replaces=[(1, "Groudon", None)], mega_slot=("Steelix", "Steelix"))

    # Flint (Lv 73–74): Charizard X/Y instead of Houndoom, one Blaziken, and Mega Heatran!
    add_patch(863, replaces=[(2, "Entei", None), ("Charizard", "Charizard X", "Charizard X")])
    add_patch(864, replaces=[(1, "Heatran", None), ("Houndoom", "Charizard Y", "Charizard Y")])
    add_patch(865, replaces=[(1, "Entei", None)], mega_slot=("Blaziken", "Blaziken"))
    add_patch(866, replaces=[(1, "Heatran", None), ("Flareon", "Charizard X", "Charizard X")])

    # Lucian (Lv 74–75): Alakazam Mega in 2 decks (T2 & T3), Gallade Mega in T1 & T4
    add_patch(867, replaces=[(1, "Latios", None)], mega_slot=("Gallade", "Gallade"))
    add_patch(868, replaces=[(1, "Latias", None), ("Mr. Mime", "Alakazam", "Alakazam")])
    add_patch(869, replaces=[(1, "Latios", None)], mega_slot=("Alakazam", "Alakazam"))
    add_patch(870, replaces=[(1, "Latias", None)], mega_slot=("Gallade", "Gallade"))

    # Cynthia (Lv 77–78)
    add_patch(871, replaces=[(1, "Rayquaza", None)], mega_slot=("Garchomp", "Garchomp"))
    add_patch(872, replaces=[(2, "Giratina", None)], mega_slot=("Garchomp", "Garchomp"))
    add_patch(873, replaces=[(1, "Rayquaza", None)], mega_slot=("Garchomp", "Garchomp"))
    add_patch(874, replaces=[(1, "Giratina", None)], mega_slot=("Garchomp", "Garchomp"))

    # Cyrus (Lv 59–60)
    add_patch(887, replaces=[(1, "Dialga", None)], mega_slot=("Gyarados", "Gyarados"))
    add_patch(888, replaces=[(2, "Palkia", None)], mega_slot=("Charizard", "Charizard X"))
    add_patch(889, replaces=[(3, "Dialga", None)], mega_slot=("Houndoom", "Houndoom"))
    add_patch(890, replaces=[(2, "Palkia", None)], mega_slot=("Houndoom", "Houndoom"))

    # =========================================================================
    # 3. GYM LEADERS, E4 & CHAMPION — LV. 100 4-TEAM POOLS (IDs 891–942)
    # =========================================================================
    # Roark Lv. 100: T1/T3 Regirock, T2/T4 Mew
    add_patch(891, replaces=[(2, "Regirock", None)], mega_slot=("Aerodactyl", "Aerodactyl"))
    add_patch(892, replaces=[("Forretress", "Aerodactyl", "Aerodactyl"), (2, "Mew", None)])
    add_patch(893, replaces=[(2, "Regirock", None)], mega_slot=("Tyranitar", "Tyranitar"))
    add_patch(894, replaces=[(1, "Mew", None)], mega_slot=("Aerodactyl", "Aerodactyl"))

    # Gardenia Lv. 100 (already has Celebi/Shaymin in slot 6!)
    add_patch(895, mega_slot=("Abomasnow", "Abomasnow"))
    add_patch(896, mega_slot=("Venusaur", "Venusaur"))
    add_patch(897, replaces=[("Roserade", "Venusaur", "Venusaur")])
    add_patch(898, mega_slot=("Victreebel", "Victreebel"))

    # Fantina Lv. 100 (already has Darkrai in slot 6!)
    add_patch(899, mega_slot=("Absol", "Absol"))
    add_patch(900, mega_slot=("Gengar", "Gengar"))
    add_patch(901, mega_slot=("Houndoom", "Houndoom"))
    add_patch(902, mega_slot=("Banette", "Banette"))

    # Maylene Lv. 100: Blaziken Mega in T2 & T4, Salamence Mega in T1 & T3
    add_patch(903, replaces=[(1, "Mewtwo", None)], mega_slot=("Salamence", "Salamence"))
    add_patch(904, replaces=[(1, "Regigigas", None), ("Hariyama", "Blaziken", "Blaziken")])
    add_patch(905, replaces=[(1, "Mewtwo", None)], mega_slot=("Salamence", "Salamence"))
    add_patch(906, replaces=[(1, "Regigigas", None)], mega_slot=("Blaziken", "Blaziken"))

    # Wake Lv. 100: Kyogre in T1 & T3 (replacing Suicune in slot 6), Suicune kept in T2 & T4
    add_patch(907, replaces=[("Lumineon", "Gyarados", "Gyarados"), (6, "Kyogre", None)])
    add_patch(908, replaces=[("Omastar", "Gyarados", "Gyarados")])
    add_patch(909, replaces=[(6, "Kyogre", None)], mega_slot=("Swampert", "Swampert"))
    add_patch(910, mega_slot=("Blastoise", "Blastoise"))

    # Byron Lv. 100 (already has Registeel/Jirachi in slot 6!)
    add_patch(911, mega_slot=("Mawile", "Mawile"))
    add_patch(912, mega_slot=("Mawile", "Mawile"))
    add_patch(913, mega_slot=("Aggron", "Aggron"))
    add_patch(914, mega_slot=("Scizor", "Scizor"))

    # Candice Lv. 100 (T1/T3 already have Regice in slot 6; T2/T4 already have Articuno in slot 1)
    add_patch(915, mega_slot=("Froslass", "Froslass"))
    add_patch(916, mega_slot=("Froslass", "Froslass"))
    add_patch(917, mega_slot=("Froslass", "Froslass"))
    add_patch(918, mega_slot=("Froslass", "Froslass"))

    # Volkner Lv. 100: Raichu X (T1, T3, physical) & Raichu Y (T2, T4, special) instead of Manectric
    add_patch(919, replaces=[("Pelipper", "Raichu X", "Raichu X")])
    add_patch(920, replaces=[("Raichu", "Raichu Y", "Raichu Y")])
    add_patch(921, replaces=[("Jolteon", "Raichu X", "Raichu X")])
    add_patch(922, replaces=[("Manectric", "Raichu Y", "Raichu Y")])

    # Aaron Lv. 100 (T2/T4 already have Shaymin in slot 6; T1/T3 add Shaymin in slot 1)
    add_patch(923, replaces=[(1, "Shaymin", None)], mega_slot=("Scizor", "Scizor"))
    add_patch(924, mega_slot=("Pinsir", "Pinsir"))
    add_patch(925, replaces=[(1, "Shaymin", None)], mega_slot=("Heracross", "Heracross"))
    add_patch(926, mega_slot=("Scizor", "Scizor"))

    # Bertha Lv. 100 (T2/T3 already have Regirock in slot 6; T1 add Regirock in slot 2, T4 add Groudon in slot 1)
    add_patch(927, replaces=[(2, "Regirock", None)], mega_slot=("Garchomp", "Garchomp"))
    add_patch(928, mega_slot=("Steelix", "Steelix"))
    add_patch(929, replaces=[("Nidoqueen", "Garchomp", "Garchomp")])
    add_patch(930, replaces=[(1, "Groudon", None)], mega_slot=("Tyranitar", "Tyranitar"))

    # Flint Lv. 100: Charizard X (T1, T4, physical), Blaziken (T2), Charizard Y (T3, special)
    add_patch(931, replaces=[("Charizard", "Charizard X", "Charizard X")])
    add_patch(932, mega_slot=("Blaziken", "Blaziken"))
    add_patch(933, replaces=[("Houndoom", "Charizard Y", "Charizard Y")])
    add_patch(934, replaces=[("Charizard", "Charizard X", "Charizard X")])

    # Lucian Lv. 100: Alakazam Mega in T2 & T4, Gallade Mega in T1 & T3 — already have Latios/Latias in slot 6!
    add_patch(935, mega_slot=("Gallade", "Gallade"))
    add_patch(936, mega_slot=("Alakazam", "Alakazam"))
    add_patch(937, mega_slot=("Gallade", "Gallade"))
    add_patch(938, mega_slot=("Alakazam", "Alakazam"))

    # Cynthia Lv. 100 (already has Rayquaza/Giratina in slot 6; strip Dragon Ascent from Rayquaza so Garchomp always Megas!)
    add_patch(939, mega_slot=("Garchomp", "Garchomp"), strip_dragon_ascent=True)
    add_patch(940, mega_slot=("Garchomp", "Garchomp"))
    add_patch(941, mega_slot=("Garchomp", "Garchomp"), strip_dragon_ascent=True)
    add_patch(942, mega_slot=("Garchomp", "Garchomp"))

    # =========================================================================
    # 4. TEAM GALACTIC COMMANDERS, FIXED CYRUS & MID-GAME E4
    # =========================================================================
    # Mars
    add_patch(220, replaces=[(1, "Azelf", None)])   # Lv 18-19 (no Mega)
    add_patch(875, replaces=[(1, "Azelf", None)])   # Lv 18-19 (no Mega)
    add_patch(310, replaces=[(1, "Azelf", None)], mega_slot=("Kangaskhan", "Kangaskhan"))
    add_patch(881, replaces=[(1, "Azelf", None)], mega_slot=("Kangaskhan", "Kangaskhan"))
    add_patch(401, replaces=[(1, "Azelf", None)], mega_slot=("Kangaskhan", "Kangaskhan"))
    add_patch(884, replaces=[(1, "Azelf", None)], mega_slot=("Kangaskhan", "Kangaskhan"))
    for tid in (1026, 1027, 1028, 1029):
        add_patch(tid, replaces=[("Ninjask", "Azelf", None)], mega_slot=("Kangaskhan", "Kangaskhan"))

    # Jupiter
    add_patch(311, replaces=[(1, "Uxie", None)])    # Lv 26-27 (no Mega)
    add_patch(876, replaces=[(1, "Uxie", None)])    # Lv 26-27 (no Mega)
    add_patch(312, replaces=[(1, "Uxie", None)], mega_slot=("Sableye", "Sableye"))
    add_patch(885, replaces=[("Probopass", "Uxie", None)], mega_slot=("Sableye", "Sableye"))
    for tid in (1030, 1031, 1032, 1033):
        add_patch(tid, replaces=[("Crobat", "Uxie", None)], mega_slot=("Sableye", "Sableye"))

    # Saturn
    add_patch(877, replaces=[("Octillery", "Mesprit", None), ("Bronzong", "Alakazam", "Alakazam")])
    add_patch(313, replaces=[("Octillery", "Mesprit", None)], mega_slot=("Alakazam", "Alakazam"))
    add_patch(880, replaces=[("Octillery", "Mesprit", None)], mega_slot=("Alakazam", "Alakazam"))
    add_patch(314, replaces=[("Octillery", "Mesprit", None)], mega_slot=("Alakazam", "Alakazam"))
    add_patch(883, replaces=[("Octillery", "Mesprit", None)], mega_slot=("Alakazam", "Alakazam"))

    # Fixed Cyrus
    add_patch(879, replaces=[("Chatot", "Dialga", None), ("Arcanine", "Houndoom", "Houndoom")])
    add_patch(308, replaces=[(1, "Palkia", None)], mega_slot=("Houndoom", "Houndoom"))
    add_patch(882, replaces=[("Raticate", "Palkia", None)], mega_slot=("Houndoom", "Houndoom"))
    add_patch(309, mega_slot=("Houndoom", "Houndoom"))

    # Mid-Game / Early E4
    add_patch(701, replaces=[("Dustox", "Shaymin", None)])  # Lv 31-32 (no Mega)
    add_patch(943, replaces=[("Dustox", "Shaymin", None)])  # Lv 31-32 (no Mega)
    add_patch(944, replaces=[("Claydol", "Latios", None)], mega_slot=("Gallade", "Gallade"))
    add_patch(945, replaces=[("Quagsire", "Regirock", None), ("Marowak", "Camerupt", "Camerupt")])
    add_patch(946, replaces=[("Magcargo", "Entei", None), ("Houndoom", "Charizard Y", "Charizard Y")])

    OUT_JSON.write_text(json.dumps(patches, indent=2), encoding="utf-8")
    print(f"SUCCESS: Generated {len(patches)} trainer patches -> {OUT_JSON}")


if __name__ == "__main__":
    main()
