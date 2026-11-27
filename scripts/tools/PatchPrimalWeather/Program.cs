using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchPrimalWeather;

internal static class Program
{
    private sealed record WeatherVfxSpec(
        string DonorName,
        string NewName,
        string CabName,
        float RateMult,
        float BurstMult,
        float IntervalDiv,
        float SpeedMult,
        float SizeMult,
        int MinMaxParticles,
        (float R, float G, float B, float A) PrimaryTint,
        (float R, float G, float B, float A) SecondaryTint,
        float ColorScaleMult);

    private static AssetTypeValueField CloneField(AssetTypeValueField src)
    {
        var clone = new AssetTypeValueField
        {
            TemplateField = src.TemplateField,
            Value = src.Value != null
                ? new AssetTypeValue(
                    src.Value.ValueType,
                    src.Value.ValueType == AssetValueType.ByteArray && src.Value.AsByteArray != null
                        ? (byte[])src.Value.AsByteArray.Clone()
                        : src.Value.AsObject)
                : null,
            Children = new List<AssetTypeValueField>(src.Children.Count)
        };
        foreach (var ch in src.Children)
            clone.Children.Add(CloneField(ch));
        return clone;
    }

    private static byte[] PackBundleLz4(AssetBundleFile bun, AssetsFile file)
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

