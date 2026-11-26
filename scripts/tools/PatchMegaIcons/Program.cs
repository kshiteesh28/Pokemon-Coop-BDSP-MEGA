using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PatchMegaIcons;

record SpriteEntry(int species, int form, bool is_shiny, string sprite_name, int x, int y, int w, int h);
record AtlasManifest(int width, int height, List<SpriteEntry> sprites);

class Program
{
    static void Main(string[] args)
    {
        string repoRoot = "/Users/kshiteesh/Developer/Pokemon-mod-coop/Pokemon-Coop-version";
        string lpRoot = "/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant";
        string deltaRoot = Path.Combine(repoRoot, "romfs-delta/Data/StreamingAssets/AssetAssistant");

        string atlasRawPath = "/tmp/za_icons_atlas/za_mega_icons_atlas.raw";
        string manifestPath = "/tmp/za_icons_atlas/manifest.json";

        var manifest = JsonSerializer.Deserialize<AtlasManifest>(File.ReadAllText(manifestPath))!;
        byte[] rawPixels = File.ReadAllBytes(atlasRawPath);
        if (rawPixels.Length != manifest.width * manifest.height * 4)
            throw new Exception($"Expected {manifest.width * manifest.height * 4} bytes, got {rawPixels.Length}");

        // =========================================================================
        // 1. Patch UIs/textures_mass/texturemass
        // =========================================================================
        string srcTmPath = Path.Combine(lpRoot, "UIs/textures_mass/texturemass");
        string dstTmDir = Path.Combine(deltaRoot, "UIs/textures_mass");
        Directory.CreateDirectory(dstTmDir);
        string dstTmPath = Path.Combine(dstTmDir, "texturemass");

        var am = new AssetsManager();
        var tmBundle = am.LoadBundleFile(srcTmPath, true);
        var tmAssets = am.LoadAssetsFileFromBundle(tmBundle, 0, true);
        var afile = tmAssets.file;

        // Locate TextureMass SpriteAtlas, a template Texture2D, and template Sprite pm0359_12_00_00
        var atlasInfo = afile.AssetInfos.First(a => a.TypeId == (int)AssetClassID.SpriteAtlas);
        var atlasBf = am.GetBaseField(tmAssets, atlasInfo);

        var tmplTexInfo = afile.AssetInfos.First(a => a.TypeId == (int)AssetClassID.Texture2D);
        var tmplSpriteInfo = afile.AssetInfos.First(a =>
            a.TypeId == (int)AssetClassID.Sprite &&
            am.GetBaseField(tmAssets, a)["m_Name"].AsString == "pm0359_12_00_00");
        var tmplSpriteBf = am.GetBaseField(tmAssets, tmplSpriteInfo);

        // Locate pm0359_12_00_00's RenderDataMap entry as exact template
        var tmplRdk = tmplSpriteBf["m_RenderDataKey"];
        uint tk0 = tmplRdk["first"]["data[0]"].AsUInt;
        uint tk1 = tmplRdk["first"]["data[1]"].AsUInt;
        long tkSec = tmplRdk["second"].AsLong;

        var rdmArr = atlasBf["m_RenderDataMap"]["Array"];
        var tmplRdmEntry = rdmArr.Children.First(e =>
            e["first"]["first"]["data[0]"].AsUInt == tk0 &&
            e["first"]["first"]["data[1]"].AsUInt == tk1 &&
            e["first"]["second"].AsLong == tkSec);

        // 1a. Add new 1024x1024 RGBA32 Texture2D asset (inline image data, no .resS modification needed)
        const long newTexPathId = 7777000000000001L;
        var newTexBf = am.GetBaseField(tmAssets, tmplTexInfo);
        newTexBf["m_Name"].AsString = "sactx-1024x1024-RGBA32-TextureMass-zamega";
        newTexBf["m_Width"].AsInt = manifest.width;
        newTexBf["m_Height"].AsInt = manifest.height;
        newTexBf["m_CompleteImageSize"].AsInt = rawPixels.Length;
        newTexBf["m_TextureFormat"].AsInt = 4; // RGBA32
        newTexBf["m_MipCount"].AsInt = 1;
        newTexBf["m_ColorSpace"].AsInt = 1; // sRGB
        newTexBf["image data"].AsByteArray = rawPixels;
        newTexBf["m_StreamData"]["offset"].AsULong = 0;
        newTexBf["m_StreamData"]["size"].AsUInt = 0;
        newTexBf["m_StreamData"]["path"].AsString = "";

        var newTexInfo = AssetFileInfo.Create(afile, newTexPathId, (int)AssetClassID.Texture2D, null);
        newTexInfo.SetNewData(newTexBf);
        afile.Metadata.AddAssetInfo(newTexInfo);

        // 1b. Add all 36 Sprite assets and register them in SpriteAtlas
        var packedSpritesArr = atlasBf["m_PackedSprites"]["Array"];
        var packedNamesArr = atlasBf["m_PackedSpriteNamesToIndex"]["Array"];

        int addedSprites = 0;
        for (int i = 0; i < manifest.sprites.Count; i++)
        {
            var sp = manifest.sprites[i];
            long spPathId = 7777000000000100L + i;
            uint g0 = 0x5A414D45U; // "ZAME"
            uint g1 = 0x47413230U; // "GA20"
            uint g2 = 0x32360000U | (uint)sp.species;
            uint g3 = ((uint)sp.form << 16) | (sp.is_shiny ? 1U : 0U) | ((uint)(i + 1) << 24);
            long keySecond = 21300000L;

            var spBf = am.GetBaseField(tmAssets, tmplSpriteInfo);
            spBf["m_Name"].AsString = sp.sprite_name;
            spBf["m_Rect"]["x"].AsFloat = 0;
            spBf["m_Rect"]["y"].AsFloat = 0;
            spBf["m_Rect"]["width"].AsFloat = sp.w;
            spBf["m_Rect"]["height"].AsFloat = sp.h;
            spBf["m_RenderDataKey"]["first"]["data[0]"].AsUInt = g0;
            spBf["m_RenderDataKey"]["first"]["data[1]"].AsUInt = g1;
            spBf["m_RenderDataKey"]["first"]["data[2]"].AsUInt = g2;
            spBf["m_RenderDataKey"]["first"]["data[3]"].AsUInt = g3;
            spBf["m_RenderDataKey"]["second"].AsLong = keySecond;
            spBf["m_SpriteAtlas"]["m_FileID"].AsInt = 0;
            spBf["m_SpriteAtlas"]["m_PathID"].AsLong = atlasInfo.PathId;

            var spInfo = AssetFileInfo.Create(afile, spPathId, (int)AssetClassID.Sprite, null);
            spInfo.SetNewData(spBf);
            afile.Metadata.AddAssetInfo(spInfo);

            // Append to m_PackedSprites
            var pptr = ValueBuilder.DefaultValueFieldFromArrayTemplate(packedSpritesArr);
            pptr["m_FileID"].AsInt = 0;
            pptr["m_PathID"].AsLong = spPathId;
            packedSpritesArr.Children.Add(pptr);

            // Append to m_PackedSpriteNamesToIndex
            var nameField = ValueBuilder.DefaultValueFieldFromArrayTemplate(packedNamesArr);
            nameField.AsString = sp.sprite_name;
            packedNamesArr.Children.Add(nameField);

            // Append to m_RenderDataMap (clone from template)
            var rdmEntry = tmplRdmEntry.Clone();
            rdmEntry["first"]["first"]["data[0]"].AsUInt = g0;
            rdmEntry["first"]["first"]["data[1]"].AsUInt = g1;
            rdmEntry["first"]["first"]["data[2]"].AsUInt = g2;
            rdmEntry["first"]["first"]["data[3]"].AsUInt = g3;
            rdmEntry["first"]["second"].AsLong = keySecond;

            var val = rdmEntry["second"];
            val["texture"]["m_FileID"].AsInt = 0;
            val["texture"]["m_PathID"].AsLong = newTexPathId;
            val["textureRect"]["x"].AsFloat = sp.x;
            val["textureRect"]["y"].AsFloat = sp.y;
            val["textureRect"]["width"].AsFloat = sp.w;
            val["textureRect"]["height"].AsFloat = sp.h;
            val["textureRectOffset"]["x"].AsFloat = 0;
            val["textureRectOffset"]["y"].AsFloat = 0;
            val["atlasRectOffset"]["x"].AsFloat = sp.x;
            val["atlasRectOffset"]["y"].AsFloat = sp.y;
            val["uvTransform"]["x"].AsFloat = 100f;
            val["uvTransform"]["y"].AsFloat = sp.x + sp.w * 0.5f;
            val["uvTransform"]["z"].AsFloat = 100f;
            val["uvTransform"]["w"].AsFloat = sp.y + sp.h * 0.5f;
            val["downscaleMultiplier"].AsFloat = 1f;
            val["settingsRaw"].AsUInt = 1U;
            rdmArr.Children.Add(rdmEntry);

            addedSprites++;
        }

        atlasInfo.SetNewData(atlasBf);

        // Save modified texturemass bundle with LZ4 compression (preserving .resS directory entry!)
        tmBundle.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
        string tmpUncomp = dstTmPath + ".uncomp";
        using (var w = new AssetsFileWriter(tmpUncomp))
            tmBundle.file.Write(w);

        var reBundle = new AssetBundleFile();
        using (var fs = File.OpenRead(tmpUncomp))
        using (var r = new AssetsFileReader(fs))
        {
            reBundle.Read(r);
            using var outFs = File.Create(dstTmPath);
            using var outW = new AssetsFileWriter(outFs);
            reBundle.Pack(outW, AssetBundleCompressionType.LZ4);
        }
        File.Delete(tmpUncomp);
        Console.WriteLine($"[texturemass] Added 1 Texture2D + {addedSprites} Sprites -> {dstTmPath} ({new FileInfo(dstTmPath).Length:N0} bytes)");

        // =========================================================================
        // 2. Patch UIs/masterdatas/uimasterdatas (UIDatabase.PokemonIcon)
        // =========================================================================
        string uiPath = Path.Combine(deltaRoot, "UIs/masterdatas/uimasterdatas");
        var am2 = new AssetsManager();
        var uiBundle = am2.LoadBundleFile(uiPath, true);
        var uiAssets = am2.LoadAssetsFileFromBundle(uiBundle, 0, true);
        var uiInfo = uiAssets.file.AssetInfos.First(a =>
            a.TypeId == (int)AssetClassID.MonoBehaviour &&
            am2.GetBaseField(uiAssets, a)["m_Name"].AsString == "UIDatabase");
        var uiBf = am2.GetBaseField(uiAssets, uiInfo);

        var iconLookup = manifest.sprites.ToDictionary(
            s => (s.species, s.form, s.is_shiny),
            s => s.sprite_name);

        int updatedRows = 0;
        var iconRows = uiBf["PokemonIcon"]["Array"];
        foreach (var row in iconRows.Children)
        {
            int uid = row["UniqueID"].AsInt;
            int sp = uid / 10000;
            int fm = (uid / 100) % 100;
            bool isShiny = (uid % 2) == 1;
            if (iconLookup.TryGetValue((sp, fm, isShiny), out string newAssetName))
            {
                row["AssetName"].AsString = newAssetName;
                updatedRows++;
            }
        }
        uiInfo.SetNewData(uiBf);

        uiBundle.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(uiAssets.file);
        string uiTmp = uiPath + ".uncomp";
        using (var w = new AssetsFileWriter(uiTmp))
            uiBundle.file.Write(w);

        am2.UnloadAll();

        var reUi = new AssetBundleFile();
        using (var fs = File.OpenRead(uiTmp))
        using (var r = new AssetsFileReader(fs))
        {
            reUi.Read(r);
            using var outFs = File.Create(uiPath);
            using var outW = new AssetsFileWriter(outFs);
            reUi.Pack(outW, AssetBundleCompressionType.LZ4);
        }
        File.Delete(uiTmp);
        Console.WriteLine($"[uimasterdatas] Updated {updatedRows} UIDatabase.PokemonIcon rows -> {uiPath} ({new FileInfo(uiPath).Length:N0} bytes)");
    }
}
