#!/usr/bin/env python3
"""Blender headless script to prepare animation-ready Mega Charizard X & Y FBX files
for Unity 2019.4.27f1 (LumiTool / Melonscript) using BDSP's pm0006_00_00 bone naming.

Modes:
  1. Standalone (uses 6-mega-y.glb's 104-bone Pokemon rig normalized to BDSP pm0006_00_00 bone names):
     blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
       --out-dir build/mega-fbx

  2. With ripped BDSP base Charizard FBX/GLB (from AssetRipper on pm0006_00_00 + pm0006_00):
     blender --background --python scripts/prepare_mega_charizard_fbx.py -- \
       --base-fbx /path/to/pm0006_00_00.fbx \
       --out-dir build/mega-fbx
"""

import argparse
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector

# Explicit mappings from 3DS/GLTF Mega Charizard Y bone names (after stripping _0XX suffix)
# to BDSP pm0006_00_00 bone names so all 25 BDSP AnimationClips bind cleanly.
BONE_ALIASES = {
    "LFingerA": "LFingerA1",
    "LFingerB": "LFingerB1",
    "LFingerC": "LFingerC1",
    "RFingerA": "RFingerA1",
    "RFingerB": "RFingerB1",
    "RFingerC": "RFingerC1",
}

JUNK_MESH_NAMES = {"Icosphere"}


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def normalize_bdsp_bone_name(raw_name: str) -> str:
    # Strip GLTF export numeric suffixes such as Origin_02, Waist_03, EndLToe_010, pm0006_52_BodySkin_097
    cleaned = re.sub(r"_0\d+$", "", raw_name)
    return BONE_ALIASES.get(cleaned, cleaned)


def import_model(path: Path) -> list:
    existing = set(bpy.context.scene.objects)
    ext = path.suffix.lower()
    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(path))
    else:
        raise ValueError(f"Unsupported model extension: {path}")
    return [o for o in bpy.context.scene.objects if o not in existing]


def filter_valid_meshes(objects: list) -> list:
    valid = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        if obj.name in JUNK_MESH_NAMES or (len(obj.data.materials) == 0 and len(obj.vertex_groups) == 0 and len(obj.data.vertices) < 100):
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        valid.append(obj)
    return valid


def join_meshes(meshes: list, target_name: str):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = target_name
    return joined


def world_bounds(obj) -> tuple[Vector, Vector]:
    corners = [obj.matrix_world @ Vector(v) for v in obj.bound_box]
    mins = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    maxs = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    return mins, maxs


def align_mesh_to_reference(mega_mesh, ref_mesh) -> None:
    ref_min, ref_max = world_bounds(ref_mesh)
    mega_min, mega_max = world_bounds(mega_mesh)

    ref_h = max(ref_max.z - ref_min.z, 1e-5)
    mega_h = max(mega_max.z - mega_min.z, 1e-5)
    scale_ratio = ref_h / mega_h

    mega_mesh.scale = (
        mega_mesh.scale.x * scale_ratio,
        mega_mesh.scale.y * scale_ratio,
        mega_mesh.scale.z * scale_ratio,
    )
    bpy.context.view_layer.update()

    # Align X/Y center and feet (Z min) to the reference mesh
    mega_min, mega_max = world_bounds(mega_mesh)
    ref_cx = 0.5 * (ref_min.x + ref_max.x)
    ref_cy = 0.5 * (ref_min.y + ref_max.y)
    mega_cx = 0.5 * (mega_min.x + mega_max.x)
    mega_cy = 0.5 * (mega_min.y + mega_max.y)

    mega_mesh.location.x += ref_cx - mega_cx
    mega_mesh.location.y += ref_cy - mega_cy
    mega_mesh.location.z += ref_min.z - mega_min.z

    bpy.ops.object.select_all(action="DESELECT")
    mega_mesh.select_set(True)
    bpy.context.view_layer.objects.active = mega_mesh
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def normalize_and_limit_weights(mesh_obj, limit: int = 4) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_obj
    if len(mesh_obj.vertex_groups) == 0:
        return
    try:
        bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=limit)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    except Exception as exc:
        print(f"Warning: weight normalization skipped on {mesh_obj.name}: {exc}")


