using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchMegaWorld;

internal static class Program
{
    private const string RepoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
    private const string LumiDprDir = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr";
    private const string DeltaAssetDir = RepoRoot + "/romfs-delta/Data/StreamingAssets/AssetAssistant";
    private const string DeltaDprDir = DeltaAssetDir + "/Dpr";
    private const string DeltaMsgPath = DeltaAssetDir + "/Message/english";

    private sealed record BallSpec(
        string StoneName,
        string MegaName,
        int ItemId,
        int FlagId,
        string Stage,
        string LocationName,
        string PlaceDataAsset,
        int ZoneId,
        int AnchorBallIndex
    );

    private sealed record NpcSpec(
        string StoneName,
        string MegaName,
        int ItemId,
        int FlagId,
        string Stage,
        string LocationName,
        string EvScriptAsset,
        string NpcLabel,
        string Dialogue
    );

    // All 41 Exploration Poké Balls (guaranteed unused flags from 2532..3114)
    private static readonly BallSpec[] Balls =
    {
        // Early Game (Badges 0-2)
        new("Charizardite Y", "Mega Charizard Y", 678, 2532, "Early Game", "Oreburgh Gate (1F)",            "PlaceData_D13R0101", 253, 0),
        new("Tyranitarite",   "Mega Tyranitar",   669, 2546, "Early Game", "Oreburgh Mine (B1F)",           "PlaceData_D01R0102", 196, 0),
        new("Aggronite",      "Mega Aggron",      667, 2551, "Early Game", "Oreburgh Mine (B1F)",           "PlaceData_D01R0102", 196, 1),
        new("Venusaurite",    "Mega Venusaur",    659, 2552, "Early Game", "Route 205 South / Floaroma",    "PlaceData_A03",      359, 0),
        new("Manectite",      "Mega Manectric",   682, 2553, "Early Game", "Valley Windworks (Exterior)",   "PlaceData_A03",      197, 0),
        new("Sceptilite",     "Mega Sceptile",    753, 2554, "Early Game", "Eterna Forest",                 "PlaceData_D03R0101", 200, 0),
        new("Pinsirite",      "Mega Pinsir",      671, 2555, "Early Game", "Eterna Forest",                 "PlaceData_D03R0101", 200, 1),
        new("Heracronite",    "Mega Heracross",   680, 2557, "Early Game", "Eterna Forest",                 "PlaceData_D03R0101", 200, 2),
        new("Victreebelite",  "Mega Victreebel",  838, 2558, "Early Game", "Eterna Forest",                 "PlaceData_D03R0101", 200, 3),
        new("Absolite Z",     "Mega Absol Z",     622, 2559, "Early Game", "Old Chateau (Dining Hall)",     "PlaceData_D25R0102", 307, 0),
        new("Gengarite",      "Mega Gengar",      656, 2560, "Early Game", "Old Chateau (2F Corridor)",     "PlaceData_D25R0103", 308, 0),
        new("Chandelurite",   "Mega Chandelure", 1059, 2570, "Early Game", "Old Chateau (2F TV Room)",      "PlaceData_D25R0108", 313, 0),
        new("Metagrossite",   "Mega Metagross",   758, 2589, "Early Game", "Wayward Cave (1F)",             "PlaceData_D21R0101", 292, 0),
        new("Mawilite",       "Mega Mawile",      681, 2593, "Early Game", "Wayward Cave (1F)",             "PlaceData_D21R0101", 292, 1),
        new("Garchompite",    "Mega Garchomp",    683, 2602, "Early Game", "Wayward Cave (Secret B1F)",     "PlaceData_D21R0102", 293, 0),
        new("Sablenite",      "Mega Sableye",     754, 2603, "Early Game", "Wayward Cave (Secret B1F)",     "PlaceData_D21R0102", 293, 1),

        // Mid Game (Badges 3-5)
        new("Charizardite X", "Mega Charizard X", 660, 2621, "Mid Game", "Route 207 (Mt. Coronet path)",    "PlaceData_A02",      364, 0),
        new("Garchompite Z",  "Mega Garchomp Z", 1083, 2678, "Mid Game", "Mt. Coronet (1F South)",          "PlaceData_D05R0101", 203, 0),
        new("Gardevoirite",   "Mega Gardevoir",   657, 2701, "Mid Game", "Amity Square (Hearthome)",        "PlaceData_D11R0101", 251, 0),
        new("Dragoninite",    "Mega Dragonite",   840, 2742, "Mid Game", "Route 209",                       "PlaceData_A05",      367, 0),
        new("Alakazite",      "Mega Alakazam",    679, 2743, "Mid Game", "Solaceon Ruins (Treasure Room)",  "PlaceData_D07R0116", 240, 0),
        new("Salamencite",    "Mega Salamence",   769, 2745, "Mid Game", "Mt. Coronet (1F Route 211 side)", "PlaceData_D05R0102", 204, 0),
        new("Kangaskhanite",  "Mega Kangaskhan",  675, 2778, "Mid Game", "Route 215",                       "PlaceData_A05",      394, 0),
        new("Houndoominite",  "Mega Houndoom",    666, 2781, "Mid Game", "Route 214",                       "PlaceData_A13",      392, 0),
        new("Feraligite",     "Mega Feraligatr",  859, 2795, "Mid Game", "Route 213 (Beach)",               "PlaceData_A13",      385, 0),
        new("Sharpedonite",   "Mega Sharpedo",    759, 2796, "Mid Game", "Valor Lakefront",                 "PlaceData_A13",      347, 0),
        new("Gyaradosite",    "Mega Gyarados",    676, 2797, "Mid Game", "Lake Valor",                      "PlaceData_D28R0101", 326, 0),

        // Mid-Late Game (Badges 6-8)
        new("Heatranite",     "Mega Heatran",     869, 3065, "Mid-Late Game", "Fuego Ironworks",               "PlaceData_D04R0101", 202, 0),
        new("Skarmorite",     "Mega Skarmory",    867, 3067, "Mid-Late Game", "Iron Island (B1F)",             "PlaceData_D24R0102", 300, 0),
        new("Steelixite",     "Mega Steelix",     761, 3068, "Mid-Late Game", "Iron Island (B2F)",             "PlaceData_D24R0103", 301, 0),
        new("Lucarionite Z",  "Mega Lucario Z",  1101, 3069, "Mid-Late Game", "Iron Island (B3F)",             "PlaceData_D24R0105", 303, 0),
        new("Latiasite",      "Mega Latias",      684, 3070, "Mid-Late Game", "Lake Verity",                   "PlaceData_D27R0101", 323, 0),
        new("Latiosite",      "Mega Latios",      685, 3071, "Mid-Late Game", "Lake Acuity",                   "PlaceData_D29R0101", 329, 0),
        new("Abomasite",      "Mega Abomasnow",   674, 3072, "Mid-Late Game", "Route 216",                     "PlaceData_A09",      395, 0),
        new("Mewtwonite X",   "Mega Mewtwo X",    662, 3073, "Mid-Late Game", "Mt. Coronet (2F/3F Interior)",  "PlaceData_D05R0112", 214, 0),
        new("Mewtwonite Y",   "Mega Mewtwo Y",    663, 3074, "Mid-Late Game", "Mt. Coronet (Snowy Exterior)",  "PlaceData_D05R0113", 215, 0),

        // Victory Road (5 Niche / Lower-Tier Mega Stones)
        new("Beedrillite",    "Mega Beedrill",    770, 3075, "Victory Road", "Victory Road (1F)",              "PlaceData_D09R0101", 244, 0),
        new("Pidgeotite",     "Mega Pidgeot",     762, 3076, "Victory Road", "Victory Road (1F)",              "PlaceData_D09R0101", 244, 1),
        new("Banettite",      "Mega Banette",     668, 3077, "Victory Road", "Victory Road (B1F)",             "PlaceData_D09R0102", 245, 0),
        new("Cameruptite",    "Mega Camerupt",    767, 3078, "Victory Road", "Victory Road (B1F)",             "PlaceData_D09R0102", 245, 1),
        new("Glalitite",      "Mega Glalie",      763, 3079, "Victory Road", "Victory Road (2F)",              "PlaceData_D09R0104", 247, 0),
    };

