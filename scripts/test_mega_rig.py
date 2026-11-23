#!/usr/bin/env python3
"""Universal Offline 3D Rig, Switch AssetBundle & Animation Verification Framework for ALL Mega Pokemon.

Automatically discovers every custom Mega form (pmXXXX_YY_00 where YY != 00) in romfs-delta,
compares it dynamically against its species' vanilla BDSP base bundle (pmXXXX_00_00 + common/pmXXXX_00),
verifies Switch invariants, bindpose parity, weight hygiene, Dpr.bin manifest byte sizes, and renders
auto-framed 3-angle visual contact sheets in headless Blender (Cycles CPU).

Usage:
  python3 scripts/test_mega_rig.py                         # Auto-discover & verify all Mega bundles in romfs-delta
  python3 scripts/test_mega_rig.py --forms pm0006_01_00    # Verify specific form(s)
  python3 scripts/test_mega_rig.py --rebuild               # Rebuild forms with matching .blend + BuiltBundles, patch Dpr.bin, then verify & render
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TOOL_PROJ = REPO_ROOT / "scripts/tools/MegaBundleTool/MegaBundleTool.csproj"
DELTA_BATTLE = REPO_ROOT / "romfs-delta/Data/StreamingAssets/AssetAssistant/Pokemon Database/pokemons/battle"
DPR_OUT = REPO_ROOT / "romfs-delta/Data/StreamingAssets/AssetAssistant/Dpr.bin"
LUMI_DPR = Path("/Users/kshiteesh/Desktop/pokemon/Luminescent Platinum 2.2F BD/romfs/Data/StreamingAssets/AssetAssistant/Dpr.bin")
MEGA_FBX_DIR = REPO_ROOT / "build/mega-fbx"
RENDER_DIR = REPO_ROOT / "build/test-renders"
BLENDER_BIN = Path("/tmp/blender_hfs/Blender/Blender.app/Contents/MacOS/Blender")

MEGA_FORM_RE = re.compile(r"^pm(\d{4})_(\d{2})_00$")


def env_with_dotnet() -> dict:
    env = os.environ.copy()
    env["PATH"] = f"/usr/local/share/dotnet:/opt/homebrew/bin:{env.get('PATH', '')}"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    return env


def discover_mega_forms() -> list[str]:
    forms = []
    if not DELTA_BATTLE.exists():
        return forms
    for p in sorted(DELTA_BATTLE.iterdir()):
        if not p.is_file():
            continue
        m = MEGA_FORM_RE.match(p.name)
        if m and m.group(2) != "00":
            forms.append(p.name)
    return forms


def find_donor_blend(form_id: str) -> Path | None:
    exact = MEGA_FBX_DIR / f"{form_id}.blend"
    if exact.exists():
        return exact
    species = form_id[:6]
    candidates = sorted(MEGA_FBX_DIR.glob(f"{species}_*.blend"))
    return candidates[-1] if candidates else None


def find_built_bundle(form_id: str) -> Path | None:
    species = form_id[:6]
    candidates = [
        REPO_ROOT / f"build/ripped-{species}/unity-project/ExportedProject/BuiltBundles/pokemons/battle/{form_id}",
        REPO_ROOT / f"build/BuiltBundles/pokemons/battle/{form_id}",
    ]
    for c in candidates:
        if c.exists():
            return c
    return None


def export_bone_info(blend_path: Path, out_json: Path) -> None:
    expr = f"""
import bpy, json
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
parents = {{b.name: (b.parent.name if b.parent else '') for b in arm.data.bones}}
vg_names = [vg.name for vg in mesh.vertex_groups]
with open({str(out_json)!r}, 'w') as f:
    json.dump({{'parents': parents, 'vg_names': vg_names}}, f)
