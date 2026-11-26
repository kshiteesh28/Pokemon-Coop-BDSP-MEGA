using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace MegaBundleTool;

internal static partial class Program
{
    private static uint ComputeUnityCrc32(string s)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : (crc >> 1);
        }
        return ~crc;
    }

    private static string ResolveRoleFromVanillaClipName(string? clipName, int slotIndex)
    {
        if (!string.IsNullOrEmpty(clipName))
        {
            string nl = clipName.ToLowerInvariant();
            if (nl.Contains("waita")) return "waitA01";
            if (nl.Contains("waitb")) return "waitB01";
            if (nl.Contains("land")) return "landA01";
            if (nl.Contains("roar")) return "roar01";
            if (nl.Contains("buturi")) return "buturi01";
            if (nl.Contains("tokusyu")) return "tokusyu01";
            if (nl.Contains("damage")) return "damageS01";
            if (nl.Contains("down")) return "down01";
            if (nl.Contains("walk")) return "walk01";
            if (nl.Contains("happy")) return "roar01";
            if (nl.Contains("hate")) return "roar01";
            if (nl.Contains("eat")) return "waitB01";
            if (nl.Contains("kw01_wait")) return "waitA01";
        }
        return slotIndex switch
        {
            0 => "waitA01",
            1 => "waitB01",
            2 or 3 or 4 => "landA01",
            5 or 6 or 7 or 12 or 13 or 14 or 15 or 16 => "buturi01",
            8 or 9 or 10 or 11 or 17 or 18 or 19 or 20 or 21 => "tokusyu01",
            24 => "damageS01",
            25 => "down01",
            26 => "roar01",
            27 => "walk01",
            28 or 29 or 31 or 32 => "roar01",
            30 or 33 => "waitB01",
            _ => "waitA01"
        };
    }

    /// <summary>
    /// Builds a Nintendo Switch BDSP battle bundle with the Mega's NATIVE Legends: Z-A TRSKL skeleton
    /// and converted TRANM -> Unity m_DenseClip AnimationClips embedded locally (m_FileID = 0),
    /// preserving BDSP's BattlePokemonEntity, Animator, shaders, and _locators intact.
    /// Args: <species> <formId> <cabName> <meshJson> <rigAnimJson> <texDir> <outBundle>
    /// </summary>
    private static int BuildNativeZaBundle(string[] args)
    {
        if (args.Length < 7)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool build-native-za-bundle <species> <formId> <cabName> <meshJson> <rigAnimJson> <texDir> <outBundle>");
            return 2;
        }
        string species = args[0];
        string formId = args[1];
        string newCab = args[2];
        string meshJsonPath = args[3];
        string rigAnimJsonPath = args[4];
        string texDir = args[5];
        string outBundle = args[6];

        var (vanBattlePath, vanCommonPath) = ResolveDonorPaths(species);

        // 1. Parse mesh JSON
        var meshDoc = JsonDocument.Parse(File.ReadAllText(meshJsonPath)).RootElement;
        var jVerts = meshDoc.GetProperty("verts");
        var jNorms = meshDoc.GetProperty("normals");
        var jUvs = meshDoc.GetProperty("uvs");
        var jW = meshDoc.GetProperty("weights");
        var jI = meshDoc.GetProperty("indices");
        var jSubs = meshDoc.GetProperty("submeshes");
        int vCount = jVerts.GetArrayLength();

        const int stride0 = 48;
        const int stride1 = 32;
        int stream1Off = Align16(vCount * stride0);
        var vData = new byte[stream1Off + vCount * stride1];

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int v = 0; v < vCount; v++)
        {
            float vx = jVerts[v][0].GetSingle(), vy = jVerts[v][1].GetSingle(), vz = jVerts[v][2].GetSingle();
            float nx = jNorms[v][0].GetSingle(), ny = jNorms[v][1].GetSingle(), nz = jNorms[v][2].GetSingle();
            float u = jUvs[v][0].GetSingle(), uvY = jUvs[v][1].GetSingle();
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

        // Support UInt16 when vCount <= 65535 (all Pokemon are <25k), UInt32 fallback otherwise
        bool use32Idx = vCount > 65535;
        var subMeta = new List<(uint firstByte, uint indexCount)>();
        byte[] idxBytes;
        int totalIndices = 0;
        if (!use32Idx)
        {
            var allIdx = new List<ushort>();
            foreach (var sub in jSubs.EnumerateArray())
            {
                uint firstByte = (uint)(allIdx.Count * 2);
                uint count = (uint)sub.GetArrayLength();
                foreach (var elem in sub.EnumerateArray())
                    allIdx.Add((ushort)elem.GetInt32());
                subMeta.Add((firstByte, count));
            }
            totalIndices = allIdx.Count;
            idxBytes = new byte[allIdx.Count * 2];
            for (int i = 0; i < allIdx.Count; i++)
                Array.Copy(BitConverter.GetBytes(allIdx[i]), 0, idxBytes, i * 2, 2);
        }
        else
        {
            var allIdx = new List<uint>();
            foreach (var sub in jSubs.EnumerateArray())
            {
                uint firstByte = (uint)(allIdx.Count * 4);
                uint count = (uint)sub.GetArrayLength();
                foreach (var elem in sub.EnumerateArray())
                    allIdx.Add((uint)elem.GetInt32());
                subMeta.Add((firstByte, count));
            }
            totalIndices = allIdx.Count;
            idxBytes = new byte[allIdx.Count * 4];
            for (int i = 0; i < allIdx.Count; i++)
                Array.Copy(BitConverter.GetBytes(allIdx[i]), 0, idxBytes, i * 4, 4);
        }

        // 2. Parse rig_anim JSON + raw float32 samples blob
        var rigDoc = JsonDocument.Parse(File.ReadAllText(rigAnimJsonPath)).RootElement;
        float rigScale = rigDoc.GetProperty("rig_scale").GetSingle();
        var jRigOff = rigDoc.GetProperty("rig_offset");
        var rigOffset = new Vector3(jRigOff[0].GetSingle(), jRigOff[1].GetSingle(), jRigOff[2].GetSingle());
        var jNodes = rigDoc.GetProperty("nodes");
        int nodeCount = jNodes.GetArrayLength();
        var rigNodeList = rigDoc.GetProperty("rig_node_list").EnumerateArray().Select(e => e.GetInt32()).ToList();
        string samplesPath = Path.Combine(Path.GetDirectoryName(rigAnimJsonPath)!, rigDoc.GetProperty("samples_file").GetString()!);
        byte[] samplesBlob = File.ReadAllBytes(samplesPath);

        // 3. Load vanilla donor battle & common bundles
        var am = new AssetsManager();
        var battleBun = am.LoadBundleFile(vanBattlePath, true);
        var battleInst = am.LoadAssetsFileFromBundle(battleBun, 0, false);
        AssetsFileInstance? commonInst = null;
        if (vanCommonPath != null)
        {
            var commonBun = am.LoadBundleFile(vanCommonPath, true);
            commonInst = am.LoadAssetsFileFromBundle(commonBun, 0, false);
        }
        var templateCommonBun = am.LoadBundleFile(GetTemplateCommonPath(), true);
        var templateCommonInst = am.LoadAssetsFileFromBundle(templateCommonBun, 0, false);
        var typeSrcInst = commonInst ?? templateCommonInst;
        var f = battleInst.file;

        // Build map of vanilla common AnimationClip PathID -> Name for exact slot remapping
        var commonClipNames = new Dictionary<long, string>();
        if (commonInst != null)
        {
            foreach (var ci in commonInst.file.GetAssetsOfType(AssetClassID.AnimationClip))
                commonClipNames[ci.PathId] = am.GetBaseField(commonInst, ci)["m_Name"].AsString;
        }

        string baseFormId = $"{species}_00_00";
        string lumiAltFormId = $"{species}_11_00";
        battleBun.file.BlockAndDirInfo.DirectoryInfos[0].Name = newCab;

        long rootGoPid = 0;
        long rootTransPid = 0;
        foreach (var goInfo in f.GetAssetsOfType(AssetClassID.GameObject))
        {
            var goBf = am.GetBaseField(battleInst, goInfo);
            string goName = goBf["m_Name"].AsString;
            if (goName == baseFormId || goName == lumiAltFormId)
            {
                goBf["m_Name"].AsString = formId;
                goInfo.SetNewData(goBf);
                rootGoPid = goInfo.PathId;
            }
        }
        foreach (var ti in f.GetAssetsOfType(AssetClassID.Transform))
        {
            var tb = am.GetBaseField(battleInst, ti);
            if (tb["m_GameObject.m_PathID"].AsLong == rootGoPid || tb["m_Father.m_PathID"].AsLong == 0)
            {
                rootTransPid = ti.PathId;
                if (rootGoPid == 0) rootGoPid = tb["m_GameObject.m_PathID"].AsLong;
                break;
            }
        }
        if (rootTransPid == 0)
            throw new InvalidOperationException($"Could not locate root Transform in {vanBattlePath}");

        foreach (var abInfo in f.GetAssetsOfType(AssetClassID.AssetBundle))
        {
            var abBf = am.GetBaseField(battleInst, abInfo);
            abBf["m_Name"].AsString = $"pokemons/battle/{formId}";
            abBf["m_AssetBundleName"].AsString = $"pokemons/battle/{formId}";
            foreach (var cont in abBf["m_Container.Array"].Children)
            {
                string k = cont["first"].AsString;
                if (k.Contains(baseFormId))
                    cont["first"].AsString = k.Replace(baseFormId, formId);
                else if (k.Contains(lumiAltFormId))
                    cont["first"].AsString = k.Replace(lumiAltFormId, formId);
            }
            abInfo.SetNewData(abBf);
        }

        long nextPid = f.AssetInfos.Max(a => a.PathId) + 100;
        int EnsureTypeTreeIndex(int typeId)
        {
            int idx = f.Metadata.TypeTreeTypes.FindIndex(t => t.TypeId == typeId);
            if (idx >= 0) return idx;
            var srcType = typeSrcInst.file.Metadata.FindTypeTreeTypeByID(typeId)
                ?? templateCommonInst.file.Metadata.FindTypeTreeTypeByID(typeId)
                ?? throw new InvalidOperationException($"Missing TypeTreeType {typeId}");
            f.Metadata.TypeTreeTypes.Add(srcType);
            return f.Metadata.TypeTreeTypes.Count - 1;
        }
        int goTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.GameObject);
        int transTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Transform);
        int texTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Texture2D);
        int meshTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Mesh);
        int matTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.Material);
        int clipTypeIdx = EnsureTypeTreeIndex((int)AssetClassID.AnimationClip);

        // 4. Spawn ZA_Rig + Native TRSKL GameObject/Transform hierarchy under rootTransPid!
        var templateGoInfo = f.GetAssetsOfType(AssetClassID.GameObject).First();
        var templateTransInfo = f.GetAssetsOfType(AssetClassID.Transform).First();

        long zaRigGoPid = nextPid++;
        long zaRigTransPid = nextPid++;

        var nodeGoPids = new long[nodeCount];
        var nodeTransPids = new long[nodeCount];
        var nodeChildren = new List<int>[nodeCount];
        var rootChildrenIdx = new List<int>();
        for (int i = 0; i < nodeCount; i++)
        {
            nodeGoPids[i] = nextPid++;
            nodeTransPids[i] = nextPid++;
            nodeChildren[i] = new List<int>();
        }
        for (int i = 0; i < nodeCount; i++)
        {
            int p = jNodes[i].GetProperty("parent").GetInt32();
            if (p >= 0 && p < nodeCount)
                nodeChildren[p].Add(i);
            else
                rootChildrenIdx.Add(i);
        }

        void AddBoneNode(long goPid, long transPid, string name, long parentTransPid, List<long> childTransPids, Vector3 pos, Quaternion rot, Vector3 scl, int rootOrder)
        {
            var goBf = am.GetBaseField(battleInst, templateGoInfo);
            goBf["m_Name"].AsString = name;
            goBf["m_Layer"].AsInt = 0;
            goBf["m_Tag"].AsUShort = 0;
            goBf["m_IsActive"].AsBool = true;
            var comps = goBf["m_Component.Array"];
            comps.Children.Clear();
            var cElem = ValueBuilder.DefaultValueFieldFromArrayTemplate(comps);
            cElem["component.m_FileID"].AsInt = 0;
            cElem["component.m_PathID"].AsLong = transPid;
            comps.Children.Add(cElem);
            var goInfo = new AssetFileInfo { PathId = goPid, TypeId = (int)AssetClassID.GameObject, TypeIdOrIndex = goTypeIdx };
            goInfo.SetNewData(goBf);
            f.Metadata.AddAssetInfo(goInfo);

            var tb = am.GetBaseField(battleInst, templateTransInfo);
            tb["m_GameObject.m_FileID"].AsInt = 0;
            tb["m_GameObject.m_PathID"].AsLong = goPid;
            tb["m_LocalPosition.x"].AsFloat = pos.X;
            tb["m_LocalPosition.y"].AsFloat = pos.Y;
            tb["m_LocalPosition.z"].AsFloat = pos.Z;
            tb["m_LocalRotation.x"].AsFloat = rot.X;
            tb["m_LocalRotation.y"].AsFloat = rot.Y;
            tb["m_LocalRotation.z"].AsFloat = rot.Z;
            tb["m_LocalRotation.w"].AsFloat = rot.W;
            tb["m_LocalScale.x"].AsFloat = scl.X;
            tb["m_LocalScale.y"].AsFloat = scl.Y;
            tb["m_LocalScale.z"].AsFloat = scl.Z;
            tb["m_Father.m_FileID"].AsInt = 0;
            tb["m_Father.m_PathID"].AsLong = parentTransPid;
            if (!tb["m_RootOrder"].IsDummy) tb["m_RootOrder"].AsInt = rootOrder;
            var chArr = tb["m_Children.Array"];
            chArr.Children.Clear();
            foreach (long cp in childTransPids)
            {
                var ce = ValueBuilder.DefaultValueFieldFromArrayTemplate(chArr);
                ce["m_FileID"].AsInt = 0;
                ce["m_PathID"].AsLong = cp;
                chArr.Children.Add(ce);
            }
            var tInfo = new AssetFileInfo { PathId = transPid, TypeId = (int)AssetClassID.Transform, TypeIdOrIndex = transTypeIdx };
            tInfo.SetNewData(tb);
            f.Metadata.AddAssetInfo(tInfo);
        }

        // Attach ZA_Rig to root Transform
        var rootTransInfo = f.GetAssetInfo(rootTransPid)!;
        var rootTransBf = am.GetBaseField(battleInst, rootTransInfo);
        var rootChildrenArr = rootTransBf["m_Children.Array"];
        int zaRigRootOrder = rootChildrenArr.Children.Count;
        var zaRigChildRef = ValueBuilder.DefaultValueFieldFromArrayTemplate(rootChildrenArr);
        zaRigChildRef["m_FileID"].AsInt = 0;
        zaRigChildRef["m_PathID"].AsLong = zaRigTransPid;
        rootChildrenArr.Children.Add(zaRigChildRef);
        rootTransInfo.SetNewData(rootTransBf);

        AddBoneNode(
            zaRigGoPid, zaRigTransPid, "ZA_Rig", rootTransPid,
            rootChildrenIdx.Select(i => nodeTransPids[i]).ToList(),
            rigOffset, Quaternion.Identity, new Vector3(rigScale, rigScale, rigScale), zaRigRootOrder);

        // Track TransformRecs for exact rest-pose world matrix & m_BindPose computation
        var zaTData = new Dictionary<long, TransformRec>
        {
            [rootTransPid] = new TransformRec(formId, 0, Vector3.Zero, Quaternion.Identity, Vector3.One),
            [zaRigTransPid] = new TransformRec("ZA_Rig", rootTransPid, rigOffset, Quaternion.Identity, new Vector3(rigScale, rigScale, rigScale)),
        };

        long waistTransPid = zaRigTransPid;
        for (int i = 0; i < nodeCount; i++)
        {
            var jn = jNodes[i];
            string uname = jn.GetProperty("unique_name").GetString()!;
            int p = jn.GetProperty("parent").GetInt32();
            long parentPid = (p >= 0 && p < nodeCount) ? nodeTransPids[p] : zaRigTransPid;
            var jp = jn.GetProperty("pos");
            var jr = jn.GetProperty("rot");
            var js = jn.GetProperty("scl");
            var pos = new Vector3(jp[0].GetSingle(), jp[1].GetSingle(), jp[2].GetSingle());
            var rot = new Quaternion(jr[0].GetSingle(), jr[1].GetSingle(), jr[2].GetSingle(), jr[3].GetSingle());
            var scl = new Vector3(js[0].GetSingle(), js[1].GetSingle(), js[2].GetSingle());
            int order = (p >= 0 && p < nodeCount) ? nodeChildren[p].IndexOf(i) : rootChildrenIdx.IndexOf(i);

            AddBoneNode(
                nodeGoPids[i], nodeTransPids[i], uname, parentPid,
                nodeChildren[i].Select(c => nodeTransPids[c]).ToList(),
                pos, rot, scl, order);
            zaTData[nodeTransPids[i]] = new TransformRec(uname, parentPid, pos, rot, scl);
            if (uname.Equals("waist", StringComparison.OrdinalIgnoreCase))
                waistTransPid = nodeTransPids[i];
        }

        // Compute exact m_BindPose[bi] = Inverse(worldMat(ZA_Bone[rigNodeList[bi]]))
        var nativeBonePids = new List<long>(rigNodeList.Count);
        var nativeBindPoses = new List<Matrix4x4>(rigNodeList.Count);
        for (int bi = 0; bi < rigNodeList.Count; bi++)
        {
            int nidx = Math.Clamp(rigNodeList[bi], 0, nodeCount - 1);
            long bPid = nodeTransPids[nidx];
            nativeBonePids.Add(bPid);
            var w = GetWorld(zaTData, bPid);
            if (!Matrix4x4.Invert(w, out var invW))
                invW = Matrix4x4.Identity;
            nativeBindPoses.Add(invW);
        }

        // 5. Inject RGBA32 Textures (sRGB for _col, Linear for _emi)
        var vanTexTemplateInfo = typeSrcInst.file.GetAssetsOfType(AssetClassID.Texture2D).FirstOrDefault()
            ?? templateCommonInst.file.GetAssetsOfType(AssetClassID.Texture2D).First();
        var texTemplateInst = typeSrcInst.file.GetAssetsOfType(AssetClassID.Texture2D).Count > 0 ? typeSrcInst : templateCommonInst;
        var texPids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var texMeta = JsonDocument.Parse(File.ReadAllText(Path.Combine(texDir, "textures.json"))).RootElement;
        foreach (var prop in texMeta.EnumerateObject())
        {
            string stem = prop.Name;
            int w = prop.Value[0].GetInt32(), h = prop.Value[1].GetInt32();
            byte[] rgba = File.ReadAllBytes(Path.Combine(texDir, $"{stem}.raw"));
            var texBf = am.GetBaseField(texTemplateInst, vanTexTemplateInfo);
            texBf["m_Name"].AsString = $"{formId}_{stem}";
            texBf["m_ForcedFallbackFormat"].AsInt = 4;
            texBf["m_Width"].AsInt = w;
            texBf["m_Height"].AsInt = h;
            texBf["m_CompleteImageSize"].AsUInt = (uint)rgba.Length;
            texBf["m_TextureFormat"].AsInt = 4;
            texBf["m_MipCount"].AsInt = 1;
            texBf["m_IsReadable"].AsBool = false;
            texBf["m_ImageCount"].AsInt = 1;
            texBf["m_TextureDimension"].AsInt = 2;
            texBf["m_ColorSpace"].AsInt = stem.EndsWith("_emi", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            texBf["m_TextureSettings.m_FilterMode"].AsInt = 1;
            texBf["m_TextureSettings.m_WrapU"].AsInt = 2;
            texBf["m_TextureSettings.m_WrapV"].AsInt = 0;
            texBf["m_TextureSettings.m_WrapW"].AsInt = 0;
            texBf["m_StreamData.offset"].AsULong = 0;
            texBf["m_StreamData.size"].AsUInt = 0;
            texBf["m_StreamData.path"].AsString = "";
            texBf["image data"].AsByteArray = rgba;
            long pid = nextPid++;
            var newInfo = new AssetFileInfo { PathId = pid, TypeId = (int)AssetClassID.Texture2D, TypeIdOrIndex = texTypeIdx };
            newInfo.SetNewData(texBf);
            f.Metadata.AddAssetInfo(newInfo);
            texPids[stem] = pid;
        }

        // 6. Build native-rigged Mesh + bind to primary BodySkin SkinnedMeshRenderer
        var smrInfo = FindPrimaryBodySmr(am, battleInst);
        var smrBf = am.GetBaseField(battleInst, smrInfo);
        var loadedMeshBf = TryLoadVanillaBodyMesh(am, battleInst, commonInst, smrBf);
        var vanMeshBf = loadedMeshBf ?? am.GetBaseField(templateCommonInst, templateCommonInst.file.GetAssetsOfType(AssetClassID.Mesh).First());

        vanMeshBf["m_Name"].AsString = $"{formId}_BodySkin";
        vanMeshBf["m_IndexFormat"].AsInt = use32Idx ? 1 : 0;
        vanMeshBf["m_IndexBuffer.Array"].AsByteArray = idxBytes;
        vanMeshBf["m_VertexData.m_VertexCount"].AsUInt = (uint)vCount;
        vanMeshBf["m_VertexData.m_DataSize"].AsByteArray = vData;
        vanMeshBf["m_StreamData.offset"].AsULong = 0;
        vanMeshBf["m_StreamData.size"].AsUInt = 0;
        vanMeshBf["m_StreamData.path"].AsString = "";

        var bpArr = vanMeshBf["m_BindPose.Array"];
        bpArr.Children.Clear();
        for (int bi = 0; bi < nativeBindPoses.Count; bi++)
        {
            var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(bpArr);
            WriteUnityMat(elem, nativeBindPoses[bi]);
            bpArr.Children.Add(elem);
        }

        var vChArr = vanMeshBf["m_VertexData.m_Channels.Array"].Children;
        for (int ci = 0; ci < vChArr.Count; ci++)
        {
            vChArr[ci]["stream"].AsByte = 0; vChArr[ci]["offset"].AsByte = 0;
            vChArr[ci]["format"].AsByte = 0; vChArr[ci]["dimension"].AsByte = 0;
        }
        vChArr[0]["stream"].AsByte = 0; vChArr[0]["offset"].AsByte = 0;  vChArr[0]["format"].AsByte = 0;  vChArr[0]["dimension"].AsByte = 3;
        vChArr[1]["stream"].AsByte = 0; vChArr[1]["offset"].AsByte = 12; vChArr[1]["format"].AsByte = 0;  vChArr[1]["dimension"].AsByte = 3;
        vChArr[2]["stream"].AsByte = 0; vChArr[2]["offset"].AsByte = 24; vChArr[2]["format"].AsByte = 0;  vChArr[2]["dimension"].AsByte = 4;
        vChArr[4]["stream"].AsByte = 0; vChArr[4]["offset"].AsByte = 40; vChArr[4]["format"].AsByte = 0;  vChArr[4]["dimension"].AsByte = 2;
        vChArr[12]["stream"].AsByte = 1; vChArr[12]["offset"].AsByte = 0;  vChArr[12]["format"].AsByte = 0;  vChArr[12]["dimension"].AsByte = 4;
        vChArr[13]["stream"].AsByte = 1; vChArr[13]["offset"].AsByte = 16; vChArr[13]["format"].AsByte = 11; vChArr[13]["dimension"].AsByte = 4;

        var subsArr = vanMeshBf["m_SubMeshes.Array"];
        while (subsArr.Children.Count < subMeta.Count)
            subsArr.Children.Add(ValueBuilder.DefaultValueFieldFromArrayTemplate(subsArr));
        while (subsArr.Children.Count > subMeta.Count)
            subsArr.Children.RemoveAt(subsArr.Children.Count - 1);
        for (int si = 0; si < subMeta.Count; si++)
        {
            subsArr.Children[si]["firstByte"].AsUInt = subMeta[si].firstByte;
            subsArr.Children[si]["indexCount"].AsUInt = subMeta[si].indexCount;
            subsArr.Children[si]["topology"].AsInt = 0;
            subsArr.Children[si]["baseVertex"].AsInt = 0;
            subsArr.Children[si]["firstVertex"].AsUInt = 0;
            subsArr.Children[si]["vertexCount"].AsUInt = (uint)vCount;
        }

        // Generous AABB (1.5x extent) + m_UpdateWhenOffscreen = true so attack lunges never cull
        float cx = 0.5f * (minX + maxX), cy = 0.5f * (minY + maxY), cz = 0.5f * (minZ + maxZ);
        float ex = MathF.Max(0.75f * (maxX - minX), 0.5f);
        float ey = MathF.Max(0.75f * (maxY - minY), 0.5f);
        float ez = MathF.Max(0.75f * (maxZ - minZ), 0.5f);
        vanMeshBf["m_LocalAABB.m_Center.x"].AsFloat = cx;
        vanMeshBf["m_LocalAABB.m_Center.y"].AsFloat = cy;
        vanMeshBf["m_LocalAABB.m_Center.z"].AsFloat = cz;
        vanMeshBf["m_LocalAABB.m_Extent.x"].AsFloat = ex;
        vanMeshBf["m_LocalAABB.m_Extent.y"].AsFloat = ey;
        vanMeshBf["m_LocalAABB.m_Extent.z"].AsFloat = ez;

        long meshPid = nextPid++;
        var newMeshInfo = new AssetFileInfo { PathId = meshPid, TypeId = (int)AssetClassID.Mesh, TypeIdOrIndex = meshTypeIdx };
        newMeshInfo.SetNewData(vanMeshBf);
        f.Metadata.AddAssetInfo(newMeshInfo);

        smrBf["m_Mesh.m_FileID"].AsInt = 0;
        smrBf["m_Mesh.m_PathID"].AsLong = meshPid;
        smrBf["m_RootBone.m_FileID"].AsInt = 0;
        smrBf["m_RootBone.m_PathID"].AsLong = waistTransPid;
        if (!smrBf["m_UpdateWhenOffscreen"].IsDummy)
            smrBf["m_UpdateWhenOffscreen"].AsBool = true;
        smrBf["m_AABB.m_Center.x"].AsFloat = cx;
        smrBf["m_AABB.m_Center.y"].AsFloat = cy;
        smrBf["m_AABB.m_Center.z"].AsFloat = cz;
        smrBf["m_AABB.m_Extent.x"].AsFloat = ex;
        smrBf["m_AABB.m_Extent.y"].AsFloat = ey;
        smrBf["m_AABB.m_Extent.z"].AsFloat = ez;

        var smrBonesArr = smrBf["m_Bones.Array"];
        smrBonesArr.Children.Clear();
        foreach (long bp in nativeBonePids)
        {
            var elem = ValueBuilder.DefaultValueFieldFromArrayTemplate(smrBonesArr);
            elem["m_FileID"].AsInt = 0;
            elem["m_PathID"].AsLong = bp;
            smrBonesArr.Children.Add(elem);
        }

        // 7. Patch Materials (identical proven BDSP anime shader setup)
        AssetFileInfo? donorBodyMatInfo = null;
        var smrMats = smrBf["m_Materials.Array"].Children;
        for (int s = 0; s < smrMats.Count; s++)
        {
            var mi = f.GetAssetInfo(smrMats[s]["m_PathID"].AsLong);
            if (mi == null) continue;
            string nm = am.GetBaseField(battleInst, mi)["m_Name"].AsString;
            if (!nm.Contains("Eye", StringComparison.OrdinalIgnoreCase) && !nm.Contains("Mouth", StringComparison.OrdinalIgnoreCase))
            {
                donorBodyMatInfo = mi;
                break;
            }
        }

        var patchedMatPids = new HashSet<long>();
        for (int matSlot = 0; matSlot < smrMats.Count; matSlot++)
        {
            long origMatPid = smrMats[matSlot]["m_PathID"].AsLong;
            var origMatInfo = f.GetAssetInfo(origMatPid);
            if (origMatInfo == null) continue;
            var origMb = am.GetBaseField(battleInst, origMatInfo);
            string mName = origMb["m_Name"].AsString;
            bool isEyeMat = mName.Contains("Eye", StringComparison.OrdinalIgnoreCase);
            bool isMouthMat = mName.Contains("Mouth", StringComparison.OrdinalIgnoreCase);
            if (!isEyeMat && !patchedMatPids.Add(origMatPid)) continue;

            float origScaleX = 2f;
            foreach (var te in origMb["m_SavedProperties.m_TexEnvs.Array"].Children)
            {
                string pn = te["first"].AsString;
                if (pn == "_Col0Tex" || pn == "_BaseMap" || pn == "_MainTex")
                {
                    origScaleX = te["second.m_Scale.x"].AsFloat;
                    if (origScaleX < 0.1f) origScaleX = 1f;
                    break;
                }
            }

            AssetTypeValueField mb = (isEyeMat && donorBodyMatInfo != null)
                ? am.GetBaseField(battleInst, donorBodyMatInfo)
                : origMb;
            bool hasCustomEmi = texPids.ContainsKey($"Sub{matSlot}_emi");

            mb["m_Name"].AsString = isEyeMat ? $"{formId}-Iris{matSlot}"
                : (isMouthMat ? $"{formId}-Part{matSlot}" : mName.Replace(baseFormId, formId));

            string kw = mb["m_ShaderKeywords"].AsString;
            var cleanKw = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(k => k != "USE_LAYER1" && k != "USE_RIM_MASK_TEX" && k != "USE_SPECULAR_MASK_TEX"
                    && k != "USE_SPHEREMASK_TEX" && (hasCustomEmi || k != "USE_EMISSION_MASK_TEX"));
            mb["m_ShaderKeywords"].AsString = string.Join(" ", cleanKw);
            foreach (var fl in mb["m_SavedProperties.m_Floats.Array"].Children)
            {
                string fk = fl["first"].AsString;
                if (fk == "_Layer1Enable" || fk == "_Layer1OverLerpValue" || fk == "_RimMaskEnable" ||
                    fk == "_SpecularMaskEnable" || fk == "_SphereMaskEnable" ||
                    (!hasCustomEmi && fk == "_EmissionMaskUse"))
                    fl["second"].AsFloat = 0f;
                if (isEyeMat && fk == "_CullMode")
                    fl["second"].AsFloat = 0f;
            }
            foreach (var texEntry in mb["m_SavedProperties.m_TexEnvs.Array"].Children)
            {
                string prop = texEntry["first"].AsString;
                var tex = texEntry["second.m_Texture"];
                bool isCol = prop == "_Col0Tex" || prop == "_BaseMap" || prop == "_MainTex";
                bool isEmi = prop == "_EmissionMaskTex" || prop == "_EmissionMap";
                if (!isCol && !(isEmi && hasCustomEmi))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = 0;
                    continue;
                }
                if (isCol)
                {
                    if (isEyeMat)
                    {
                        texEntry["second.m_Scale.x"].AsFloat = origScaleX;
                        texEntry["second.m_Scale.y"].AsFloat = 1f;
                        texEntry["second.m_Offset.x"].AsFloat = 0f;
                        texEntry["second.m_Offset.y"].AsFloat = 0f;
                    }
                    if (texPids.TryGetValue($"Sub{matSlot}_col", out long pSub))
                    {
                        tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pSub;
                    }
                }
                else if (isEmi && texPids.TryGetValue($"Sub{matSlot}_emi", out long pSubE))
                {
                    tex["m_FileID"].AsInt = 0; tex["m_PathID"].AsLong = pSubE;
                }
            }

            if (isEyeMat && donorBodyMatInfo != null)
            {
                long newMatPid = nextPid++;
                var newMatInfo = new AssetFileInfo { PathId = newMatPid, TypeId = (int)AssetClassID.Material, TypeIdOrIndex = matTypeIdx };
                newMatInfo.SetNewData(mb);
                f.Metadata.AddAssetInfo(newMatInfo);
                smrMats[matSlot]["m_FileID"].AsInt = 0;
                smrMats[matSlot]["m_PathID"].AsLong = newMatPid;
            }
            else
            {
                origMatInfo.SetNewData(mb);
            }
        }
        smrInfo.SetNewData(smrBf);

        // Disable & detach ALL leftover vanilla secondary SkinnedMeshRenderers
        // (including Charizard vanilla FireCore/FireMask, which are bound to the static vanilla skeleton)
        foreach (var si in f.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
        {
            if (si.PathId == smrInfo.PathId) continue;
            var sb = am.GetBaseField(battleInst, si);
            sb["m_Enabled"].AsBool = false;
            sb["m_Mesh.m_FileID"].AsInt = 0;
            sb["m_Mesh.m_PathID"].AsLong = 0;
            si.SetNewData(sb);
        }

        // 8. Embed converted Legends: Z-A TRANM -> Unity m_DenseClip AnimationClips locally (m_FileID = 0)!
        var clipTemplateInst = (commonInst != null && commonInst.file.GetAssetsOfType(AssetClassID.AnimationClip).Count > 0)
            ? commonInst : templateCommonInst;
        var clipTemplateInfo = clipTemplateInst.file.GetAssetsOfType(AssetClassID.AnimationClip).First();

        var roleToClipPid = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var jClip in rigDoc.GetProperty("clips").EnumerateArray())
        {
            string role = jClip.GetProperty("role").GetString()!;
            bool isLoop = jClip.GetProperty("loop").GetBoolean();
            int frameCount = jClip.GetProperty("frame_count").GetInt32();
            int curveCount = jClip.GetProperty("curve_count").GetInt32();
            float sampleRate = jClip.GetProperty("sample_rate").GetSingle();
            int byteOffset = jClip.GetProperty("sample_byte_offset").GetInt32();
            int floatCount = jClip.GetProperty("sample_float_count").GetInt32();
            if (floatCount != frameCount * curveCount)
                throw new InvalidOperationException($"Clip {role} sample count mismatch: {floatCount} != {frameCount}*{curveCount}");

            var clipBf = am.GetBaseField(clipTemplateInst, clipTemplateInfo);
            clipBf["m_Name"].AsString = $"{formId}_{role}";
            clipBf["m_Legacy"].AsBool = false;
            clipBf["m_Compressed"].AsBool = false;
            clipBf["m_UseHighQualityCurve"].AsBool = false;
            clipBf["m_SampleRate"].AsFloat = sampleRate;
            clipBf["m_WrapMode"].AsInt = 0;
            clipBf["m_HasGenericRootTransform"].AsBool = false;
            clipBf["m_HasMotionFloatCurves"].AsBool = false;
            clipBf["m_Events.Array"].Children.Clear();

            // Clear StreamedClip & ConstantClip so ALL curves live cleanly in DenseClip
            var sc = clipBf["m_MuscleClip.m_Clip.data.m_StreamedClip"];
            sc["data.Array"].Children.Clear();
            sc["curveCount"].AsUInt = 0;
            clipBf["m_MuscleClip.m_Clip.data.m_ConstantClip.data.Array"].Children.Clear();

            // Populate DenseClip
            var dc = clipBf["m_MuscleClip.m_Clip.data.m_DenseClip"];
            dc["m_FrameCount"].AsInt = frameCount;
            dc["m_CurveCount"].AsUInt = (uint)curveCount;
            dc["m_SampleRate"].AsFloat = sampleRate;
            dc["m_BeginTime"].AsFloat = 0f;

            var sArr = dc["m_SampleArray.Array"];
            sArr.Children.Clear();
            var sampleTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(sArr);
            var samples = new float[floatCount];
            Buffer.BlockCopy(samplesBlob, byteOffset, samples, 0, floatCount * 4);
            var sChildren = new List<AssetTypeValueField>(floatCount);
            for (int k = 0; k < floatCount; k++)
            {
                var elem = sampleTemplate.Clone();
                elem.AsFloat = samples[k];
                sChildren.Add(elem);
            }
            sArr.Children = sChildren;

            // Timing & Loop flags
            float stopTime = MathF.Max((frameCount - 1) / sampleRate, 1f / sampleRate);
            clipBf["m_MuscleClip.m_StartTime"].AsFloat = 0f;
            clipBf["m_MuscleClip.m_StopTime"].AsFloat = stopTime;
            clipBf["m_MuscleClip.m_LoopTime"].AsBool = isLoop;
            clipBf["m_MuscleClip.m_LoopBlend"].AsBool = false;

            // ValueArrayDelta (start = frame 0, stop = last frame)
            var vadArr = clipBf["m_MuscleClip.m_ValueArrayDelta.Array"];
            vadArr.Children.Clear();
            var vadTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(vadArr);
            var vadChildren = new List<AssetTypeValueField>(curveCount);
            int lastFrameBase = (frameCount - 1) * curveCount;
            for (int c = 0; c < curveCount; c++)
            {
                var elem = vadTemplate.Clone();
                elem["m_Start"].AsFloat = samples[c];
                elem["m_Stop"].AsFloat = samples[lastFrameBase + c];
                vadChildren.Add(elem);
            }
            vadArr.Children = vadChildren;
            clipBf["m_MuscleClip.m_ValueArrayReferencePose.Array"].Children.Clear();

            // GenericBindings (exact CRC32 of "ZA_Rig/..." paths)
            var gbArr = clipBf["m_ClipBindingConstant.genericBindings.Array"];
            gbArr.Children.Clear();
            var gbTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(gbArr);
            var gbChildren = new List<AssetTypeValueField>();
            int checkCurves = 0;
            foreach (var jb in jClip.GetProperty("bindings").EnumerateArray())
            {
                string path = jb.GetProperty("path").GetString()!;
                uint attr = jb.GetProperty("attr").GetUInt32();
                var elem = gbTemplate.Clone();
                elem["path"].AsUInt = ComputeUnityCrc32(path);
                elem["attribute"].AsUInt = attr;
                elem["script.m_FileID"].AsInt = 0;
                elem["script.m_PathID"].AsLong = 0;
                elem["typeID"].AsInt = 4; // Transform
                elem["customType"].AsByte = 0;
                elem["isPPtrCurve"].AsByte = 0;
                gbChildren.Add(elem);
                checkCurves += (attr == 2 ? 4 : 3);
            }
            if (checkCurves != curveCount)
                throw new InvalidOperationException($"Clip {role} binding channel sum {checkCurves} != curveCount {curveCount}");
            gbArr.Children = gbChildren;
            clipBf["m_ClipBindingConstant.pptrCurveMapping.Array"].Children.Clear();

            // Exact m_MuscleClipSize matching Unity 2019.4 (+388B runtime header)
            uint mcSize = (uint)clipBf["m_MuscleClip"].WriteToByteArray().Length + 388u;
            clipBf["m_MuscleClipSize"].AsUInt = mcSize;

            long clipPid = nextPid++;
            var newClipInfo = new AssetFileInfo { PathId = clipPid, TypeId = (int)AssetClassID.AnimationClip, TypeIdOrIndex = clipTypeIdx };
            newClipInfo.SetNewData(clipBf);
            f.Metadata.AddAssetInfo(newClipInfo);
            roleToClipPid[role] = clipPid;
        }

        // 9. Rewire BattlePokemonEntity._animationPlayer._clips to our embedded local clips!
        int rewiredSlots = 0;
        foreach (var mbInfo in f.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var mb = am.GetBaseField(battleInst, mbInfo);
            if (!mb.Children.Any(c => c.FieldName == "_animationPlayer")) continue;
            var clipsArr = mb["_animationPlayer._clips.Array"].Children;
            for (int i = 0; i < clipsArr.Count; i++)
            {
                int origFid = clipsArr[i]["m_FileID"].AsInt;
                long origPid = clipsArr[i]["m_PathID"].AsLong;
                if (origPid == 0) continue; // preserve intentional null slots (e.g. [22],[23])
                commonClipNames.TryGetValue(origPid, out string? origName);
                string role = ResolveRoleFromVanillaClipName(origName, i);
                if (!roleToClipPid.TryGetValue(role, out long targetPid))
                    targetPid = roleToClipPid["waitA01"];
                clipsArr[i]["m_FileID"].AsInt = 0;
                clipsArr[i]["m_PathID"].AsLong = targetPid;
                rewiredSlots++;
            }
            mbInfo.SetNewData(mb);
            break;
        }

        // 10. Save LZ4-packed Nintendo Switch AssetBundle
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
        Directory.CreateDirectory(Path.GetDirectoryName(outBundle)!);
        File.Move(tmpLz4, outBundle, true);
        Console.WriteLine($"Built NATIVE ZA Switch bundle -> {outBundle} ({new FileInfo(outBundle).Length:N0}B, bones={nativeBonePids.Count}, clips={roleToClipPid.Count}, rewired={rewiredSlots}, verts={vCount}, tris={totalIndices / 3})");
        return 0;
    }

    /// <summary>
    /// Fresh AssetsTools.NET round-trip verification of a native ZA bundle:
    /// checks Switch TargetPlatform, zero shaders, valid Transform tree, valid _locators,
    /// 100% CRC32 path match for every embedded AnimationClip, 100% sRGB albedo textures,
    /// AND evaluates actual serialized m_DenseClip curves for Idle, Roar, and Attack frames -> .obj!
    /// </summary>
    private static int VerifyNativeBundle(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: MegaBundleTool verify-native-bundle <bundlePath> [outObjDir]");
            return 2;
        }
        string bundlePath = args[0];
        string? outDir = args.Length > 1 ? args[1] : null;
        string formId = Path.GetFileName(bundlePath);

        var am = new AssetsManager();
        var bun = am.LoadBundleFile(bundlePath, true);
        var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
        var f = inst.file;

        var failures = new List<string>();
        void Check(bool cond, string msg)
        {
            if (!cond) failures.Add(msg);
            Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {msg}");
        }

        Console.WriteLine($"\n=== Native Round-Trip Verify: {formId} ===");
        Check(f.Metadata.TargetPlatform == 38, $"TargetPlatform == 38 (Switch), actual={f.Metadata.TargetPlatform}");
        Check(f.GetAssetsOfType(AssetClassID.Shader).Count == 0, $"Embedded Shaders == 0");

        // Verify all Textures are sRGB (_col)
        int badColorSpace = 0;
        foreach (var ti in f.GetAssetsOfType(AssetClassID.Texture2D))
        {
            var tb = am.GetBaseField(inst, ti);
            string nm = tb["m_Name"].AsString;
            if (nm.Contains("_col", StringComparison.OrdinalIgnoreCase) && tb["m_ColorSpace"].AsInt != 1)
                badColorSpace++;
        }
        Check(badColorSpace == 0, $"All _col Texture2D m_ColorSpace == 1 (sRGB), bad={badColorSpace}");

        // Build full Transform hierarchy & CRC32 path map
        var tData = ReadTransforms(am, inst);
        var hashToTransPid = new Dictionary<uint, long>();
        foreach (var pid in tData.Keys)
        {
            var parts = new List<string>();
            long cur = pid;
            int guard = 0;
            while (cur != 0 && tData.TryGetValue(cur, out var tr) && guard++ < 64)
            {
                if (tr.Parent != 0) parts.Add(tr.Name);
                cur = tr.Parent;
            }
            parts.Reverse();
            if (parts.Count > 0)
                hashToTransPid[ComputeUnityCrc32(string.Join("/", parts))] = pid;
        }

        // Verify BattlePokemonEntity._animationPlayer._clips & _locators
        var bpeInfo = f.GetAssetsOfType(AssetClassID.MonoBehaviour)
            .FirstOrDefault(m => am.GetBaseField(inst, m).Children.Any(c => c.FieldName == "_animationPlayer"));
        Check(bpeInfo != null, "BattlePokemonEntity MonoBehaviour present");

        var clipInfosByName = new Dictionary<string, AssetFileInfo>(StringComparer.OrdinalIgnoreCase);
        int localClipCount = 0;
        int brokenClipRefs = 0;
        int brokenLocators = 0;
        if (bpeInfo != null)
        {
            var bpe = am.GetBaseField(inst, bpeInfo);
            foreach (var cRef in bpe["_animationPlayer._clips.Array"].Children)
            {
                int fid = cRef["m_FileID"].AsInt;
                long pid = cRef["m_PathID"].AsLong;
                if (pid == 0) continue;
                if (fid == 0)
                {
                    var ci = f.GetAssetInfo(pid);
                    if (ci == null) brokenClipRefs++;
                    else localClipCount++;
                }
            }
            if (!bpe["_locators.Array"].IsDummy)
            {
                foreach (var loc in bpe["_locators.Array"].Children)
                {
                    var trRef = loc["transform"];
                    if (!trRef.IsDummy)
                    {
                        long lpid = trRef["m_PathID"].AsLong;
                        if (lpid != 0 && !tData.ContainsKey(lpid)) brokenLocators++;
                    }
                }
            }
        }
        Check(localClipCount >= 20 && brokenClipRefs == 0, $"_animationPlayer._clips rewired locally ({localClipCount} local refs, broken={brokenClipRefs})");
        Check(brokenLocators == 0, $"All _locators Transform PPtrs valid (broken={brokenLocators})");

        // Verify every embedded native Mega AnimationClip (skip any leftover vanilla StreamedClip)
        int totalBindings = 0, matchedBindings = 0, badClipSizes = 0;
        foreach (var ci in f.GetAssetsOfType(AssetClassID.AnimationClip))
        {
            var cb = am.GetBaseField(inst, ci);
            string cname = cb["m_Name"].AsString;
            clipInfosByName[cname] = ci;
            if (!cname.StartsWith(formId + "_", StringComparison.OrdinalIgnoreCase)) continue;
            var dc = cb["m_MuscleClip.m_Clip.data.m_DenseClip"];
            int fc = dc["m_FrameCount"].AsInt;
            uint cc = dc["m_CurveCount"].AsUInt;
            int sc = dc["m_SampleArray.Array"].Children.Count;
            if (fc < 2 || cc == 0 || sc != fc * cc) badClipSizes++;

            foreach (var gb in cb["m_ClipBindingConstant.genericBindings.Array"].Children)
            {
                totalBindings++;
                if (hashToTransPid.ContainsKey(gb["path"].AsUInt)) matchedBindings++;
            }
        }
        Check(clipInfosByName.Count >= 9, $"Embedded AnimationClips >= 9 (actual={clipInfosByName.Count})");
        Check(badClipSizes == 0, $"All DenseClip sample arrays exact (bad={badClipSizes})");
        Check(totalBindings > 0 && matchedBindings == totalBindings, $"CRC32 genericBindings Transform matches: {matchedBindings}/{totalBindings}");

        // Verify Mesh, Bones, BindPoses, and optionally dump animated Idle / Roar / Attack OBJs!
        var smrInfo = FindPrimaryBodySmr(am, inst);
        var smrBf = am.GetBaseField(inst, smrInfo);
        var meshBf = am.GetBaseField(inst, f.GetAssetInfo(smrBf["m_Mesh.m_PathID"].AsLong)!);
        var bonePids = smrBf["m_Bones.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
        var bindPoses = meshBf["m_BindPose.Array"].Children.Select(ReadUnityMat).ToList();
        Check(bonePids.Count == bindPoses.Count && bonePids.Count >= 10, $"Native SMR bones == m_BindPose ({bonePids.Count})");
        Check(bonePids.All(p => tData.ContainsKey(p)), "All SMR m_Bones PPtrs resolve to valid Transforms");

        if (outDir != null)
        {
            Directory.CreateDirectory(outDir);
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
            for (int v = 0; v < vCount; v++)
            {
                int po = posBase + v * posStride;
                verts[v] = new Vector3(BitConverter.ToSingle(raw, po), BitConverter.ToSingle(raw, po + 4), BitConverter.ToSingle(raw, po + 8));
                int wo = wBase + v * wStride;
                int io = iBase + v * iStride;
                weights[v] = (
                    BitConverter.ToInt32(raw, io), BitConverter.ToInt32(raw, io + 4),
                    BitConverter.ToInt32(raw, io + 8), BitConverter.ToInt32(raw, io + 12),
                    BitConverter.ToSingle(raw, wo), BitConverter.ToSingle(raw, wo + 4),
                    BitConverter.ToSingle(raw, wo + 8), BitConverter.ToSingle(raw, wo + 12));
            }
            var idxBuf = meshBf["m_IndexBuffer.Array"].AsByteArray;
            int idxFmt = meshBf["m_IndexFormat"].AsInt;
            var tris = new List<(int a, int b, int c)>();
            foreach (var sub in meshBf["m_SubMeshes.Array"].Children)
            {
                uint count = sub["indexCount"].AsUInt;
                uint firstByte = sub["firstByte"].AsUInt;
                int baseVert = sub["baseVertex"].AsInt;
                for (uint t = 0; t + 2 < count; t += 3)
                {
                    int a = idxFmt == 0 ? BitConverter.ToUInt16(idxBuf, (int)(firstByte + t * 2)) + baseVert : (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + t * 4)) + baseVert;
                    int b = idxFmt == 0 ? BitConverter.ToUInt16(idxBuf, (int)(firstByte + (t + 1) * 2)) + baseVert : (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + (t + 1) * 4)) + baseVert;
                    int c = idxFmt == 0 ? BitConverter.ToUInt16(idxBuf, (int)(firstByte + (t + 2) * 2)) + baseVert : (int)BitConverter.ToUInt32(idxBuf, (int)(firstByte + (t + 2) * 4)) + baseVert;
                    tris.Add((a, b, c));
                }
            }

            void DumpClipFrame(string role, float normT, string tag)
            {
                string key = $"{formId}_{role}";
                if (!clipInfosByName.TryGetValue(key, out var ci)) return;
                var cb = am.GetBaseField(inst, ci);
                var dc = cb["m_MuscleClip.m_Clip.data.m_DenseClip"];
                int fc = dc["m_FrameCount"].AsInt;
                int cc = (int)dc["m_CurveCount"].AsUInt;
                int frame = Math.Clamp((int)MathF.Round(normT * (fc - 1)), 0, fc - 1);
                var sArr = dc["m_SampleArray.Array"].Children;
                int fBase = frame * cc;

                var animTData = new Dictionary<long, TransformRec>(tData);
                int ch = 0;
                foreach (var gb in cb["m_ClipBindingConstant.genericBindings.Array"].Children)
                {
                    uint pathHash = gb["path"].AsUInt;
                    uint attr = gb["attribute"].AsUInt;
                    if (hashToTransPid.TryGetValue(pathHash, out long tpid) && animTData.TryGetValue(tpid, out var cur))
                    {
                        if (attr == 1)
                            animTData[tpid] = cur with { Pos = new Vector3(sArr[fBase + ch].AsFloat, sArr[fBase + ch + 1].AsFloat, sArr[fBase + ch + 2].AsFloat) };
                        else if (attr == 2)
                            animTData[tpid] = cur with { Rot = Quaternion.Normalize(new Quaternion(sArr[fBase + ch].AsFloat, sArr[fBase + ch + 1].AsFloat, sArr[fBase + ch + 2].AsFloat, sArr[fBase + ch + 3].AsFloat)) };
                        else if (attr == 3)
                            animTData[tpid] = cur with { Scale = new Vector3(sArr[fBase + ch].AsFloat, sArr[fBase + ch + 1].AsFloat, sArr[fBase + ch + 2].AsFloat) };
                    }
                    ch += (attr == 2 ? 4 : 3);
                }

                var skinMats = new Matrix4x4[bonePids.Count];
                for (int i = 0; i < bonePids.Count; i++)
                    skinMats[i] = bindPoses[i] * GetWorld(animTData, bonePids[i]);

                float minY = float.MaxValue, maxY = float.MinValue;
                var sb = new StringBuilder(vCount * 40);
                for (int v = 0; v < vCount; v++)
                {
                    var w = weights[v];
                    var p = Vector3.Zero;
                    if (w.w0 > 0) p += Vector3.Transform(verts[v], skinMats[w.i0]) * w.w0;
                    if (w.w1 > 0) p += Vector3.Transform(verts[v], skinMats[w.i1]) * w.w1;
                    if (w.w2 > 0) p += Vector3.Transform(verts[v], skinMats[w.i2]) * w.w2;
                    if (w.w3 > 0) p += Vector3.Transform(verts[v], skinMats[w.i3]) * w.w3;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                    sb.AppendFormat("v {0:F5} {1:F5} {2:F5}\n", p.X, -p.Z, p.Y);
                }
                foreach (var (a, b, c) in tris)
                    sb.AppendFormat("f {0} {1} {2}\n", a + 1, b + 1, c + 1);
                string objPath = Path.Combine(outDir, $"{formId}_{tag}.obj");
                File.WriteAllText(objPath, sb.ToString());
                Console.WriteLine($"  [POSE] {tag,-10} (frame {frame}/{fc - 1}): minY={minY:F3}m, maxY={maxY:F3}m, h={maxY - minY:F3}m -> {objPath}");
            }

            DumpClipFrame("waitA01", 0.25f, "idle");
            DumpClipFrame("roar01", 0.45f, "roar");
            DumpClipFrame("buturi01", 0.45f, "attack");
        }

        am.UnloadAll(true);
        return failures.Count == 0 ? 0 : 1;
    }
}