def export_textures(mesh_obj, out_dir: Path, form_name: str) -> int:
    tex_dir = out_dir / "textures" / form_name
    if tex_dir.exists():
        for old in tex_dir.glob("*.png"):
            old.unlink()
    tex_dir.mkdir(parents=True, exist_ok=True)
    used_images = []
    seen_ids = set()
    for mat in mesh_obj.data.materials:
        if not mat or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image and id(node.image) not in seen_ids:
                seen_ids.add(id(node.image))
                used_images.append(node.image)
    count = 0
    for idx, img in enumerate(used_images):
        if img.size[0] == 0 or img.size[1] == 0:
            continue
        safe_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", img.name)
        if not safe_name.lower().endswith(".png"):
            safe_name = f"{form_name}_{idx}_{safe_name}.png"
        dst = tex_dir / safe_name
        img.filepath_raw = str(dst)
        img.file_format = "PNG"
        try:
            img.save()
            count += 1
        except Exception as exc:
            print(f"Warning: could not save texture {img.name}: {exc}")
    return count


def rename_armature_bones_to_bdsp(armature, form_name: str) -> None:
    armature.name = form_name
    seen = set()
    for bone in armature.data.bones:
        new_name = normalize_bdsp_bone_name(bone.name)
        if new_name == "pm0006_52_00":
            new_name = form_name
        if new_name in seen:
            continue
        seen.add(new_name)
        bone.name = new_name


def bind_and_export(armature, mega_mesh, out_dir: Path, form_name: str) -> None:
    normalize_and_limit_weights(mega_mesh, limit=4)
    tex_count = export_textures(mega_mesh, out_dir, form_name)

    out_dir.mkdir(parents=True, exist_ok=True)
    out_blend = out_dir / f"{form_name}.blend"
    out_fbx = out_dir / f"{form_name}.fbx"

    bpy.ops.wm.save_as_mainfile(filepath=str(out_blend))

    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    mega_mesh.select_set(True)
    bpy.context.view_layer.objects.active = armature

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
    print(
        f"Exported {out_fbx.name}: verts={len(mega_mesh.data.vertices)}, "
        f"mats={len(mega_mesh.data.materials)}, vgroups={len(mega_mesh.vertex_groups)}, "
        f"bones={len(armature.data.bones)}, textures={tex_count}"
    )


def process_with_base_rig(base_model: Path, mega_glb: Path, out_dir: Path, form_name: str) -> None:
    clear_scene()
    base_objs = import_model(base_model)
    base_armatures = [o for o in base_objs if o.type == "ARMATURE"]
    base_meshes = filter_valid_meshes(base_objs)
    if not base_armatures or not base_meshes:
        raise RuntimeError(f"Base model {base_model} must contain an Armature and Mesh")

    base_armature = base_armatures[0]
    base_mesh = max(base_meshes, key=lambda o: len(o.data.vertices))
    base_bone_names = {b.name for b in base_armature.data.bones}

    mega_objs = import_model(mega_glb)
    mega_armatures = [o for o in mega_objs if o.type == "ARMATURE"]
    mega_meshes = filter_valid_meshes(mega_objs)
    if not mega_meshes:
        raise RuntimeError(f"No valid meshes found in {mega_glb}")

    mega_mesh = join_meshes(mega_meshes, f"{form_name}_BodySkin")
    has_existing_weights = len(mega_mesh.vertex_groups) > 0

    for arm in mega_armatures:
        bpy.data.objects.remove(arm, do_unlink=True)

    align_mesh_to_reference(mega_mesh, base_mesh)

    # Remap existing Pokemon vertex groups by normalized BDSP bone name if present
    mapped_groups = 0
    if has_existing_weights:
        for vg in list(mega_mesh.vertex_groups):
            norm = normalize_bdsp_bone_name(vg.name)
            if norm in base_bone_names:
                vg.name = norm
                mapped_groups += 1

    mega_mesh.parent = base_armature
    for mod in list(mega_mesh.modifiers):
        if mod.type == "ARMATURE":
            mega_mesh.modifiers.remove(mod)
    arm_mod = mega_mesh.modifiers.new(name="Armature", type="ARMATURE")
    arm_mod.object = base_armature

    # If the model had no pre-authored Pokemon weights (e.g. Mega X), transfer from base Charizard
    if mapped_groups < 10:
        mega_mesh.vertex_groups.clear()
        dt_mod = mega_mesh.modifiers.new(name="TransferWeights", type="DATA_TRANSFER")
        dt_mod.object = base_mesh
        dt_mod.use_vert_data = True
        dt_mod.data_types_verts = {"VGROUP_WEIGHTS"}
        dt_mod.vert_mapping = "POLYINTERP_NEAREST"
        bpy.ops.object.select_all(action="DESELECT")
        mega_mesh.select_set(True)
        bpy.context.view_layer.objects.active = mega_mesh
        bpy.ops.object.datalayout_transfer(modifier=dt_mod.name)
        bpy.ops.object.modifier_apply(modifier=dt_mod.name)

    for bm in base_meshes:
        bpy.data.objects.remove(bm, do_unlink=True)

    bind_and_export(base_armature, mega_mesh, out_dir, form_name)


