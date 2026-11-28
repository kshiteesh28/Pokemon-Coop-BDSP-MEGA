using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchLegendaryBosses;

internal static class Program
{
    private const string RepoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
    private const string DeltaMasterPath = RepoRoot + "/romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas";
    private const string RomfsMasterPath = RepoRoot + "/romfs/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas";

    private const int RedOrb = 534;
    private const int BlueOrb = 535;

    private sealed record MonSpec(
        string Name, int MonsNo, int FormNo, int ItemId, int AbilityId, int NatureId, bool IsSpecial,
        int[] EvWeights, int[] Moves
    );

    private static int[] ScaleEvs(int[] weights, int budget)
    {
        if (budget <= 0) return new int[6];
        budget = Math.Min(budget, 508);
        int weightSum = weights.Sum();
        if (weightSum <= 0) return new int[6];

        var result = new int[6];
        int used = 0;
        for (int i = 0; i < 6; i++)
        {
            int raw = (int)Math.Floor((double)weights[i] * budget / weightSum);
            int rounded = Math.Clamp((raw / 4) * 4, 0, 252);
            result[i] = rounded;
            used += rounded;
        }
        int rem = ((budget - used) / 4) * 4;
        for (int pass = 0; pass < 64 && rem >= 4; pass++)
        {
            bool progress = false;
            for (int i = 0; i < 6 && rem >= 4; i++)
            {
                if (weights[i] > 0 && result[i] + 4 <= 252 && result.Sum() + 4 <= 508)
                {
                    result[i] += 4;
                    rem -= 4;
                    progress = true;
                }
            }
            if (!progress) break;
        }
        return result;
    }

    private static void CopySlot(AssetTypeValueField tp, int src, int dst)
    {
        string[] suffixes = {
            "Level", "MonsNo", "FormNo", "IsRare", "Sex", "Seikaku", "Tokusei", "Item",
            "Waza1", "Waza2", "Waza3", "Waza4",
            "TalentHp", "TalentAtk", "TalentDef", "TalentSpAtk", "TalentSpDef", "TalentAgi",
            "EffortHp", "EffortAtk", "EffortDef", "EffortSpAtk", "EffortSpDef", "EffortAgi",
            "Ball", "Seal"
        };
        foreach (var sfx in suffixes)
        {
            var sf = tp[$"P{src}{sfx}"];
            var df = tp[$"P{dst}{sfx}"];
            if (!sf.IsDummy && !df.IsDummy)
                df.AsInt = sf.AsInt;
        }
    }

    private static void ReplaceSlot(AssetTypeValueField tp, int slot, MonSpec m, int? overrideLevel = null)
    {
        int origEvBudget =
            tp[$"P{slot}EffortHp"].AsInt + tp[$"P{slot}EffortAtk"].AsInt + tp[$"P{slot}EffortDef"].AsInt +
            tp[$"P{slot}EffortSpAtk"].AsInt + tp[$"P{slot}EffortSpDef"].AsInt + tp[$"P{slot}EffortAgi"].AsInt;
        int origIvTier = new[] {
            tp[$"P{slot}TalentHp"].AsInt, tp[$"P{slot}TalentDef"].AsInt,
            tp[$"P{slot}TalentSpAtk"].AsInt, tp[$"P{slot}TalentSpDef"].AsInt, tp[$"P{slot}TalentAgi"].AsInt
        }.Max();
        if (origIvTier <= 0) origIvTier = 25;

        var evs = ScaleEvs(m.EvWeights, origEvBudget);
        if (overrideLevel.HasValue)
            tp[$"P{slot}Level"].AsInt = overrideLevel.Value;

        tp[$"P{slot}MonsNo"].AsInt = m.MonsNo;
        tp[$"P{slot}FormNo"].AsInt = m.FormNo;
        tp[$"P{slot}IsRare"].AsInt = 0;
        tp[$"P{slot}Sex"].AsInt = 0;
        tp[$"P{slot}Seikaku"].AsInt = m.NatureId;
        tp[$"P{slot}Tokusei"].AsInt = m.AbilityId;
        tp[$"P{slot}Item"].AsInt = m.ItemId;
        tp[$"P{slot}Waza1"].AsInt = m.Moves[0];
        tp[$"P{slot}Waza2"].AsInt = m.Moves[1];
        tp[$"P{slot}Waza3"].AsInt = m.Moves[2];
        tp[$"P{slot}Waza4"].AsInt = m.Moves[3];

        tp[$"P{slot}TalentHp"].AsInt = origIvTier;
        tp[$"P{slot}TalentAtk"].AsInt = m.IsSpecial ? 0 : origIvTier;
        tp[$"P{slot}TalentDef"].AsInt = origIvTier;
        tp[$"P{slot}TalentSpAtk"].AsInt = origIvTier;
        tp[$"P{slot}TalentSpDef"].AsInt = origIvTier;
        tp[$"P{slot}TalentAgi"].AsInt = origIvTier;

        tp[$"P{slot}EffortHp"].AsInt = evs[0];
        tp[$"P{slot}EffortAtk"].AsInt = evs[1];
        tp[$"P{slot}EffortDef"].AsInt = evs[2];
        tp[$"P{slot}EffortSpAtk"].AsInt = evs[3];
        tp[$"P{slot}EffortSpDef"].AsInt = evs[4];
        tp[$"P{slot}EffortAgi"].AsInt = evs[5];
    }