    // All 22 House / Street NPCs with custom Mega Pokémon lore dialogue (flags 3080..3114)
    private static readonly NpcSpec[] Npcs =
    {
        new("Staraptite",    "Mega Staraptor",   1082, 3080, "Early Game", "Jubilife City (Condominiums 2F)", "c01r0502", "ev_c01r0502_boy3",
            "Have you heard about Mega Staraptor? Its Fighting spirit awakens mid-flight! Take this Staraptite and see it soar!"),
        new("Aerodactylite", "Mega Aerodactyl",   672, 3081, "Early Game", "Oreburgh City (House)",           "c03r0201", "ev_c03r0201_oldwoman1",
            "Fossil researchers say Mega Aerodactyl's jagged rock spikes are its true ancient form. Here, take this Aerodactylite!"),
        new("Meganiumite",   "Mega Meganium",     848, 3082, "Early Game", "Floaroma Town (Flower House)",    "t03r0201", "ev_t03r0201_item_fanatic",
            "When Meganium Mega Evolves, its radiant blossoms fill the battlefield with sunlight! Keep this Meganiumite safe!"),
        new("Absolite",      "Mega Absol",        677, 3083, "Early Game", "Eterna City (Street)",            "c04",      "ev_c04_woman1",
            "Mega Absol spreads dazzling angelic wings that bounce status moves right back! I want you to have this Absolite."),
        new("Lopunnite",     "Mega Lopunny",      768, 3084, "Early Game", "Eterna City (House)",             "c04r0301", "ev_c04r0301_middleman1",
            "Mega Lopunny sheds its fluff for blinding speed and kicks that even strike Ghost types! Take this Lopunnite!"),

        new("Clefablite",    "Mega Clefable",     837, 3085, "Mid Game", "Hearthome City (House)",            "c05r1001", "ev_c05r1001_idol",
            "Moonlight lifts Mega Clefable into the sky with Fairy-and-Flying grace! This Clefablite belongs with a Trainer like you."),
        new("Chimechite",    "Mega Chimecho",    1079, 3086, "Mid Game", "Solaceon Town (House)",             "t04r0501", "ev_t04r0501_middlewoman1",
            "Listen closely—Mega Chimecho's metallic chimes resonate with Psychic and Steel harmony! Please accept this Chimechite."),
        new("Scizorite",     "Mega Scizor",       670, 3087, "Mid Game", "Route 210 (Café Cabin)",            "r210ar0101", "ev_r210ar0101_waitress",
            "A passing Trainer left this stone behind after raving about Mega Scizor's crushing steel pincers. Take this Scizorite!"),
        new("Lucarionite",   "Mega Lucario",      673, 3088, "Mid Game", "Veilstone City (House)",            "c07r0401", "ev_c07r0401_woman6",
            "Mega Lucario channels overflowing aura into Adaptability-boosted strikes! Take this Lucarionite and master its power."),
        new("Blazikenite",   "Mega Blaziken",     664, 3089, "Mid Game", "Veilstone City (Street)",           "c07",      "ev_c07_gorggeousw_iro_01",
            "With blazing wrists and relentless Speed Boost, Mega Blaziken dominates every clash! Here, take this Blazikenite!"),
        new("Medichamite",   "Mega Medicham",     665, 3090, "Mid Game", "Veilstone City (House)",            "c07r0701", "ev_c07r0701_middleman1",
            "Through Pure Power and total meditation, Mega Medicham delivers unmatched strikes. Take this Medichamite!"),
        new("Galladite",     "Mega Gallade",      756, 3091, "Mid Game", "Veilstone City (House)",            "c07r0901", "ev_c07r0901_explore",
            "Mega Gallade's flowing cape and twin blade arms embody a noble knight in battle! I'm entrusting this Galladite to you."),
        new("Blastoisinite", "Mega Blastoise",    661, 3092, "Mid Game", "Pastoria City (House)",             "c06r0301", "ev_c06r0301_oldman",
            "Mega Blastoise's massive shell cannon fires pulse blasts with Mega Launcher force! Take this Blastoisinite, youngster!"),
        new("Swampertite",   "Mega Swampert",     752, 3093, "Mid Game", "Pastoria City (Street)",            "c06",      "ev_c06_fighter",
            "In pouring rain, Mega Swampert's Swift Swim and towering muscles are unstoppable! Here, take this Swampertite!"),
        new("Slowbronite",   "Mega Slowbro",      760, 3094, "Mid Game", "Route 213 (Seaside Villa)",         "r213r0601","ev_r213r0601_gorggeousm",
            "Shellder's armored shell turns Mega Slowbro into an impenetrable spinning fortress! Enjoy this Slowbronite."),
        new("Altarianite",   "Mega Altaria",      755, 3095, "Mid Game", "Celestic Town (House)",             "t05r0101", "ev_t05r0101_oldman1",
            "Wrapped in cloud-like down, Mega Altaria awakens Dragon and Fairy power with Pixilate! Please take this Altarianite."),

        new("Starminite",    "Mega Starmie",      839, 3096, "Mid-Late Game", "Canalave City (House)",        "c02r0101", "ev_c02r0101_woman6",
            "Sailors speak of Mega Starmie's cosmic core gleaming with Huge Power across the waves! Take this Starminite!"),
        new("Darkranite",    "Mega Darkrai",      870, 3097, "Mid-Late Game", "Canalave City (Sailor's House)","c02r0501","ev_c02r0501_babyboy1_01",
            "I had a dream of a shadowed Mega Darkrai watching over Canalave... When I woke up, this Darkranite was by my bed. Take it!"),
        new("Froslassite",   "Mega Froslass",     868, 3098, "Mid-Late Game", "Route 217 (Snow Cabin)",       "r217r0101","ev_r217r0101_mount",
            "A chilling blizzard follows Mega Froslass wherever it drifts! Keep this Froslassite warm in your Bag!"),
        new("Ampharosite",   "Mega Ampharos",     658, 3099, "Mid-Late Game", "Sunyshore City (House)",       "c08r0101", "ev_c08r0101_middleman1",
            "Mega Ampharos reawakens ancient Dragon blood with a glowing mane of beacons! Take this Ampharosite!"),
        new("Raichunite X",  "Mega Raichu X",    1073, 3113, "Mid-Late Game", "Sunyshore City (Street)",      "c08",      "ev_c08_middlewoman1",
            "Mega Raichu X crackles with Electric Surge and fierce physical voltage! Here, take this Raichunite X!"),
        new("Raichunite Y",  "Mega Raichu Y",    1078, 3114, "Mid-Late Game", "Sunyshore City (House)",       "c08r0901", "ev_c08r0901_assistantm",
            "Mega Raichu Y unleashes pinpoint Thunder blasts with No Guard precision! Take this Raichunite Y!"),
    };

