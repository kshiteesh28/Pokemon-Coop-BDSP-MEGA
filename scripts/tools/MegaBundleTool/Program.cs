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
            "dump-vanilla-pm0006" => DumpVanillaPm0006(args.Skip(1).ToArray()),
            "build-mega-x-bundle" => BuildMegaXBundle(args.Skip(1).ToArray()),
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

        int idxLArm = vanNameToIdx.TryGetValue("LArm", out int lai) ? lai : -1;
        int idxLForeArm = vanNameToIdx.TryGetValue("LForeArm", out int lfai) ? lfai : -1;
        int idxLHand = vanNameToIdx.TryGetValue("LHand", out int lhi) ? lhi : -1;
        int idxRArm = vanNameToIdx.TryGetValue("RArm", out int rai) ? rai : -1;
        int idxRForeArm = vanNameToIdx.TryGetValue("RForeArm", out int rfai) ? rfai : -1;
        int idxRHand = vanNameToIdx.TryGetValue("RHand", out int rhi) ? rhi : -1;

        for (int v = 0; v < vCount; v++)
        {
            int po = posBase + v * posStride;
            float vx = BitConverter.ToSingle(raw, po) * s + tx;
            float vy = BitConverter.ToSingle(raw, po + 4) * s + ty;
            float vz = BitConverter.ToSingle(raw, po + 8) * s + tz;

            // Mega Charizard X (pm0006_01_00) was sculpted in T-pose (horizontal outstretched arms).
            // Smoothly bend its arms down & forward around BDSP's shoulders into Charizard's battle stance!
            bool isMegaXArm = false;
            bool isMegaXLeftArm = false;
            float origAbsX = MathF.Abs(vx);
            if (formId == "pm0006_01_00" && origAbsX > 0.22f && vy > 0.62f && vy < 1.06f && vz > 0.02f)
            {
                isMegaXArm = true;
                isMegaXLeftArm = vx < 0f;
                float sign = isMegaXLeftArm ? -1f : 1f;
                float blend = Math.Clamp((origAbsX - 0.22f) / 0.14f, 0f, 1f);
                var pivot = new Vector3(sign * 0.22f, 0.88f, 0.08f);
                var rel = new Vector3(vx, vy, vz) - pivot;
                var qDown = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sign * 0.62f * blend);
                var qFwd = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -sign * 0.60f * blend);
                var posedArm = Vector3.Transform(rel, qDown * qFwd) + pivot;
                vx = posedArm.X; vy = posedArm.Y; vz = posedArm.Z;
            }

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

                // Per-form hygiene for unrigged Mega Charizard X:
                // 1) Strictly bind Head + Horns + Mouth Flames to Head, zero wing/arm bleed.
                // 2) Strictly bind Arms to LArm/LForeArm/LHand (or R equivalents), zero wing bleed.
                if (formId == "pm0006_01_00")
                {
                    if (vanHeadIdx >= 0 && vy > 1.04f && vz > 0.10f && MathF.Abs(vx) < 0.58f)
                    {
                        string bn = vanBoneNames[target];
                        if (bn.Contains("Feeler") || bn.Contains("Arm") || bn.Contains("Hand") || bn.Contains("Finger") || bn.Contains("Shoulder"))
                            target = vanHeadIdx;
                    }
                    else if (isMegaXArm && idxLArm >= 0 && idxRArm >= 0)
                    {
                        float dist = origAbsX - 0.22f;
                        if (isMegaXLeftArm)
                            target = dist > 0.28f && idxLHand >= 0 ? idxLHand : (dist > 0.14f && idxLForeArm >= 0 ? idxLForeArm : idxLArm);
                        else
                            target = dist > 0.28f && idxRHand >= 0 ? idxRHand : (dist > 0.14f && idxRForeArm >= 0 ? idxRForeArm : idxRArm);
                    }
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

    private static float ReadHalf(byte[] buf, int off) => (float)BitConverter.ToHalf(buf, off);

    private static float ReadChannelFloat(byte[] buf, int off, int fmt) => fmt switch
    {
        0 => BitConverter.ToSingle(buf, off),
        1 => ReadHalf(buf, off),
        2 => buf[off] / 255f,
        3 => (sbyte)buf[off] / 127f,
        4 => BitConverter.ToUInt16(buf, off) / 65535f,
        5 => BitConverter.ToInt16(buf, off) / 32767f,
        _ => BitConverter.ToSingle(buf, off)
    };

    private static int ReadChannelInt(byte[] buf, int off, int fmt) => fmt switch
    {
        6 => buf[off],
        7 => (sbyte)buf[off],
        8 => BitConverter.ToUInt16(buf, off),
        9 => BitConverter.ToInt16(buf, off),
        10 => (int)BitConverter.ToUInt32(buf, off),
        11 => BitConverter.ToInt32(buf, off),
        _ => BitConverter.ToInt32(buf, off)
    };

    private static int GetElemSize(int fmt) => fmt switch
    {
        0 or 10 or 11 => 4,
        1 or 4 or 5 or 8 or 9 => 2,
        _ => 1
    };

    private static int DumpVanillaPm0006(string[] args)
    {
        string outJson = args.Length > 0 ? args[0] : "/tmp/mega_x_build/van_pm0006.json";
        Directory.CreateDirectory(Path.GetDirectoryName(outJson)!);

        string vanBattlePath = Path.Combine(RepoRoot, "build/pm0006-source/battle/pm0006_00_00");
        string vanCommonPath = Path.Combine(RepoRoot, "build/pm0006-source/common/pm0006_00");

        var am = new AssetsManager();
        var battleBun = am.LoadBundleFile(vanBattlePath, true);
        var battleInst = am.LoadAssetsFileFromBundle(battleBun, 0, false);
        var commonBun = am.LoadBundleFile(vanCommonPath, true);
        var commonInst = am.LoadAssetsFileFromBundle(commonBun, 0, false);

        var tData = ReadTransforms(am, battleInst);
        var smrInfo = FindPrimaryBodySmr(am, battleInst);
        var smrBf = am.GetBaseField(battleInst, smrInfo);
        var meshBf = LoadVanillaBodyMesh(am, battleInst, commonInst, smrBf);

        Console.WriteLine("=== Vanilla pm0006_00_00 SMRs & Materials ===");
        foreach (var si in battleInst.file.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
        {
            var sb = am.GetBaseField(battleInst, si);
            var go = am.GetBaseField(battleInst, battleInst.file.GetAssetInfo(sb["m_GameObject.m_PathID"].AsLong)!);
            Console.WriteLine($"  SMR: {go["m_Name"].AsString} bones={sb["m_Bones.Array"].Children.Count} mats={sb["m_Materials.Array"].Children.Count}");
            foreach (var mRef in sb["m_Materials.Array"].Children)
            {
                int fid = mRef["m_FileID"].AsInt;
                long pid = mRef["m_PathID"].AsLong;
                var mInst = fid == 0 ? battleInst : commonInst;
                var mi = mInst.file.GetAssetInfo(pid);
                if (mi != null)
                {
                    var mb = am.GetBaseField(mInst, mi);
                    Console.WriteLine($"    Mat file={fid} pid={pid} name={mb["m_Name"].AsString}");
                }
            }
        }

        var bonePids = smrBf["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var bindPoses = meshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        var bonesOut = new List<object>();
        for (int i = 0; i < bonePids.Count; i++)
        {
            Matrix4x4.Invert(bindPoses[i], out var invB);
            bonesOut.Add(new
            {
                idx = i,
                name = tData[bonePids[i]].Name,
                pos = new[] { invB.M41, invB.M42, invB.M43 }
            });
        }

        var layout = ParseVertexLayout(meshBf);
        var raw = meshBf["m_VertexData.m_DataSize"].AsByteArray;
        int vCount = layout.VertexCount;

        Console.WriteLine($"=== Vanilla BodySkin Mesh: vCount={vCount}, submeshes={meshBf["m_SubMeshes.Array"].Children.Count} ===");
        for (int ci = 0; ci < layout.Channels.Count; ci++)
        {
            var ch = layout.Channels[ci];
            if (ch.Dim > 0)
                Console.WriteLine($"  ch[{ci}]: stream={ch.Stream} off={ch.Offset} fmt={ch.Fmt} dim={ch.Dim}");
        }

        var verts = new List<float[]>(vCount);
        var normals = new List<float[]>(vCount);
        var uvs = new List<float[]>(vCount);
        var weights = new List<float[]>(vCount);
        var indices = new List<int[]>(vCount);

        var cPos = layout.Channels[0];
        var cNrm = layout.Channels[1];
        var cUv0 = layout.Channels[4];
        var cW = layout.Channels[12];
        var cI = layout.Channels[13];

        for (int v = 0; v < vCount; v++)
        {
            int po = layout.Offsets[cPos.Stream] + v * layout.Strides[cPos.Stream] + cPos.Offset;
            int esP = GetElemSize(cPos.Fmt);
            verts.Add(new[] {
                ReadChannelFloat(raw, po, cPos.Fmt),
                ReadChannelFloat(raw, po + esP, cPos.Fmt),
                ReadChannelFloat(raw, po + 2 * esP, cPos.Fmt)
            });

            if (cNrm.Dim >= 3)
            {
                int no = layout.Offsets[cNrm.Stream] + v * layout.Strides[cNrm.Stream] + cNrm.Offset;
                int esN = GetElemSize(cNrm.Fmt);
                normals.Add(new[] {
                    ReadChannelFloat(raw, no, cNrm.Fmt),
                    ReadChannelFloat(raw, no + esN, cNrm.Fmt),
                    ReadChannelFloat(raw, no + 2 * esN, cNrm.Fmt)
                });
            }
            else normals.Add(new[] { 0f, 1f, 0f });

            if (cUv0.Dim >= 2)
            {
                int uo = layout.Offsets[cUv0.Stream] + v * layout.Strides[cUv0.Stream] + cUv0.Offset;
                int esU = GetElemSize(cUv0.Fmt);
                uvs.Add(new[] {
                    ReadChannelFloat(raw, uo, cUv0.Fmt),
                    ReadChannelFloat(raw, uo + esU, cUv0.Fmt)
                });
            }
            else uvs.Add(new[] { 0f, 0f });

            int wo = layout.Offsets[cW.Stream] + v * layout.Strides[cW.Stream] + cW.Offset;
            int esW = GetElemSize(cW.Fmt);
            weights.Add(new[] {
                ReadChannelFloat(raw, wo, cW.Fmt),
                cW.Dim > 1 ? ReadChannelFloat(raw, wo + esW, cW.Fmt) : 0f,
                cW.Dim > 2 ? ReadChannelFloat(raw, wo + 2 * esW, cW.Fmt) : 0f,
                cW.Dim > 3 ? ReadChannelFloat(raw, wo + 3 * esW, cW.Fmt) : 0f
            });

            int io = layout.Offsets[cI.Stream] + v * layout.Strides[cI.Stream] + cI.Offset;
            int esI = GetElemSize(cI.Fmt);
            indices.Add(new[] {
                ReadChannelInt(raw, io, cI.Fmt),
                cI.Dim > 1 ? ReadChannelInt(raw, io + esI, cI.Fmt) : 0,
                cI.Dim > 2 ? ReadChannelInt(raw, io + 2 * esI, cI.Fmt) : 0,
                cI.Dim > 3 ? ReadChannelInt(raw, io + 3 * esI, cI.Fmt) : 0
            });
        }

        var idxBuf = meshBf["m_IndexBuffer.Array"].AsByteArray;
        int idxFmt = meshBf["m_IndexFormat"].AsInt;
        var submeshes = new List<List<int>>();
        int subIdx = 0;
        foreach (var sub in meshBf["m_SubMeshes.Array"].Children)
        {
            uint count = sub["indexCount"].AsUInt;
            uint firstByte = sub["firstByte"].AsUInt;
            int baseVert = sub["baseVertex"].AsInt;
            var list = new List<int>((int)count);
            for (uint t = 0; t < count; t++)
            {
                int vi = idxFmt == 0
                    ? BitConverter.ToUInt16(idxBuf, (int)(firstByte + t * 2)) + baseVert
                    : (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + t * 4)) + baseVert;
                list.Add(vi);
            }
            Console.WriteLine($"  submesh[{subIdx++}]: tris={count / 3}");
            submeshes.Add(list);
        }

        var payload = new { bones = bonesOut, verts, normals, uvs, weights, indices, submeshes };
        File.WriteAllText(outJson, JsonSerializer.Serialize(payload));
        Console.WriteLine($"Dumped vanilla pm0006 -> {outJson}");
        am.UnloadAll(true);
        return 0;
    }

    private static int BuildMegaXBundle(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool build-mega-x-bundle <meshJson> <texDir> <outBundle>");
            return 2;
        }
        string meshJsonPath = args[0];
        string texDir = args[1];
        string outBundle = args[2];

        string vanBattlePath = Path.Combine(RepoRoot, "build/pm0006-source/battle/pm0006_00_00");
        string vanCommonPath = Path.Combine(RepoRoot, "build/pm0006-source/common/pm0006_00");

        var doc = JsonDocument.Parse(File.ReadAllText(meshJsonPath)).RootElement;
        var jVerts = doc.GetProperty("verts");
        var jNorms = doc.GetProperty("normals");
        var jUvs = doc.GetProperty("uvs");
        var jW = doc.GetProperty("weights");
        var jI = doc.GetProperty("indices");
        var jSubs = doc.GetProperty("submeshes");
        int vCount = jVerts.GetArrayLength();

        // Stream 0: pos(12) + normal(12) + tangent(16) + uv0(8) = 48 bytes/vert
        // Stream 1: weights(16) + indices(16) = 32 bytes/vert
        const int stride0 = 48;
        const int stride1 = 32;
        int stream1Off = Align16(vCount * stride0);
        int totalDataSize = stream1Off + vCount * stride1;
        var vData = new byte[totalDataSize];

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int v = 0; v < vCount; v++)
        {
            float vx = jVerts[v][0].GetSingle();
            float vy = jVerts[v][1].GetSingle();
            float vz = jVerts[v][2].GetSingle();
            float nx = jNorms[v][0].GetSingle();
            float ny = jNorms[v][1].GetSingle();
            float nz = jNorms[v][2].GetSingle();
            float u = jUvs[v][0].GetSingle();
            float uvY = jUvs[v][1].GetSingle();

            if (vx < minX) minX = vx; if (vx > maxX) maxX = vx;
            if (vy < minY) minY = vy; if (vy > maxY) maxY = vy;
            if (vz < minZ) minZ = vz; if (vz > maxZ) maxZ = vz;

            int o0 = v * stride0;
            Array.Copy(BitConverter.GetBytes(vx), 0, vData, o0 + 0, 4);
            Array.Copy(BitConverter.GetBytes(vy), 0, vData, o0 + 4, 4);
            Array.Copy(BitConverter.GetBytes(vz), 0, vData, o0 + 8, 4);
            Array.Copy(BitConverter.GetBytes(nx), 0, vData, o0 + 12, 4);
            Array.Copy(BitConverter.GetBytes(ny), 0, vData, o0 + 16, 4);
            Array.Copy(BitConverter.GetBytes(nz), 0, vData, o0 + 20, 4);
            Array.Copy(BitConverter.GetBytes(1f), 0, vData, o0 + 24, 4);
            Array.Copy(BitConverter.GetBytes(0f), 0, vData, o0 + 28, 4);
            Array.Copy(BitConverter.GetBytes(0f), 0, vData, o0 + 32, 4);
            Array.Copy(BitConverter.GetBytes(1f), 0, vData, o0 + 36, 4);
            Array.Copy(BitConverter.GetBytes(u), 0, vData, o0 + 40, 4);
            Array.Copy(BitConverter.GetBytes(uvY), 0, vData, o0 + 44, 4);

            int o1 = stream1Off + v * stride1;
            for (int k = 0; k < 4; k++)
            {
                Array.Copy(BitConverter.GetBytes(jW[v][k].GetSingle()), 0, vData, o1 + k * 4, 4);
                Array.Copy(BitConverter.GetBytes(jI[v][k].GetInt32()), 0, vData, o1 + 16 + k * 4, 4);
            }
        }

        var allIdx = new List<ushort>();
        var subMeta = new List<(uint firstByte, uint indexCount)>();
        foreach (var sub in jSubs.EnumerateArray())
        {
            uint firstByte = (uint)(allIdx.Count * 2);
            uint count = (uint)sub.GetArrayLength();
            foreach (var elem in sub.EnumerateArray())
                allIdx.Add((ushort)elem.GetInt32());
            subMeta.Add((firstByte, count));
        }
        var idxBytes = new byte[allIdx.Count * 2];
        for (int i = 0; i < allIdx.Count; i++)
            Array.Copy(BitConverter.GetBytes(allIdx[i]), 0, idxBytes, i * 2, 2);

        var am = new AssetsManager();
        var battleBun = am.LoadBundleFile(vanBattlePath, true);
        var battleInst = am.LoadAssetsFileFromBundle(battleBun, 0, false);
        var commonBun = am.LoadBundleFile(vanCommonPath, true);
        var commonInst = am.LoadAssetsFileFromBundle(commonBun, 0, false);
        var f = battleInst.file;

        // Rename CAB archive and root GameObject to unique pm0006_01_00
        string newCab = "CAB-megacharizardx0100000000000002";
        battleBun.file.BlockAndDirInfo.DirectoryInfos[0].Name = newCab;
        foreach (var goInfo in f.GetAssetsOfType(AssetClassID.GameObject))
        {
            var goBf = am.GetBaseField(battleInst, goInfo);
            if (goBf["m_Name"].AsString == "pm0006_00_00")
            {
                goBf["m_Name"].AsString = "pm0006_01_00";
                goInfo.SetNewData(goBf);
            }
        }
        foreach (var abInfo in f.GetAssetsOfType(AssetClassID.AssetBundle))
        {
            var abBf = am.GetBaseField(battleInst, abInfo);
            abBf["m_Name"].AsString = "pokemons/battle/pm0006_01_00";
            abBf["m_AssetBundleName"].AsString = "pokemons/battle/pm0006_01_00";
            foreach (var cont in abBf["m_Container.Array"].Children)
            {
                string k = cont["first"].AsString;
                if (k.Contains("pm0006_00_00"))
                    cont["first"].AsString = k.Replace("pm0006_00_00", "pm0006_01_00");
            }
            abInfo.SetNewData(abBf);
        }

        long nextPid = f.AssetInfos.Max(a => a.PathId) + 100;

        int EnsureTypeTreeIndex(int typeId)
        {
            int idx = f.Metadata.TypeTreeTypes.FindIndex(t => t.TypeId == typeId);
            if (idx >= 0) return idx;
            var srcType = commonInst.file.Metadata.FindTypeTreeTypeByID(typeId)
                ?? throw new InvalidOperationException($"Missing TypeTreeType {typeId} in common bundle");
            f.Metadata.TypeTreeTypes.Add(srcType);
            return f.Metadata.TypeTreeTypes.Count - 1;
        }
        int texTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Texture2D);
        int meshTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Mesh);

        // Helper to clone a Texture2D template from commonInst and inject RGBA32 raw pixels
        var vanTexTemplateInfo = commonInst.file.GetAssetsOfType(AssetClassID.Texture2D).First();
        long AddRgbaTexture(string name, string rawPath, int width, int height)
        {
            var texBf = am.GetBaseField(commonInst, vanTexTemplateInfo);
            byte[] rgba = File.ReadAllBytes(rawPath);
            texBf["m_Name"].AsString = name;
            texBf["m_ForcedFallbackFormat"].AsInt = 4;
            texBf["m_Width"].AsInt = width;
            texBf["m_Height"].AsInt = height;
            texBf["m_CompleteImageSize"].AsUInt = (uint)rgba.Length;
            texBf["m_TextureFormat"].AsInt = 4; // RGBA32
            texBf["m_MipCount"].AsInt = 1;
            texBf["m_IsReadable"].AsBool = false;
            texBf["m_ImageCount"].AsInt = 1;
            texBf["m_TextureDimension"].AsInt = 2;
            texBf["m_TextureSettings.m_FilterMode"].AsInt = 1;
            texBf["m_TextureSettings.m_WrapU"].AsInt = 0; // Repeat
            texBf["m_TextureSettings.m_WrapV"].AsInt = 0; // Repeat
            texBf["m_TextureSettings.m_WrapW"].AsInt = 0;
            texBf["m_StreamData.offset"].AsULong = 0;
            texBf["m_StreamData.size"].AsUInt = 0;
            texBf["m_StreamData.path"].AsString = "";
            texBf["image data"].AsByteArray = rgba;

            long pid = nextPid++;
            var newInfo = new AssetFileInfo { PathId = pid, TypeId = (int)AssetClassID.Texture2D, TypeIdOrIndex = texTypeIdx };
            newInfo.SetNewData(texBf);
            f.Metadata.AddAssetInfo(newInfo);
            return pid;
        }

        var texMeta = JsonDocument.Parse(File.ReadAllText(Path.Combine(texDir, "textures.json"))).RootElement;
        long pidBodyACol = AddRgbaTexture("pm0006_01_00_BodyA_col", Path.Combine(texDir, "BodyA_col.raw"), texMeta.GetProperty("BodyA_col")[0].GetInt32(), texMeta.GetProperty("BodyA_col")[1].GetInt32());
        long pidBodyBCol = AddRgbaTexture("pm0006_01_00_BodyB_col", Path.Combine(texDir, "BodyB_col.raw"), texMeta.GetProperty("BodyB_col")[0].GetInt32(), texMeta.GetProperty("BodyB_col")[1].GetInt32());
        long pidBodyBEmi = AddRgbaTexture("pm0006_01_00_BodyB_emi", Path.Combine(texDir, "BodyB_emi.raw"), texMeta.GetProperty("BodyB_emi")[0].GetInt32(), texMeta.GetProperty("BodyB_emi")[1].GetInt32());
        long pidEyeCol   = AddRgbaTexture("pm0006_01_00_Eye_col",   Path.Combine(texDir, "Eye_col.raw"),   texMeta.GetProperty("Eye_col")[0].GetInt32(),   texMeta.GetProperty("Eye_col")[1].GetInt32());

        // Clone vanilla BodySkin Mesh locally into battleInst with exact vanilla 56 m_BindPose
        var smrInfo = FindPrimaryBodySmr(am, battleInst);
        var smrBf = am.GetBaseField(battleInst, smrInfo);
        var vanMeshBf = LoadVanillaBodyMesh(am, battleInst, commonInst, smrBf);

        vanMeshBf["m_Name"].AsString = "pm0006_01_00_BodySkin";
        vanMeshBf["m_IndexFormat"].AsInt = 0; // UInt16
        vanMeshBf["m_IndexBuffer.Array"].AsByteArray = idxBytes;
        vanMeshBf["m_VertexData.m_VertexCount"].AsUInt = (uint)vCount;
        vanMeshBf["m_VertexData.m_DataSize"].AsByteArray = vData;
        vanMeshBf["m_StreamData.offset"].AsULong = 0;
        vanMeshBf["m_StreamData.size"].AsUInt = 0;
        vanMeshBf["m_StreamData.path"].AsString = "";

        var chArr = vanMeshBf["m_VertexData.m_Channels.Array"].Children;
        for (int ci = 0; ci < chArr.Count; ci++)
        {
            chArr[ci]["stream"].AsByte = 0;
            chArr[ci]["offset"].AsByte = 0;
            chArr[ci]["format"].AsByte = 0;
            chArr[ci]["dimension"].AsByte = 0;
        }
        // 0:pos(s0,0,f0,d3), 1:nrm(s0,12,f0,d3), 2:tan(s0,24,f0,d4), 4:uv0(s0,40,f0,d2), 12:w(s1,0,f0,d4), 13:i(s1,16,f11,d4)
        chArr[0]["stream"].AsByte = 0; chArr[0]["offset"].AsByte = 0;  chArr[0]["format"].AsByte = 0;  chArr[0]["dimension"].AsByte = 3;
        chArr[1]["stream"].AsByte = 0; chArr[1]["offset"].AsByte = 12; chArr[1]["format"].AsByte = 0;  chArr[1]["dimension"].AsByte = 3;
        chArr[2]["stream"].AsByte = 0; chArr[2]["offset"].AsByte = 24; chArr[2]["format"].AsByte = 0;  chArr[2]["dimension"].AsByte = 4;
        chArr[4]["stream"].AsByte = 0; chArr[4]["offset"].AsByte = 40; chArr[4]["format"].AsByte = 0;  chArr[4]["dimension"].AsByte = 2;
        chArr[12]["stream"].AsByte = 1; chArr[12]["offset"].AsByte = 0;  chArr[12]["format"].AsByte = 0;  chArr[12]["dimension"].AsByte = 4;
        chArr[13]["stream"].AsByte = 1; chArr[13]["offset"].AsByte = 16; chArr[13]["format"].AsByte = 11; chArr[13]["dimension"].AsByte = 4;

        var subsArr = vanMeshBf["m_SubMeshes.Array"];
        for (int si = 0; si < Math.Min(subsArr.Children.Count, subMeta.Count); si++)
        {
            subsArr.Children[si]["firstByte"].AsUInt = subMeta[si].firstByte;
            subsArr.Children[si]["indexCount"].AsUInt = subMeta[si].indexCount;
            subsArr.Children[si]["baseVertex"].AsInt = 0;
            subsArr.Children[si]["firstVertex"].AsUInt = 0;
            subsArr.Children[si]["vertexCount"].AsUInt = (uint)vCount;
        }

        vanMeshBf["m_LocalAABB.m_Center.x"].AsFloat = 0.5f * (minX + maxX);
        vanMeshBf["m_LocalAABB.m_Center.y"].AsFloat = 0.5f * (minY + maxY);
        vanMeshBf["m_LocalAABB.m_Center.z"].AsFloat = 0.5f * (minZ + maxZ);
        vanMeshBf["m_LocalAABB.m_Extent.x"].AsFloat = 0.5f * (maxX - minX);
        vanMeshBf["m_LocalAABB.m_Extent.y"].AsFloat = 0.5f * (maxY - minY);
        vanMeshBf["m_LocalAABB.m_Extent.z"].AsFloat = 0.5f * (maxZ - minZ);

        long meshPid = nextPid++;
        var newMeshInfo = new AssetFileInfo { PathId = meshPid, TypeId = (int)AssetClassID.Mesh, TypeIdOrIndex = meshTypeIdx };
        newMeshInfo.SetNewData(vanMeshBf);
        f.Metadata.AddAssetInfo(newMeshInfo);

        smrBf["m_Mesh.m_FileID"].AsInt = 0;
        smrBf["m_Mesh.m_PathID"].AsLong = meshPid;
        smrBf["m_AABB.m_Center.x"].AsFloat = 0.5f * (minX + maxX);
        smrBf["m_AABB.m_Center.y"].AsFloat = 0.5f * (minY + maxY);
        smrBf["m_AABB.m_Center.z"].AsFloat = 0.5f * (minZ + maxZ);
        smrBf["m_AABB.m_Extent.x"].AsFloat = 0.5f * (maxX - minX);
        smrBf["m_AABB.m_Extent.y"].AsFloat = 0.5f * (maxY - minY);
        smrBf["m_AABB.m_Extent.z"].AsFloat = 0.5f * (maxZ - minZ);

        // Patch BodySkin's local (file=0) materials in-place while keeping vanilla Switch shaders (file=1)!
        var patchedMatPids = new HashSet<long>();
        foreach (var mRef in smrBf["m_Materials.Array"].Children)
        {
            long matPid = mRef["m_PathID"].AsLong;
            if (!patchedMatPids.Add(matPid)) continue;
            var matInfo = f.GetAssetInfo(matPid);
            if (matInfo == null) continue;
            var mb = am.GetBaseField(battleInst, matInfo);
            string mName = mb["m_Name"].AsString;
            mb["m_Name"].AsString = mName.Replace("pm0006_00_00", "pm0006_01_00");
            foreach (var texEntry in mb["m_SavedProperties.m_TexEnvs.Array"].Children)
            {
                string prop = texEntry["first"].AsString;
                var tex = texEntry["second.m_Texture"];
                if (mName.Contains("BodyA") && (prop == "_Col0Tex" || prop == "_BaseMap" || prop == "_MainTex"))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pidBodyACol;
                    Console.WriteLine($"  Patched {mName}.{prop} -> BodyA_col (pid={pidBodyACol})");
                }
                else if (mName.Contains("BodyB") && (prop == "_Col0Tex" || prop == "_BaseMap" || prop == "_MainTex"))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pidBodyBCol;
                    Console.WriteLine($"  Patched {mName}.{prop} -> BodyB_col (pid={pidBodyBCol})");
                }
                else if (mName.Contains("BodyB") && (prop == "_EmissionMaskTex" || prop == "_EmissionMap"))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pidBodyBEmi;
                    Console.WriteLine($"  Patched {mName}.{prop} -> BodyB_emi (pid={pidBodyBEmi})");
                }
                else if (mName.Contains("Eye") && (prop == "_Col0Tex" || prop == "_BaseMap" || prop == "_MainTex"))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pidEyeCol;
                    Console.WriteLine($"  Patched {mName}.{prop} -> Eye_col (pid={pidEyeCol})");
                }
            }
            matInfo.SetNewData(mb);
        }
        smrInfo.SetNewData(smrBf);

        // Tint vanilla FireCore & FireMask tail flame materials in-place to Electric Cyan-Blue!
        foreach (var si in f.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
        {
            if (si.PathId == smrInfo.PathId) continue;
            var sb = am.GetBaseField(battleInst, si);
            var go = am.GetBaseField(battleInst, f.GetAssetInfo(sb["m_GameObject.m_PathID"].AsLong)!);
            string sName = go["m_Name"].AsString;
            if (!sName.Contains("Fire", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var mRef in sb["m_Materials.Array"].Children)
            {
                long matPid = mRef["m_PathID"].AsLong;
                if (!patchedMatPids.Add(matPid)) continue;
                var matInfo = f.GetAssetInfo(matPid);
                if (matInfo == null) continue;
                var mb = am.GetBaseField(battleInst, matInfo);
                string mName = mb["m_Name"].AsString;
                mb["m_Name"].AsString = mName.Replace("pm0006_00_00", "pm0006_01_00");
                foreach (var col in mb["m_SavedProperties.m_Colors.Array"].Children)
                {
                    string cn = col["first"].AsString;
                    var cv = col["second"];
                    float r = cv["r"].AsFloat;
                    float g = cv["g"].AsFloat;
                    float b = cv["b"].AsFloat;
                    float lum = MathF.Max(r, MathF.Max(g, b));
                    if (r > b + 0.05f && lum > 0.05f)
                    {
                        cv["r"].AsFloat = MathF.Min(0.24f * lum, 1f);
                        cv["g"].AsFloat = MathF.Min(0.80f * lum, 1f);
                        cv["b"].AsFloat = MathF.Min(1.00f * lum, 1f);
                        Console.WriteLine($"  Tinted {mName}.{cn} -> Electric Blue ({cv["r"].AsFloat:F2},{cv["g"].AsFloat:F2},{cv["b"].AsFloat:F2})");
                    }
                }
                matInfo.SetNewData(mb);
            }
        }

        string tmp = outBundle + ".tmp";
        string tmpLz4 = outBundle + ".lz4";
        battleBun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(f);
        using (var w = new AssetsFileWriter(tmp))
            battleBun.file.Write(w);
        am.UnloadAll(true);

        var packBun = new AssetBundleFile();
        using (var r = new AssetsFileReader(tmp))
        {
            packBun.Read(r);
            using var cw = new AssetsFileWriter(tmpLz4);
            packBun.Pack(cw, AssetBundleCompressionType.LZ4);
        }
        File.Delete(tmp);
        File.Move(tmpLz4, outBundle, true);
        Console.WriteLine($"Built Mega Charizard X Switch bundle -> {outBundle} ({new FileInfo(outBundle).Length:N0} bytes, CAB={newCab}, verts={vCount}, tris={allIdx.Count / 3})");
        return 0;
    }
}
