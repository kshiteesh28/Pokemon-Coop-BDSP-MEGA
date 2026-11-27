using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchMegaForms;

internal static class Program
{
    private sealed record MegaSpec(
        int Species, int Form, int Atk, int Def, int SpAtk, int SpDef, int Spd,
        int Ability, int Type1, int Type2, int Weight, string BundleName, string ShinyBundleName, string MegaName);

    private static readonly Dictionary<string, int> TypeMap = new()
    {
        ["kTypeNormal"] = 0, ["kTypeFighting"] = 1, ["kTypeFlying"] = 2, ["kTypePoison"] = 3,
        ["kTypeGround"] = 4, ["kTypeRock"] = 5, ["kTypeBug"] = 6, ["kTypeGhost"] = 7,
        ["kTypeSteel"] = 8, ["kTypeFire"] = 9, ["kTypeWater"] = 10, ["kTypeGrass"] = 11,
        ["kTypeElectric"] = 12, ["kTypePsychic"] = 13, ["kTypeIce"] = 14, ["kTypeDragon"] = 15,
        ["kTypeDark"] = 16, ["kTypeFairy"] = 17,
    };

    private static List<MegaSpec> LoadRegistry(string headerPath)
    {
        var txt = File.ReadAllText(headerPath);
        var abilityMap = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(txt, @"constexpr\s+(?:uint16_t|int)\s+(k[A-Za-z0-9_]+)\s*=\s*(\d+)\s*;"))
            abilityMap[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

        var list = new List<MegaSpec>();
        // { species, form, stone, reqMove, hp, atk, def, spa, spd, spe, ability, type1, type2, weight, "bundle", "shinyBundle", true, "species", "Mega ..." }
        var rx = new Regex(
            @"\{\s*(\d+),\s*(\d+),\s*\d+,\s*\S+,\s*\d+,\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(k[A-Za-z0-9_]+),\s*(kType[A-Za-z]+),\s*(kType[A-Za-z]+),\s*(\d+),\s*""([^""]+)"",\s*""([^""]+)"",[^\n]*?""((?:Mega|Primal) [^""]+)""");
        foreach (Match m in rx.Matches(txt))
        {
            int abil = abilityMap[m.Groups[8].Value];
            int t1 = TypeMap[m.Groups[9].Value];
            int t2 = TypeMap[m.Groups[10].Value];
            list.Add(new MegaSpec(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value),
                int.Parse(m.Groups[5].Value),
                int.Parse(m.Groups[6].Value),
                int.Parse(m.Groups[7].Value),
                abil, t1, t2,
                int.Parse(m.Groups[11].Value),
                m.Groups[12].Value,
                m.Groups[13].Value,
                m.Groups[14].Value));
        }
        return list;
    }

    private static AssetTypeValueField CloneField(AssetTypeValueField src)
    {
        var clone = new AssetTypeValueField
        {
            TemplateField = src.TemplateField,
            Value = src.Value != null ? CopyValue(src.Value) : null,
            Children = new List<AssetTypeValueField>(src.Children.Count)
        };
        foreach (var ch in src.Children)
            clone.Children.Add(CloneField(ch));
        return clone;
    }

    private static AssetTypeValue CopyValue(AssetTypeValue v)
    {
        if (v.ValueType == AssetValueType.ByteArray && v.AsByteArray != null)
            return new AssetTypeValue(v.ValueType, (byte[])v.AsByteArray.Clone());
        return new AssetTypeValue(v.ValueType, v.AsObject);
    }

    private static void SaveBundleLz4(AssetsManager am, BundleFileInstance bun, AssetsFileInstance inst, string outPath)
    {
        string tmp = outPath + ".tmp";
        string tmpLz4 = outPath + ".lz4";
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
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.Move(tmpLz4, outPath, true);
    }

    public static int Main(string[] args)
    {
        string repo = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
        string lumi = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant";
        string delta = $"{repo}/romfs-delta/Data/StreamingAssets/AssetAssistant";
        string romfs = $"{repo}/romfs/Data/StreamingAssets/AssetAssistant";

        var megas = LoadRegistry($"{repo}/src/mega/mega_registry.hpp");
        Console.WriteLine($"Loaded {megas.Count} Mega entries from mega_registry.hpp");

        string pmlDelta = $"{delta}/Pml/personal_masterdatas";
        string dprDelta = $"{delta}/Dpr/masterdatas";
        string uiSrc    = File.Exists($"{delta}/UIs/masterdatas/uimasterdatas")
                            ? $"{delta}/UIs/masterdatas/uimasterdatas"
                            : $"{lumi}/UIs/masterdatas/uimasterdatas";
        string uiDelta  = $"{delta}/UIs/masterdatas/uimasterdatas";

        // =====================================================================
        // 1. Patch Pml/personal_masterdatas (PersonalTable + 4 parallel tables)
        // =====================================================================
        {
            var am = new AssetsManager();
            var bun = am.LoadBundleFile(pmlDelta, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            AssetFileInfo? FindMb(string name, out AssetTypeValueField bf)
            {
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var f = am.GetBaseField(inst, info);
                    if (f["m_Name"].AsString == name) { bf = f; return info; }
                }
                bf = null!;
                return null;
            }

            var pInfo  = FindMb("PersonalTable", out var pBf)!;
            var apInfo = FindMb("AddPersonalTable", out var apBf)!;
            var woInfo = FindMb("WazaOboeTable", out var woBf)!;
            var twInfo = FindMb("TamagoWazaTable", out var twBf)!;
            var evInfo = FindMb("EvolveTable", out var evBf)!;

            var pArr  = pBf["Personal.Array"].Children;
            var apArr = apBf["AddPersonal.Array"].Children;
            var woArr = woBf["WazaOboe.Array"].Children;
            var twArr = twBf["Data.Array"].Children;
            var evArr = evBf["Evolve.Array"].Children;

            if (pArr.Count != apArr.Count || pArr.Count != woArr.Count || pArr.Count != twArr.Count || pArr.Count != evArr.Count)
                throw new InvalidOperationException($"Parallel table size mismatch: P={pArr.Count} AP={apArr.Count} WO={woArr.Count} TW={twArr.Count} EV={evArr.Count}");

            int addedPersonal = 0;
            foreach (var m in megas)
            {
                var baseRow = pArr[m.Species];
                int fIdx = baseRow["form_index"].AsInt;
                int fMax = baseRow["form_max"].AsInt;
                bool exists = fIdx > 0 && m.Form < fMax && (fIdx + m.Form - 1) < pArr.Count && pArr[fIdx + m.Form - 1]["monsno"].AsInt == m.Species;
                if (exists)
                {
                    // Keep existing Alolan Raichu (26 f=1) untouched; sync stats/ability on newly appended Z-A Mega rows (>= 1466)
                    int curIdx = fIdx + m.Form - 1;
                    if ((curIdx >= 1466 || m.Species == 382 || m.Species == 383) && !(m.Species == 26 && m.Form == 1))
                    {
                        var r = pArr[curIdx];
                        r["valid_flag"].AsInt = 1;
                        r["basic_atk"].AsByte = (byte)m.Atk;
                        r["basic_def"].AsByte = (byte)m.Def;
                        r["basic_spatk"].AsByte = (byte)m.SpAtk;
                        r["basic_spdef"].AsByte = (byte)m.SpDef;
                        r["basic_agi"].AsByte = (byte)m.Spd;
                        r["type1"].AsByte = (byte)m.Type1;
                        r["type2"].AsByte = (byte)m.Type2;
                        r["tokusei1"].AsUShort = (ushort)m.Ability;
                        r["tokusei2"].AsUShort = (ushort)m.Ability;
                        r["tokusei3"].AsUShort = (ushort)m.Ability;
                        r["weight"].AsUShort = (ushort)m.Weight;
                    }
                    continue;
                }

                int newFMax = Math.Max(fMax, m.Form + 1);
                int newFIdx = pArr.Count;

                // Append contiguous block for form 1 .. newFMax-1
                for (int f = 1; f < newFMax; f++)
                {
                    int targetIdx = pArr.Count;
                    int srcIdx = (fIdx > 0 && f < fMax && (fIdx + f - 1) < pArr.Count && pArr[fIdx + f - 1]["monsno"].AsInt == m.Species)
                                     ? (fIdx + f - 1)
                                     : m.Species;

                    var pNew  = CloneField(pArr[srcIdx]);
                    var apNew = CloneField(apArr[srcIdx]);
                    var woNew = CloneField(woArr[srcIdx]);
                    var twNew = CloneField(twArr[srcIdx]);
                    var evNew = CloneField(evArr[srcIdx]);

                    pNew["id"].AsInt = targetIdx;
                    pNew["monsno"].AsInt = m.Species;
                    pNew["form_index"].AsInt = newFIdx;
                    pNew["form_max"].AsInt = newFMax;

                    if (f == m.Form)
                    {
                        pNew["valid_flag"].AsInt = 1;
                        pNew["basic_atk"].AsByte = (byte)m.Atk;
                        pNew["basic_def"].AsByte = (byte)m.Def;
                        pNew["basic_spatk"].AsByte = (byte)m.SpAtk;
                        pNew["basic_spdef"].AsByte = (byte)m.SpDef;
                        pNew["basic_agi"].AsByte = (byte)m.Spd;
                        pNew["type1"].AsByte = (byte)m.Type1;
                        pNew["type2"].AsByte = (byte)m.Type2;
                        pNew["tokusei1"].AsUShort = (ushort)m.Ability;
                        pNew["tokusei2"].AsUShort = (ushort)m.Ability;
                        pNew["tokusei3"].AsUShort = (ushort)m.Ability;
                        pNew["weight"].AsUShort = (ushort)m.Weight;
                    }

                    apNew["valid_flag"].AsInt = 1;
                    apNew["monsno"].AsInt = m.Species;
                    apNew["formno"].AsInt = f;

                    woNew["id"].AsInt = targetIdx;
                    twNew["no"].AsInt = m.Species;
                    twNew["formNo"].AsInt = f;
                    evNew["id"].AsInt = targetIdx;

                    pArr.Add(pNew);
                    apArr.Add(apNew);
                    woArr.Add(woNew);
                    twArr.Add(twNew);
                    evArr.Add(evNew);
                }

                // Point base species row to the new contiguous form block
                baseRow["form_index"].AsInt = newFIdx;
                baseRow["form_max"].AsInt = newFMax;
                addedPersonal++;
                Console.WriteLine($"  [PersonalTable] Added [{m.Species,3} f={m.Form}] {m.MegaName,-18} -> newFormIndex={newFIdx}, formMax={newFMax}");
            }

            pInfo.SetNewData(pBf);
            apInfo.SetNewData(apBf);
            woInfo.SetNewData(woBf);
            twInfo.SetNewData(twBf);
            evInfo.SetNewData(evBf);
            SaveBundleLz4(am, bun, inst, pmlDelta);
            Console.WriteLine($"Saved {pmlDelta} (total rows={pArr.Count}, added {addedPersonal} Mega forms)");
        }

        // =====================================================================
        // 2. Patch Dpr/masterdatas (PokemonInfo.Catalog, sorted by UniqueID)
        // =====================================================================
        {
            var am = new AssetsManager();
            var bun = am.LoadBundleFile(dprDelta, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            AssetFileInfo? catInfo = null;
            AssetTypeValueField catBf = null!;
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var f = am.GetBaseField(inst, info);
                if (f["m_Name"].AsString == "PokemonInfo") { catInfo = info; catBf = f; break; }
            }
            var catArr = catBf["Catalog.Array"].Children;
            var existingUid = new HashSet<int>(catArr.Select(r => r["UniqueID"].AsInt));

            var byUid = catArr.ToDictionary(r => r["UniqueID"].AsInt);
            int addedCat = 0, updatedMegaBundleCat = 0;
            foreach (var m in megas)
            {
                var baseRows = catArr.Where(r => r["MonsNo"].AsInt == m.Species && r["FormNo"].AsInt == 0).ToList();
                foreach (var br in baseRows)
                {
                    int sexRare = br["UniqueID"].AsInt % 100;
                    bool isShiny = (sexRare % 2) == 1;
                    string targetBundle = isShiny ? m.ShinyBundleName : m.BundleName;
                    int newUid = m.Species * 10000 + m.Form * 100 + sexRare;
                    if (byUid.TryGetValue(newUid, out var existingRow))
                    {
                        if (existingRow["AssetBundleName"].AsString != targetBundle)
                        {
                            existingRow["AssetBundleName"].AsString = targetBundle;
                            updatedMegaBundleCat++;
                        }
                        continue;
                    }

                    var nr = CloneField(br);
                    nr["UniqueID"].AsInt = newUid;
                    nr["FormNo"].AsInt = m.Form;
                    nr["AssetBundleName"].AsString = targetBundle;
                    catArr.Add(nr);
                    byUid[newUid] = nr;
                    existingUid.Add(newUid);
                    addedCat++;
                }
            }
            Console.WriteLine($"  [PokemonInfo.Catalog] Updated {updatedMegaBundleCat} existing Mega rows to custom 3D Mega bundles");

            catArr.Sort((a, b) => a["UniqueID"].AsInt.CompareTo(b["UniqueID"].AsInt));
            catInfo!.SetNewData(catBf);
            SaveBundleLz4(am, bun, inst, dprDelta);
            Console.WriteLine($"Saved {dprDelta} (PokemonInfo.Catalog total={catArr.Count}, added {addedCat} rows, sorted by UniqueID)");
        }

        // =====================================================================
        // 3. Patch UIs/masterdatas/uimasterdatas (UIDatabase: all 5 UniqueID sheets)
        // =====================================================================
        {
            var am = new AssetsManager();
            var bun = am.LoadBundleFile(uiSrc, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            AssetFileInfo? uiInfo = null;
            AssetTypeValueField uiBf = null!;
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var f = am.GetBaseField(inst, info);
                if (f["m_Name"].AsString == "UIDatabase") { uiInfo = info; uiBf = f; break; }
            }

            string[] sheets = { "PokemonIcon", "AshiatoIcon", "PokemonVoice", "ZukanDisplay", "ZukanCompareHeight" };
            foreach (var sheet in sheets)
            {
                var arr = uiBf[$"{sheet}.Array"].Children;
                var byUidSheet = arr.ToDictionary(r => r["UniqueID"].AsInt);
                int added = 0, fixedVoice = 0;

                foreach (var m in megas)
                {
                    int basePrefix = m.Species * 100; // Form 0
                    var baseRows = arr.Where(r => (r["UniqueID"].AsInt / 100) == basePrefix).ToList();
                    foreach (var br in baseRows)
                    {
                        int sexRare = br["UniqueID"].AsInt % 100;
                        int newUid = m.Species * 10000 + m.Form * 100 + sexRare;
                        if (byUidSheet.TryGetValue(newUid, out var existingRow))
                        {
                            if (sheet == "PokemonVoice")
                            {
                                var curEv = existingRow["WwiseEvent"].AsString;
                                var baseEv = br["WwiseEvent"].AsString;
                                if (!string.IsNullOrEmpty(baseEv) && !baseEv.StartsWith("PLAY_PV_") &&
                                    (string.IsNullOrEmpty(curEv) || curEv.StartsWith("PLAY_PV_")))
                                {
                                    existingRow["WwiseEvent"].AsString = baseEv;
                                    existingRow["stopEventId"].AsString = br["stopEventId"].AsString;
                                    fixedVoice++;
                                }
                            }
                            continue;
                        }

                        var nr = CloneField(br);
                        nr["UniqueID"].AsInt = newUid;
                        arr.Add(nr);
                        byUidSheet[newUid] = nr;
                        added++;
                    }
                }

                arr.Sort((a, b) => a["UniqueID"].AsInt.CompareTo(b["UniqueID"].AsInt));
                Console.WriteLine($"  [UIDatabase.{sheet}] total={arr.Count}, added={added}, fixedVoice={fixedVoice} rows (sorted by UniqueID)");
            }

            uiInfo!.SetNewData(uiBf);
            SaveBundleLz4(am, bun, inst, uiDelta);
            Console.WriteLine($"Saved {uiDelta}");
        }

        // Sync all 3 patched masterdatas to romfs/ if present
        if (Directory.Exists(romfs))
        {
            Directory.CreateDirectory($"{romfs}/Pml");
            Directory.CreateDirectory($"{romfs}/Dpr");
            Directory.CreateDirectory($"{romfs}/UIs/masterdatas");
            File.Copy(pmlDelta, $"{romfs}/Pml/personal_masterdatas", true);
            File.Copy(dprDelta, $"{romfs}/Dpr/masterdatas", true);
            File.Copy(uiDelta,  $"{romfs}/UIs/masterdatas/uimasterdatas", true);
            Console.WriteLine("Synced all 3 masterdatas bundles to romfs/");
        }

        Console.WriteLine("ALL 64 MEGA FORMS REGISTERED NATIVELY IN ROMFS MASTERDATAS!");
        return 0;
    }
}
