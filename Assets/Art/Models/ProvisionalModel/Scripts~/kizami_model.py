"""ステージ用仮モデルの共通処理。0.4 m のセル格子に素材を割り当て、素材ごとの閉じたメッシュにする。"""
import json
import math
import os

import bpy
from mathutils import Euler, Vector

# セル 1 つの大きさ（m）
CELL = 0.4

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLEND_DIR = os.path.join(ROOT_DIR, "Blender~")
# マップ用モデルの FBX の置き場所
STAGE_DIR = os.path.join(os.path.dirname(ROOT_DIR), "Stage")

# 面の向きと、その面の 4 頂点（セルの最小の隅からのオフセット、外から見て反時計回り）
_FACES = [
    ((1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
    ((-1, 0, 0), [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)]),
    ((0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
    ((0, -1, 0), [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)]),
    ((0, 0, 1), [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]),
    ((0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)]),
]


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.cameras, bpy.data.materials):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def cells_to_mesh(name, cells, offset):
    """セルの集まりの外面だけを張ったメッシュを作る。offset はセル (0,0,0) の最小の隅の座標。"""
    verts, faces, index = [], [], {}

    def vert(key):
        if key not in index:
            index[key] = len(verts)
            verts.append(tuple(round(k * CELL + o, 4) for k, o in zip(key, offset)))
        return index[key]

    for (x, y, z) in cells:
        for (dx, dy, dz), corners in _FACES:
            if (x + dx, y + dy, z + dz) in cells:
                continue
            faces.append([vert((x + cx, y + cy, z + cz)) for cx, cy, cz in corners])

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    mesh.name = name
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def build_model(model_name, grid, size):
    """grid は {(x, y, z): 素材名}。size はセル数 (X, Y, Z)。底面の中心を原点にしてルートの下に素材ごとのメッシュを置く。"""
    root = bpy.data.objects.new(model_name, None)
    bpy.context.collection.objects.link(root)
    offset = (-size[0] * CELL / 2, -size[1] * CELL / 2, 0.0)

    by_material = {}
    for key, mat in grid.items():
        by_material.setdefault(mat, set()).add(key)

    for mat_name in sorted(by_material):
        obj = cells_to_mesh(f"{model_name}_{mat_name}", by_material[mat_name], offset)
        mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
        mat.diffuse_color = MATERIAL_PREVIEW_COLORS.get(mat_name, (0.5, 0.5, 0.5, 1))
        obj.data.materials.append(mat)
        obj.parent = root
    return root


# 確認用の表示色。取り込み側で見た目を作るので、FBX の見た目には使わない
MATERIAL_PREVIEW_COLORS = {
    "Brick": (0.55, 0.32, 0.22, 1),
    "Mortar": (0.78, 0.76, 0.70, 1),
    "Concrete": (0.6, 0.6, 0.6, 1),
    "Rebar": (0.15, 0.25, 0.6, 1),
    "Metal": (0.3, 0.3, 0.35, 1),
    "Wood": (0.6, 0.45, 0.25, 1),
    "Glass": (0.55, 0.75, 0.85, 1),
}


def build_model_from_boxes(model_name, boxes, size):
    """boxes は [(素材名, (x0, y0, z0), (x1, y1, z1))]（セル単位、終端は含まない）。大きい建物用。

    箱ごとに閉じた直方体にして、素材ごとに 1 つのメッシュにまとめる。セルごとに面を張るとポリゴンが多すぎるため。
    """
    for i, (_, lo_a, hi_a) in enumerate(boxes):
        for _, lo_b, hi_b in boxes[i + 1:]:
            if all(lo_a[k] < hi_b[k] and lo_b[k] < hi_a[k] for k in range(3)):
                raise ValueError(f"箱が重なっている: {lo_a}-{hi_a} と {lo_b}-{hi_b}")

    root = bpy.data.objects.new(model_name, None)
    bpy.context.collection.objects.link(root)
    offset = (-size[0] * CELL / 2, -size[1] * CELL / 2, 0.0)

    by_material = {}
    for mat_name, lo_c, hi_c in boxes:
        by_material.setdefault(mat_name, []).append((lo_c, hi_c))

    for mat_name in sorted(by_material):
        verts, faces = [], []
        for lo_c, hi_c in by_material[mat_name]:
            base = len(verts)
            for cz in (lo_c[2], hi_c[2]):
                for cy in (lo_c[1], hi_c[1]):
                    for cx in (lo_c[0], hi_c[0]):
                        verts.append(tuple(round(c * CELL + o, 4) for c, o in zip((cx, cy, cz), offset)))
            # 頂点の番号は x + 2y + 4z
            for quad in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
                faces.append([base + q for q in quad])
        name = f"{model_name}_{mat_name}"
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
        mat.diffuse_color = MATERIAL_PREVIEW_COLORS.get(mat_name, (0.5, 0.5, 0.5, 1))
        obj.data.materials.append(mat)
        obj.parent = root
    return root


# 窓のガラスの板 1 枚の最大の幅（m）。開口はこの幅以下の板に等分する
GLASS_PANE_MAX_WIDTH = 1.6