    private static string Slug(string s) =>
        new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static int FloatBits(float v) => BitConverter.SingleToInt32Bits(v);

    private static AssetTypeValueField MakeArg(AssetTypeValueField argArray, int argType, int data)
    {
        var a = ValueBuilder.DefaultValueFieldFromArrayTemplate(argArray);
        a["argType"].AsInt = argType;
        a["data"].AsInt = data;
        return a;
    }

    private static AssetTypeValueField MakeCmd(AssetTypeValueField cmdsArray, params (int argType, int data)[] args)
    {
        var cmd = ValueBuilder.DefaultValueFieldFromArrayTemplate(cmdsArray);
        var argArr = cmd["Arg.Array"];
        argArr.Children.Clear();
        foreach (var (t, d) in args)
            argArr.Children.Add(MakeArg(argArr, t, d));
        return cmd;
    }

    private static int EnsureString(AssetTypeValueField strListArr, string s)
    {
        for (int i = 0; i < strListArr.Children.Count; i++)
            if (strListArr.Children[i].AsString == s)
                return i;
        var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(strListArr);
        elem.AsString = s;
        strListArr.Children.Add(elem);
        return strListArr.Children.Count - 1;
    }

    private static void SaveBundleLz4(AssetsManager am, BundleFileInstance bun, AssetsFile file, string outPath)
    {
        string tmp = outPath + ".tmp";
        string tmpLz4 = outPath + ".lz4";
        bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(file);
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
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.Move(tmpLz4, outPath, true);
    }

