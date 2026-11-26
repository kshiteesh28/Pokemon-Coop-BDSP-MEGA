using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

var repoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
var srcMaster = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas";
var patchJsonPath = "/tmp/boss_roster_patch.json";

var outPaths = new[]
{
    Path.Combine(repoRoot, "romfs/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas"),
    Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr/masterdatas"),
};

// All 92 valid Mega Stone item IDs from src/mega/main.cpp
var megaStoneIds = new HashSet<int>
{
    656, 657, 658, 659, 660, 661, 662, 663, 664, 665, 666, 667, 668, 669, 670, 671,
    672, 673, 674, 675, 676, 677, 678, 679, 680, 681, 682, 683, 684, 685,
    752, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 767, 768, 769, 770,
    837, 838, 839, 840, 848, 859, 867, 868, 869, 870, 871, 897, 898, 899,
    1058, 1059, 1060, 1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1069, 1070,
    1071, 1072, 1073, 1078, 1079, 622, 1082, 1083, 1101, 1102,
    1268, 1272, 1273, 1274, 1275, 1276, 1277, 426
};

var legendarySpeciesIds = new HashSet<int>
{
    144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251,
    377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
    480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493
};

var fantinaStoryIds = new HashSet<int> { 831, 832, 833, 834 };

static int[] ScaleEvs(int[] weights, int budget)
{
    if (budget <= 0) return new int[6];
    budget = Math.Min(budget, 508);
    int weightSum = weights.Sum();
    if (weightSum <= 0) return new int[6];

    var result = new int[6];
    int used = 0;
    int primaryIdx = 0;
    for (int i = 0; i < 6; i++)
    {
        if (weights[i] > weights[primaryIdx]) primaryIdx = i;
        int raw = (int)Math.Floor((double)weights[i] * budget / weightSum);
        int rounded = (raw / 4) * 4;
        rounded = Math.Clamp(rounded, 0, 252);
        result[i] = rounded;
        used += rounded;
    }

    // Distribute remaining budget in multiples of 4 to non-zero weight stats (respecting 252 cap & 508 total)
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

using var doc = JsonDocument.Parse(File.ReadAllText(patchJsonPath));
var patchList = doc.RootElement.EnumerateArray().ToList();
Console.WriteLine($"Loaded {patchList.Count} boss trainer patches from {patchJsonPath}");

var am = new AssetsManager();
var bun = am.LoadBundleFile(srcMaster, true);
var origCompression = bun.file.Header.GetCompressionType();
var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

AssetFileInfo? trainerTableInfo = null;
AssetTypeValueField? trainerTableBf = null;

foreach (var mb in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
{
    var bf = am.GetBaseField(inst, mb);
    if (bf["m_Name"].AsString == "TrainerTable")
    {
        trainerTableInfo = mb;
        trainerTableBf = bf;
        break;
    }
}

if (trainerTableInfo == null || trainerTableBf == null)
    throw new Exception("TrainerTable MonoBehaviour not found in masterdatas!");

var tpArr = trainerTableBf["TrainerPoke.Array"].Children;
int totalReplacements = 0;
int totalStoneAssignments = 0;
int totalMoveStrips = 0;

foreach (var p in patchList)
{
    int tid = p.GetProperty("id").GetInt32();
    if (tid < 0 || tid >= tpArr.Count)
        throw new Exception($"Trainer ID {tid} out of range (max={tpArr.Count})");

    var tp = tpArr[tid];

    // Clear any pre-existing Mega Stones on this trainer before applying exact designated Mega Stone
    for (int s = 1; s <= 6; s++)
    {
        int curItem = tp[$"P{s}Item"].AsInt;
        if (megaStoneIds.Contains(curItem))
            tp[$"P{s}Item"].AsInt = 0;
    }

    foreach (var opObj in p.GetProperty("ops").EnumerateArray())
    {
        string op = opObj.GetProperty("op").GetString()!;
        int slot = opObj.GetProperty("slot").GetInt32();
        if (slot < 1 || slot > 6)
            throw new Exception($"Invalid slot {slot} for trainer {tid}");

        if (op == "replace")
        {
            // Read replaced slot's existing EV budget and IV tier so new mon matches battle difficulty!
            int origEvTotal =
                tp[$"P{slot}EffortHp"].AsInt +
                tp[$"P{slot}EffortAtk"].AsInt +
                tp[$"P{slot}EffortDef"].AsInt +
                tp[$"P{slot}EffortSpAtk"].AsInt +
                tp[$"P{slot}EffortSpDef"].AsInt +
                tp[$"P{slot}EffortAgi"].AsInt;

            int origIvTier = new[]
            {
                tp[$"P{slot}TalentHp"].AsInt,
                tp[$"P{slot}TalentDef"].AsInt,
                tp[$"P{slot}TalentSpAtk"].AsInt,
                tp[$"P{slot}TalentSpDef"].AsInt,
                tp[$"P{slot}TalentAgi"].AsInt
            }.Max();
            if (origIvTier <= 0) origIvTier = 25;

            int mons = opObj.GetProperty("mons").GetInt32();
            int form = opObj.GetProperty("form").GetInt32();
            int ability = opObj.GetProperty("ability").GetInt32();
            int nature = opObj.GetProperty("nature").GetInt32();
            bool isSpecial = opObj.GetProperty("is_special").GetBoolean();
            int item = opObj.GetProperty("item").GetInt32();
            var evWeights = opObj.GetProperty("evs").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var moves = opObj.GetProperty("moves").EnumerateArray().Select(m => m.GetInt32()).ToArray();

            var scaledEvs = ScaleEvs(evWeights, origEvTotal);

            tp[$"P{slot}MonsNo"].AsInt = mons;
            tp[$"P{slot}FormNo"].AsInt = form;
            tp[$"P{slot}IsRare"].AsInt = 0;
            tp[$"P{slot}Sex"].AsInt = 0;
            tp[$"P{slot}Seikaku"].AsInt = nature;
            tp[$"P{slot}Tokusei"].AsInt = ability;
            tp[$"P{slot}Item"].AsInt = item;
            tp[$"P{slot}Waza1"].AsInt = moves[0];
            tp[$"P{slot}Waza2"].AsInt = moves[1];
            tp[$"P{slot}Waza3"].AsInt = moves[2];
            tp[$"P{slot}Waza4"].AsInt = moves[3];

            tp[$"P{slot}TalentHp"].AsInt = origIvTier;
            tp[$"P{slot}TalentAtk"].AsInt = isSpecial ? 0 : origIvTier;
            tp[$"P{slot}TalentDef"].AsInt = origIvTier;
            tp[$"P{slot}TalentSpAtk"].AsInt = origIvTier;
            tp[$"P{slot}TalentSpDef"].AsInt = origIvTier;
            tp[$"P{slot}TalentAgi"].AsInt = origIvTier;

            tp[$"P{slot}EffortHp"].AsInt = scaledEvs[0];
            tp[$"P{slot}EffortAtk"].AsInt = scaledEvs[1];
            tp[$"P{slot}EffortDef"].AsInt = scaledEvs[2];
            tp[$"P{slot}EffortSpAtk"].AsInt = scaledEvs[3];
            tp[$"P{slot}EffortSpDef"].AsInt = scaledEvs[4];
            tp[$"P{slot}EffortAgi"].AsInt = scaledEvs[5];

            totalReplacements++;
        }
        else if (op == "set_item")
        {
            int item = opObj.GetProperty("item").GetInt32();
            tp[$"P{slot}Item"].AsInt = item;
            totalStoneAssignments++;
        }
        else if (op == "replace_move")
        {
            int oldMove = opObj.GetProperty("old_move").GetInt32();
            int newMove = opObj.GetProperty("new_move").GetInt32();
            for (int w = 1; w <= 4; w++)
            {
                if (tp[$"P{slot}Waza{w}"].AsInt == oldMove)
                {
                    tp[$"P{slot}Waza{w}"].AsInt = newMove;
                    totalMoveStrips++;
                }
            }
        }
    }
}

trainerTableInfo.SetNewData(trainerTableBf);

// Write modified AssetsFile back into bundle, then LZ4-pack for Switch (matches ~723 KB Luminescent Platinum format)
using var assetsMs = new MemoryStream();
using (var aw = new AssetsFileWriter(assetsMs))
{
    inst.file.Write(aw);
}
bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(assetsMs.ToArray());

byte[] rawBunBytes;
using (var uncompressedBunMs = new MemoryStream())
{
    using (var ubw = new AssetsFileWriter(uncompressedBunMs))
    {
        bun.file.Write(ubw);
        rawBunBytes = uncompressedBunMs.ToArray();
    }
}
var packBun = new AssetBundleFile();
using (var readMs = new MemoryStream(rawBunBytes))
using (var ubr = new AssetsFileReader(readMs))
{
    packBun.Read(ubr);
    byte[] compressedBytes;
    using (var compressedMs = new MemoryStream())
    {
        using (var cw = new AssetsFileWriter(compressedMs))
        {
            packBun.Pack(cw, AssetBundleCompressionType.LZ4);
            compressedBytes = compressedMs.ToArray();
        }
    }
    foreach (var outPath in outPaths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, compressedBytes);
        Console.WriteLine($"Wrote LZ4-packed masterdatas ({compressedBytes.Length:N0} bytes) -> {outPath}");
    }
}

Console.WriteLine($"Applied: {totalReplacements} full slot replacements, {totalStoneAssignments} Mega Stone assignments, {totalMoveStrips} Dragon Ascent strips.");

// =========================================================================
// POST-WRITE ROUND-TRIP VERIFICATION (Fresh AssetsManager reload from disk)
// =========================================================================
var verifyAm = new AssetsManager();
var verifyBun = verifyAm.LoadBundleFile(outPaths[0], true);
var verifyInst = verifyAm.LoadAssetsFileFromBundle(verifyBun, 0, false);
AssetTypeValueField? vBf = null;
foreach (var mb in verifyInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
{
    var bf = verifyAm.GetBaseField(verifyInst, mb);
    if (bf["m_Name"].AsString == "TrainerTable")
    {
        vBf = bf;
        break;
    }
}
if (vBf == null) throw new Exception("VERIFY FAILED: TrainerTable missing on reload!");

var vTpArr = vBf["TrainerPoke.Array"].Children;
int errors = 0;
foreach (var p in patchList)
{
    int tid = p.GetProperty("id").GetInt32();
    string tName = p.GetProperty("name").GetString()!;
    var tp = vTpArr[tid];
    var replacedSlots = p.GetProperty("ops").EnumerateArray()
        .Where(o => o.GetProperty("op").GetString() == "replace")
        .Select(o => o.GetProperty("slot").GetInt32())
        .ToHashSet();

    int maxLv = 0;
    int legendCount = 0;
    int megaStoneCount = 0;
    for (int s = 1; s <= 6; s++)
    {
        int mons = tp[$"P{s}MonsNo"].AsInt;
        if (mons <= 0) continue;
        int lv = tp[$"P{s}Level"].AsInt;
        int item = tp[$"P{s}Item"].AsInt;
        int tok = tp[$"P{s}Tokusei"].AsInt;
        int nat = tp[$"P{s}Seikaku"].AsInt;
        maxLv = Math.Max(maxLv, lv);

        if (legendarySpeciesIds.Contains(mons)) legendCount++;
        if (megaStoneIds.Contains(item)) megaStoneCount++;

        int[] evs =
        {
            tp[$"P{s}EffortHp"].AsInt, tp[$"P{s}EffortAtk"].AsInt, tp[$"P{s}EffortDef"].AsInt,
            tp[$"P{s}EffortSpAtk"].AsInt, tp[$"P{s}EffortSpDef"].AsInt, tp[$"P{s}EffortAgi"].AsInt
        };
        int[] ivs =
        {
            tp[$"P{s}TalentHp"].AsInt, tp[$"P{s}TalentAtk"].AsInt, tp[$"P{s}TalentDef"].AsInt,
            tp[$"P{s}TalentSpAtk"].AsInt, tp[$"P{s}TalentSpDef"].AsInt, tp[$"P{s}TalentAgi"].AsInt
        };
        int[] wazas = { tp[$"P{s}Waza1"].AsInt, tp[$"P{s}Waza2"].AsInt, tp[$"P{s}Waza3"].AsInt, tp[$"P{s}Waza4"].AsInt };

        if (mons > 1025 || lv < 1 || lv > 100 || nat < 0 || nat > 24 || tok <= 0)
        {
            Console.WriteLine($"ERR tid={tid} ({tName}) P{s}: invalid core field mons={mons} lv={lv} nat={nat} tok={tok}");
            errors++;
        }
        if (replacedSlots.Contains(s) && (evs.Any(e => e < 0 || e > 252) || evs.Sum() > 510))
        {
            Console.WriteLine($"ERR tid={tid} ({tName}) P{s}: invalid EVs [{string.Join(",", evs)}] sum={evs.Sum()}");
            errors++;
        }
        if (ivs.Any(v => v < 0 || v > 31))
        {
            Console.WriteLine($"ERR tid={tid} ({tName}) P{s}: invalid IVs [{string.Join(",", ivs)}]");
            errors++;
        }
        var nonZeroMoves = wazas.Where(w => w > 0).ToArray();
        if (replacedSlots.Contains(s))
        {
            if (nonZeroMoves.Length != 4 || nonZeroMoves.Distinct().Count() != 4)
            {
                Console.WriteLine($"ERR tid={tid} ({tName}) P{s}: replaced slot must have 4 unique non-zero moves [{string.Join(",", wazas)}]");
                errors++;
            }
        }
        else if (nonZeroMoves.Length != nonZeroMoves.Distinct().Count())
        {
            Console.WriteLine($"ERR tid={tid} ({tName}) P{s}: duplicate moves [{string.Join(",", wazas)}]");
            errors++;
        }
    }

    if (legendCount < 1)
    {
        Console.WriteLine($"ERR tid={tid} ({tName}): missing Mandatory Legendary!");
        errors++;
    }
    bool expectMega = (maxLv > 33) || fantinaStoryIds.Contains(tid);
    if (expectMega && megaStoneCount != 1)
    {
        Console.WriteLine($"ERR tid={tid} ({tName}, Lv {maxLv}): expected 1 Mega Stone, got {megaStoneCount}");
        errors++;
    }
    if (!expectMega && megaStoneCount != 0)
    {
        Console.WriteLine($"ERR tid={tid} ({tName}, Lv {maxLv}): expected 0 Mega Stones, got {megaStoneCount}");
        errors++;
    }
}

if (errors > 0)
    throw new Exception($"VERIFICATION FAILED with {errors} errors!");

Console.WriteLine($"ROUND-TRIP VERIFICATION PASSED: All {patchList.Count} boss trainers valid (EVs, IVs, moves, abilities, natures, Legendaries, Mega Stones)!");
