using System;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchMegaWorld;

internal static class Program
{
    private const string RepoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
    private const string LumiDprDir = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr";
    private const string DeltaDprDir = RepoRoot + "/romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr";

    private static AssetTypeValueField MakeArg(AssetTypeValueField argArrayTemplate, int argType, int data)
    {
        var a = ValueBuilder.DefaultValueFieldFromArrayTemplate(argArrayTemplate);
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

    private static int FloatBits(float v) => BitConverter.SingleToInt32Bits(v);

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

    private static void PatchMasterdatas()
    {
        string mdPath = Path.Combine(DeltaDprDir, "masterdatas");
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(mdPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        AssetFileInfo? a01Info = null;
        AssetTypeValueField? a01Bf = null;
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            if (bf["m_Name"].AsString == "PlaceData_A01")
            {
                a01Info = info;
                a01Bf = bf;
                break;
            }
        }
        if (a01Info == null || a01Bf == null)
            throw new InvalidOperationException("PlaceData_A01 not found in Dpr/masterdatas");

        var arr = a01Bf["Data.Array"];
        // Remove any prior copy for idempotency
        arr.Children.RemoveAll(e => e["ID"].AsString == "FLD_ITEM_MEGA_CHARIZARD_X");

        // Add visible red Poké Ball for Charizardite X (660) in Twinleaf Town center (zoneID=422, Pos=(115, 878), H=1)
        var ball = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
        ball["ID"].AsString = "FLD_ITEM_MEGA_CHARIZARD_X";
        ball["zoneID"].AsInt = 422;
        ball["TrainerID"].AsInt = 0;
        ball["ObjectGraphicIndex"].AsInt = 500; // Red Poké Ball
        ball["ColorIndex"].AsInt = 0;
        ball["Position.x"].AsFloat = 115.0f;
        ball["Position.y"].AsFloat = 878.0f;
        ball["HeightLayer"].AsInt = 1;
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
        ball["TalkLabel"].AsString = "ev_fld_item_mega_charizard_x";
        ball["ContactLabel"].AsString = "";
        ball["Work"].AsInt = 2551; // Guaranteed unused vanish flag
        ball["Dowsing"].AsInt = 0;
        ball["LoadFirst"].AsByte = 0;
        ball["DoNotLoad"].AsInt = 4000;
        ball["TalkToRange"].AsFloat = 1.25f;
        ball["TalkToSize.x"].AsFloat = 0.0f;
        ball["TalkToSize.y"].AsFloat = 0.0f;
        ball["TalkBit"].AsInt = 15;
        arr.Children.Add(ball);

        a01Info.SetNewData(a01Bf);
        SaveBundleLz4(am, bun, inst.file, mdPath);
        Console.WriteLine($"Patched PlaceData_A01 in {mdPath} (added FLD_ITEM_MEGA_CHARIZARD_X at (115, 878), Work=2551)");
    }

    private static void PatchEvScript()
    {
        string srcEv = Path.Combine(LumiDprDir, "ev_script");
        string dstEv = Path.Combine(DeltaDprDir, "ev_script");
        var am = new AssetsManager();
        var bun = am.LoadBundleFile(srcEv, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

        bool patchedFldItem = false;
        bool patchedHouseNpc = false;

        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(inst, info);
            string name = bf["m_Name"].AsString;

            if (name == "fld_item")
            {
                var scripts = bf["Scripts.Array"];
                scripts.Children.RemoveAll(s => s["Label"].AsString == "ev_fld_item_mega_charizard_x");

                var sc = ValueBuilder.DefaultValueFieldFromArrayTemplate(scripts);
                sc["Label"].AsString = "ev_fld_item_mega_charizard_x";
                var cmds = sc["Commands.Array"];
                cmds.Children.Clear();
                // _FLD_ITEM_EVENT (187), item=660 (Charizardite X), count=1
                cmds.Children.Add(MakeCmd(cmds, (0, 187), (1, FloatBits(660f)), (1, FloatBits(1f))));
                // _END (3)
                cmds.Children.Add(MakeCmd(cmds, (0, 3)));
                scripts.Children.Add(sc);

                info.SetNewData(bf);
                patchedFldItem = true;
                Console.WriteLine("Patched ev_script/fld_item: added ev_fld_item_mega_charizard_x -> Charizardite X (660)");
            }
            else if (name == "t01r0301")
            {
                // Twinleaf Town Southwest House: T01R0301_MAN2A gives Charizardite Y (678) once on flag 2552
                var strList = bf["StrList.Array"];
                int idxNormal = EnsureString(strList, "ev_t01r0301_man2a_01");
                int idxItemEvent = EnsureString(strList, "ev_item_event");

                var scripts = bf["Scripts.Array"];
                foreach (var sc in scripts.Children)
                {
                    if (sc["Label"].AsString != "ev_t01r0301_man2a") continue;
                    var cmds = sc["Commands.Array"];
                    cmds.Children.Clear();
                    // _TALK_OBJ_START (151)
                    cmds.Children.Add(MakeCmd(cmds, (0, 151)));
                    // _IF_FLAGON_JUMP (45), flag=2552 -> ev_t01r0301_man2a_01 (normal dialogue after receiving stone)
                    cmds.Children.Add(MakeCmd(cmds, (0, 45), (3, 2552), (5, idxNormal)));
                    // _LDVAL (60), SCWK_TEMP0 (238) = 678 (Charizardite Y)
                    cmds.Children.Add(MakeCmd(cmds, (0, 60), (2, 238), (1, FloatBits(678f))));
                    // _LDVAL (60), SCWK_TEMP1 (239) = 1
                    cmds.Children.Add(MakeCmd(cmds, (0, 60), (2, 239), (1, FloatBits(1f))));
                    // _FLAG_SET (41), flag=2552
                    cmds.Children.Add(MakeCmd(cmds, (0, 41), (3, 2552)));
                    // _CHG_COMMON_SCR (22), "ev_item_event" (native NPC gift fanfare + AddItem)
                    cmds.Children.Add(MakeCmd(cmds, (0, 22), (5, idxItemEvent)));
                    // _TALK_CLOSE (81), _TALK_OBJ_END (153), _END (3)
                    cmds.Children.Add(MakeCmd(cmds, (0, 81)));
                    cmds.Children.Add(MakeCmd(cmds, (0, 153)));
                    cmds.Children.Add(MakeCmd(cmds, (0, 3)));
                    patchedHouseNpc = true;
                    break;
                }
                info.SetNewData(bf);
                Console.WriteLine("Patched ev_script/t01r0301: ev_t01r0301_man2a -> Charizardite Y (678) on flag 2552");
            }
        }

        if (!patchedFldItem || !patchedHouseNpc)
            throw new InvalidOperationException($"Patch incomplete: fld_item={patchedFldItem}, t01r0301={patchedHouseNpc}");

        SaveBundleLz4(am, bun, inst.file, dstEv);
        Console.WriteLine($"Wrote {dstEv} ({new FileInfo(dstEv).Length:N0}B)");
    }

    private static void VerifyRoundTrip()
    {
        var am = new AssetsManager();
        var mdBun = am.LoadBundleFile(Path.Combine(DeltaDprDir, "masterdatas"), true);
        var mdInst = am.LoadAssetsFileFromBundle(mdBun, 0, false);
        bool foundBall = false;
        foreach (var info in mdInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(mdInst, info);
            if (bf["m_Name"].AsString == "PlaceData_A01")
            {
                foundBall = bf["Data.Array"].Children.Any(e =>
                    e["ID"].AsString == "FLD_ITEM_MEGA_CHARIZARD_X" &&
                    e["ObjectGraphicIndex"].AsInt == 500 &&
                    e["Work"].AsInt == 2551 &&
                    e["TalkLabel"].AsString == "ev_fld_item_mega_charizard_x");
                break;
            }
        }

        var evBun = am.LoadBundleFile(Path.Combine(DeltaDprDir, "ev_script"), true);
        var evInst = am.LoadAssetsFileFromBundle(evBun, 0, false);
        bool foundFld = false, foundNpc = false;
        foreach (var info in evInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var bf = am.GetBaseField(evInst, info);
            string nm = bf["m_Name"].AsString;
            if (nm == "fld_item")
                foundFld = bf["Scripts.Array"].Children.Any(s => s["Label"].AsString == "ev_fld_item_mega_charizard_x");
            else if (nm == "t01r0301")
                foundNpc = bf["StrList.Array"].Children.Any(s => s.AsString == "ev_item_event");
        }
        am.UnloadAll(true);

        if (!foundBall || !foundFld || !foundNpc)
            throw new InvalidOperationException($"Round-trip verification FAILED: ball={foundBall}, fld={foundFld}, npc={foundNpc}");
        Console.WriteLine("[PASS] Round-trip verification: PlaceData_A01 Poké Ball + fld_item (660) + t01r0301 NPC (678)");
    }

    public static int Main()
    {
        PatchMasterdatas();
        PatchEvScript();
        VerifyRoundTrip();
        return 0;
    }
}
