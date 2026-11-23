#!/usr/bin/env python3
"""Blender headless script to prepare Mega Charizard X & Y FBX files for Unity 2019.4.27f1 (LumiTool / Melonscript).

Usage:
  blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
    --base-fbx /path/to/pm0006_00_00.fbx \
    --out-dir build/mega-fbx
"""

import argparse
import math
import sys
from pathlib import Path

import bpy


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def transfer_rig_and_export(base_fbx: Path, mega_glb: Path, out_fbx: Path, form_name: str) -> None:
    clear_scene()

    # 1. Import base Charizard FBX (extracted from BDSP 1.3.0 pm0006_00_00 via AssetStudio/AssetRipper)
    bpy.ops.import_scene.fbx(filepath=str(base_fbx))
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    base_meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not armatures or not base_meshes:
        raise RuntimeError(f"Base FBX {base_fbx} must contain an Armature and Mesh")

    base_armature = armatures[0]
    base_mesh = base_meshes[0]

    # 2. Import Mega Charizard GLB
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(mega_glb))
    imported = [o for o in bpy.context.scene.objects if o not in existing]
    mega_meshes = [o for o in imported if o.type == "MESH"]
    mega_armatures = [o for o in imported if o.type == "ARMATURE"]
    if not mega_meshes:
        raise RuntimeError(f"No meshes found in {mega_glb}")

    # Join all Mega sub-objects into a single mesh while preserving material slots/submeshes
    bpy.ops.object.select_all(action="DESELECT")
    for obj in mega_meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = mega_meshes[0]
    if len(mega_meshes) > 1:
        bpy.ops.object.join()
    mega_mesh = bpy.context.view_layer.objects.active
    mega_mesh.name = f"{form_name}_skin"

    # Remove imported non-BDSP armature if present so we bind cleanly to BDSP's Charizard rig
    for arm in mega_armatures:
        bpy.data.objects.remove(arm, do_unlink=True)

    # Match bounding-box height & origin to base Charizard mesh
    base_h = max(base_mesh.dimensions.z, 1e-5)
    mega_h = max(mega_mesh.dimensions.z, 1e-5)
    scale_ratio = base_h / mega_h
    mega_mesh.scale = (
        mega_mesh.scale.x * scale_ratio,
        mega_mesh.scale.y * scale_ratio,
        mega_mesh.scale.z * scale_ratio,
    )
    bpy.ops.object.select_all(action="DESELECT")
    mega_mesh.select_set(True)
    bpy.context.view_layer.objects.active = mega_mesh
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # 3. Parent Mega mesh to BDSP Charizard Armature and transfer skinning weights from base Charizard
    mega_mesh.parent = base_armature
    arm_mod = mega_mesh.modifiers.new(name="Armature", type="ARMATURE")
    arm_mod.object = base_armature

    dt_mod = mega_mesh.modifiers.new(name="TransferWeights", type="DATA_TRANSFER")
    dt_mod.object = base_mesh
    dt_mod.use_vert_data = True
    dt_mod.data_types_verts = {"VGROUP_WEIGHTS"}
    dt_mod.vert_mapping = "POLYINTERP_NEAREST"
    bpy.ops.object.datalayout_transfer(modifier=dt_mod.name)
    bpy.ops.object.modifier_apply(modifier=dt_mod.name)

    # Remove base mesh, keeping BDSP Armature + rigged Mega mesh
    for bm in base_meshes:
        bpy.data.objects.remove(bm, do_unlink=True)

    # 4. Export FBX configured for Unity 2019.4.27f1
    out_fbx.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(out_fbx),
        use_selection=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )
    print(f"Exported {out_fbx}")


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-fbx", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, default=Path("build/mega-fbx"))
    args = parser.parse_args(argv)

    repo_root = Path(__file__).resolve().parent.parent
    transfer_rig_and_export(
        args.base_fbx,
        repo_root / "assets/mega/6-mega-x.glb",
        args.out_dir / "pm0006_01_00.fbx",
        "pm0006_01_00",
    )
    transfer_rig_and_export(
        args.base_fbx,
        repo_root / "assets/mega/6-mega-y.glb",
        args.out_dir / "pm0006_02_00.fbx",
        "pm0006_02_00",
    )


if __name__ == "__main__":
    main()