    private static void ValidateSpecs()
    {
        if (Balls.Length + Npcs.Length != 63)
            throw new InvalidOperationException($"Expected 63 Mega Stones, got {Balls.Length + Npcs.Length}");
        var itemIds = Balls.Select(b => b.ItemId).Concat(Npcs.Select(n => n.ItemId)).ToList();
        var flagIds = Balls.Select(b => b.FlagId).Concat(Npcs.Select(n => n.FlagId)).ToList();
        if (itemIds.Distinct().Count() != 63)
            throw new InvalidOperationException("Duplicate ItemId detected in Mega Stone placement specs!");
        if (flagIds.Distinct().Count() != 63)
            throw new InvalidOperationException("Duplicate FlagId detected in Mega Stone placement specs!");
        if (itemIds.Contains(696) || itemIds.Contains(744) || itemIds.Contains(773))
            throw new InvalidOperationException("Mega Ring / Key Stone must NOT be included!");
        Console.WriteLine($"Validated all 63 unique Mega Stones ({Balls.Length} Poké Balls + {Npcs.Length} NPCs, 0 Mega Ring).");
    }

    private static Dictionary<string, (float x, float y, int h)> PatchMasterdatas()
    {
        string mdPath = Path.Combine(DeltaDprDir, "masterdatas");
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(mdPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        var placedCoords = new Dictionary<string, (float x, float y, int h)>();
        var ballsByAsset = Balls.GroupBy(b => b.PlaceDataAsset).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            string assetName = bf["m_Name"].AsString;
            if (!ballsByAsset.TryGetValue(assetName, out var specs)) continue;

            var arr = bf["Data.Array"];
            // Remove any prior mega ball entries for idempotency
            arr.Children.RemoveAll(e => e["ID"].AsString.StartsWith("FLD_ITEM_MEGA_"));

            // Track occupied grid tiles per zone in this PlaceData asset
            var occupied = new HashSet<(int z, int x, int y)>();
            foreach (var e in arr.Children)
                occupied.Add((e["zoneID"].AsInt, (int)MathF.Round(e["Position.x"].AsFloat), (int)MathF.Round(e["Position.y"].AsFloat)));

            foreach (var spec in specs)
            {
                var zoneBalls = arr.Children
                    .Where(e => e["zoneID"].AsInt == spec.ZoneId && e["ObjectGraphicIndex"].AsInt == 500)
                    .ToList();
                if (zoneBalls.Count == 0)
                    throw new InvalidOperationException($"No anchor Poké Ball in {assetName} zone={spec.ZoneId} for {spec.StoneName}");

                var anchor = zoneBalls[Math.Min(spec.AnchorBallIndex, zoneBalls.Count - 1)];
                int ax = (int)MathF.Round(anchor["Position.x"].AsFloat);
                int ay = (int)MathF.Round(anchor["Position.y"].AsFloat);
                int ah = anchor["HeightLayer"].AsInt;

                // Pick closest unoccupied neighbor tile (preferring +1X, -1X, +1Y, -1Y)
                (int dx, int dy)[] offsets = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (2, 0), (0, 2) };
                int px = ax + 1, py = ay;
                foreach (var (dx, dy) in offsets)
                {
                    if (!occupied.Contains((spec.ZoneId, ax + dx, ay + dy)))
                    {
                        px = ax + dx;
                        py = ay + dy;
                        break;
                    }
                }
                occupied.Add((spec.ZoneId, px, py));

                string slug = Slug(spec.StoneName);
                var ball = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
                ball["ID"].AsString = $"FLD_ITEM_MEGA_{slug.ToUpperInvariant()}";
                ball["zoneID"].AsInt = spec.ZoneId;
                ball["TrainerID"].AsInt = 0;
                ball["ObjectGraphicIndex"].AsInt = 500;
                ball["ColorIndex"].AsInt = 0;
                ball["Position.x"].AsFloat = px;
                ball["Position.y"].AsFloat = py;
                ball["HeightLayer"].AsInt = ah;
                ball["HeightIgnore"].AsByte = 0;
                ball["Size.x"].AsFloat = 0.0f;
                ball["Size.y"].AsFloat = 0.0f;
                ball["Rotation"].AsInt = 0;
                ball["MoveLimit.x"].AsFloat = 0.0f;
                ball["MoveLimit.y"].AsFloat = 0.0f;
                ball["EventType"].AsInt = 0;
                ball["MoveCode"].AsInt = 0;
                ball["MoveParam0"].AsInt = 0;
                ball["MoveParam1"].AsInt = 0;
                ball["MoveParam2"].AsInt = 0;
                ball["TalkLabel"].AsString = $"ev_fld_item_mega_{slug}";
                ball["ContactLabel"].AsString = "";
                ball["Work"].AsInt = spec.FlagId;
                ball["Dowsing"].AsInt = 0;
                ball["LoadFirst"].AsByte = 0;
                ball["DoNotLoad"].AsInt = 4000;
                ball["TalkToRange"].AsFloat = 1.25f;
                ball["TalkToSize.x"].AsFloat = 0.0f;
                ball["TalkToSize.y"].AsFloat = 0.0f;
                ball["TalkBit"].AsInt = 15;
                arr.Children.Add(ball);

                placedCoords[spec.StoneName] = (px, py, ah);
            }

            info.SetNewData(bf);
        }

