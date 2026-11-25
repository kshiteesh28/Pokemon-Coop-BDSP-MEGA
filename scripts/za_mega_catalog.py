#!/usr/bin/env python3
"""Single-source-of-truth catalog for all 63 Gen 1-4 Mega Evolutions playable in BDSP / Luminescent Platinum.

Every Mega Stone item ID matches src/mega/main.cpp::kAllMegaStones[] verbatim.
Every Normal and Shiny bundle receives a deterministic, globally unique 32-char CAB- archive ID
to prevent Unity AssetBundle cache collisions when switching forms or Normal/Shiny variants.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass


@dataclass(frozen=True)
class MegaFormSpec:
    species: int
    form: int
    name: str
    region: str  # kanto | johto | hoenn | sinnoh
    za_form_num: int  # 51 for first Mega (or X), 52 for second Mega (Y or Z)
    stone_item_id: int  # 0 for Rayquaza (Dragon Ascent)
    in_lumi_delta: bool  # True if pmXXXX_00_00 already exists in Luminescent Platinum 2.2F BD romfs
    height_ratio: float = 1.00

    @property
    def species_prefix(self) -> str:
        return f"pm{self.species:04d}"

    @property
    def za_pack(self) -> str:
        return f"pm{self.species:04d}_{self.za_form_num:02d}_00"

    @property
    def normal_bundle(self) -> str:
        # Legacy shipped Charizard/Absol use _01/_02; all other Megas use Game Freak's native _51/_52
        # to guarantee zero collision with BDSP female gender-difference bundles (pmXXXX_01_00).
        num = self.form if self.species in (6, 359) else self.za_form_num
        return f"pm{self.species:04d}_{num:02d}_00"

    @property
    def shiny_bundle(self) -> str:
        num = self.form if self.species in (6, 359) else self.za_form_num
        return f"pm{self.species:04d}_{num:02d}_10"

    def cab_id(self, shiny: bool) -> str:
        tag = f"bdsp-za-mega:{self.shiny_bundle if shiny else self.normal_bundle}"
        digest = hashlib.sha256(tag.encode("utf-8")).hexdigest()[:32]
        return f"CAB-{digest}"


MEGA_CATALOG: tuple[MegaFormSpec, ...] = (
    # --- Kanto (21) ---
    MegaFormSpec(3,   1, "Mega Venusaur",    "kanto",  51, 659,  True,  1.00),
    MegaFormSpec(6,   1, "Mega Charizard X", "kanto",  51, 660,  True,  1.05),
    MegaFormSpec(6,   2, "Mega Charizard Y", "kanto",  52, 678,  True,  1.04),
    MegaFormSpec(9,   1, "Mega Blastoise",   "kanto",  51, 661,  True,  1.02),
    MegaFormSpec(15,  1, "Mega Beedrill",    "kanto",  51, 770,  False, 1.05),
    MegaFormSpec(18,  1, "Mega Pidgeot",     "kanto",  51, 762,  False, 1.05),
    MegaFormSpec(26,  1, "Mega Raichu X",    "kanto",  51, 1073, True,  1.04),
    MegaFormSpec(26,  2, "Mega Raichu Y",    "kanto",  52, 1078, True,  1.04),
    MegaFormSpec(36,  1, "Mega Clefable",    "kanto",  51, 837,  False, 1.02),
    MegaFormSpec(65,  1, "Mega Alakazam",    "kanto",  51, 679,  False, 1.02),
    MegaFormSpec(71,  1, "Mega Victreebel",  "kanto",  51, 838,  True,  1.02),
    MegaFormSpec(80,  1, "Mega Slowbro",     "kanto",  51, 760,  True,  1.04),
    MegaFormSpec(94,  1, "Mega Gengar",      "kanto",  51, 656,  False, 1.00),
    MegaFormSpec(115, 1, "Mega Kangaskhan",  "kanto",  51, 675,  True,  1.00),
    MegaFormSpec(121, 1, "Mega Starmie",     "kanto",  51, 839,  False, 1.02),
    MegaFormSpec(127, 1, "Mega Pinsir",      "kanto",  51, 671,  False, 1.04),
    MegaFormSpec(130, 1, "Mega Gyarados",    "kanto",  51, 676,  True,  1.02),
    MegaFormSpec(142, 1, "Mega Aerodactyl",  "kanto",  51, 672,  False, 1.12),
    MegaFormSpec(149, 1, "Mega Dragonite",   "kanto",  51, 840,  True,  1.04),
    MegaFormSpec(150, 1, "Mega Mewtwo X",    "kanto",  51, 662,  True,  1.05),
    MegaFormSpec(150, 2, "Mega Mewtwo Y",    "kanto",  52, 663,  True,  0.96),
    # --- Johto (9) ---
    MegaFormSpec(154, 1, "Mega Meganium",    "johto",  51, 848,  True,  1.02),
    MegaFormSpec(160, 1, "Mega Feraligatr",  "johto",  51, 859,  True,  1.04),
    MegaFormSpec(181, 1, "Mega Ampharos",    "johto",  51, 658,  False, 1.02),
    MegaFormSpec(208, 1, "Mega Steelix",     "johto",  51, 761,  True,  1.04),
    MegaFormSpec(212, 1, "Mega Scizor",      "johto",  51, 670,  True,  1.04),
    MegaFormSpec(214, 1, "Mega Heracross",   "johto",  51, 680,  False, 1.04),
    MegaFormSpec(227, 1, "Mega Skarmory",    "johto",  51, 867,  True,  1.04),
    MegaFormSpec(229, 1, "Mega Houndoom",    "johto",  51, 666,  False, 1.04),
    MegaFormSpec(248, 1, "Mega Tyranitar",   "johto",  51, 669,  True,  1.05),
    # --- Hoenn (22) ---
    MegaFormSpec(254, 1, "Mega Sceptile",    "hoenn",  51, 753,  True,  1.04),
    MegaFormSpec(257, 1, "Mega Blaziken",    "hoenn",  51, 664,  True,  1.02),
    MegaFormSpec(260, 1, "Mega Swampert",    "hoenn",  51, 752,  False, 1.04),
    MegaFormSpec(282, 1, "Mega Gardevoir",   "hoenn",  51, 657,  True,  1.00),
    MegaFormSpec(302, 1, "Mega Sableye",     "hoenn",  51, 754,  False, 0.95),
    MegaFormSpec(303, 1, "Mega Mawile",      "hoenn",  51, 681,  False, 1.04),
    MegaFormSpec(306, 1, "Mega Aggron",      "hoenn",  51, 667,  True,  1.04),
    MegaFormSpec(308, 1, "Mega Medicham",    "hoenn",  51, 665,  False, 1.02),
    MegaFormSpec(310, 1, "Mega Manectric",   "hoenn",  51, 682,  False, 1.04),
    MegaFormSpec(319, 1, "Mega Sharpedo",    "hoenn",  51, 759,  True,  1.04),
    MegaFormSpec(323, 1, "Mega Camerupt",    "hoenn",  51, 767,  True,  1.02),
    MegaFormSpec(334, 1, "Mega Altaria",     "hoenn",  51, 755,  True,  1.04),
    MegaFormSpec(354, 1, "Mega Banette",     "hoenn",  51, 668,  False, 1.02),
    MegaFormSpec(358, 1, "Mega Chimecho",    "hoenn",  51, 1079, False, 1.02),
    MegaFormSpec(359, 1, "Mega Absol",       "hoenn",  51, 677,  True,  1.00),
    MegaFormSpec(359, 2, "Mega Absol Z",     "hoenn",  52, 622,  True,  1.02),
    MegaFormSpec(362, 1, "Mega Glalie",      "hoenn",  51, 763,  False, 1.04),
    MegaFormSpec(373, 1, "Mega Salamence",   "hoenn",  51, 769,  True,  1.04),
    MegaFormSpec(376, 1, "Mega Metagross",   "hoenn",  51, 758,  False, 1.04),
    MegaFormSpec(380, 1, "Mega Latias",      "hoenn",  51, 684,  False, 1.04),
    MegaFormSpec(381, 1, "Mega Latios",      "hoenn",  51, 685,  True,  1.04),
    MegaFormSpec(384, 1, "Mega Rayquaza",    "hoenn",  51, 0,    True,  1.06),
    # --- Sinnoh (11) ---
    MegaFormSpec(398, 1, "Mega Staraptor",   "sinnoh", 51, 1082, True,  1.04),
    MegaFormSpec(428, 1, "Mega Lopunny",     "sinnoh", 51, 768,  False, 1.02),
    MegaFormSpec(445, 1, "Mega Garchomp",    "sinnoh", 51, 683,  True,  1.02),
    MegaFormSpec(445, 2, "Mega Garchomp Z",  "sinnoh", 52, 1083, True,  1.04),
    MegaFormSpec(448, 1, "Mega Lucario",     "sinnoh", 51, 673,  True,  1.02),
    MegaFormSpec(448, 2, "Mega Lucario Z",   "sinnoh", 52, 1101, True,  1.04),
    MegaFormSpec(460, 1, "Mega Abomasnow",   "sinnoh", 51, 674,  True,  1.04),
    MegaFormSpec(475, 1, "Mega Gallade",     "sinnoh", 51, 756,  True,  1.02),
    MegaFormSpec(478, 1, "Mega Froslass",    "sinnoh", 51, 868,  False, 1.02),
    MegaFormSpec(485, 1, "Mega Heatran",     "sinnoh", 51, 869,  False, 1.04),
    MegaFormSpec(491, 1, "Mega Darkrai",     "sinnoh", 51, 870,  True,  1.04),
)

BY_NORMAL_BUNDLE: dict[str, MegaFormSpec] = {m.normal_bundle: m for m in MEGA_CATALOG}


def validate_catalog() -> None:
    assert len(MEGA_CATALOG) == 63, f"Expected 63 Gen 1-4 Mega forms, got {len(MEGA_CATALOG)}"
    seen_keys = set()
    seen_bundles = set()
    seen_cabs = set()
    seen_stones = set()
    for m in MEGA_CATALOG:
        key = (m.species, m.form)
        assert key not in seen_keys, f"Duplicate (species, form): {key}"
        seen_keys.add(key)
        for b in (m.normal_bundle, m.shiny_bundle):
            assert b not in seen_bundles, f"Duplicate bundle: {b}"
            seen_bundles.add(b)
        for is_shiny in (False, True):
            cab = m.cab_id(is_shiny)
            assert len(cab) == 36 and cab.startswith("CAB-"), f"Invalid CAB ID: {cab}"
            assert cab not in seen_cabs, f"Duplicate CAB ID: {cab}"
            seen_cabs.add(cab)
        if m.stone_item_id != 0:
            assert 0 < m.stone_item_id < 1278, f"Out-of-range stone ID {m.stone_item_id} for {m.name}"
            assert m.stone_item_id not in seen_stones, f"Duplicate stone ID {m.stone_item_id}"
            seen_stones.add(m.stone_item_id)


def filter_catalog(batch: str | None = None, forms: list[str] | None = None) -> list[MegaFormSpec]:
    if forms:
        out = []
        for f in forms:
            norm = f[:-2] + "00" if f.endswith("_10") else f
            if norm not in BY_NORMAL_BUNDLE:
                raise KeyError(f"Unknown Mega form '{f}' (expected e.g. pm0445_01_00)")
            out.append(BY_NORMAL_BUNDLE[norm])
        return out
    if batch is None or batch == "all":
        return list(MEGA_CATALOG)
    if batch == "unblocked":
        return [m for m in MEGA_CATALOG if m.in_lumi_delta]
    if batch == "blocked":
        return [m for m in MEGA_CATALOG if not m.in_lumi_delta]
    if batch in ("kanto", "johto", "hoenn", "sinnoh"):
        return [m for m in MEGA_CATALOG if m.region == batch]
    raise ValueError(f"Unknown batch '{batch}' (choose all|unblocked|blocked|kanto|johto|hoenn|sinnoh)")