"""
    subprocess.run(
        [str(BLENDER_BIN), "-b", str(blend_path), "--python-expr", expr],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


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

world = bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (0.14, 0.16, 0.20, 1.0)
world.node_tree.nodes['Background'].inputs[1].default_value = 0.8

def load_obj(path, color):
    bpy.ops.wm.obj_import(filepath=str(path))
    obj = bpy.context.selected_objects[0]
    mat = bpy.data.materials.new(name='Mat')
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = color
        bsdf.inputs['Roughness'].default_value = 0.65
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj

idle_center = load_obj({str(obj_idle)!r}, (0.95, 0.56, 0.22, 1.0))
# Auto-compute spacing and camera distance from the species' actual bounding box!
bbox = [idle_center.matrix_world @ Vector(c) for c in idle_center.bound_box]
min_z = min(p.z for p in bbox)
max_z = max(p.z for p in bbox)
span_x = max(p.x for p in bbox) - min(p.x for p in bbox)
span_y = max(p.y for p in bbox) - min(p.y for p in bbox)
height = max(max_z - min_z, 0.5)
radius = max(span_x, span_y, height, 0.8)
spacing = max(radius * 0.95, 1.2)

idle_center.location = (0.0, 0.0, 0.0)
idle_center.rotation_euler = (0.0, 0.0, math.radians(35.0))

roar_left = load_obj({str(obj_roar)!r}, (0.85, 0.45, 0.18, 1.0))
roar_left.location = (-spacing, 0.0, 0.0)
roar_left.rotation_euler = (0.0, 0.0, math.radians(35.0))

idle_right = load_obj({str(obj_idle)!r}, (0.95, 0.56, 0.22, 1.0))
idle_right.location = (spacing, 0.0, 0.0)
idle_right.rotation_euler = (0.0, 0.0, math.radians(90.0))

for name, loc, energy in [
    ('Key', (radius * 2.0, -radius * 2.5, radius * 2.5), 250.0 * radius * radius),
    ('Fill', (-radius * 2.0, -radius * 2.0, radius * 1.5), 110.0 * radius * radius),
]:
    ldata = bpy.data.lights.new(name, type='POINT')
    ldata.energy = energy
    lobj = bpy.data.objects.new(name, ldata)
    bpy.context.collection.objects.link(lobj)
    lobj.location = loc

bpy.ops.mesh.primitive_plane_add(size=max(spacing * 6.0, 10.0), location=(0.0, 0.0, 0.0))
ground = bpy.context.active_object
gmat = bpy.data.materials.new(name='Ground')
gmat.use_nodes = True
gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.20, 0.22, 0.25, 1.0)
ground.data.materials.append(gmat)

cam_data = bpy.data.cameras.new('Cam')
cam_obj = bpy.data.objects.new('Cam', cam_data)
bpy.context.collection.objects.link(cam_obj)
scene.camera = cam_obj
cam_dist = max(spacing * 3.1, height * 3.5)
mid_z = 0.5 * (min_z + max_z)
cam_obj.location = (0.0, -cam_dist, mid_z + height * 0.40)
direction = Vector((0.0, 0.0, mid_z)) - cam_obj.location
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
    parser = argparse.ArgumentParser(description="Universal Mega Pokemon offline 3D rig & bundle test framework")
    parser.add_argument("--forms", nargs="*", default=None, help="Specific form IDs (e.g. pm0006_01_00 pm0448_01_00). Defaults to all Mega forms in romfs-delta.")
    parser.add_argument("--rebuild", action="store_true", help="Rebuild each form with Lumi vanilla-skeleton pipeline and patch Dpr.bin before verifying")
    parser.add_argument("--no-render", action="store_true", help="Skip Blender PNG preview rendering for fast CI checks")
    args = parser.parse_args()

    forms = args.forms if args.forms else discover_mega_forms()
    if not forms:
        print("No Mega form bundles found in romfs-delta.")
        return 1

    print(f"Discovered {len(forms)} Mega form(s): {', '.join(forms)}")
    RENDER_DIR.mkdir(parents=True, exist_ok=True)

    if args.rebuild:
        rebuilt_any = False
        for form_id in forms:
            donor_blend = find_donor_blend(form_id)
            built_bundle = find_built_bundle(form_id)
            if not donor_blend or not built_bundle:
                print(f"  [SKIP REBUILD] {form_id}: missing donor blend ({donor_blend}) or BuiltBundle ({built_bundle})")
                continue
            bone_json = Path(f"/tmp/{form_id}_bone_info.json")
            export_bone_info(donor_blend, bone_json)
            rc = run_dotnet_tool(["rebuild-lumi", form_id, str(bone_json), str(built_bundle), str(DELTA_BATTLE / form_id)])
            bone_json.unlink(missing_ok=True)
            if rc != 0:
                return rc
            shiny_id = form_id[:-2] + "01"
            shutil.copy2(DELTA_BATTLE / form_id, DELTA_BATTLE / shiny_id)
            rebuilt_any = True

        if rebuilt_any:
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
    for form_id in forms:
        bundle = DELTA_BATTLE / form_id
        if not bundle.exists():
            print(f"  [FAIL] Missing bundle: {bundle}")
            overall_rc = 1
            continue
        rc = run_dotnet_tool(["verify-and-dump", str(bundle), str(RENDER_DIR)])
        if rc != 0:
            overall_rc = 1
        if not args.no_render:
            obj_idle = RENDER_DIR / f"{form_id}_idle.obj"
            obj_roar = RENDER_DIR / f"{form_id}_roar.obj"
            if obj_idle.exists() and obj_roar.exists():
                render_preview_png(form_id, obj_idle, obj_roar, RENDER_DIR / f"{form_id}_preview.png")

    if DPR_OUT.exists():
        dpr_bytes = DPR_OUT.read_bytes()
        for form_id in forms:
            bundle = DELTA_BATTLE / form_id
            if not bundle.exists():
                continue
            sz = bundle.stat().st_size
            if sz.to_bytes(4, "little") not in dpr_bytes:
                print(f"  [FAIL] Dpr.bin does not contain exact size {sz} for {form_id}")
                overall_rc = 1
            else:
                print(f"  [PASS] Dpr.bin contains exact size {sz:,} for {form_id}")

    print("\n=== TEST SUITE RESULT:", "ALL PASSED" if overall_rc == 0 else "FAILED", "===")
    return overall_rc


if __name__ == "__main__":
    raise SystemExit(main())
