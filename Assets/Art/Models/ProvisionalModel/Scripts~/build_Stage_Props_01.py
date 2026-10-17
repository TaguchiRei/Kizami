"""小物 6 種（Stage_Prop_*_01）を作る。ボクセルにはせず、通常攻撃のメッシュ切断で壊す。

小物は 1 つのメッシュにまとめ、素材ごとにマテリアルのスロットを分ける（サブメッシュ）。CuttableObject は 1 つのメッシュを切るので、
小物 1 つが 1 回だけ切れる単位になる。切り口にふたを張るので、部品はそれぞれ閉じた直方体にする。グリッドと厚みの決まりは適用しない。
ルートの空オブジェクト `<モデル名>` の下に、メッシュ `<モデル名>_Mesh` を 1 つ置く。
"""
import importlib
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# 直方体の 8 頂点（中心からの向き）と 6 面。頂点の番号は x + 2y + 4z
_CORNERS = [(x, y, z) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
_QUADS = ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5))


def box(mat, center, size, rot_x=0.0, rot_z=0.0):
    """部品の直方体。center と size は m、rot_x・rot_z は度。"""
    return mat, Vector(center), Vector(size), rot_x, rot_z


def build_prop(name, parts):
    """部品の直方体を 1 つのメッシュにまとめ、素材ごとにマテリアルのスロットを割り当てる。"""
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    materials = sorted({p[0] for p in parts})
    verts, faces, face_mats = [], [], []
    for mat, center, size, rot_x, rot_z in parts:
        rot = Matrix.Rotation(math.radians(rot_z), 3, 'Z') @ Matrix.Rotation(math.radians(rot_x), 3, 'X')
        base = len(verts)
        for c in _CORNERS:
            local = Vector((c[0] * size.x / 2, c[1] * size.y / 2, c[2] * size.z / 2))
            verts.append(tuple(round(v, 5) for v in center + rot @ local))
        for quad in _QUADS:
            faces.append([base + q for q in quad])
            face_mats.append(materials.index(mat))
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    for mat_name in materials:
        m = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
        m.diffuse_color = km.MATERIAL_PREVIEW_COLORS.get(mat_name, (0.5, 0.5, 0.5, 1))
        mesh.materials.append(m)
    for poly, index in zip(mesh.polygons, face_mats):
        poly.material_index = index
    # メッシュ切断のライブラリは UV のないメッシュを登録できないので、面ごとに 0〜1 の UV を張る
    mesh.uv_layers.new(name="UVMap")
    mesh.update()
    obj = bpy.data.objects.new(f"{name}_Mesh", mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    return root


def bench():
    """長さ 1.8 × 奥行き 0.6 × 高さ 0.8 m。木の座面と背もたれ、鉄の脚。"""
    parts = [box("Wood", (0, -0.05 + i * 0.17, 0.43), (1.8, 0.14, 0.05)) for i in range(2)]
    parts.append(box("Wood", (0, 0.24, 0.65), (1.8, 0.05, 0.2), rot_x=-10))
    for x in (-0.75, 0.75):
        parts += [
            box("Metal", (x, -0.18, 0.2), (0.06, 0.06, 0.4)),
            box("Metal", (x, 0.2, 0.2), (0.06, 0.06, 0.4)),
            box("Metal", (x, 0.25, 0.53), (0.06, 0.06, 0.25), rot_x=-10),
        ]
    return parts


def street_light():
    """高さ 8 m の街灯。コンクリートの基礎、鉄の柱と腕、腕の先にガラスの灯具。腕は +Y に 1.6 m 張り出す。"""
    return [
        box("Concrete", (0, 0, 0.15), (0.4, 0.4, 0.3)),
        box("Metal", (0, 0, 4.0), (0.18, 0.18, 7.4)),
        box("Metal", (0, 0.8, 7.75), (0.12, 1.75, 0.12)),
        box("Metal", (0, 1.6, 7.62), (0.35, 0.5, 0.14)),
        box("Glass", (0, 1.6, 7.5), (0.3, 0.42, 0.1)),
    ]


def guardrail(end):
    """長さ 4 m のガードレール。鉄の支柱 2 本とレール。end なら東の半分のレールを段々に地面へ下げた端の形にする。"""
    parts = [
        box("Metal", (-1.8, 0.1, 0.4), (0.1, 0.1, 0.8)),
        box("Metal", (0.0 if end else 1.8, 0.1, 0.4), (0.1, 0.1, 0.8)),
    ]
    if not end:
        return parts + [box("Metal", (0, -0.02, 0.58), (4.0, 0.06, 0.32))]
    return parts + [
        box("Metal", (-0.8, -0.02, 0.58), (2.4, 0.06, 0.32)),
        box("Metal", (0.95, -0.02, 0.42), (1.1, 0.06, 0.32)),
        box("Metal", (1.75, -0.02, 0.22), (0.5, 0.06, 0.32)),
    ]


def traffic_light():
    """高さ 5.5 m の信号。鉄の柱、+Y に 3.6 m 張り出す腕、腕の先に灯器とガラスのレンズ 3 つ。"""
    parts = [
        box("Concrete", (0, 0, 0.15), (0.4, 0.4, 0.3)),
        box("Metal", (0, 0, 2.8), (0.2, 0.2, 5.0)),
        box("Metal", (0, 1.8, 5.25), (0.12, 3.6, 0.14)),
        box("Metal", (0, 3.1, 4.95), (0.3, 1.2, 0.4)),
    ]
    parts += [box("Glass", (-0.17, 2.7 + i * 0.4, 4.95), (0.04, 0.28, 0.28)) for i in range(3)]
    return parts


def barricade():
    """幅 1.2 × 奥行き 0.5 × 高さ 1.0 m の A 形のバリケード。木の横板 2 枚と、傾いた鉄の脚。"""
    parts = [
        box("Wood", (0, -0.05, 0.85), (1.2, 0.04, 0.2)),
        box("Wood", (0, -0.12, 0.45), (1.2, 0.04, 0.2)),
    ]
    for x in (-0.5, 0.5):
        parts += [
            box("Metal", (x, -0.1, 0.48), (0.05, 0.05, 1.0), rot_x=12),
            box("Metal", (x, 0.1, 0.48), (0.05, 0.05, 1.0), rot_x=-12),
        ]
    return parts


def construction_fence():
    """幅 2 × 奥行き 0.2 × 高さ 3 m の工事用のフェンスの 1 枚。鉄の板と縁の柱、コンクリートの足。"""
    return [
        box("Metal", (0, 0, 1.6), (1.9, 0.04, 2.7)),
        box("Metal", (-0.97, 0, 1.55), (0.06, 0.06, 2.9)),
        box("Metal", (0.97, 0, 1.55), (0.06, 0.06, 2.9)),
        box("Concrete", (-0.8, 0, 0.075), (0.35, 0.2, 0.15)),
        box("Concrete", (0.8, 0, 0.075), (0.35, 0.2, 0.15)),
    ]


PROPS = {
    "Stage_Prop_Bench_01": bench,
    "Stage_Prop_StreetLight_01": street_light,
    "Stage_Prop_Guardrail_Straight_01": lambda: guardrail(False),
    "Stage_Prop_Guardrail_End_01": lambda: guardrail(True),
    "Stage_Prop_TrafficLight_01": traffic_light,
    "Stage_Prop_Barricade_01": barricade,
    "Stage_Prop_ConstructionFence_01": construction_fence,
}

result = {}
for name, make in PROPS.items():
    km.clear_scene()
    root = build_prop(name, make())
    mesh = root.children[0].data
    result[name] = {"tris": sum(len(p.vertices) - 2 for p in mesh.polygons), "materials": [m.name for m in mesh.materials]}
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
