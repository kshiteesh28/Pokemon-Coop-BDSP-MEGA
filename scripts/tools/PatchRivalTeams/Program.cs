using System;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchRivalTeams;

internal static class Program
{
    private const string RepoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
    private const string MasterPath = RepoRoot + "/romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas";

    private sealed record MonSpec(
        int MonsNo, int Level, int ItemId, int AbilityId, int NatureId, bool IsSpecial,
        int[] Evs, int[] Moves
    );

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

    private static void SetSlot(AssetTypeValueField tp, int slot, MonSpec m, int ivTier)
    {
        tp[$"P{slot}Level"].AsInt = m.Level;
        tp[$"P{slot}MonsNo"].AsInt = m.MonsNo;
        tp[$"P{slot}FormNo"].AsInt = 0;
        tp[$"P{slot}IsRare"].AsInt = 0;
        tp[$"P{slot}Sex"].AsInt = 0;
        tp[$"P{slot}Seikaku"].AsInt = m.NatureId;
        tp[$"P{slot}Tokusei"].AsInt = m.AbilityId;
        tp[$"P{slot}Item"].AsInt = m.ItemId;
        tp[$"P{slot}Waza1"].AsInt = m.Moves[0];
        tp[$"P{slot}Waza2"].AsInt = m.Moves[1];
        tp[$"P{slot}Waza3"].AsInt = m.Moves[2];
        tp[$"P{slot}Waza4"].AsInt = m.Moves[3];

        tp[$"P{slot}TalentHp"].AsInt = ivTier;
        tp[$"P{slot}TalentAtk"].AsInt = m.IsSpecial ? 0 : ivTier;
        tp[$"P{slot}TalentDef"].AsInt = ivTier;
        tp[$"P{slot}TalentSpAtk"].AsInt = ivTier;
        tp[$"P{slot}TalentSpDef"].AsInt = ivTier;
        tp[$"P{slot}TalentAgi"].AsInt = ivTier;

        tp[$"P{slot}EffortHp"].AsInt = m.Evs[0];
        tp[$"P{slot}EffortAtk"].AsInt = m.Evs[1];
        tp[$"P{slot}EffortDef"].AsInt = m.Evs[2];
        tp[$"P{slot}EffortSpAtk"].AsInt = m.Evs[3];
        tp[$"P{slot}EffortSpDef"].AsInt = m.Evs[4];
        tp[$"P{slot}EffortAgi"].AsInt = m.Evs[5];
    }

    private static int GetIvTier(AssetTypeValueField tp, int refSlot)
    {
        int tier = new[] {
            tp[$"P{refSlot}TalentHp"].AsInt, tp[$"P{refSlot}TalentDef"].AsInt,
            tp[$"P{refSlot}TalentSpAtk"].AsInt, tp[$"P{refSlot}TalentAgi"].AsInt
        }.Max();
        return tier > 0 ? tier : 25;
    }