    private static void BuildWeatherVfxBundle(string bdspRoot, string deltaRoot, string romfsRoot, WeatherVfxSpec spec)
    {
        string donorPath = Path.Combine(bdspRoot, "Effects/effect/prefab/battle", spec.DonorName);
        if (!File.Exists(donorPath))
            throw new FileNotFoundException($"Missing vanilla BDSP weather bundle: {donorPath}");

        var am = new AssetsManager();
        var bun = am.LoadBundleFile(donorPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
        var f = inst.file;

        bun.file.BlockAndDirInfo.DirectoryInfos[0].Name = spec.CabName;

        foreach (var info in f.AssetInfos)
        {
            var cid = (AssetClassID)info.TypeId;
            if (cid == AssetClassID.AssetBundle)
            {
                var bf = am.GetBaseField(inst, info);
                bf["m_Name"].AsString = $"effect/prefab/battle/{spec.NewName}";
                bf["m_AssetBundleName"].AsString = $"effect/prefab/battle/{spec.NewName}";
                foreach (var cont in bf["m_Container.Array"].Children)
                {
                    string k = cont["first"].AsString;
                    if (k.Contains(spec.DonorName, StringComparison.OrdinalIgnoreCase))
                        cont["first"].AsString = k.Replace(spec.DonorName, spec.NewName, StringComparison.OrdinalIgnoreCase);
                }
                info.SetNewData(bf);
            }
            else if (cid == AssetClassID.GameObject)
            {
                var bf = am.GetBaseField(inst, info);
                if (bf["m_Name"].AsString.Equals(spec.DonorName, StringComparison.OrdinalIgnoreCase))
                {
                    bf["m_Name"].AsString = spec.NewName;
                    info.SetNewData(bf);
                }
            }
            else if (cid == AssetClassID.ParticleSystem)
            {
                var bf = am.GetBaseField(inst, info);
                var go = am.GetBaseField(inst, f.GetAssetInfo(bf["m_GameObject.m_PathID"].AsLong)!);
                string goName = go["m_Name"].AsString;

                var em = bf["EmissionModule"];
                var sm = bf["InitialModule"];

                float rate = em["rateOverTime.scalar"].AsFloat;
                if (rate > 0f)
                    em["rateOverTime.scalar"].AsFloat = rate * spec.RateMult;

                foreach (var b in em["m_Bursts.Array"].Children)
                {
                    float cnt = b["countCurve.scalar"].AsFloat;
                    if (cnt > 0f && !(goName.Contains("dome", StringComparison.OrdinalIgnoreCase) || goName.Equals("sun", StringComparison.OrdinalIgnoreCase)))
                        b["countCurve.scalar"].AsFloat = MathF.Round(cnt * spec.BurstMult);
                    float rep = b["repeatInterval"].AsFloat;
                    if (rep > 0.015f && spec.IntervalDiv > 1f)
                        b["repeatInterval"].AsFloat = MathF.Max(0.015f, rep / spec.IntervalDiv);
                }

                float spd = sm["startSpeed.scalar"].AsFloat;
                if (spd > 0f)
                    sm["startSpeed.scalar"].AsFloat = spd * spec.SpeedMult;

                float sz = sm["startSize.scalar"].AsFloat;
                if (sz > 0f && !goName.Contains("dome", StringComparison.OrdinalIgnoreCase))
                    sm["startSize.scalar"].AsFloat = sz * spec.SizeMult;

                if (sm["maxNumParticles"].AsInt < spec.MinMaxParticles)
                    sm["maxNumParticles"].AsInt = spec.MinMaxParticles;

                // Tint particle startColor on primary emitters for unmistakable Primal weather visuals
                bool isSecondary = goName.Contains("dome", StringComparison.OrdinalIgnoreCase) ||
                                   goName.Contains("sibuki", StringComparison.OrdinalIgnoreCase) ||
                                   goName.Contains("sand2", StringComparison.OrdinalIgnoreCase);
                var tint = isSecondary ? spec.SecondaryTint : spec.PrimaryTint;
                var maxCol = sm["startColor.maxColor"];
                maxCol["r"].AsFloat = Math.Clamp(maxCol["r"].AsFloat * tint.R, 0f, 1f);
                maxCol["g"].AsFloat = Math.Clamp(maxCol["g"].AsFloat * tint.G, 0f, 1f);
                maxCol["b"].AsFloat = Math.Clamp(maxCol["b"].AsFloat * tint.B, 0f, 1f);
                maxCol["a"].AsFloat = Math.Clamp(maxCol["a"].AsFloat * tint.A, 0f, 1f);

                info.SetNewData(bf);
            }
            else if (cid == AssetClassID.Material)
            {
                var bf = am.GetBaseField(inst, info);
                string mName = bf["m_Name"].AsString;
                bf["m_Name"].AsString = mName.Replace(spec.DonorName, spec.NewName, StringComparison.OrdinalIgnoreCase);

                bool isSecondary = mName.Contains("dome", StringComparison.OrdinalIgnoreCase) ||
                                   mName.Contains("sibuki", StringComparison.OrdinalIgnoreCase) ||
                                   mName.Contains("sand2", StringComparison.OrdinalIgnoreCase);
                var tint = isSecondary ? spec.SecondaryTint : spec.PrimaryTint;

                foreach (var c in bf["m_SavedProperties.m_Colors.Array"].Children)
                {
                    if (c["first"].AsString == "_MulColor")
                    {
                        var v = c["second"];
                        v["r"].AsFloat = tint.R;
                        v["g"].AsFloat = tint.G;
                        v["b"].AsFloat = tint.B;
                        v["a"].AsFloat = tint.A;
                    }
                }
                foreach (var fl in bf["m_SavedProperties.m_Floats.Array"].Children)
                {
                    if (fl["first"].AsString == "_ColorScale")
                        fl["second"].AsFloat = fl["second"].AsFloat * spec.ColorScaleMult;
                }
                info.SetNewData(bf);
            }
        }

        byte[] packed = PackBundleLz4(bun.file, f);
        am.UnloadAll(true);

        foreach (var root in new[] { deltaRoot, romfsRoot })
        {
            if (!Directory.Exists(root)) continue;
            string outPath = Path.Combine(root, "Effects/effect/prefab/battle", spec.NewName);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, packed);
        }

        // Round-trip verify
        var vam = new AssetsManager();
        var vbun = vam.LoadBundleFile(Path.Combine(deltaRoot, "Effects/effect/prefab/battle", spec.NewName), true);
        var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
        if (vinst.file.Metadata.TargetPlatform != 38)
            throw new Exception($"Weather bundle {spec.NewName} TargetPlatform != 38");
        var ab = vinst.file.GetAssetsOfType(AssetClassID.AssetBundle).First();
        string abName = vam.GetBaseField(vinst, ab)["m_AssetBundleName"].AsString;
        if (abName != $"effect/prefab/battle/{spec.NewName}")
            throw new Exception($"Weather bundle {spec.NewName} AssetBundleName mismatch: {abName}");
        vam.UnloadAll(true);

        Console.WriteLine($"[PASS] Built & verified {spec.NewName} ({packed.Length:N0} bytes, CAB={spec.CabName})");
    }

