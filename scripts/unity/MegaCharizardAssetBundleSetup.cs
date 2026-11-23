#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity 2019.4.27f1 Editor helper for binding Mega Charizard X (pm0006_01_00)
/// and Mega Charizard Y (pm0006_02_00) meshes onto BDSP's pm0006_00_00 prefab,
/// preserving all 24+ pm0006_00 AnimationClips, RuntimeAnimatorController,
/// PokemonCustomNodeAnim, and battle effect locators.
/// </summary>
[InitializeOnLoad]
public class MegaCharizardAssetBundleSetup : AssetPostprocessor
{
    private const string BasePrefabPath = "Assets/pokemons/battle/pm0006/pm0006_00_00.prefab";
    private const string MegaXModelPath = "Assets/MegaCharizard/pm0006_01_00.fbx";
    private const string MegaYModelPath = "Assets/MegaCharizard/pm0006_02_00.fbx";

    static MegaCharizardAssetBundleSetup()
    {
        EditorApplication.delayCall += RunIfTriggered;
    }

    private static void RunIfTriggered()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var triggerPath = Path.Combine(projectRoot, "TriggerMegaBuild.txt");
        if (!File.Exists(triggerPath))
        {
            return;
        }

        try
        {
            File.Delete(triggerPath);
        }
        catch (Exception)
        {
            // Ignore lock errors
        }

        SetupAndBuildBundles();
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.Contains("pm0006_01_00") && !assetPath.Contains("pm0006_02_00"))
        {
            return;
        }

        var importer = assetImporter as ModelImporter;
        if (importer == null)
        {
            return;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false; // Animations come from BDSP common/pm0006_00
        importer.isReadable = true;
        importer.importBlendShapes = true;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.globalScale = 1.0f;
    }

    [MenuItem("Tools/MegaExtension/Setup Mega Charizard Prefabs & Bundles")]
    [MenuItem("Assets/MegaExtension/Setup Mega Charizard Prefabs & Bundles")]
    public static void SetupMegaCharizardPrefabs()
    {
        var logLines = new List<string>();
        LogLine(logLines, "Starting SetupMegaCharizardPrefabs...");

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (basePrefab == null)
        {
            LogLine(logLines, $"ERROR: Base prefab not found at '{BasePrefabPath}'.");
            FlushLog(logLines);
            return;
        }

        BuildVariantPrefab(basePrefab, MegaXModelPath, "pm0006_01_00", "pokemons/battle/pm0006_01_00", logLines);
        BuildVariantPrefab(basePrefab, MegaYModelPath, "pm0006_02_00", "pokemons/battle/pm0006_02_00", logLines);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LogLine(logLines, "Mega Charizard X & Y prefabs and AssetBundle tags are ready.");
        FlushLog(logLines);
    }

    [MenuItem("Tools/MegaExtension/Setup & Build AssetBundles")]
    [MenuItem("Assets/MegaExtension/Setup & Build AssetBundles")]
    public static void SetupAndBuildBundles()
    {
        var logLines = new List<string>();
        try
        {
            SetupMegaCharizardPrefabs();

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BuiltBundles"));
            Directory.CreateDirectory(outDir);

            var target = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Switch, BuildTarget.Switch)
                ? BuildTarget.Switch
                : EditorUserBuildSettings.activeBuildTarget;

            LogLine(logLines, $"Building AssetBundles for target {target} into {outDir}...");
            var manifest = BuildPipeline.BuildAssetBundles(
                outDir,
                BuildAssetBundleOptions.ChunkBasedCompression,
                target
            );
            LogLine(logLines, manifest != null
                ? $"AssetBundle build complete. Bundles: {string.Join(", ", manifest.GetAllAssetBundles())}"
                : "ERROR: BuildPipeline.BuildAssetBundles returned null.");
        }
        catch (Exception ex)
        {
            LogLine(logLines, $"EXCEPTION: {ex}");
        }
        finally
        {
            FlushLog(logLines);
        }
    }

    private static void BuildVariantPrefab(
        GameObject basePrefab,
        string fbxPath,
        string formId,
        string bundleName,
        List<string> logLines
    )
    {
        var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxRoot == null)
        {
            LogLine(logLines, $"WARNING: Skipping {formId} — model not found at '{fbxPath}'.");
            return;
        }

        var srcRenderer = fbxRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        if (srcRenderer == null || srcRenderer.sharedMesh == null)
        {
            LogLine(logLines, $"ERROR: No SkinnedMeshRenderer found in '{fbxPath}'.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = formId;

        var boneLookup = new Dictionary<string, Transform>();
        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
        {
            if (!boneLookup.ContainsKey(t.name))
            {
                boneLookup[t.name] = t;
            }
        }

        SkinnedMeshRenderer targetBody = null;
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name.Contains("BodySkin"))
            {
                targetBody = smr;
                break;
            }
        }

        if (targetBody == null)
        {
            LogLine(logLines, "ERROR: Could not find BodySkin SkinnedMeshRenderer on base prefab.");
            UnityEngine.Object.DestroyImmediate(instance);
            return;
        }

        var remappedBones = new Transform[srcRenderer.bones.Length];
        int matched = 0;
        for (int i = 0; i < srcRenderer.bones.Length; i++)
        {
            var srcBone = srcRenderer.bones[i];
            if (srcBone != null && boneLookup.TryGetValue(srcBone.name, out var destBone))
            {
                remappedBones[i] = destBone;
                matched++;
            }
            else
            {
                remappedBones[i] = boneLookup.TryGetValue("Origin", out var origin) ? origin : targetBody.rootBone;
            }
        }

        targetBody.name = $"{formId}_BodySkin";
        targetBody.sharedMesh = srcRenderer.sharedMesh;
        targetBody.bones = remappedBones;
        targetBody.sharedMaterials = srcRenderer.sharedMaterials;
        if (boneLookup.TryGetValue("Origin", out var rootOrigin))
        {
            targetBody.rootBone = rootOrigin;
        }

        var outDir = "Assets/pokemons/battle/pm0006";
        Directory.CreateDirectory(outDir);
        var outPrefabPath = $"{outDir}/{formId}.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, outPrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);

        var importer = AssetImporter.GetAtPath(outPrefabPath);
        if (importer != null)
        {
            importer.assetBundleName = bundleName;
        }

        LogLine(logLines, $"Built {outPrefabPath} (matched {matched}/{srcRenderer.bones.Length} bones) -> bundle '{bundleName}'.");
    }

    private static void LogLine(List<string> lines, string message)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] [MegaExtension] {message}";
        lines.Add(stamped);
        Debug.Log(stamped);
    }

    private static void FlushLog(List<string> lines)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var logPath = Path.Combine(projectRoot, "MegaExtension.log");
        File.AppendAllLines(logPath, lines);
    }
}
#endif
