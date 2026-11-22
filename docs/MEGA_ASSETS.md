# Mega Charizard model sources

The unconverted source models for the Mega prototype are stored as `assets/mega/6-mega-x.glb` and `assets/mega/6-mega-y.glb`. They are **not BDSP AssetBundles** and cannot be copied directly into RomFS.

| Form | Creator | Original model | License | SHA-256 | Source properties |
| --- | --- | --- | --- | --- | --- |
| X | Rubilax | [CHARIZARD X](https://sketchfab.com/3d-models/charizard-x-006674c184434287b0b661d552a910f6) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `b80a23fd7a1dcbd8bcd14525fec5a5fb4bd213a6fef34f482fa12bbae18b9496` | 7 meshes; no skin; no animations |
| Y | Juliocesar6328 | [Charizard Mega Y](https://sketchfab.com/3d-models/charizard-mega-y-678cfe2604be4bf39718a7173557be3e) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | `d21a38cad88530601b68671db8a19405245fe859c9557ec7ddfda02ff466bda4` | 5 meshes; 1 skin; no animations |

These GLBs were obtained from [huanghdm88/pokemon-3d-models](https://github.com/huanghdm88/pokemon-3d-models), whose [attribution file](https://github.com/huanghdm88/pokemon-3d-models/blob/main/ATTRIBUTIONS.md) records the original creators, sources, and licenses. Hashes were checked against its [model manifest](https://github.com/huanghdm88/pokemon-3d-models/blob/main/models-manifest.json). The underlying Pokémon character remains The Pokémon Company's intellectual property.

Conversion work required before runtime use:

1. Decode glTF's Draco mesh compression and WebP textures.
2. Rig X to a compatible Charizard battle skeleton; retarget or create battle animations for both forms.
3. Import into the Unity version specified by [LumiTool's guide](https://luminescent.team/rom-hacking/lumitool-guides/introduction), assign BDSP-compatible materials, and build/postprocess bundles.
4. Register both forms in the matching RomFS `PokemonInfo`, personal data, icons, and audio paths.
5. Load each form in Ryujinx and verify mesh, texture, animation, scale, and switching behavior.

Keep the creator attribution with any transformed or distributed bundle. A GLB passing hash checks proves source integrity, not game compatibility.
