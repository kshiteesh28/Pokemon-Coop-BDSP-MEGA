using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

var repoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
var mainCppPath = Path.Combine(repoRoot, "src/mega/main.cpp");

var pmlIn = Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Pml/personal_masterdatas");
var pmlOuts = new[]
{
    Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Pml/personal_masterdatas"),
    Path.Combine(repoRoot, "romfs/Data/StreamingAssets/AssetAssistant/Pml/personal_masterdatas"),
};

var engIn = Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Message/english");
var engOuts = new[]
{
    Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Message/english"),
    Path.Combine(repoRoot, "romfs/Data/StreamingAssets/AssetAssistant/Message/english"),
};

// Map fallback icon constants in src/mega/main.cpp to guaranteed-valid native Mega Stone / stone icon IDs
var fallbackIconMap = new Dictionary<string, int>
{
    ["kSunStone"]     = 671, // Pinsirite (amber/gold Mega Stone)
    ["kMoonStone"]    = 667, // Aggronite (steel/silver Mega Stone)
    ["kFireStone"]    = 660, // Charizardite X (fire Mega Stone)
    ["kThunderStone"] = 658, // Ampharosite (electric Mega Stone)
    ["kWaterStone"]   = 661, // Blastoisinite (water Mega Stone)
    ["kLeafStone"]    = 659, // Venusaurite (grass Mega Stone)
    ["kShinyStone"]   = 657, // Gardevoirite (fairy/psychic Mega Stone)
    ["kDuskStone"]    = 656, // Gengarite (ghost/dark Mega Stone)
    ["kDawnStone"]    = 683, // Garchompite (dragon/fighting Mega Stone)
    ["kIceStone"]     = 763, // Glalitite (ice Mega Stone)
};

static bool IsNativeGen6MegaStone(int id) =>
    (id >= 656 && id <= 685) || (id >= 752 && id <= 764) || (id >= 767 && id <= 770);

// Parse all 92 Mega Stones directly from src/mega/main.cpp so C++ and RomFS never drift!
var cppText = File.ReadAllText(mainCppPath);
var stones = new List<(int Id, int IconId, string Name, string Desc)>();
var rx = new Regex(@"\{\s*(\d+)\s*,\s*(k\w+)\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\}");
foreach (Match m in rx.Matches(cppText))
{
    int id = int.Parse(m.Groups[1].Value);
    string fbKey = m.Groups[2].Value;
    string name = m.Groups[3].Value;
    string desc = m.Groups[4].Value;
    int iconId = IsNativeGen6MegaStone(id) ? id : fallbackIconMap[fbKey];
    stones.Add((id, iconId, name, desc));
}
if (stones.Count != 92)
    throw new Exception($"Expected 92 Mega Stones in kAllMegaStones, parsed {stones.Count}");
Console.WriteLine($"Parsed all {stones.Count} Mega Stones from src/mega/main.cpp.");

var primalOrbs = new (int Id, int IconId, string Name, string Desc)[]
{
    (534, 534, "Red Orb",  "A shiny red orb that is said to have a legend tied to it. Have Groudon hold it to undergo Primal Reversion in battle."),
    (535, 535, "Blue Orb", "A shiny blue orb that is said to have a legend tied to it. Have Kyogre hold it to undergo Primal Reversion in battle."),
};

var keyItems = new (int Id, int IconId, string Name, string Desc)[]
{
    (696, 111, "Mega Ring",     "This ring contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."),
    (744, 111, "Mega Bracelet", "This bracelet contains an untold power that somehow enables Pokémon carrying Mega Stones to Mega Evolve in battle."),
    (773, 111, "Key Stone",     "A stone filled with an unexplained power. It makes Pokémon that hold a Mega Stone Mega Evolve."),
};

static byte[] PackBundleLz4(AssetBundleFile bun, AssetsFile file)
{
    using var assetsMs = new MemoryStream();
    using (var aw = new AssetsFileWriter(assetsMs))
        file.Write(aw);
    bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(assetsMs.ToArray());

    byte[] rawBun;
    using (var rawMs = new MemoryStream())
    using (var bw = new AssetsFileWriter(rawMs))
    {
        bun.Write(bw);
        rawBun = rawMs.ToArray();
    }

    var packBun = new AssetBundleFile();
    using var readMs = new MemoryStream(rawBun);
    using var br = new AssetsFileReader(readMs);
    packBun.Read(br);
    using var outMs = new MemoryStream();
    using var cw = new AssetsFileWriter(outMs);
    packBun.Pack(cw, AssetBundleCompressionType.LZ4);
    return outMs.ToArray();
}

