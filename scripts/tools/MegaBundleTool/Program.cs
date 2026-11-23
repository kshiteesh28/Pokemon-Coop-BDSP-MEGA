using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace MegaBundleTool;

internal static class Program
{
    private const long VanillaSmrPathId = 2918863038459730742L;
    private const long VanillaMeshPathId = -7865952186285964533L;
    private const long CustomMeshPathId = 900001L;

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
            0 => 4,  // Float32
            1 => 2,  // Float16
            2 => 1,  // UNorm8
            3 => 1,  // SNorm8
            4 => 2,  // UNorm16
            5 => 2,  // SNorm16
            6 => 1,  // UInt8
            7 => 1,  // SInt8
            8 => 2,  // UInt16
            9 => 2,  // SInt16
            10 => 4, // UInt32
            11 => 4, // SInt32
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

    private static int RebuildLumi(string[] args)
    {
        string boneInfoPath = args.Length > 0 ? args[0] : "/tmp/mega_bone_info.json";
        if (!File.Exists(boneInfoPath))
        {
            Console.Error.WriteLine($"Missing bone info JSON: {boneInfoPath}");
            return 1;
        }

        var infoDoc = JsonDocument.Parse(File.ReadAllText(boneInfoPath));
        var boneParents = infoDoc.RootElement.GetProperty("parents").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        var vgNames = infoDoc.RootElement.GetProperty("vg_names").EnumerateArray()
            .Select(e => e.GetString()!).ToList();

        // Load vanilla BDSP pm0006_00_00 (battle + common) as the single source of truth for the 56-bone rig & bindposes!
        var vanAm = new AssetsManager();
        var vanBattleBun = vanAm.LoadBundleFile(Path.Combine(LumiRoot, "battle/pm0006_00_00"), true);
        var vanBattleInst = vanAm.LoadAssetsFileFromBundle(vanBattleBun, 0, false);
        var vanBattleFile = vanBattleInst.file;

        var vanCommonBun = vanAm.LoadBundleFile(Path.Combine(LumiRoot, "common/pm0006_00"), true);
        var vanCommonInst = vanAm.LoadAssetsFileFromBundle(vanCommonBun, 0, false);
        var vanCommonFile = vanCommonInst.file;

        var vanTransforms = ReadTransforms(vanAm, vanBattleInst);
        var vanSmr = vanAm.GetBaseField(vanBattleInst, vanBattleFile.GetAssetInfo(VanillaSmrPathId)!);
        var vanMesh = vanAm.GetBaseField(vanCommonInst, vanCommonFile.GetAssetInfo(VanillaMeshPathId)!);

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

        Console.WriteLine($"Loaded vanilla BDSP pm0006_00_00 reference: {vanBoneNames.Count} bones, {vanBindPoses.Count} exact bindPoses.");
        Console.WriteLine($"  Vanilla bind-pose key joints: Spine1={vanBindWorldPos["Spine1"]}, Head={vanBindWorldPos["Head"]}, Jaw={vanBindWorldPos["Jaw"]}, LFoot={vanBindWorldPos["LFoot"]}");

        int ResolveVanillaBoneIdx(string boneName, out string resolvedName)
        {
            string cur = boneName;
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
            resolvedName = "Spine1";
            return vanNameToIdx["Spine1"];
        }

        // Build 104 -> 56 bone index map
        var map104To56 = new int[vgNames.Count];
        int exactCount = 0, parentCount = 0;
        for (int i = 0; i < vgNames.Count; i++)
        {
            map104To56[i] = ResolveVanillaBoneIdx(vgNames[i], out string resolved);
            if (resolved == vgNames[i]) exactCount++; else parentCount++;
        }
        Console.WriteLine($"Mapped {vgNames.Count} 3DS vertex groups -> 56 vanilla BDSP bones (exact={exactCount}, parent-chain={parentCount}).");

        foreach (var formId in new[] { "pm0006_01_00", "pm0006_02_00" })
        {
            Console.WriteLine($"\n=== Rebuilding {formId} with Exact Lumi 56-Bone Skeleton & Vanilla BindPoses ===");
            string targetPath = Path.Combine(DeltaBattleDir, formId);
            string builtPath = Path.Combine(BuiltBattleDir, formId);
            string tmp = targetPath + ".tmp";
            string tmpLz4 = targetPath + ".lz4";

            var am = new AssetsManager();
            var bun = am.LoadBundleFile(targetPath, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
            var f = inst.file;

            // Load original un-mutated BuiltBundles mesh so rebuild is 100% idempotent!
            var sBun = am.LoadBundleFile(builtPath, true);
            var sInst = am.LoadAssetsFileFromBundle(sBun, 0, false);
            var sFile = sInst.file;
            var sMeshInfo = sFile.GetAssetsOfType(AssetClassID.Mesh).First();
            var sMeshBf = am.GetBaseField(sInst, sMeshInfo);

            var layout = ParseVertexLayout(sMeshBf);
            var raw = (byte[])sMeshBf["m_VertexData.m_DataSize"].AsByteArray.Clone();
            int vCount = layout.VertexCount;

            int posStream = layout.Channels[0].Stream;
            int posOff = layout.Channels[0].Offset;
            int posStride = layout.Strides[posStream];
            int posBase = layout.Offsets[posStream];

            int wStream = layout.Channels[12].Stream;
            int wOff = layout.Channels[12].Offset;
            int wStride = layout.Strides[wStream];
            int wBase = layout.Offsets[wStream];

            int iStream = layout.Channels[13].Stream;
            int iOff = layout.Channels[13].Offset;
            int iStride = layout.Strides[iStream];
            int iBase = layout.Offsets[iStream];

            // Compute anatomical joint centroids in raw BuiltBundles mesh space (using Head, Jaw, Spine1, LFoot/RFoot)!
            int vanHeadIdx = vanNameToIdx["Head"];
            int vanJawIdx = vanNameToIdx["Jaw"];
            int vanSpine1Idx = vanNameToIdx["Spine1"];
            int vanLFootIdx = vanNameToIdx["LFoot"];
            int vanRFootIdx = vanNameToIdx["RFoot"];

            Vector3 sumHead = Vector3.Zero, sumJaw = Vector3.Zero, sumSpine = Vector3.Zero, sumFeet = Vector3.Zero;
            int nHead = 0, nJaw = 0, nSpine = 0, nFeet = 0;
            float rawMinY = float.MaxValue, rawMaxY = float.MinValue;

            for (int v = 0; v < vCount; v++)
            {
                int po = posBase + v * posStride + posOff;
                var p = new Vector3(
                    BitConverter.ToSingle(raw, po),
                    BitConverter.ToSingle(raw, po + 4),
                    BitConverter.ToSingle(raw, po + 8));

                if (p.Y < rawMinY) rawMinY = p.Y;
                if (p.Y > rawMaxY) rawMaxY = p.Y;

                int wo = wBase + v * wStride + wOff;
                int io = iBase + v * iStride + iOff;
                float w0 = BitConverter.ToSingle(raw, wo);
                int origIdx0 = BitConverter.ToInt32(raw, io);
                int mapped0 = (origIdx0 >= 0 && origIdx0 < map104To56.Length) ? map104To56[origIdx0] : vanSpine1Idx;

                if (w0 > 0.4f)
                {
                    if (mapped0 == vanHeadIdx) { sumHead += p; nHead++; }
                    else if (mapped0 == vanJawIdx) { sumJaw += p; nJaw++; }
                    else if (mapped0 == vanSpine1Idx) { sumSpine += p; nSpine++; }
                    else if (mapped0 == vanLFootIdx || mapped0 == vanRFootIdx) { sumFeet += p; nFeet++; }
                }
            }

            Vector3 rawHead = nHead > 0 ? sumHead / nHead : new Vector3(0, rawMaxY * 0.75f, 0);
            Vector3 rawJaw = nJaw > 0 ? sumJaw / nJaw : rawHead;
            Vector3 rawFeet = nFeet > 0 ? sumFeet / nFeet : new Vector3(0, rawMinY, 0);
            Vector3 rawSpine = nSpine > 0 ? sumSpine / nSpine : new Vector3(0, (rawMinY + rawMaxY) * 0.33f, 0);

            // ANATOMICAL ALIGNMENT (NOT bounding-box Z center!):
            // 1. Scale so Head height above feet matches vanilla Charizard's Head height (1.318m -> ~1.35m for Mega)
            //    while capping total mesh height near vanilla Charizard (1.74m).
            float rawHeadAboveFeet = MathF.Max(rawHead.Y - rawMinY, 1e-3f);
            float rawTotalH = MathF.Max(rawMaxY - rawMinY, 1e-3f);
            float scaleByHead = vanBindWorldPos["Head"].Y / rawHeadAboveFeet;
            float scaleByHeight = 1.78f / rawTotalH;
            // Blend: prioritize matching Head/Jaw joint height so neck/head/jaw sit right on BDSP's Head & Jaw pivots!
            float s = scaleByHead;
            if (rawTotalH * s > 2.25f) s = 2.25f / rawTotalH;
            if (rawTotalH * s < 1.50f) s = 1.50f / rawTotalH;

            // 2. Align X to 0 (centered), Y so feet sit at y = 0.000,
            //    and Z so the Head/Jaw/Feet anchor directly onto BDSP's Head/Jaw Z pivot!
            //    Check rawHead.Z vs vanilla Head.Z (0.458) and rawFeet.Z vs vanilla LFoot.Z (-0.049).
            float tx = -rawHead.X * s;
            float ty = -rawMinY * s;
            float tzByHead = (vanBindWorldPos["Head"].Z + 0.06f) - rawHead.Z * s;
            float tzByFeet = vanBindWorldPos["LFoot"].Z - rawFeet.Z * s;
            // Weight Head/Jaw Z heavily (70%) + Feet Z (30%) so Jaw sits right on BDSP's Jaw hinge!
            float tz = 0.70f * tzByHead + 0.30f * tzByFeet;

            Console.WriteLine($"  Anatomical alignment: s={s:F6} (scaleByHead={scaleByHead:F6}, scaleByH={scaleByHeight:F6}), trans=({tx:F3}, {ty:F3}, {tz:F3})");
            Console.WriteLine($"  Aligned Head=({rawHead.X*s+tx:F3}, {rawHead.Y*s+ty:F3}, {rawHead.Z*s+tz:F3}) vs BDSP Head={vanBindWorldPos["Head"]}");
            Console.WriteLine($"  Aligned Jaw =({rawJaw.X*s+tx:F3}, {rawJaw.Y*s+ty:F3}, {rawJaw.Z*s+tz:F3}) vs BDSP Jaw ={vanBindWorldPos["Jaw"]}");
            Console.WriteLine($"  Aligned Feet=({rawFeet.X*s+tx:F3}, {rawFeet.Y*s+ty:F3}, {rawFeet.Z*s+tz:F3}) vs BDSP LFoot={vanBindWorldPos["LFoot"]}");

            // Bake anatomical transform into vertex positions AND remap all bone indices 104 -> 56!
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            Span<int> mergedIdx = stackalloc int[4];
            Span<float> mergedW = stackalloc float[4];
            for (int v = 0; v < vCount; v++)
            {
                int po = posBase + v * posStride + posOff;
                float vx = BitConverter.ToSingle(raw, po) * s + tx;
                float vy = BitConverter.ToSingle(raw, po + 4) * s + ty;
                float vz = BitConverter.ToSingle(raw, po + 8) * s + tz;
                Array.Copy(BitConverter.GetBytes(vx), 0, raw, po, 4);
                Array.Copy(BitConverter.GetBytes(vy), 0, raw, po + 4, 4);
                Array.Copy(BitConverter.GetBytes(vz), 0, raw, po + 8, 4);

                if (vx < minX) minX = vx; if (vx > maxX) maxX = vx;
                if (vy < minY) minY = vy; if (vy > maxY) maxY = vy;
                if (vz < minZ) minZ = vz; if (vz > maxZ) maxZ = vz;

                // Remap 4 bone influences from [0..103] -> [0..55], merging duplicates & renormalizing!
                int wo = wBase + v * wStride + wOff;
                int io = iBase + v * iStride + iOff;
                int mCount = 0;

                for (int k = 0; k < 4; k++)
                {
                    float wk = BitConverter.ToSingle(raw, wo + k * 4);
                    int ik = BitConverter.ToInt32(raw, io + k * 4);
                    if (wk <= 1e-6f) continue;
                    int target = (ik >= 0 && ik < map104To56.Length) ? map104To56[ik] : vanSpine1Idx;
                    // Guard against nearest-surface wing/arm weight bleed onto Mega X's head & mouth flames
                    if (formId == "pm0006_01_00" && vy > 1.10f && vz > 0.25f && MathF.Abs(vx) < 0.48f)
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
                    mergedIdx[0] = vanSpine1Idx;
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

            // Copy fresh mesh data from BuiltBundles into target custom mesh (900001)
            var meshInfo = f.GetAssetInfo(CustomMeshPathId)!;
            var meshBf = am.GetBaseField(inst, meshInfo);

            // Replace vertex buffer with baked BDSP-space + 56-bone remapped data
            meshBf["m_VertexData.m_DataSize"].AsByteArray = raw;

            // Update m_LocalAABB to exact baked BDSP-space bounds
            meshBf["m_LocalAABB.m_Center.x"].AsFloat = 0.5f * (minX + maxX);
            meshBf["m_LocalAABB.m_Center.y"].AsFloat = 0.5f * (minY + maxY);
            meshBf["m_LocalAABB.m_Center.z"].AsFloat = 0.5f * (minZ + maxZ);
            meshBf["m_LocalAABB.m_Extent.x"].AsFloat = 0.5f * (maxX - minX);
            meshBf["m_LocalAABB.m_Extent.y"].AsFloat = 0.5f * (maxY - minY);
            meshBf["m_LocalAABB.m_Extent.z"].AsFloat = 0.5f * (maxZ - minZ);

            // Disable Mega Y's untextured tail-flame stencil shells (submeshes 3 & 4)
            if (formId == "pm0006_02_00")
            {
                var subs = meshBf["m_SubMeshes.Array"].Children;
                for (int si = 3; si < subs.Count; si++)
                    subs[si]["indexCount"].AsUInt = 0;
            }

            // Set m_BindPose to EXACT vanilla BDSP pm0006_00_00 56 bindposes (verbatim Melonscript parity!)
            var bpArr = meshBf["m_BindPose.Array"];
            bpArr.Children.Clear();
            for (int i = 0; i < vanBindPoses.Count; i++)
            {
                var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(bpArr);
                WriteUnityMat(elem, vanBindPoses[i]);
                bpArr.Children.Add(elem);
            }

            // Set SMR m_Bones to EXACT vanilla BDSP pm0006_00_00 56 bones
            var smrInfo = f.GetAssetInfo(VanillaSmrPathId)!;
            var smrBf = am.GetBaseField(inst, smrInfo);
            var bonesArr = smrBf["m_Bones.Array"];
            bonesArr.Children.Clear();
            for (int i = 0; i < vanBonePids.Count; i++)
            {
                var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(bonesArr);
                elem["m_FileID"].AsInt = 0;
                elem["m_PathID"].AsLong = vanBonePids[i];
                bonesArr.Children.Add(elem);
            }

            // Update SMR m_AABB too so Unity never frustum-culls the model!
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

            Console.WriteLine($"  SAVED {formId}: size={new FileInfo(targetPath).Length:N0}, bones=56, bindPoses=56, bounds=X[{minX:F2},{maxX:F2}] Y[{minY:F2},{maxY:F2}] Z[{minZ:F2},{maxZ:F2}]");
        }

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
        Directory.CreateDirectory(outDir);

        var am = new AssetsManager();

        // Load vanilla pm0006_00_00 reference for invariant comparison
        var vanBattleBun = am.LoadBundleFile(Path.Combine(LumiRoot, "battle/pm0006_00_00"), true);
        var vanBattleInst = am.LoadAssetsFileFromBundle(vanBattleBun, 0, false);
        var vanCommonBun = am.LoadBundleFile(Path.Combine(LumiRoot, "common/pm0006_00"), true);
        var vanCommonInst = am.LoadAssetsFileFromBundle(vanCommonBun, 0, false);
        var vanMeshBf = am.GetBaseField(vanCommonInst, vanCommonInst.file.GetAssetInfo(VanillaMeshPathId)!);
        var vanBindPoses = vanMeshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();

        var bun = am.LoadBundleFile(bundlePath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
        var f = inst.file;

        var failures = new List<string>();
        void Assert(bool cond, string msg)
        {
            if (!cond) failures.Add(msg);
            Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {msg}");
        }

        Console.WriteLine($"\n=== Verifying {Path.GetFileName(bundlePath)} ===");
        Assert(f.Metadata.TargetPlatform == 38, $"TargetPlatform == 38 (Switch), actual={f.Metadata.TargetPlatform}");
        Assert(f.GetAssetsOfType(AssetClassID.Shader).Count == 0, $"Embedded Shaders == 0, actual={f.GetAssetsOfType(AssetClassID.Shader).Count}");
        Assert(f.Metadata.Externals.Count == 2, $"Externals == 2, actual={f.Metadata.Externals.Count}");

        var mbSizes = f.GetAssetsOfType(AssetClassID.MonoBehaviour).Select(m => (int)m.ByteSize).OrderByDescending(x => x).ToArray();
        Assert(mbSizes.SequenceEqual(new[] { 2204, 800, 64 }), $"Vanilla MonoBehaviour sizes [2204, 800, 64], actual=[{string.Join(", ", mbSizes)}]");

        var tData = ReadTransforms(am, inst);
        var smrBf = am.GetBaseField(inst, f.GetAssetInfo(VanillaSmrPathId)!);
        var meshBf = am.GetBaseField(inst, f.GetAssetInfo(CustomMeshPathId)!);

        var bonePids = smrBf["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var bindPoses = meshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        Assert(bonePids.Count == 56, $"SMR m_Bones count == 56 (vanilla BDSP parity), actual={bonePids.Count}");
        Assert(bindPoses.Count == 56, $"Mesh m_BindPose count == 56 (vanilla BDSP parity), actual={bindPoses.Count}");

        float maxBpErr = 0f;
        for (int i = 0; i < Math.Min(bindPoses.Count, vanBindPoses.Count); i++)
        {
            var a = bindPoses[i]; var b = vanBindPoses[i];
            float d = MathF.Abs(a.M11 - b.M11) + MathF.Abs(a.M22 - b.M22) + MathF.Abs(a.M33 - b.M33)
                    + MathF.Abs(a.M41 - b.M41) + MathF.Abs(a.M42 - b.M42) + MathF.Abs(a.M43 - b.M43);
            if (d > maxBpErr) maxBpErr = d;
        }
        Assert(maxBpErr < 1e-4f, $"m_BindPose exact parity with vanilla pm0006_00_00 (maxErr={maxBpErr:E2})");

        // Parse mesh geometry + skinning
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

            if ((w0 > 0 && (i0 < 0 || i0 >= 56)) || (w1 > 0 && (i1 < 0 || i1 >= 56)) ||
                (w2 > 0 && (i2 < 0 || i2 >= 56)) || (w3 > 0 && (i3 < 0 || i3 >= 56)))
                anyOutOfRangeBone = true;
            if (MathF.Abs((w0 + w1 + w2 + w3) - 1f) > 0.02f)
                anyBadWeightSum = true;
        }
        Assert(!anyNan, "Zero NaN vertices");
        Assert(!anyOutOfRangeBone, "All active vertex bone indices within [0..55]");
        Assert(!anyBadWeightSum, "All vertex bone weight sums == 1.0");

        // Parse active triangles from submeshes with indexCount > 0
        var idxBuf = meshBf["m_IndexBuffer.Array"].AsByteArray;
        int idxFmt = meshBf["m_IndexFormat"].AsInt; // 0=UInt16, 1=UInt32
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

        // Evaluate 2 poses via exact Unity LBS:
        // Pose A: Prefab Battle Idle pose (including BDSP's exact 16.68-deg open Jaw!)
        // Pose B: Synthetic Roar / Wing-Flap stress pose (Waist +12 deg pitch, Jaw +20 deg open, LFeeler1/RFeeler1 +/-25 deg flap)
        long pidWaist = tData.First(kv => kv.Value.Name == "Waist").Key;
        long pidJaw = tData.First(kv => kv.Value.Name == "Jaw").Key;
        long pidLWing = tData.First(kv => kv.Value.Name == "LFeeler1").Key;
        long pidRWing = tData.First(kv => kv.Value.Name == "RFeeler1").Key;

        var roarOverrides = new Dictionary<long, Quaternion>
        {
            [pidWaist] = tData[pidWaist].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.20f),
            [pidJaw] = tData[pidJaw].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.30f),
            [pidLWing] = tData[pidLWing].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
            [pidRWing] = tData[pidRWing].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.35f),
        };

        void EvalAndDumpPose(string poseName, Dictionary<long, Quaternion>? overrides, bool checkAnat)
        {
            var skinMats = new Matrix4x4[56];
            for (int i = 0; i < 56; i++)
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
                    if (w.i0 == 5) { sumHead += p; nHead++; }
                    else if (w.i0 == 6) { sumJaw += p; nJaw++; }
                    else if (w.i0 == 7) { sumTongue += p; nTongue++; }
                }
            }

            if (checkAnat)
            {
                Vector3 cHead = nHead > 0 ? sumHead / nHead : Vector3.Zero;
                Vector3 cJaw = nJaw > 0 ? sumJaw / nJaw : Vector3.Zero;
                Vector3 cTongue = nTongue > 0 ? sumTongue / nTongue : Vector3.Zero;
                Assert(minY >= -0.15f && minY <= 0.25f, $"{poseName}: Feet grounded (minY={minY:F3} in [-0.15, 0.25])");
                Assert(maxY >= 1.40f && maxY <= 2.35f, $"{poseName}: Proportionate height (maxY={maxY:F3} in [1.40, 2.35])");
                if (nJaw > 0 && nHead > 0)
                    Assert(cJaw.Y < cHead.Y, $"{poseName}: Jaw BELOW Head (Jaw.Y={cJaw.Y:F3} < Head.Y={cHead.Y:F3})");
                if (nTongue > 0 && nHead > 0)
                    Assert(cTongue.Y < cHead.Y, $"{poseName}: Tongue BELOW Head (Tongue.Y={cTongue.Y:F3} < Head.Y={cHead.Y:F3})");
            }

            // Write OBJ in Unity-to-Blender Y-up->Z-up conversion: (x, y, z) -> (x, -z, y)
            string objPath = Path.Combine(outDir, $"{Path.GetFileName(bundlePath)}_{poseName}.obj");
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