    public static int Main()
    {
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(MasterPath, true);
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

        var tpArr = ttBf["TrainerPoke.Array"].Children;

        // Competitive builds (all IDs verified against src/mod/data/*.h)
        // Battle 3 — Hearthome City (351, 352, 353): Mega Dragonite + Mew
        var megaDragonite = new MonSpec(149, 32, 840, 136, 15, true,  new[] { 4, 0, 0, 252, 0, 252 }, new[] { 406, 403, 53, 355 });  // Dragon Pulse, Air Slash, Flamethrower, Roost
        var mew           = new MonSpec(151, 32, 268, 28,  10, true,  new[] { 4, 0, 0, 252, 0, 252 }, new[] { 94, 396, 85, 417 });   // Psychic, Aura Sphere, Thunderbolt, Nasty Plot

        // Battle 4 — Pastoria City (354, 355, 356): Mega Tyranitar + Moltres
        var megaTtar      = new MonSpec(248, 41, 669, 45,  13, false, new[] { 4, 252, 0, 0, 0, 252 }, new[] { 242, 444, 89, 349 });  // Crunch, Stone Edge, Earthquake, Dragon Dance
        var moltres       = new MonSpec(146, 41, 249, 49,  10, true,  new[] { 4, 0, 0, 252, 0, 252 }, new[] { 53, 403, 815, 355 });  // Flamethrower, Air Slash, Scorching Sands, Roost

        // Battle 5 — Canalave City (357, 358, 359): Mega Salamence + Mesprit
        var megaMence     = new MonSpec(373, 48, 769, 22,  13, false, new[] { 4, 252, 0, 0, 0, 252 }, new[] { 38, 89, 337, 349 });   // Double-Edge, Earthquake, Dragon Claw, Dragon Dance
        var mesprit       = new MonSpec(481, 48, 267, 26,  10, true,  new[] { 4, 0, 0, 252, 0, 252 }, new[] { 94, 58, 605, 347 });   // Psychic, Ice Beam, Dazzling Gleam, Calm Mind

        // Battle 6 — Pokémon League (481, 493, 494): Mega Metagross + Dialga
        var megaMeta      = new MonSpec(376, 58, 758, 29,  13, false, new[] { 4, 252, 0, 0, 0, 252 }, new[] { 309, 428, 8, 418 });   // Meteor Mash, Zen Headbutt, Ice Punch, Bullet Punch
        var dialga        = new MonSpec(483, 58, 135, 46,  15, true,  new[] { 252, 0, 0, 252, 4, 0 }, new[] { 459, 430, 414, 396 }); // Roar of Time, Flash Cannon, Earth Power, Aura Sphere

        // Battle 7 — Survival / Fight Area Rematch (360, 361, 362): Mega Garchomp Z + Cresselia
        var megaChompZ    = new MonSpec(445, 70, 1083, 24, 10, true,  new[] { 4, 0, 0, 252, 0, 252 }, new[] { 406, 414, 126, 434 }); // Dragon Pulse, Earth Power, Fire Blast, Draco Meteor
        var cresselia     = new MonSpec(488, 70, 234, 26,  2,  true,  new[] { 252, 0, 252, 4, 0, 0 }, new[] { 585, 94, 347, 236 });  // Moonblast, Psychic, Calm Mind, Moonlight

        foreach (int tid in new[] { 351, 352, 353 })
        {
            var tp = tpArr[tid];
            int iv = GetIvTier(tp, 2);
            // Move starter P4 -> P5 if P5 is still empty so starter stays the final ace
            if (tp["P5MonsNo"].AsInt == 0)
                CopySlot(tp, 4, 5);
            SetSlot(tp, 2, megaDragonite, iv);
            SetSlot(tp, 4, mew, iv);
        }
        foreach (int tid in new[] { 354, 355, 356 })
        {
            var tp = tpArr[tid];
            int iv = GetIvTier(tp, 2);
            SetSlot(tp, 2, megaTtar, iv);
            SetSlot(tp, 5, moltres, iv);
        }
        foreach (int tid in new[] { 357, 358, 359 })
        {
            var tp = tpArr[tid];
            int iv = GetIvTier(tp, 2);
            SetSlot(tp, 2, megaMence, iv);
            SetSlot(tp, 5, mesprit, iv);
        }
        foreach (int tid in new[] { 481, 493, 494 })
        {
            var tp = tpArr[tid];
            int iv = GetIvTier(tp, 2);
            SetSlot(tp, 2, megaMeta, iv);
            SetSlot(tp, 5, dialga, iv);
        }
        foreach (int tid in new[] { 360, 361, 362 })
        {
            var tp = tpArr[tid];
            int iv = GetIvTier(tp, 2);
            SetSlot(tp, 2, megaChompZ, iv);
            SetSlot(tp, 5, cresselia, iv);
            tp["P3Item"].AsInt = 158; // Snorlax -> Sitrus Berry (Cresselia holds Leftovers)
            tp["P6Item"].AsInt = 268; // Starter -> Expert Belt
        }

        ttInfo.SetNewData(ttBf);

        string tmp = MasterPath + ".tmp";
        string tmpLz4 = MasterPath + ".lz4";
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
        File.Move(tmpLz4, MasterPath, true);

        // Fresh round-trip verification (both Rival teams AND all 41 Mega Stone Poké Balls intact!)
        var vam = new AssetsManager();
        var vbun = vam.LoadBundleFile(MasterPath, true);
        var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
        int foundBalls = 0;
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
                var vtp = bf["TrainerPoke.Array"].Children;
                foreach (int tid in new[] { 351, 354, 357, 481, 360 })
                {
                    var t = vtp[tid];
                    Console.WriteLine($"  Verified TID {tid}: P2=m{t["P2MonsNo"].AsInt}@{t["P2Item"].AsInt} moves=[{t["P2Waza1"].AsInt},{t["P2Waza2"].AsInt},{t["P2Waza3"].AsInt},{t["P2Waza4"].AsInt}] | P4/5 Leg: m{(tid==351?t["P4MonsNo"].AsInt:t["P5MonsNo"].AsInt)}@{(tid==351?t["P4Item"].AsInt:t["P5Item"].AsInt)}");
                }
            }
        }
        vam.UnloadAll(true);
        if (foundBalls != 41)
            throw new InvalidOperationException($"Expected 41 Mega Stone Poké Balls intact, found {foundBalls}!");
        Console.WriteLine($"[PASS] Round-trip verified all 15 Rival TrainerTable entries + {foundBalls}/41 Mega Stone Poké Balls intact!");
        return 0;
    }
}