    public static int Main(string[] args)
    {
        string repo = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
        string lumi = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant";
        string bdsp = "/Users/kshiteesh/Desktop/pokemon/brilliant diamond/rom-extract/Data/StreamingAssets/AssetAssistant";
        string delta = Path.Combine(repo, "romfs-delta/Data/StreamingAssets/AssetAssistant");
        string romfs = Path.Combine(repo, "romfs/Data/StreamingAssets/AssetAssistant");

        // 1. Build 3 distinct high-intensity Primal/Mega weather VFX bundles
        var specs = new[]
        {
            // Heavy Rain (Primordial Sea): 3x torrential downpour, faster splashes, deep storm-sapphire streaks
            new WeatherVfxSpec(
                DonorName: "et001_rain01",
                NewName: "et005_heavyrain01",
                CabName: "CAB-primalheavyrain050000000000001",
                RateMult: 2.0f,
                BurstMult: 3.0f,
                IntervalDiv: 1.6f,
                SpeedMult: 1.45f,
                SizeMult: 1.35f,
                MinMaxParticles: 2500,
                PrimaryTint: (0.62f, 0.88f, 1.00f, 1.00f),
                SecondaryTint: (0.42f, 0.76f, 1.00f, 1.00f),
                ColorScaleMult: 1.55f),

            // Extremely Harsh Sunlight (Desolate Land): blazing oversized crimson-gold solar flare & scorching heat dome
            new WeatherVfxSpec(
                DonorName: "et004_sunny01",
                NewName: "et006_extremesun01",
                CabName: "CAB-primalextremesun06000000000002",
                RateMult: 2.0f,
                BurstMult: 1.0f,
                IntervalDiv: 1.45f,
                SpeedMult: 1.25f,
                SizeMult: 1.45f,
                MinMaxParticles: 1500,
                PrimaryTint: (1.00f, 0.56f, 0.20f, 1.00f),
                SecondaryTint: (1.00f, 0.40f, 0.14f, 1.00f),
                ColorScaleMult: 1.75f),

            // Strong Winds (Delta Stream): swirling emerald-teal gale winds
            new WeatherVfxSpec(
                DonorName: "et003_sandstorm01",
                NewName: "et007_strongwinds01",
                CabName: "CAB-megastrongwinds070000000000003",
                RateMult: 1.5f,
                BurstMult: 1.5f,
                IntervalDiv: 1.25f,
                SpeedMult: 1.35f,
                SizeMult: 1.15f,
                MinMaxParticles: 1500,
                PrimaryTint: (0.38f, 1.00f, 0.72f, 1.00f),
                SecondaryTint: (0.72f, 1.00f, 0.88f, 1.00f),
                ColorScaleMult: 1.50f),
        };

        foreach (var s in specs)
            BuildWeatherVfxBundle(bdsp, delta, romfs, s);

        // 2. Patch Effects/masterdatas/effectdatabase -> register the 3 new BattleEffectData entries
        {
            string edSrc = File.Exists(Path.Combine(delta, "Effects/masterdatas/effectdatabase"))
                ? Path.Combine(delta, "Effects/masterdatas/effectdatabase")
                : Path.Combine(lumi, "Effects/masterdatas/effectdatabase");
            var am = new AssetsManager();
            var bun = am.LoadBundleFile(edSrc, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            AssetFileInfo? edInfo = null;
            AssetTypeValueField? edBf = null;
            foreach (var mb in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = am.GetBaseField(inst, mb);
                if (bf["m_Name"].AsString == "EffectDatabase") { edInfo = mb; edBf = bf; break; }
            }
            if (edInfo == null || edBf == null)
                throw new Exception("EffectDatabase not found!");

            var arr = edBf["BattleEffectData.Array"].Children;
            var existingNames = new HashSet<string>(arr.Select(r => r["AssetName"].AsString), StringComparer.OrdinalIgnoreCase);
            var tmpl = arr.First(r => r["AssetName"].AsString == "et001_rain01");
            int added = 0;
            foreach (var s in specs)
            {
                if (existingNames.Contains(s.NewName)) continue;
                var row = CloneField(tmpl);
                row["AssetBundleName"].AsString = $"effect/prefab/battle/{s.NewName}";
                row["AssetName"].AsString = s.NewName;
                arr.Add(row);
                existingNames.Add(s.NewName);
                added++;
            }
            edInfo.SetNewData(edBf);
            byte[] packed = PackBundleLz4(bun.file, inst.file);
            am.UnloadAll(true);

            foreach (var root in new[] { delta, romfs })
            {
                if (!Directory.Exists(root)) continue;
                string outPath = Path.Combine(root, "Effects/masterdatas/effectdatabase");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, packed);
            }
            Console.WriteLine($"[PASS] Patched Effects/masterdatas/effectdatabase (+{added} BattleEffectData rows, total={arr.Count})");
        }

        // 3. Patch Battle/battle_masterdatas -> BattleDataTable.WeatherData[5..8]
        {
            string bmSrc = File.Exists(Path.Combine(delta, "Battle/battle_masterdatas"))
                ? Path.Combine(delta, "Battle/battle_masterdatas")
                : Path.Combine(lumi, "Battle/battle_masterdatas");
            var am = new AssetsManager();
            var bun = am.LoadBundleFile(bmSrc, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);

            AssetFileInfo? btInfo = null;
            AssetTypeValueField? btBf = null;
            foreach (var mb in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = am.GetBaseField(inst, mb);
                if (bf["m_Name"].AsString == "BattleDataTable") { btInfo = mb; btBf = bf; break; }
            }
            if (btInfo == null || btBf == null)
                throw new Exception("BattleDataTable not found!");

            var wd = btBf["WeatherData.Array"].Children;
            var tmplSun = wd[1];
            var tmplRain = wd[2];
            var tmplHail = wd[3];
            var tmplSand = wd[4];

            while (wd.Count < 9)
                wd.Add(CloneField(tmplRain));

            void SetWeatherRow(int idx, AssetTypeValueField tmpl, string mainFile, string regName, float lightPower)
            {
                var row = CloneField(tmpl);
                row["MainFileName"].AsString = mainFile;
                row["RegisterName"].AsString = regName;
                row["LightPower"].AsFloat = lightPower;
                wd[idx] = row;
            }

            // [5] Heavy Rain (Primordial Sea)
            SetWeatherRow(5, tmplRain, "et005_heavyrain01",   "Set_State_BattleRain",      0.42f);
            // [6] Extremely Harsh Sunlight (Desolate Land)
            SetWeatherRow(6, tmplSun,  "et006_extremesun01",  "Set_State_BattleSunny",     1.45f);
            // [7] Strong Winds (Delta Stream)
            SetWeatherRow(7, tmplSand, "et007_strongwinds01", "Set_State_BattleSandStorm", 0.85f);
            // [8] Snow (Luminescent Platinum)
            SetWeatherRow(8, tmplHail, "et002_hail01",        "Set_State_BattleHail",      0.85f);

            btInfo.SetNewData(btBf);
            byte[] packed = PackBundleLz4(bun.file, inst.file);
            am.UnloadAll(true);

            foreach (var root in new[] { delta, romfs })
            {
                if (!Directory.Exists(root)) continue;
                string outPath = Path.Combine(root, "Battle/battle_masterdatas");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, packed);
            }

            // Round-trip verify BattleDataTable.WeatherData
            var vam = new AssetsManager();
            var vbun = vam.LoadBundleFile(Path.Combine(delta, "Battle/battle_masterdatas"), true);
            var vinst = vam.LoadAssetsFileFromBundle(vbun, 0, false);
            foreach (var mb in vinst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var bf = vam.GetBaseField(vinst, mb);
                if (bf["m_Name"].AsString != "BattleDataTable") continue;
                var vwd = bf["WeatherData.Array"].Children;
                Console.WriteLine($"[PASS] Verified BattleDataTable.WeatherData (count={vwd.Count}):");
                for (int i = 0; i < vwd.Count; i++)
                    Console.WriteLine($"  [{i}] Main='{vwd[i]["MainFileName"].AsString}' Reg='{vwd[i]["RegisterName"].AsString}' Light={vwd[i]["LightPower"].AsFloat:F2}");
            }
            vam.UnloadAll(true);
        }

        Console.WriteLine("ALL PRIMAL WEATHER VFX & MASTERDATAS PATCHED & VERIFIED!");
        return 0;
    }
}