    public static int Main()
    {
        // Verified competitive builds (all IDs verified from src/mod/data/{moves,abilities,items}.h)
        var hoOh = new MonSpec(
            "Ho-Oh", 250, 0, 1120 /* Heavy-Duty Boots */, 144 /* Regenerator */, 3 /* Adamant */, false,
            new[] { 248, 252, 0, 0, 0, 8 }, new[] { 221 /* Sacred Fire */, 413 /* Brave Bird */, 89 /* Earthquake */, 105 /* Recover */ }
        );
        var lugia = new MonSpec(
            "Lugia", 249, 0, 1120 /* Heavy-Duty Boots */, 136 /* Multiscale */, 5 /* Bold */, true,
            new[] { 252, 0, 160, 0, 0, 96 }, new[] { 177 /* Aeroblast */, 58 /* Ice Beam */, 347 /* Calm Mind */, 105 /* Recover */ }
        );
        var arceusCynthia = new MonSpec(
            "Arceus", 493, 0, 251 /* Silk Scarf */, 121 /* Multitype */, 13 /* Jolly */, false,
            new[] { 4, 252, 0, 0, 0, 252 }, new[] { 245 /* Extreme Speed */, 14 /* Swords Dance */, 89 /* Earthquake */, 421 /* Shadow Claw */ }
        );
        var arceusCyrus = new MonSpec(
            "Arceus", 493, 0, 270 /* Life Orb */, 121 /* Multitype */, 10 /* Timid */, true,
            new[] { 4, 0, 0, 252, 0, 252 }, new[] { 449 /* Judgment */, 247 /* Shadow Ball */, 411 /* Focus Blast */, 105 /* Recover */ }
        );
        var deoxys = new MonSpec(
            "Deoxys", 386, 0, 275 /* Focus Sash */, 46 /* Pressure */, 10 /* Timid */, true,
            new[] { 4, 0, 0, 252, 0, 252 }, new[] { 354 /* Psycho Boost */, 58 /* Ice Beam */, 411 /* Focus Blast */, 247 /* Shadow Ball */ }
        );
        var phione = new MonSpec(
            "Phione", 489, 0, 158 /* Sitrus Berry */, 93 /* Hydration */, 15 /* Modest */, true,
            new[] { 252, 0, 0, 252, 4, 0 }, new[] { 503 /* Scald */, 58 /* Ice Beam */, 605 /* Dazzling Gleam */, 347 /* Calm Mind */ }
        );
        var manaphy = new MonSpec(
            "Manaphy", 490, 0, 234 /* Leftovers */, 93 /* Hydration */, 10 /* Timid */, true,
            new[] { 4, 0, 0, 252, 0, 252 }, new[] { 294 /* Tail Glow */, 57 /* Surf */, 58 /* Ice Beam */, 412 /* Energy Ball */ }
        );

        var am = new AssetsManager();
        var bun = am.LoadBundleFile(DeltaMasterPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        AssetFileInfo? ttInfo = null;
        AssetTypeValueField? ttBf = null;
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            if (bf["m_Name"].AsString == "TrainerTable")
            {
                ttInfo = info;
                ttBf = bf;
                break;
            }
        }
        if (ttInfo == null || ttBf == null)
            throw new InvalidOperationException("TrainerTable not found!");

        var tdArr = ttBf["TrainerData.Array"].Children;
        var tpArr = ttBf["TrainerPoke.Array"].Children;
        var ttArr = ttBf["TrainerType.Array"].Children;

        var typeLabels = new Dictionary<int, string>();
        for (int i = 0; i < ttArr.Count; i++)
            typeLabels[i] = ttArr[i]["LabelTrType"].AsString;

        int patchedSlots = 0;

        // 1. Aaron (E4 1): Replace ALL Shaymin (492) with Ho-Oh (250) and Lugia (249)
        var aaronHoOh = new (int tid, int slot)[] { (701, 1), (855, 1), (857, 1), (923, 1), (925, 1), (2924, 6), (4924, 6) };
        var aaronLugia = new (int tid, int slot)[] { (943, 4), (856, 1), (858, 1), (924, 6), (926, 6), (2926, 6), (4926, 6) };
        foreach (var (tid, slot) in aaronHoOh) { ReplaceSlot(tpArr[tid], slot, hoOh); patchedSlots++; }
        foreach (var (tid, slot) in aaronLugia) { ReplaceSlot(tpArr[tid], slot, lugia); patchedSlots++; }

        // 2. Lucian (E4 4): Add Deoxys (386) to every story, League, and rematch team (preserving Latias/Latios + Mega)
        var lucianDeoxys = new (int tid, int slot)[] {
            (944, 4),
            (867, 3), (868, 5), (869, 2), (870, 3),
            (935, 1), (936, 2), (937, 3), (938, 3)
        };
        foreach (var (tid, slot) in lucianDeoxys) { ReplaceSlot(tpArr[tid], slot, deoxys); patchedSlots++; }

        // 3. Cynthia (Champion): Add Arceus (493) to every League & rematch team (preserving Giratina/Rayquaza + Mega Garchomp)
        var cynthiaArceus = new (int tid, int slot)[] {
            (700, 5),
            (871, 5), (872, 5), (873, 3), (874, 4),
            (939, 3), (940, 3), (941, 3), (942, 5)
        };
        foreach (var (tid, slot) in cynthiaArceus) { ReplaceSlot(tpArr[tid], slot, arceusCynthia); patchedSlots++; }

        // 4. Boss Cyrus: Add Arceus (493) to all 8 main battles (preserving Dialga/Palkia + Mega)
        var cyrusArceus = new (int tid, int slot)[] {
            (879, 1), (308, 4), (882, 3), (309, 1),
            (887, 3), (888, 3), (889, 2), (890, 4)
        };
        foreach (var (tid, slot) in cyrusArceus) { ReplaceSlot(tpArr[tid], slot, arceusCyrus); patchedSlots++; }

        // 5. Team Galactic Commanders (Mars, Jupiter, Saturn): Phione (early) & Manaphy (late/post-game)
        var galacticPhione = new (int tid, int slot)[] {
            (220, 3), (875, 3), // Mars Valley Windworks
            (311, 3), (876, 3), // Jupiter Eterna Building
        };
        foreach (var (tid, slot) in galacticPhione) { ReplaceSlot(tpArr[tid], slot, phione); patchedSlots++; }
        // Saturn TID 877 (3 mons: P1 Mega Alakazam, P2 Mesprit, P3 Toxicroak) -> shift Toxicroak to P4 and put Phione in P3
        if (tpArr[877]["P4MonsNo"].AsInt == 0)
            CopySlot(tpArr[877], 3, 4);
        ReplaceSlot(tpArr[877], 3, phione, 33);
        patchedSlots++;

        var galacticManaphy = new (int tid, int slot)[] {
            (310, 2), (881, 5), (401, 2), (884, 5),             // Mars Lake Verity & Spear Pillar
            (1026, 3), (1027, 3), (1028, 3), (1029, 3),         // Mars Post-game
            (312, 5), (885, 4),                                 // Jupiter Spear Pillar
            (1030, 2), (1031, 2), (1032, 2), (1033, 2),         // Jupiter Post-game
            (313, 4), (880, 4), (314, 4), (883, 4),             // Saturn Lake Valor & Galactic HQ
        };
        foreach (var (tid, slot) in galacticManaphy) { ReplaceSlot(tpArr[tid], slot, manaphy); patchedSlots++; }

        // 6. Equip Blue Orb (535) on EVERY Kyogre (382) and Red Orb (534) on EVERY Groudon (383) across TrainerTable
        int primalOrbsEquipped = 0;
        for (int tid = 0; tid < tpArr.Count; tid++)
        {
            var tp = tpArr[tid];
            for (int s = 1; s <= 6; s++)
            {
                int m = tp[$"P{s}MonsNo"].AsInt;
                if (m == 382)
                {
                    tp[$"P{s}Item"].AsInt = BlueOrb;
                    primalOrbsEquipped++;
                }
                else if (m == 383)
                {
                    tp[$"P{s}Item"].AsInt = RedOrb;
                    primalOrbsEquipped++;
                }
            }
        }

        ttInfo.SetNewData(ttBf);

        // Save LZ4-packed bundle to both romfs-delta and romfs
        string tmp = DeltaMasterPath + ".tmp";
        string tmpLz4 = DeltaMasterPath + ".lz4";
        bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(inst.file);
        using (var w = new AssetsFileWriter(tmp))
            bun.file.Write(w);
        am.UnloadAll(true);

        var packBun = new AssetBundleFile();
        using (var r = new AssetsFileReader(tmp))
        {
            packBun.Read(r);
            using var cw = new AssetsFileWriter(tmpLz4);
            packBun.Pack(cw, AssetBundleCompressionType.LZ4);
        }
        File.Delete(tmp);
        byte[] finalBytes = File.ReadAllBytes(tmpLz4);
        File.Delete(tmpLz4);

        File.WriteAllBytes(DeltaMasterPath, finalBytes);
        File.WriteAllBytes(RomfsMasterPath, finalBytes);
        Console.WriteLine($"Patched {patchedSlots} legendary boss slots + equipped {primalOrbsEquipped} Primal Orbs ({finalBytes.Length:N0} bytes).");

        // Fresh round-trip verification
        var all35 = new HashSet<int> {
            144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251,
            377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
            480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493
        };
        var foundLegends = new HashSet<int>();
        int foundBalls = 0;
        int aaronShaymin = 0;
        int unequippedPrimals = 0;

        var vam = new AssetsManager();
        var vbun = vam.LoadBundleFile(RomfsMasterPath, true);
        var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
        foreach (var info in vinst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = vam.GetBaseField(vinst, info);
            string nm = bf["m_Name"].AsString;
            if (nm.StartsWith("PlaceData"))
            {
                var arr = bf["Data.Array"];
                if (!arr.IsDummy)
                    foundBalls += arr.Children.Count(e => e["ID"].AsString.StartsWith("FLD_ITEM_MEGA_"));
            }
            else if (nm == "TrainerTable")
            {
                var vtd = bf["TrainerData.Array"].Children;
                var vtp = bf["TrainerPoke.Array"].Children;
                var vtt = bf["TrainerType.Array"].Children;
                for (int tid = 0; tid < vtd.Count && tid < vtp.Count; tid++)
                {
                    int typeId = vtd[tid]["TypeID"].AsInt;
                    string tlbl = (typeId >= 0 && typeId < vtt.Count) ? vtt[typeId]["LabelTrType"].AsString : "";
                    for (int s = 1; s <= 6; s++)
                    {
                        int m = vtp[tid][$"P{s}MonsNo"].AsInt;
                        int it = vtp[tid][$"P{s}Item"].AsInt;
                        if (all35.Contains(m)) foundLegends.Add(m);
                        if (tlbl == "DP_Trainers_Type_TR_BIGFOUR1_01" && m == 492) aaronShaymin++;
                        if (m == 382 && it != BlueOrb) unequippedPrimals++;
                        if (m == 383 && it != RedOrb) unequippedPrimals++;
                    }
                }
            }
        }
        vam.UnloadAll(true);

        var missing = all35.Except(foundLegends).OrderBy(x => x).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Missing legendaries: {string.Join(", ", missing)}");
        if (aaronShaymin > 0)
            throw new InvalidOperationException($"Aaron still has {aaronShaymin} Shaymin slots!");
        if (unequippedPrimals > 0)
            throw new InvalidOperationException($"Found {unequippedPrimals} Kyogre/Groudon without Primal Orbs!");
        if (foundBalls != 40)
            throw new InvalidOperationException($"Expected 40 Mega Stone Poké Balls intact, found {foundBalls}!");

        Console.WriteLine($"[PASS] Round-trip verified: 35/35 Gen 1-4 Legendaries used, 0 Aaron Shaymin, 0 unequipped Kyogre/Groudon, {foundBalls}/40 Mega Stone Poké Balls intact!");
        return 0;
    }
}