def extract_glass(boxes, size):
    """boxes から Glass の箱を抜き、残りの箱と、ガラスの板の配置を返す。

    ガラスはボクセルにせず、Unity で GlassPane の板を置く。配置は Unity の座標（FBX の書き出しで (-x, z, -y) になる）で、
    板の底面の中心 `Position`、幅と高さ `Size`、Y 軸まわりの回転 `Yaw`（度）を持つ。板は厚さの向きが Z のときに yaw 0。
    """
    rest, panes = [], []
    offset = (-size[0] * CELL / 2, -size[1] * CELL / 2, 0.0)
    for mat_name, lo_c, hi_c in boxes:
        if mat_name != "Glass":
            rest.append((mat_name, lo_c, hi_c))
            continue
        lo = [c * CELL + o for c, o in zip(lo_c, offset)]
        hi = [c * CELL + o for c, o in zip(hi_c, offset)]
        # 開口の幅の向きは、箱の水平の辺の長い方
        along = 0 if hi[0] - lo[0] >= hi[1] - lo[1] else 1
        across = 1 - along
        width, height = hi[along] - lo[along], hi[2] - lo[2]
        count = max(1, math.ceil(width / GLASS_PANE_MAX_WIDTH - 1e-6))
        pane_width = width / count
        middle = (lo[across] + hi[across]) / 2
        for i in range(count):
            center = [0.0, 0.0]
            center[along] = lo[along] + pane_width * (i + 0.5)
            center[across] = middle
            panes.append({
                "Position": [round(-center[0], 4), round(lo[2], 4), round(-center[1], 4)],
                "Size": [round(pane_width, 4), round(height, 4)],
                "Yaw": 0 if along == 0 else 90,
            })
    return rest, panes


def save_glass(model_name, panes, fbx_dir):
    """ガラスの板の配置を、FBX の隣に `<モデル名>_Glass.json` で書き出す。"""
    with open(os.path.join(fbx_dir, model_name + "_Glass.json"), "w", encoding="utf-8") as f:
        json.dump({"Panes": panes}, f, ensure_ascii=False, indent=1)


def build_floor_models(prefix, parts):
    """建物の階ごとのモデルをまとめて作る。parts は [(階の種類, 箱, (幅, 奥行き, 高さ) のセル数)]。

    ガラスの箱は抜き出して配置の JSON にし、環境変数 KIZAMI_EXPORT が 1 なら FBX と JSON を Stage に書き出す。モデルごとの report を返す。
    """
    result = {}
    for part, boxes, size in parts:
        clear_scene()
        boxes, panes = extract_glass(boxes, size)
        root = build_model_from_boxes(f"{prefix}_{part}_01", boxes, size)
        result[root.name] = dict(report(root), glass_panes=len(panes))
        if os.environ.get("KIZAMI_EXPORT") == "1":
            save_and_export(root, STAGE_DIR)
            if panes:
                save_glass(root.name, panes, STAGE_DIR)
    return result


def report(root):
    """寸法・グリッドの倍数・閉じているかを返す。"""
    import bmesh

    parts = {}
    lo, hi = [math.inf] * 3, [-math.inf] * 3
    for obj in root.children:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        boundary = sum(1 for e in bm.edges if len(e.link_faces) < 2)
        bm.free()
        off_grid = 0
        for v in obj.data.vertices:
            for i in range(3):
                lo[i], hi[i] = min(lo[i], v.co[i]), max(hi[i], v.co[i])
                if abs(v.co[i] / CELL - round(v.co[i] / CELL)) > 1e-4:
                    off_grid += 1
        parts[obj.name] = {
            "tris": sum(len(p.vertices) - 2 for p in obj.data.polygons),
            "boundary_edges": boundary,
            "off_grid_coords": off_grid,
            "data_name_match": obj.name == obj.data.name,
            "material": [m.name for m in obj.data.materials],
        }
    return {
        "model": root.name,
        "size": [round(hi[i] - lo[i], 4) for i in range(3)],
        "min": [round(v, 4) for v in lo],
        "parts": parts,
    }


def render_ortho(path, center=(0, 0, 2), dist=40.0, rot_x=70, rot_z=-35, ortho_scale=8.0, res=(900, 900)):
    """オルソカメラを立てて Workbench で png に焼く。rot_x は伏角（90 で真横）、rot_z は方位角。"""
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("_shot_cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ortho_scale
    cam = bpy.data.objects.new("_shot_cam", cam_data)
    scene.collection.objects.link(cam)
    rot = Euler((math.radians(rot_x), 0, math.radians(rot_z)), 'XYZ')
    cam.rotation_euler = rot
    cam.location = Vector(center) + rot.to_matrix() @ Vector((0, 0, dist))

    prev_cam, prev_engine = scene.camera, scene.render.engine
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.type = 'SOLID'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_xray = False
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

    scene.camera, scene.render.engine = prev_cam, prev_engine
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data)


def save_and_export(root, fbx_dir=ROOT_DIR):
    """`.blend` を Blender~ に保存し、FBX を fbx_dir に書き出す。"""
    os.makedirs(BLEND_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, root.name + ".blend"), copy=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(fbx_dir, root.name + ".fbx"),
        use_selection=False,
        object_types={'EMPTY', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        bake_space_transform=True,
        add_leaf_bones=False,
    )