def process_standalone_mega_y(mega_y_glb: Path, out_dir: Path) -> None:
    clear_scene()
    objs = import_model(mega_y_glb)
    armatures = [o for o in objs if o.type == "ARMATURE"]
    meshes = filter_valid_meshes(objs)
    if not armatures or not meshes:
        raise RuntimeError(f"Mega Y model {mega_y_glb} must contain an Armature and Mesh")

    armature = armatures[0]
    mega_mesh = join_meshes(meshes, "pm0006_02_00_BodySkin")

    # Rename both armature bones and vertex groups to exact BDSP pm0006_00_00 bone names
    rename_armature_bones_to_bdsp(armature, "pm0006_02_00")
    for vg in mega_mesh.vertex_groups:
        norm = normalize_bdsp_bone_name(vg.name)
        if norm == "pm0006_52_00":
            norm = "pm0006_02_00"
        vg.name = norm

    mega_mesh.parent = armature
    for mod in list(mega_mesh.modifiers):
        if mod.type == "ARMATURE":
            mega_mesh.modifiers.remove(mod)
    arm_mod = mega_mesh.modifiers.new(name="Armature", type="ARMATURE")
    arm_mod.object = armature

    bind_and_export(armature, mega_mesh, out_dir, "pm0006_02_00")


def process_standalone_mega_x(mega_y_glb: Path, mega_x_glb: Path, out_dir: Path) -> None:
    # Use Mega Y's cleaned BDSP-named Pokemon skeleton as the donor rig for unrigged Mega X
    clear_scene()
    y_objs = import_model(mega_y_glb)
    y_armatures = [o for o in y_objs if o.type == "ARMATURE"]
    y_meshes = filter_valid_meshes(y_objs)
    if not y_armatures or not y_meshes:
        raise RuntimeError(f"Donor rig {mega_y_glb} must contain an Armature and Mesh")

    armature = y_armatures[0]
    y_mesh = join_meshes(y_meshes, "donor_charizard")
    rename_armature_bones_to_bdsp(armature, "pm0006_01_00")
    for vg in y_mesh.vertex_groups:
        norm = normalize_bdsp_bone_name(vg.name)
        if norm == "pm0006_52_00":
            norm = "pm0006_01_00"
        vg.name = norm

    x_objs = import_model(mega_x_glb)
    x_armatures = [o for o in x_objs if o.type == "ARMATURE"]
    x_meshes = filter_valid_meshes(x_objs)
    if not x_meshes:
        raise RuntimeError(f"No valid meshes found in {mega_x_glb}")
    for arm in x_armatures:
        bpy.data.objects.remove(arm, do_unlink=True)

    mega_x = join_meshes(x_meshes, "pm0006_01_00_BodySkin")
    align_mesh_to_reference(mega_x, y_mesh)

    dt_mod = mega_x.modifiers.new(name="TransferWeights", type="DATA_TRANSFER")
    dt_mod.object = y_mesh
    dt_mod.use_vert_data = True
    dt_mod.data_types_verts = {"VGROUP_WEIGHTS"}
    dt_mod.vert_mapping = "POLYINTERP_NEAREST"

    bpy.ops.object.select_all(action="DESELECT")
    mega_x.select_set(True)
    bpy.context.view_layer.objects.active = mega_x
    bpy.ops.object.datalayout_transfer(modifier=dt_mod.name)
    bpy.ops.object.modifier_apply(modifier=dt_mod.name)

    mega_x.parent = armature
    arm_mod = mega_x.modifiers.new(name="Armature", type="ARMATURE")
    arm_mod.object = armature

    bpy.data.objects.remove(y_mesh, do_unlink=True)
    bind_and_export(armature, mega_x, out_dir, "pm0006_01_00")


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--base-fbx",
        type=Path,
        default=None,
        help="Optional ripped BDSP pm0006_00_00 FBX/GLB. If omitted, uses 6-mega-y.glb's BDSP-named rig.",
    )
    parser.add_argument("--out-dir", type=Path, default=Path("build/mega-fbx"))
    args = parser.parse_args(argv)

    repo_root = Path(__file__).resolve().parent.parent
    mega_x_glb = repo_root / "assets/mega/6-mega-x.glb"
    mega_y_glb = repo_root / "assets/mega/6-mega-y.glb"

    if args.base_fbx is not None:
        process_with_base_rig(args.base_fbx, mega_x_glb, args.out_dir, "pm0006_01_00")
        process_with_base_rig(args.base_fbx, mega_y_glb, args.out_dir, "pm0006_02_00")
    else:
        process_standalone_mega_y(mega_y_glb, args.out_dir)
        process_standalone_mega_x(mega_y_glb, mega_x_glb, args.out_dir)


if __name__ == "__main__":
    main()
