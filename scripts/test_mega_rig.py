#!/usr/bin/env python3
"""Offline 3D Rig, Switch AssetBundle & Animation Verification Framework for Mega Charizard X/Y.

Usage:
  python3 scripts/test_mega_rig.py              # Verify existing romfs-delta bundles + render visual previews
  python3 scripts/test_mega_rig.py --rebuild    # Rebuild bundles with Lumi 56-bone pipeline, patch Dpr.bin, then verify & render
"""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TOOL_PROJ = REPO_ROOT / "scripts/tools/MegaBundleTool/MegaBundleTool.csproj"
DELTA_BATTLE = REPO_ROOT / "romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/battle"
DPR_OUT = REPO_ROOT / "romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr.bin"
LUMI_DPR = Path("/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr.bin")
BLEND_Y = REPO_ROOT / "build/mega-fbx/pm0006_02_00.blend"
RENDER_DIR = REPO_ROOT / "build/test-renders"
BLENDER_BIN = Path("/tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender")


def env_with_dotnet() -> dict:
    env = os.environ.copy()
    env["PATH"] = f"/usr/local/share/dotnet:/opt/homebrew/bin:{env.get('PATH', '')}"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    return env


def export_bone_info(out_json: Path) -> None:
    expr = f"""
import bpy, json
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
parents = {{b.name: (b.parent.name if b.parent else '') for b in arm.data.bones}}
vg_names = [vg.name for vg in mesh.vertex_groups]
with open({str(out_json)!r}, 'w') as f:
    json.dump({{'parents': parents, 'vg_names': vg_names}}, f)
"""
    subprocess.run([str(BLENDER_BIN), "-b", str(BLEND_Y), "--python-expr", expr], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def run_dotnet_tool(args: list[str]) -> int:
    cmd = ["dotnet", "run", "--project", str(TOOL_PROJ), "--"] + args
    proc = subprocess.run(cmd, env=env_with_dotnet())
    return proc.returncode


def render_preview_png(form_id: str, obj_idle: Path, obj_roar: Path, out_png: Path) -> None:
    if not BLENDER_BIN.exists():
        print(f"  [SKIP] Blender not found at {BLENDER_BIN}, skipping PNG render.")
        return

    render_script = f"""
import bpy, math
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 12
scene.cycles.use_denoising = False
scene.render.resolution_x = 1280
scene.render.resolution_y = 640
scene.render.image_settings.file_format = 'PNG'

# Neutral studio world + key/fill lights
world = bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
world.node_tree.nodes['Background'].inputs[1].default_value = 0.8

for name, loc, energy in [('Key', (4.0, -5.0, 5.0), 800.0), ('Fill', (-4.0, -4.0, 3.0), 350.0)]:
    ldata = bpy.data.lights.new(name, type='POINT')
    ldata.energy = energy
    lobj = bpy.data.objects.new(name, ldata)
    bpy.context.collection.objects.link(lobj)
    lobj.location = loc

# Import idle mesh at origin, side copy at X=+2.5, roar pose at X=-2.5
def load_obj(path, loc, rot_z_deg, color):
    bpy.ops.wm.obj_import(filepath=str(path))
    obj = bpy.context.selected_objects[0]
    obj.location = loc
    obj.rotation_euler = (0.0, 0.0, math.radians(rot_z_deg))
    mat = bpy.data.materials.new(name='Mat')
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = color
        bsdf.inputs['Roughness'].default_value = 0.65
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj

# Left: Roar pose 3/4, Center: Idle 3/4 battle view, Right: Idle pure side profile (shows Jaw & posture clearly!)
load_obj({str(obj_roar)!r}, (-2.5, 0.0, 0.0), 35.0, (0.85, 0.45, 0.18, 1.0))
load_obj({str(obj_idle)!r}, ( 0.0, 0.0, 0.0), 35.0, (0.95, 0.56, 0.22, 1.0))
load_obj({str(obj_idle)!r}, ( 2.5, 0.0, 0.0), 90.0, (0.95, 0.56, 0.22, 1.0))

# Ground plane grid reference at Z = 0
bpy.ops.mesh.primitive_plane_add(size=14.0, location=(0.0, 0.0, 0.0))
ground = bpy.context.active_object
gmat = bpy.data.materials.new(name='Ground')
gmat.use_nodes = True
gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.20, 0.22, 0.25, 1.0)
ground.data.materials.append(gmat)

# Camera framing all 3 views cleanly
cam_data = bpy.data.cameras.new('Cam')
cam_obj = bpy.data.objects.new('Cam', cam_data)
bpy.context.collection.objects.link(cam_obj)
scene.camera = cam_obj
cam_obj.location = (0.0, -7.8, 1.6)
direction = Vector((0.0, 0.0, 0.90)) - cam_obj.location
cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 45.0

scene.render.filepath = {str(out_png)!r}
bpy.ops.render.render(write_still=True)
"""
    subprocess.run(
        [str(BLENDER_BIN), "-b", "--python-expr", render_script],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    print(f"  [RENDER] Saved visual contact sheet -> {out_png}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rebuild", action="store_true", help="Rebuild bundles with Lumi 56-bone pipeline first")
    args = parser.parse_args()

    RENDER_DIR.mkdir(parents=True, exist_ok=True)

    if args.rebuild:
        bone_json = Path("/tmp/mega_bone_info.json")
        export_bone_info(bone_json)
        rc = run_dotnet_tool(["rebuild-lumi", str(bone_json)])
        if rc != 0:
            return rc
        bone_json.unlink(missing_ok=True)

        # Copy shiny slots
        shutil.copy2(DELTA_BATTLE / "pm0006_01_00", DELTA_BATTLE / "pm0006_01_01")
        shutil.copy2(DELTA_BATTLE / "pm0006_02_00", DELTA_BATTLE / "pm0006_02_01")

        # Update Dpr.bin manifest
        subprocess.run(
            [
                sys.executable,
                str(REPO_ROOT / "scripts/patch_dpr_manifest.py"),
                "--source", str(LUMI_DPR),
                "--delta-root", str(REPO_ROOT / "romfs-delta/Data/StreamingAssets/AssetAssistant"),
                "--output", str(DPR_OUT),
            ],
            check=True,
        )

    overall_rc = 0
    for form_id in ("pm0006_01_00", "pm0006_02_00"):
        bundle = DELTA_BATTLE / form_id
        rc = run_dotnet_tool(["verify-and-dump", str(bundle), str(RENDER_DIR)])
        if rc != 0:
            overall_rc = 1
        obj_idle = RENDER_DIR / f"{form_id}_idle.obj"
        obj_roar = RENDER_DIR / f"{form_id}_roar.obj"
        if obj_idle.exists() and obj_roar.exists():
            render_preview_png(form_id, obj_idle, obj_roar, RENDER_DIR / f"{form_id}_preview.png")

    # Verify Dpr.bin contains exact byte sizes
    dpr_bytes = DPR_OUT.read_bytes()
    for form_id in ("pm0006_01_00", "pm0006_02_00"):
        sz = (DELTA_BATTLE / form_id).stat().st_size
        if sz.to_bytes(4, "little") not in dpr_bytes:
            print(f"  [FAIL] Dpr.bin does not contain exact size {sz} for {form_id}")
            overall_rc = 1
        else:
            print(f"  [PASS] Dpr.bin contains exact size {sz:,} for {form_id}")

    print("\n=== TEST SUITE RESULT:", "ALL PASSED" if overall_rc == 0 else "FAILED", "===")
    return overall_rc


if __name__ == "__main__":
    raise SystemExit(main())