// =========================================================================
// 1. PATCH ItemTable in Pml/personal_masterdatas
// =========================================================================
{
    var am = new AssetsManager();
    var bun = am.LoadBundleFile(pmlIn, true);
    var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

    AssetFileInfo? itemTableInfo = null;
    AssetTypeValueField? itemTableBf = null;
    foreach (var mb in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        var bf = am.GetBaseField(inst, mb);
        if (bf["m_Name"].AsString == "ItemTable")
        {
            itemTableInfo = mb;
            itemTableBf = bf;
            break;
        }
    }
    if (itemTableInfo == null || itemTableBf == null)
        throw new Exception("ItemTable not found in personal_masterdatas!");

    var items = itemTableBf["Item.Array"].Children;
    var charcoal = items[249];
    var magmaStone = items[458];

    void ApplyTemplate(AssetTypeValueField target, AssetTypeValueField tmpl, int iconId)
    {
        target["type"].AsInt = tmpl["type"].AsInt;
        target["group"].AsInt = tmpl["group"].AsInt;
        target["group_id"].AsInt = tmpl["group_id"].AsInt;
        target["fld_pocket"].AsInt = tmpl["fld_pocket"].AsInt;
        target["field_func"].AsInt = tmpl["field_func"].AsInt;
        target["battle_func"].AsInt = tmpl["battle_func"].AsInt;
        target["flags0"].AsUInt = tmpl["flags0"].AsUInt;
        if (target["sort"].AsInt == 0 || target["sort"].AsInt == 255)
            target["sort"].AsInt = tmpl["sort"].AsInt;
        target["iconid"].AsInt = iconId;
    }

    foreach (var s in stones)
        ApplyTemplate(items[s.Id], charcoal, s.IconId);
    foreach (var o in primalOrbs)
        ApplyTemplate(items[o.Id], charcoal, o.IconId);
    foreach (var k in keyItems)
        ApplyTemplate(items[k.Id], magmaStone, k.IconId);

    itemTableInfo.SetNewData(itemTableBf);
    var packed = PackBundleLz4(bun.file, inst.file);
    foreach (var p in pmlOuts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, packed);
        Console.WriteLine($"Wrote patched personal_masterdatas ({packed.Length:N0} bytes) -> {p}");
    }

    // Round-trip verify personal_masterdatas
    var vam = new AssetsManager();
    var vbun = vam.LoadBundleFile(pmlOuts[0], true);
    var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
    foreach (var mb in vinst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        var bf = vam.GetBaseField(vinst, mb);
        if (bf["m_Name"].AsString != "ItemTable") continue;
        var vItems = bf["Item.Array"].Children;
        foreach (var s in stones)
        {
            if (vItems[s.Id]["iconid"].AsInt != s.IconId || (vItems[s.Id]["flags0"].AsUInt & 0x80000000U) != 0)
                throw new Exception($"ItemTable verify failed for {s.Name} ({s.Id})");
        }
    }
    Console.WriteLine("ROUND-TRIP VERIFIED: All 92 Mega Stones + 3 Key Items active with valid icons in personal_masterdatas!");
}

// =========================================================================
// 2. PATCH all 7 item message tables in Message/english
// =========================================================================
{
    var am = new AssetsManager();
    var bun = am.LoadBundleFile(engIn, true);
    var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

    var allEntries = stones.Select(s => (s.Id, s.Name, s.Desc))
        .Concat(primalOrbs.Select(o => (o.Id, o.Name, o.Desc)))
        .Concat(keyItems.Select(k => (k.Id, k.Name, k.Desc)))
        .ToList();

    int tablesPatched = 0;
    foreach (var mb in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        var bf = am.GetBaseField(inst, mb);
        var nm = bf["m_Name"].AsString;
        bool isName = nm == "english_ss_itemname" ||
                      nm == "english_ss_itemname_acc" ||
                      nm == "english_ss_itemname_plural" ||
                      nm == "english_ss_itemname_classified" ||
                      nm == "english_ss_itemname_acc_classified" ||
                      nm == "english_ss_itemname_plural_classified";
        bool isPlural = nm.Contains("plural");
        bool isInfo = nm == "english_ss_iteminfo";
        if (!isName && !isInfo) continue;

        var arr = bf["labelDataArray.Array"].Children;
        foreach (var e in allEntries)
        {
            if (e.Id >= arr.Count) continue;
            var ld = arr[e.Id];
            var words = ld["wordDataArray.Array"].Children;
            if (words.Count == 0) continue;
            string text = isInfo ? e.Desc : (isPlural ? e.Name + "s" : e.Name);
            words[0]["str"].AsString = text;
            for (int w = 1; w < words.Count; w++)
                words[w]["str"].AsString = "";
        }
        mb.SetNewData(bf);
        tablesPatched++;
    }
    if (tablesPatched != 7)
        throw new Exception($"Expected 7 item message tables in Message/english, patched {tablesPatched}");

    var packed = PackBundleLz4(bun.file, inst.file);
    foreach (var p in engOuts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, packed);
        Console.WriteLine($"Wrote patched Message/english ({packed.Length:N0} bytes) -> {p}");
    }

    // Round-trip verify Message/english
    var vam = new AssetsManager();
    var vbun = vam.LoadBundleFile(engOuts[0], true);
    var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
    foreach (var mb in vinst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
    {
        var bf = vam.GetBaseField(vinst, mb);
        var nm = bf["m_Name"].AsString;
        if (nm != "english_ss_itemname" && nm != "english_ss_iteminfo") continue;
        var arr = bf["labelDataArray.Array"].Children;
        foreach (var checkId in new[] { 622, 837, 859, 868, 1073, 1078 })
        {
            var txt = string.Concat(arr[checkId]["wordDataArray.Array"].Children.Select(w => w["str"].AsString));
            if (string.IsNullOrWhiteSpace(txt) || txt == "???" || txt == "---")
                throw new Exception($"Message/english verify failed for {nm}[{checkId}] -> '{txt}'");
            Console.WriteLine($"  Verified {nm}[{checkId}] = '{txt}'");
        }
    }
    Console.WriteLine("ROUND-TRIP VERIFIED: All 92 Mega Stones + 3 Key Items named & described in Message/english!");
}