        if (placedCoords.Count != Balls.Length)
            throw new InvalidOperationException($"Placed {placedCoords.Count}/{Balls.Length} Poké Balls!");

        SaveBundleLz4(am, bun, inst.file, mdPath);
        Console.WriteLine($"Patched {Balls.Length} Mega Stone Poké Balls into Dpr/masterdatas.");
        return placedCoords;
    }

    private static void PatchMessageEnglish()
    {
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(DeltaMsgPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        bool patched = false;
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            if (bf["m_Name"].AsString != "english_dp_scenario3") continue;

            var arr = bf["labelDataArray.Array"];
            // Remove any prior mega gift labels for idempotency
            arr.Children.RemoveAll(e => e["labelName"].AsString.StartsWith("00_msg_mega_gift_"));

            var template = arr.Children.Last();
            foreach (var npc in Npcs)
            {
                string labelName = $"00_msg_mega_gift_{Slug(npc.StoneName)}";
                int idx = arr.Children.Count;
                var clone = template.Clone();
                clone["labelIndex"].AsInt = idx;
                clone["arrayIndex"].AsInt = idx;
                clone["labelName"].AsString = labelName;
                var words = clone["wordDataArray.Array"].Children;
                words[0]["str"].AsString = npc.Dialogue;
                words[0]["strWidth"].AsFloat = npc.Dialogue.Length * 12.0f;
                for (int w = 1; w < words.Count; w++)
                    words[w]["str"].AsString = "";
                arr.Children.Add(clone);
            }

            info.SetNewData(bf);
            patched = true;
            break;
        }
        if (!patched)
            throw new InvalidOperationException("english_dp_scenario3 not found in Message/english!");

        SaveBundleLz4(am, bun, inst.file, DeltaMsgPath);
        Console.WriteLine($"Injected {Npcs.Length} custom Mega Pokémon NPC dialogues into Message/english (english_dp_scenario3).");
    }

    private static void PatchEvScript()
    {
        string srcEv = Path.Combine(LumiDprDir, "ev_script");
        string dstEv = Path.Combine(DeltaDprDir, "ev_script");
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(srcEv, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        bool patchedFldItem = false;
        var npcsByAsset = Npcs.GroupBy(n => n.EvScriptAsset).ToDictionary(g => g.Key, g => g.ToList());
        int patchedNpcs = 0;

        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            string name = bf["m_Name"].AsString;

            if (name == "fld_item")
            {
                var scripts = bf["Scripts.Array"];
                scripts.Children.RemoveAll(s => s["Label"].AsString.StartsWith("ev_fld_item_mega_"));
                foreach (var ball in Balls)
                {
                    var sc = ValueBuilder.DefaultValueFieldFromArrayTemplate(scripts);
                    sc["Label"].AsString = $"ev_fld_item_mega_{Slug(ball.StoneName)}";
                    var cmds = sc["Commands.Array"];
                    cmds.Children.Clear();
                    cmds.Children.Add(MakeCmd(cmds, (0, 187), (1, FloatBits(ball.ItemId)), (1, FloatBits(1f))));
                    cmds.Children.Add(MakeCmd(cmds, (0, 3)));
                    scripts.Children.Add(sc);
                }
                info.SetNewData(bf);
                patchedFldItem = true;
            }
            else if (npcsByAsset.TryGetValue(name, out var specs))
            {
                var strList = bf["StrList.Array"];
                var scripts = bf["Scripts.Array"];
                int idxItemEvent = EnsureString(strList, "ev_item_event");

                foreach (var npc in specs)
                {
                    var origSc = scripts.Children.FirstOrDefault(s => s["Label"].AsString == npc.NpcLabel);
                    if (origSc == null)
                        throw new InvalidOperationException($"NPC label '{npc.NpcLabel}' not found in ev_script/{name}");

                    string origCloneLabel = npc.NpcLabel + "_mega_orig";
                    scripts.Children.RemoveAll(s => s["Label"].AsString == origCloneLabel);

                    // Clone original NPC script body (skipping its leading _TALK_OBJ_START since wrapper runs it)
                    var cloned = origSc.Clone();
                    cloned["Label"].AsString = origCloneLabel;
                    var clonedCmds = cloned["Commands.Array"].Children;
                    if (clonedCmds.Count > 1 && clonedCmds[0]["Arg.Array"].Children.Count > 0 &&
                        clonedCmds[0]["Arg.Array"].Children[0]["data"].AsInt == 151)
                    {
                        clonedCmds.RemoveAt(0);
                    }
                    scripts.Children.Add(cloned);

                    int idxOrigClone = EnsureString(strList, origCloneLabel);
                    int idxCustomMsg = EnsureString(strList, $"dp_scenario3%00_msg_mega_gift_{Slug(npc.StoneName)}");

                    var cmds = origSc["Commands.Array"];
                    cmds.Children.Clear();
                    // _TALK_OBJ_START (151)
                    cmds.Children.Add(MakeCmd(cmds, (0, 151)));
                    // _IF_FLAGON_JUMP (45), flag -> origCloneLabel (normal dialogue after gift)
                    cmds.Children.Add(MakeCmd(cmds, (0, 45), (3, npc.FlagId), (5, idxOrigClone)));
                    // _TALKMSG (70), custom Mega Pokémon dialogue
                    cmds.Children.Add(MakeCmd(cmds, (0, 70), (5, idxCustomMsg)));
                    // _LAST_KEYWAIT (78)
                    cmds.Children.Add(MakeCmd(cmds, (0, 78)));
                    // _LDVAL (60), SCWK_TEMP0 (238) = itemId
                    cmds.Children.Add(MakeCmd(cmds, (0, 60), (2, 238), (1, FloatBits(npc.ItemId))));
                    // _LDVAL (60), SCWK_TEMP1 (239) = 1
                    cmds.Children.Add(MakeCmd(cmds, (0, 60), (2, 239), (1, FloatBits(1f))));
                    // _FLAG_SET (41), flag
                    cmds.Children.Add(MakeCmd(cmds, (0, 41), (3, npc.FlagId)));
                    // _CHG_COMMON_SCR (22), "ev_item_event"
                    cmds.Children.Add(MakeCmd(cmds, (0, 22), (5, idxItemEvent)));
                    // _TALK_CLOSE (81), _TALK_OBJ_END (153), _END (3)
                    cmds.Children.Add(MakeCmd(cmds, (0, 81)));
                    cmds.Children.Add(MakeCmd(cmds, (0, 153)));
                    cmds.Children.Add(MakeCmd(cmds, (0, 3)));
                    patchedNpcs++;
                }
                info.SetNewData(bf);
            }
        }

        if (!patchedFldItem || patchedNpcs != Npcs.Length)
            throw new InvalidOperationException($"ev_script patch incomplete: fld_item={patchedFldItem}, npcs={patchedNpcs}/{Npcs.Length}");

        SaveBundleLz4(am, bun, inst.file, dstEv);
        Console.WriteLine($"Patched {Balls.Length} fld_item scripts + {patchedNpcs} NPC scripts into Dpr/ev_script ({new FileInfo(dstEv).Length:N0}B).");
    }

    private static void VerifyRoundTrip()
    {
        var am = new AssetsManager();

        // 1. Verify Dpr/masterdatas
        var mdBun = am.LoadBundleFile(Path.Combine(DeltaDprDir, "masterdatas"), true);
        var mdInst = am.LoadAssetsFileFromBundle(mdBun, 0, false);
        int foundBalls = 0;
        foreach (var info in mdInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(mdInst, info);
            if (!bf["m_Name"].AsString.StartsWith("PlaceData")) continue;
            var arr = bf["Data.Array"];
            if (arr.IsDummy) continue;
            foundBalls += arr.Children.Count(e => e["ID"].AsString.StartsWith("FLD_ITEM_MEGA_") && e["ObjectGraphicIndex"].AsInt == 500);
        }

        // 2. Verify Message/english
        var msgBun = am.LoadBundleFile(DeltaMsgPath, true);
        var msgInst = am.LoadAssetsFileFromBundle(msgBun, 0, false);
        int foundMsgs = 0;
        foreach (var info in msgInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(msgInst, info);
            if (bf["m_Name"].AsString == "english_dp_scenario3")
            {
                foundMsgs = bf["labelDataArray.Array"].Children.Count(e => e["labelName"].AsString.StartsWith("00_msg_mega_gift_"));
                break;
            }
        }

        // 3. Verify Dpr/ev_script
        var evBun = am.LoadBundleFile(Path.Combine(DeltaDprDir, "ev_script"), true);
        var evInst = am.LoadAssetsFileFromBundle(evBun, 0, false);
        int foundFld = 0, foundNpcs = 0;
        foreach (var info in evInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(evInst, info);
            string nm = bf["m_Name"].AsString;
            if (nm == "fld_item")
                foundFld = bf["Scripts.Array"].Children.Count(s => s["Label"].AsString.StartsWith("ev_fld_item_mega_"));
            else
                foundNpcs += bf["Scripts.Array"].Children.Count(s => s["Label"].AsString.EndsWith("_mega_orig"));
        }
        am.UnloadAll(true);

        if (foundBalls != Balls.Length || foundFld != Balls.Length || foundNpcs != Npcs.Length || foundMsgs != Npcs.Length)
            throw new InvalidOperationException($"Round-trip verify FAILED: balls={foundBalls}/{Balls.Length}, fld={foundFld}/{Balls.Length}, npcs={foundNpcs}/{Npcs.Length}, msgs={foundMsgs}/{Npcs.Length}");

        Console.WriteLine($"[PASS] Round-trip verified: {foundBalls}/{Balls.Length} Poké Balls, {foundFld}/{Balls.Length} fld_item scripts, {foundNpcs}/{Npcs.Length} NPC gifts, {foundMsgs}/{Npcs.Length} custom dialogues!");
    }

    private static void WriteLocationsMarkdown(Dictionary<string, (float x, float y, int h)> coords)
    {
        var lines = new List<string>
        {
            "# Mega Stone Locations Guide (All 63 Mega Stones)",
            "",
            "All **63 playable Mega Stones** are placed across Sinnoh as **41 visible Poké Balls** (in caves, forests, routes, lakes, and Victory Road) and **22 House/Street NPC gifts** (with custom Mega Pokémon dialogue). Mega Rayquaza Mega Evolves by knowing **Dragon Ascent** (no Mega Stone required). The **Mega Ring** is intentionally not placed here.",
            "",
            "- Every pickup is tracked in your save file (`SaveData.bin`) via its dedicated unused `FlagWork` ID (`2532`–`3114`).",
            "- All top powerhouse and Z-form stones (**Charizardite X/Y, Tyranitarite, Metagrossite, Garchompite & Z, Absolite Z, Dragoninite, Salamencite, Lucarionite**) are obtainable in the first half of the game (Badges 1–4).",
            "- Five niche/lower-tier stones (**Beedrillite, Pidgeotite, Banettite, Cameruptite, Glalitite**) are placed in Victory Road.",
            ""
        };

        string[] stages = { "Early Game", "Mid Game", "Mid-Late Game", "Victory Road" };
        var stageHeaders = new Dictionary<string, string>
        {
            ["Early Game"]    = "## Early Game — Badges 0–2 (Jubilife, Oreburgh, Floaroma, Eterna & Wayward Cave)",
            ["Mid Game"]      = "## Mid Game — Badges 3–5 (Mt. Coronet South, Hearthome, Solaceon, Veilstone, Pastoria & Celestic)",
            ["Mid-Late Game"] = "## Mid–Late Game — Badges 6–8 (Fuego Ironworks, Canalave, Iron Island, Lakes, Mt. Coronet & Sunyshore)",
            ["Victory Road"]  = "## Victory Road — Niche / Lower-Tier Mega Stones",
        };

        foreach (var stage in stages)
        {
            lines.Add(stageHeaders[stage]);
            lines.Add("");
            lines.Add("| Mega Stone | Mega Pokémon | Item ID | Location | Source | Details / Coords | Flag ID |");
            lines.Add("| :--- | :--- | :---: | :--- | :--- | :--- | :---: |");

            var stageBalls = Balls.Where(b => b.Stage == stage).Select(b =>
            {
                var c = coords[b.StoneName];
                return (b.StoneName, b.MegaName, b.ItemId, b.LocationName, Source: "Poké Ball",
                        Details: $"`{b.PlaceDataAsset}` (zone {b.ZoneId}) at `({c.x:F0}, {c.y:F0})`, H={c.h}", b.FlagId);
            });
            var stageNpcs = Npcs.Where(n => n.Stage == stage).Select(n =>
                (n.StoneName, n.MegaName, n.ItemId, n.LocationName, Source: "NPC Gift",
                 Details: $"`{n.EvScriptAsset}` / `{n.NpcLabel}`", n.FlagId));

            foreach (var row in stageBalls.Concat(stageNpcs).OrderBy(r => r.LocationName).ThenBy(r => r.StoneName))
                lines.Add($"| **{row.StoneName}** | {row.MegaName} | `{row.ItemId}` | {row.LocationName} | {row.Source} | {row.Details} | `{row.FlagId}` |");

            lines.Add("");
        }

        string mdOut = Path.Combine(RepoRoot, "docs/MEGA_STONE_LOCATIONS.md");
        File.WriteAllLines(mdOut, lines);
        Console.WriteLine($"Wrote {mdOut} ({Balls.Length + Npcs.Length} entries).");
    }

    public static int Main()
    {
        ValidateSpecs();
        var coords = PatchMasterdatas();
        PatchMessageEnglish();
        PatchEvScript();
        VerifyRoundTrip();
        WriteLocationsMarkdown(coords);
        return 0;
    }
}
