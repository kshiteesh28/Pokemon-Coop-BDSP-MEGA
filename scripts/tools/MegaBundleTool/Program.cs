using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace MegaBundleTool;

internal static class Program
{
    private static readonly string RepoRoot =
        "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
    private static readonly string LumiRoot =
        "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons";
    private static readonly string BuiltBattleDir =
        Path.Combine(RepoRoot, "build/ripped-pm0006/unity-project/ExportedProject/BuiltBundles/pokemons/battle");
    private static readonly string DeltaBattleDir =
        Path.Combine(RepoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/battle");

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool <rebuild-lumi|verify-and-dump> [args...]");
            return 2;
        }

        return args[0] switch
        {
            "rebuild-lumi" => RebuildLumi(args.Skip(1).ToArray()),
            "verify-and-dump" => VerifyAndDump(args.Skip(1).ToArray()),
            _ => throw new ArgumentException($"Unknown command: {args[0]}")
        };
    }

    private sealed record TransformRec(string Name, long Parent, Vector3 Pos, Quaternion Rot, Vector3 Scale);

    private static string GetSpeciesPrefix(string formId)
    {
        var m = Regex.Match(formId, @"^(pm\d{4})_");
        if (!m.Success)
            throw new ArgumentException($"Invalid Pokemon bundle form ID '{formId}' (expected pmXXXX_YY_ZZ)");
        return m.Groups[1].Value;
    }

    private static Dictionary<long, TransformRec> ReadTransforms(AssetsManager am, AssetsFileInstance inst)
    {
        var f = inst.file;
        var map = new Dictionary<long, TransformRec>();
        foreach (var ti in f.GetAssetsOfType(AssetClassID.Transform))
        {
            var tb = am.GetBaseField(inst, ti);
            var go = am.GetBaseField(inst, f.GetAssetInfo(tb["m_GameObject.m_PathID"].AsLong)!);
            var p = tb["m_LocalPosition"];
            var r = tb["m_LocalRotation"];
            var sc = tb["m_LocalScale"];
            map[ti.PathId] = new TransformRec(
                go["m_Name"].AsString,
                tb["m_Father.m_PathID"].AsLong,
                new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat),
                new Quaternion(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat, r["w"].AsFloat),
                new Vector3(sc["x"].AsFloat, sc["y"].AsFloat, sc["z"].AsFloat));
        }
        return map;
    }

    /// <summary>
    /// Dynamically locates the primary BodySkin SkinnedMeshRenderer in any Pokemon battle bundle
    /// (prefers BodySkin name match, falling back to the SMR with the most bones).
    /// </summary>
    private static AssetFileInfo FindPrimaryBodySmr(AssetsManager am, AssetsFileInstance inst)
    {
        var f = inst.file;
        var smrs = f.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer);
        if (smrs.Count == 0)
            throw new InvalidOperationException("No SkinnedMeshRenderer found in bundle.");

        AssetFileInfo? best = null;
        int bestScore = -1;
        foreach (var si in smrs)
        {
            var sb = am.GetBaseField(inst, si);
            var go = am.GetBaseField(inst, f.GetAssetInfo(sb["m_GameObject.m_PathID"].AsLong)!);
            string name = go["m_Name"].AsString;
            int boneCount = sb["m_Bones.Array"].Children.Count;
            int score = boneCount + (name.Contains("BodySkin", StringComparison.OrdinalIgnoreCase) ? 10000 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = si;
            }
        }
        return best!;
    }

    private static Matrix4x4 GetWorld(Dictionary<long, TransformRec> tData, long pid, Dictionary<long, Quaternion>? rotOverrides = null)
    {
        var d = tData[pid];
        var rot = (rotOverrides != null && rotOverrides.TryGetValue(pid, out var r)) ? r : d.Rot;
        var local = Matrix4x4.CreateScale(d.Scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(d.Pos);
        return d.Parent == 0 ? local : local * GetWorld(tData, d.Parent, rotOverrides);
    }

    private static Matrix4x4 ReadUnityMat(AssetTypeValueField bp) => new(
        bp["e00"].AsFloat, bp["e10"].AsFloat, bp["e20"].AsFloat, bp["e30"].AsFloat,
        bp["e01"].AsFloat, bp["e11"].AsFloat, bp["e21"].AsFloat, bp["e31"].AsFloat,
        bp["e02"].AsFloat, bp["e12"].AsFloat, bp["e22"].AsFloat, bp["e32"].AsFloat,
        bp["e03"].AsFloat, bp["e13"].AsFloat, bp["e23"].AsFloat, bp["e33"].AsFloat);

    private static void WriteUnityMat(AssetTypeValueField bp, Matrix4x4 m)
    {
        bp["e00"].AsFloat = m.M11; bp["e01"].AsFloat = m.M21; bp["e02"].AsFloat = m.M31; bp["e03"].AsFloat = m.M41;
        bp["e10"].AsFloat = m.M12; bp["e11"].AsFloat = m.M22; bp["e12"].AsFloat = m.M32; bp["e13"].AsFloat = m.M42;
        bp["e20"].AsFloat = m.M13; bp["e21"].AsFloat = m.M23; bp["e22"].AsFloat = m.M33; bp["e23"].AsFloat = m.M43;
        bp["e30"].AsFloat = m.M14; bp["e31"].AsFloat = m.M24; bp["e32"].AsFloat = m.M34; bp["e33"].AsFloat = m.M44;
    }

    private static int Align16(int n) => (n + 15) & ~15;

    private static int GetChannelBytes(int fmt, int dim)
    {
        if (dim == 0) return 0;
        int elemSize = fmt switch
        {
            0 => 4,
            1 => 2,
            2 => 1,
            3 => 1,
            4 => 2,
            5 => 2,
            6 => 1,
            7 => 1,
            8 => 2,
            9 => 2,
            10 => 4,
            11 => 4,
            _ => 4
        };
        return elemSize * dim;
    }

    private sealed record StreamLayout(int[] Strides, int[] Offsets, int VertexCount, List<(int Stream, int Offset, int Fmt, int Dim)> Channels);

    private static StreamLayout ParseVertexLayout(AssetTypeValueField meshBf)
    {
        int vCount = meshBf["m_VertexData.m_VertexCount"].AsInt;
        var chArr = meshBf["m_VertexData.m_Channels.Array"].Children;
        var chs = new List<(int Stream, int Offset, int Fmt, int Dim)>();
        var strides = new int[4];
        foreach (var c in chArr)
        {
            int st = c["stream"].AsInt;
            int off = c["offset"].AsInt;
            int fmt = c["format"].AsInt;
            int dim = c["dimension"].AsInt;
            chs.Add((st, off, fmt, dim));
            if (dim > 0)
                strides[st] = Math.Max(strides[st], off + GetChannelBytes(fmt, dim));
        }
        var offsets = new int[4];
        int cur = 0;
        for (int s = 0; s < 4; s++)
        {
            offsets[s] = cur;
            if (strides[s] > 0)
                cur = Align16(cur + vCount * strides[s]);
        }
        return new StreamLayout(strides, offsets, vCount, chs);
    }

    private static AssetTypeValueField LoadVanillaBodyMesh(AssetsManager am, AssetsFileInstance vanBattleInst, AssetsFileInstance vanCommonInst, AssetTypeValueField vanSmr)
    {
        int meshFileId = vanSmr["m_Mesh.m_FileID"].AsInt;
        long meshPathId = vanSmr["m_Mesh.m_PathID"].AsLong;
        if (meshFileId == 0)
            return am.GetBaseField(vanBattleInst, vanBattleInst.file.GetAssetInfo(meshPathId)!);
        return am.GetBaseField(vanCommonInst, vanCommonInst.file.GetAssetInfo(meshPathId)!);
    }

    /// <summary>
    /// Rebuilds one or more Mega form bundles against their species' exact vanilla BDSP skeleton & bindposes.
    /// Args: <formId> <boneInfoJson> [builtBundlePath] [targetBundlePath]
    /// </summary>
    private static int RebuildLumi(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool rebuild-lumi <formId> <boneInfoJson> [builtBundlePath] [targetBundlePath]");
            return 2;
        }

        string formId = args[0];
        string boneInfoPath = args[1];
        string builtPath = args.Length > 2 ? args[2] : Path.Combine(BuiltBattleDir, formId);
        string targetPath = args.Length > 3 ? args[3] : Path.Combine(DeltaBattleDir, formId);

        if (!File.Exists(boneInfoPath))
        {
            Console.Error.WriteLine($"Missing bone info JSON: {boneInfoPath}");
            return 1;
        }

        string species = GetSpeciesPrefix(formId);
        string vanBattlePath = Path.Combine(LumiRoot, $"battle/{species}_00_00");
        string vanCommonPath = Path.Combine(LumiRoot, $"common/{species}_00");

        var infoDoc = JsonDocument.Parse(File.ReadAllText(boneInfoPath));
        var boneParents = infoDoc.RootElement.GetProperty("parents").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        var vgNames = infoDoc.RootElement.GetProperty("vg_names").EnumerateArray()
            .Select(e => e.GetString()!).ToList();

        var vanAm = new AssetsManager();
        var vanBattleBun = vanAm.LoadBundleFile(vanBattlePath, true);
        var vanBattleInst = vanAm.LoadAssetsFileFromBundle(vanBattleBun, 0, false);
        var vanCommonBun = vanAm.LoadBundleFile(vanCommonPath, true);
        var vanCommonInst = vanAm.LoadAssetsFileFromBundle(vanCommonBun, 0, false);

        var vanTransforms = ReadTransforms(vanAm, vanBattleInst);
        var vanSmrInfo = FindPrimaryBodySmr(vanAm, vanBattleInst);
        var vanSmr = vanAm.GetBaseField(vanBattleInst, vanSmrInfo);
        var vanMesh = LoadVanillaBodyMesh(vanAm, vanBattleInst, vanCommonInst, vanSmr);

        var vanBonePids = vanSmr["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var vanBoneNames = vanBonePids.Select(pid => vanTransforms[pid].Name).ToList();
        var vanBindPoses = vanMesh["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        var vanBindWorldPos = new Dictionary<string, Vector3>();
        var vanNameToIdx = new Dictionary<string, int>();

        for (int i = 0; i < vanBoneNames.Count; i++)
        {
            vanNameToIdx[vanBoneNames[i]] = i;
            Matrix4x4.Invert(vanBindPoses[i], out var invB);
            vanBindWorldPos[vanBoneNames[i]] = new Vector3(invB.M41, invB.M42, invB.M43);
        }

        float vanMinY = vanMesh["m_LocalAABB.m_Center.y"].AsFloat - vanMesh["m_LocalAABB.m_Extent.y"].AsFloat;
        float vanHeight = 2f * vanMesh["m_LocalAABB.m_Extent.y"].AsFloat;
        int fallbackRootIdx = vanNameToIdx.TryGetValue("Spine1", out int sp1) ? sp1 : (vanNameToIdx.TryGetValue("Waist", out int wst) ? wst : 0);

        int ResolveVanillaBoneIdx(string boneName, out string resolvedName)
        {
            string? cur = boneName;
            int guard = 0;
            while (!string.IsNullOrEmpty(cur) && guard++ < 32)
            {
                if (vanNameToIdx.TryGetValue(cur, out int idx))
                {
                    resolvedName = cur;
                    return idx;
                }
                if (!boneParents.TryGetValue(cur, out cur))
                    break;
            }
            resolvedName = vanBoneNames[fallbackRootIdx];
            return fallbackRootIdx;
        }

        var mapToVanilla = new int[vgNames.Count];
        int exactCount = 0, parentCount = 0;
        for (int i = 0; i < vgNames.Count; i++)
        {
            mapToVanilla[i] = ResolveVanillaBoneIdx(vgNames[i], out string resolved);
            if (resolved == vgNames[i]) exactCount++; else parentCount++;
        }

        Console.WriteLine($"\n=== Rebuilding {formId} against vanilla {species}_00_00 ({vanBoneNames.Count} bones, h={vanHeight:F3}m) ===");
        Console.WriteLine($"  Mapped {vgNames.Count} source vertex groups -> {vanBoneNames.Count} vanilla bones (exact={exactCount}, parent-chain={parentCount})");

        string tmp = targetPath + ".tmp";
        string tmpLz4 = targetPath + ".lz4";

        var am = new AssetsManager();
        var bun = am.LoadBundleFile(targetPath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
        var f = inst.file;

        var sBun = am.LoadBundleFile(builtPath, true);
        var sInst = am.LoadAssetsFileFromBundle(sBun, 0, false);
        var sFile = sInst.file;
        var sMeshInfo = sFile.GetAssetsOfType(AssetClassID.Mesh).First();
        var sMeshBf = am.GetBaseField(sInst, sMeshInfo);

        var layout = ParseVertexLayout(sMeshBf);
        var raw = (byte[])sMeshBf["m_VertexData.m_DataSize"].AsByteArray.Clone();
        int vCount = layout.VertexCount;

        int posBase = layout.Offsets[layout.Channels[0].Stream] + layout.Channels[0].Offset;
        int posStride = layout.Strides[layout.Channels[0].Stream];
        int wBase = layout.Offsets[layout.Channels[12].Stream] + layout.Channels[12].Offset;
        int wStride = layout.Strides[layout.Channels[12].Stream];
        int iBase = layout.Offsets[layout.Channels[13].Stream] + layout.Channels[13].Offset;
        int iStride = layout.Strides[layout.Channels[13].Stream];

        int vanHeadIdx = vanNameToIdx.TryGetValue("Head", out int hi) ? hi : -1;
        int vanJawIdx = vanNameToIdx.TryGetValue("Jaw", out int ji) ? ji : -1;
        int vanLFootIdx = vanNameToIdx.TryGetValue("LFoot", out int lfi) ? lfi : -1;
        int vanRFootIdx = vanNameToIdx.TryGetValue("RFoot", out int rfi) ? rfi : -1;

        Vector3 sumHead = Vector3.Zero, sumJaw = Vector3.Zero, sumFeet = Vector3.Zero;
        int nHead = 0, nJaw = 0, nFeet = 0;
        float rawMinY = float.MaxValue, rawMaxY = float.MinValue;

        for (int v = 0; v < vCount; v++)
        {
            int po = posBase + v * posStride;
            var p = new Vector3(
                BitConverter.ToSingle(raw, po),
                BitConverter.ToSingle(raw, po + 4),
                BitConverter.ToSingle(raw, po + 8));

            if (p.Y < rawMinY) rawMinY = p.Y;
            if (p.Y > rawMaxY) rawMaxY = p.Y;

            int wo = wBase + v * wStride;
            int io = iBase + v * iStride;
            float w0 = BitConverter.ToSingle(raw, wo);
            int origIdx0 = BitConverter.ToInt32(raw, io);
            int mapped0 = (origIdx0 >= 0 && origIdx0 < mapToVanilla.Length) ? mapToVanilla[origIdx0] : fallbackRootIdx;

            if (w0 > 0.4f)
            {
                if (mapped0 == vanHeadIdx) { sumHead += p; nHead++; }
                else if (mapped0 == vanJawIdx) { sumJaw += p; nJaw++; }
                else if (mapped0 == vanLFootIdx || mapped0 == vanRFootIdx) { sumFeet += p; nFeet++; }
            }
        }

        float rawTotalH = MathF.Max(rawMaxY - rawMinY, 1e-3f);
        float s = vanHeight / rawTotalH;
        Vector3 rawHead = nHead > 0 ? sumHead / nHead : new Vector3(0, rawMaxY * 0.75f, 0);
        Vector3 rawFeet = nFeet > 0 ? sumFeet / nFeet : new Vector3(0, rawMinY, 0);

        if (nHead > 0 && vanBindWorldPos.TryGetValue("Head", out var vanHeadPos))
        {
            float rawHeadAboveFeet = MathF.Max(rawHead.Y - rawMinY, 1e-3f);
            float scaleByHead = (vanHeadPos.Y - vanMinY) / rawHeadAboveFeet;
            s = Math.Clamp(scaleByHead, (0.85f * vanHeight) / rawTotalH, (1.30f * vanHeight) / rawTotalH);
        }

        float tx = -rawHead.X * s;
        float ty = vanMinY - rawMinY * s;
        float tz = 0f;
        if (nHead > 0 && vanBindWorldPos.TryGetValue("Head", out var vh))
        {
            float tzByHead = (vh.Z + 0.06f) - rawHead.Z * s;
            float tzByFeet = (nFeet > 0 && vanBindWorldPos.TryGetValue("LFoot", out var vf))
                ? vf.Z - rawFeet.Z * s
                : tzByHead;
            tz = 0.70f * tzByHead + 0.30f * tzByFeet;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        var mergedIdx = new int[4];
        var mergedW = new float[4];

        for (int v = 0; v < vCount; v++)
        {
            int po = posBase + v * posStride;
            float vx = BitConverter.ToSingle(raw, po) * s + tx;
            float vy = BitConverter.ToSingle(raw, po + 4) * s + ty;
            float vz = BitConverter.ToSingle(raw, po + 8) * s + tz;
            Array.Copy(BitConverter.GetBytes(vx), 0, raw, po, 4);
            Array.Copy(BitConverter.GetBytes(vy), 0, raw, po + 4, 4);
            Array.Copy(BitConverter.GetBytes(vz), 0, raw, po + 8, 4);

            if (vx < minX) minX = vx; if (vx > maxX) maxX = vx;
            if (vy < minY) minY = vy; if (vy > maxY) maxY = vy;
            if (vz < minZ) minZ = vz; if (vz > maxZ) maxZ = vz;

            int wo = wBase + v * wStride;
            int io = iBase + v * iStride;
            int mCount = 0;

            for (int k = 0; k < 4; k++)
            {
                float wk = BitConverter.ToSingle(raw, wo + k * 4);
                int ik = BitConverter.ToInt32(raw, io + k * 4);
                if (wk <= 1e-6f) continue;
                int target = (ik >= 0 && ik < mapToVanilla.Length) ? mapToVanilla[ik] : fallbackRootIdx;

                // Per-form hygiene: guard Mega Charizard X's mouth-flames against nearest-surface wing/arm bleed
                if (formId == "pm0006_01_00" && vanHeadIdx >= 0 && vy > 1.10f && vz > 0.25f && MathF.Abs(vx) < 0.48f)
                {
                    string bn = vanBoneNames[target];
                    if (bn.Contains("Feeler") || bn.Contains("Arm") || bn.Contains("Hand") || bn.Contains("Finger"))
                        target = vanHeadIdx;
                }

                int found = -1;
                for (int m = 0; m < mCount; m++)
                {
                    if (mergedIdx[m] == target) { found = m; break; }
                }
                if (found >= 0) mergedW[found] += wk;
                else if (mCount < 4) { mergedIdx[mCount] = target; mergedW[mCount] = wk; mCount++; }
            }

            if (mCount == 0)
            {
                mergedIdx[0] = fallbackRootIdx;
                mergedW[0] = 1f;
                mCount = 1;
            }

            float wSum = 0f;
            for (int m = 0; m < mCount; m++) wSum += mergedW[m];
            if (wSum > 1e-6f)
                for (int m = 0; m < mCount; m++) mergedW[m] /= wSum;

            for (int k = 0; k < 4; k++)
            {
                float wk = k < mCount ? mergedW[k] : 0f;
                int ik = k < mCount ? mergedIdx[k] : 0;
                Array.Copy(BitConverter.GetBytes(wk), 0, raw, wo + k * 4, 4);
                Array.Copy(BitConverter.GetBytes(ik), 0, raw, io + k * 4, 4);
            }
        }

        var smrInfo = FindPrimaryBodySmr(am, inst);
        var smrBf = am.GetBaseField(inst, smrInfo);
        long customMeshPid = smrBf["m_Mesh.m_PathID"].AsLong;
        var meshInfo = f.GetAssetInfo(customMeshPid)!;
        var meshBf = am.GetBaseField(inst, meshInfo);

        meshBf["m_VertexData.m_DataSize"].AsByteArray = raw;
        meshBf["m_LocalAABB.m_Center.x"].AsFloat = 0.5f * (minX + maxX);
        meshBf["m_LocalAABB.m_Center.y"].AsFloat = 0.5f * (minY + maxY);
        meshBf["m_LocalAABB.m_Center.z"].AsFloat = 0.5f * (minZ + maxZ);
        meshBf["m_LocalAABB.m_Extent.x"].AsFloat = 0.5f * (maxX - minX);
        meshBf["m_LocalAABB.m_Extent.y"].AsFloat = 0.5f * (maxY - minY);
        meshBf["m_LocalAABB.m_Extent.z"].AsFloat = 0.5f * (maxZ - minZ);

        // Disable Mega Charizard Y's untextured 3DS tail-flame stencil shells (BDSP renders native FireCore/FireMask)
        if (formId == "pm0006_02_00")
        {
            var subs = meshBf["m_SubMeshes.Array"].Children;
            for (int si = 3; si < subs.Count; si++)
                subs[si]["indexCount"].AsUInt = 0;
        }

        var bpArr = meshBf["m_BindPose.Array"];
        bpArr.Children.Clear();
        for (int i = 0; i < vanBindPoses.Count; i++)
        {
            var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(bpArr);
            WriteUnityMat(elem, vanBindPoses[i]);
            bpArr.Children.Add(elem);
        }

        var bonesArr = smrBf["m_Bones.Array"];
        bonesArr.Children.Clear();
        for (int i = 0; i < vanBonePids.Count; i++)
        {
            var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(bonesArr);
            elem["m_FileID"].AsInt = 0;
            elem["m_PathID"].AsLong = vanBonePids[i];
            bonesArr.Children.Add(elem);
        }

        smrBf["m_AABB.m_Center.x"].AsFloat = 0.5f * (minX + maxX);
        smrBf["m_AABB.m_Center.y"].AsFloat = 0.5f * (minY + maxY);
        smrBf["m_AABB.m_Center.z"].AsFloat = 0.5f * (minZ + maxZ);
        smrBf["m_AABB.m_Extent.x"].AsFloat = 0.5f * (maxX - minX);
        smrBf["m_AABB.m_Extent.y"].AsFloat = 0.5f * (maxY - minY);
        smrBf["m_AABB.m_Extent.z"].AsFloat = 0.5f * (maxZ - minZ);

        meshInfo.SetNewData(meshBf);
        smrInfo.SetNewData(smrBf);

        bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(f);
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
        File.Move(tmpLz4, targetPath, true);

        Console.WriteLine($"  SAVED {formId}: size={new FileInfo(targetPath).Length:N0}, bones={vanBonePids.Count}, bindPoses={vanBindPoses.Count}, bounds=X[{minX:F2},{maxX:F2}] Y[{minY:F2},{maxY:F2}] Z[{minZ:F2},{maxZ:F2}]");
        vanAm.UnloadAll(true);
        return 0;
    }

    private static int VerifyAndDump(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool verify-and-dump <bundle_path> <out_dir>");
            return 2;
        }

        string bundlePath = args[0];
        string outDir = args[1];
        string formId = Path.GetFileName(bundlePath);
        string species = GetSpeciesPrefix(formId);
        Directory.CreateDirectory(outDir);

        string vanBattlePath = Path.Combine(LumiRoot, $"battle/{species}_00_00");
        string vanCommonPath = Path.Combine(LumiRoot, $"common/{species}_00");

        var am = new AssetsManager();

        var vanBattleBun = am.LoadBundleFile(vanBattlePath, true);
        var vanBattleInst = am.LoadAssetsFileFromBundle(vanBattleBun, 0, false);
        var vanCommonBun = am.LoadBundleFile(vanCommonPath, true);
        var vanCommonInst = am.LoadAssetsFileFromBundle(vanCommonBun, 0, false);

        var vanSmrInfo = FindPrimaryBodySmr(am, vanBattleInst);
        var vanSmrBf = am.GetBaseField(vanBattleInst, vanSmrInfo);
        var vanMeshBf = LoadVanillaBodyMesh(am, vanBattleInst, vanCommonInst, vanSmrBf);
        var vanTransforms = ReadTransforms(am, vanBattleInst);
        var vanBonePids = vanSmrBf["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var vanBoneNames = vanBonePids.Select(pid => vanTransforms[pid].Name).ToList();
        var vanBindPoses = vanMeshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        var vanMbSizes = vanBattleInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour)
            .Select(m => (int)m.ByteSize).OrderByDescending(x => x).ToArray();
        int vanExternalCount = vanBattleInst.file.Metadata.Externals.Count;
        float vanMinY = vanMeshBf["m_LocalAABB.m_Center.y"].AsFloat - vanMeshBf["m_LocalAABB.m_Extent.y"].AsFloat;
        float vanHeight = 2f * vanMeshBf["m_LocalAABB.m_Extent.y"].AsFloat;

        var bun = am.LoadBundleFile(bundlePath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
        var f = inst.file;

        var failures = new List<string>();
        void Assert(bool cond, string msg)
        {
            if (!cond) failures.Add(msg);
            Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {msg}");
        }

        Console.WriteLine($"\n=== Verifying {formId} (vs vanilla {species}_00_00: {vanBonePids.Count} bones, h={vanHeight:F2}m) ===");
        Assert(f.Metadata.TargetPlatform == 38, $"TargetPlatform == 38 (Switch), actual={f.Metadata.TargetPlatform}");
        Assert(f.GetAssetsOfType(AssetClassID.Shader).Count == 0, $"Embedded Shaders == 0, actual={f.GetAssetsOfType(AssetClassID.Shader).Count}");
        Assert(f.Metadata.Externals.Count == vanExternalCount, $"Externals == {vanExternalCount} (vanilla parity), actual={f.Metadata.Externals.Count}");

        var mbSizes = f.GetAssetsOfType(AssetClassID.MonoBehaviour).Select(m => (int)m.ByteSize).OrderByDescending(x => x).ToArray();
        Assert(mbSizes.SequenceEqual(vanMbSizes), $"MonoBehaviour sizes [{string.Join(", ", vanMbSizes)}], actual=[{string.Join(", ", mbSizes)}]");

        var tData = ReadTransforms(am, inst);
        var smrInfo = FindPrimaryBodySmr(am, inst);
        var smrBf = am.GetBaseField(inst, smrInfo);
        long customMeshPid = smrBf["m_Mesh.m_PathID"].AsLong;
        var meshBf = am.GetBaseField(inst, f.GetAssetInfo(customMeshPid)!);

        var bonePids = smrBf["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var bindPoses = meshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        int expectedBones = vanBonePids.Count;
        Assert(bonePids.Count == expectedBones, $"SMR m_Bones count == {expectedBones} (vanilla parity), actual={bonePids.Count}");
        Assert(bindPoses.Count == expectedBones, $"Mesh m_BindPose count == {expectedBones} (vanilla parity), actual={bindPoses.Count}");

        float maxBpErr = 0f;
        for (int i = 0; i < Math.Min(bindPoses.Count, vanBindPoses.Count); i++)
        {
            var a = bindPoses[i]; var b = vanBindPoses[i];
            float d = MathF.Abs(a.M11 - b.M11) + MathF.Abs(a.M22 - b.M22) + MathF.Abs(a.M33 - b.M33)
                    + MathF.Abs(a.M41 - b.M41) + MathF.Abs(a.M42 - b.M42) + MathF.Abs(a.M43 - b.M43);
            if (d > maxBpErr) maxBpErr = d;
        }
        Assert(maxBpErr < 1e-4f, $"m_BindPose exact parity with vanilla {species}_00_00 (maxErr={maxBpErr:E2})");

        var layout = ParseVertexLayout(meshBf);
        var raw = meshBf["m_VertexData.m_DataSize"].AsByteArray;
        int vCount = layout.VertexCount;

        int posBase = layout.Offsets[layout.Channels[0].Stream] + layout.Channels[0].Offset;
        int posStride = layout.Strides[layout.Channels[0].Stream];
        int wBase = layout.Offsets[layout.Channels[12].Stream] + layout.Channels[12].Offset;
        int wStride = layout.Strides[layout.Channels[12].Stream];
        int iBase = layout.Offsets[layout.Channels[13].Stream] + layout.Channels[13].Offset;
        int iStride = layout.Strides[layout.Channels[13].Stream];

        var verts = new Vector3[vCount];
        var weights = new (int i0, int i1, int i2, int i3, float w0, float w1, float w2, float w3)[vCount];
        bool anyOutOfRangeBone = false;
        bool anyBadWeightSum = false;
        bool anyNan = false;

        for (int v = 0; v < vCount; v++)
        {
            int po = posBase + v * posStride;
            verts[v] = new Vector3(
                BitConverter.ToSingle(raw, po),
                BitConverter.ToSingle(raw, po + 4),
                BitConverter.ToSingle(raw, po + 8));
            if (float.IsNaN(verts[v].X) || float.IsNaN(verts[v].Y) || float.IsNaN(verts[v].Z)) anyNan = true;

            int wo = wBase + v * wStride;
            int io = iBase + v * iStride;
            float w0 = BitConverter.ToSingle(raw, wo);
            float w1 = BitConverter.ToSingle(raw, wo + 4);
            float w2 = BitConverter.ToSingle(raw, wo + 8);
            float w3 = BitConverter.ToSingle(raw, wo + 12);
            int i0 = BitConverter.ToInt32(raw, io);
            int i1 = BitConverter.ToInt32(raw, io + 4);
            int i2 = BitConverter.ToInt32(raw, io + 8);
            int i3 = BitConverter.ToInt32(raw, io + 12);
            weights[v] = (i0, i1, i2, i3, w0, w1, w2, w3);

            if ((w0 > 0 && (i0 < 0 || i0 >= expectedBones)) || (w1 > 0 && (i1 < 0 || i1 >= expectedBones)) ||
                (w2 > 0 && (i2 < 0 || i2 >= expectedBones)) || (w3 > 0 && (i3 < 0 || i3 >= expectedBones)))
                anyOutOfRangeBone = true;
            if (MathF.Abs((w0 + w1 + w2 + w3) - 1f) > 0.02f)
                anyBadWeightSum = true;
        }
        Assert(!anyNan, "Zero NaN vertices");
        Assert(!anyOutOfRangeBone, $"All active vertex bone indices within [0..{expectedBones - 1}]");
        Assert(!anyBadWeightSum, "All vertex bone weight sums == 1.0");

        var idxBuf = meshBf["m_IndexBuffer.Array"].AsByteArray;
        int idxFmt = meshBf["m_IndexFormat"].AsInt;
        var tris = new List<(int a, int b, int c)>();
        foreach (var sub in meshBf["m_SubMeshes.Array"].Children)
        {
            uint count = sub["indexCount"].AsUInt;
            if (count == 0) continue;
            uint firstByte = sub["firstByte"].AsUInt;
            int baseVert = sub["baseVertex"].AsInt;
            for (uint t = 0; t + 2 < count; t += 3)
            {
                int a, b, c;
                if (idxFmt == 0)
                {
                    a = BitConverter.ToUInt16(idxBuf, (int)(firstByte + t * 2)) + baseVert;
                    b = BitConverter.ToUInt16(idxBuf, (int)(firstByte + (t + 1) * 2)) + baseVert;
                    c = BitConverter.ToUInt16(idxBuf, (int)(firstByte + (t + 2) * 2)) + baseVert;
                }
                else
                {
                    a = (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + t * 4)) + baseVert;
                    b = (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + (t + 1) * 4)) + baseVert;
                    c = (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + (t + 2) * 4)) + baseVert;
                }
                tris.Add((a, b, c));
            }
        }

        // Dynamic anatomical bone indices by name (works for every species!)
        var boneNameToIdx = new Dictionary<string, int>();
        for (int i = 0; i < bonePids.Count; i++)
            if (tData.TryGetValue(bonePids[i], out var tr) && !boneNameToIdx.ContainsKey(tr.Name))
                boneNameToIdx[tr.Name] = i;

        int headIdx = boneNameToIdx.TryGetValue("Head", out int hIdx) ? hIdx : -1;
        int jawIdx = boneNameToIdx.TryGetValue("Jaw", out int jIdx) ? jIdx : -1;
        int tongueIdx = boneNameToIdx.TryGetValue("Tongue", out int tIdx) ? tIdx : -1;

        // Build dynamic roar/action stress pose from whichever bones exist on this species
        var roarOverrides = new Dictionary<long, Quaternion>();
        void AddRot(string boneName, Vector3 axis, float rad)
        {
            var match = tData.FirstOrDefault(kv => kv.Value.Name == boneName);
            if (match.Key != 0)
                roarOverrides[match.Key] = match.Value.Rot * Quaternion.CreateFromAxisAngle(axis, rad);
        }
        AddRot("Waist", Vector3.UnitZ, 0.20f);
        AddRot("Jaw", Vector3.UnitY, -0.30f);
        if (tData.Values.Any(v => v.Name == "LFeeler1"))
        {
            AddRot("LFeeler1", Vector3.UnitZ, 0.35f);
            AddRot("RFeeler1", Vector3.UnitZ, -0.35f);
        }
        else
        {
            AddRot("LArm", Vector3.UnitZ, 0.30f);
            AddRot("RArm", Vector3.UnitZ, -0.30f);
        }

        void EvalAndDumpPose(string poseName, Dictionary<long, Quaternion>? overrides, bool checkAnat)
        {
            var skinMats = new Matrix4x4[expectedBones];
            for (int i = 0; i < expectedBones; i++)
            {
                var w = GetWorld(tData, bonePids[i], overrides);
                skinMats[i] = bindPoses[i] * w;
            }

            var posed = new Vector3[vCount];
            float minY = float.MaxValue, maxY = float.MinValue;
            Vector3 sumHead = Vector3.Zero, sumJaw = Vector3.Zero, sumTongue = Vector3.Zero;
            int nHead = 0, nJaw = 0, nTongue = 0;

            for (int v = 0; v < vCount; v++)
            {
                var w = weights[v];
                var p = Vector3.Zero;
                if (w.w0 > 0) p += Vector3.Transform(verts[v], skinMats[w.i0]) * w.w0;
                if (w.w1 > 0) p += Vector3.Transform(verts[v], skinMats[w.i1]) * w.w1;
                if (w.w2 > 0) p += Vector3.Transform(verts[v], skinMats[w.i2]) * w.w2;
                if (w.w3 > 0) p += Vector3.Transform(verts[v], skinMats[w.i3]) * w.w3;
                posed[v] = p;

                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                if (w.w0 > 0.4f)
                {
                    if (w.i0 == headIdx) { sumHead += p; nHead++; }
                    else if (w.i0 == jawIdx) { sumJaw += p; nJaw++; }
                    else if (w.i0 == tongueIdx) { sumTongue += p; nTongue++; }
                }
            }

            if (checkAnat)
            {
                float loMinY = vanMinY - 0.20f, hiMinY = vanMinY + 0.30f;
                float loH = 0.65f * vanHeight, hiH = 1.60f * vanHeight;
                float posedH = maxY - minY;
                Assert(minY >= loMinY && minY <= hiMinY, $"{poseName}: Ground/hover aligned (minY={minY:F3} in [{loMinY:F2}, {hiMinY:F2}])");
                Assert(posedH >= loH && posedH <= hiH, $"{poseName}: Proportionate height (h={posedH:F3}m in [{loH:F2}, {hiH:F2}] vs vanilla {vanHeight:F2}m)");
                if (nJaw > 0 && nHead > 0)
                {
                    var cHead = sumHead / nHead;
                    var cJaw = sumJaw / nJaw;
                    Assert(cJaw.Y < cHead.Y, $"{poseName}: Jaw BELOW Head (Jaw.Y={cJaw.Y:F3} < Head.Y={cHead.Y:F3})");
                }
                if (nTongue > 0 && nHead > 0)
                {
                    var cHead = sumHead / nHead;
                    var cTongue = sumTongue / nTongue;
                    Assert(cTongue.Y < cHead.Y, $"{poseName}: Tongue BELOW Head (Tongue.Y={cTongue.Y:F3} < Head.Y={cHead.Y:F3})");
                }
            }

            string objPath = Path.Combine(outDir, $"{formId}_{poseName}.obj");
            var sb = new StringBuilder(vCount * 40);
            for (int v = 0; v < vCount; v++)
            {
                var p = posed[v];
                sb.AppendFormat("v {0:F5} {1:F5} {2:F5}\n", p.X, -p.Z, p.Y);
            }
            foreach (var (a, b, c) in tris)
                sb.AppendFormat("f {0} {1} {2}\n", a + 1, b + 1, c + 1);
            File.WriteAllText(objPath, sb.ToString());
        }

        EvalAndDumpPose("idle", null, true);
        EvalAndDumpPose("roar", roarOverrides, false);

        am.UnloadAll(true);
        return failures.Count == 0 ? 0 : 1;
    }
}
