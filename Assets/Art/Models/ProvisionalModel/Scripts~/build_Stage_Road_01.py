"""道路の部品（Stage_Road_*_01）を作る。道路はボクセルにしない。

車道と歩道の段差だけを作り、白線は後でシェーダーで描く。車道は地面の上に厚さ 0.05 m の板を敷き、歩道はその上に 0.2 m 高く載せる。
道幅は大通り・中通り・細い道の 3 種類で、それぞれ直線・十字路・T 字路を作る。細い道には歩道がなく、T 字路は十字路で兼ねる。
直線は長さ 16 m で、道の向きは Unity の Z。十字路と T 字路は道幅の正方形で、T 字路は Unity の +Z 側を閉じる。
ロータリーは半径 40 m の中央ロータリーで、4 方向に大通りがつながる。中の島（半径 18 m）は天照塔の土台になる。
ルートの空オブジェクト `<モデル名>` の下に、素材ごとのメッシュ `<モデル名>_<素材>` を置く。
"""
import importlib
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# 車道の板の厚さと、歩道の上面の高さ（m）
ROAD_TOP, SIDEWALK_TOP = 0.05, 0.25
# 直線の長さ（m）
STRAIGHT_LENGTH = 16.0
# 道の種類ごとの、車道の幅と片側の歩道の幅（m）
WIDTHS = {"Main": (19.2, 4.0), "Middle": (9.6, 2.4), "Narrow": (4.8, 0.0)}
# ロータリーの外周・歩道の内側・中の島の半径（m）と、円周の分割数
ROTARY_RADIUS, ROTARY_SIDEWALK, ROTARY_ISLAND, ROTARY_SEGMENTS = 40.0, 36.0, 18.0, 64

_CORNERS = [(x, y, z) for z in (0, 1) for y in (0, 1) for x in (0, 1)]
_QUADS = ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5))


class MeshBuilder:
    """素材ごとに頂点と面を貯めて、最後に 1 つのルートの下へメッシュを作る。"""

    def __init__(self):
        self.parts = {}

    def add(self, mat, verts, faces):
        v, f = self.parts.setdefault(mat, ([], []))
        base = len(v)
        v.extend(verts)
        f.extend([tuple(base + i for i in face) for face in faces])

    def box(self, mat, x0, y0, x1, y1, z0, z1):
        """Blender の座標の直方体。"""
        lo, hi = (x0, y0, z0), (x1, y1, z1)
        verts = [tuple(hi[k] if c[k] else lo[k] for k in range(3)) for c in _CORNERS]
        self.add(mat, verts, _QUADS)

    def sector(self, mat, r0, r1, a0, a1, z0, z1, segments):
        """角度 a0〜a1（ラジアン）、半径 r0〜r1 の扇形の板。r0 が 0 なら中心を 1 点にする。"""
        verts, faces = [], []
        for i in range(segments + 1):
            a = a0 + (a1 - a0) * i / segments
            c, s = math.cos(a), math.sin(a)
            verts += [(r0 * c, r0 * s, z0), (r1 * c, r1 * s, z0), (r1 * c, r1 * s, z1), (r0 * c, r0 * s, z1)]
        for i in range(segments):
            p, q = 4 * i, 4 * (i + 1)
            faces += [(p + 3, p + 2, q + 2, q + 3), (p, q, q + 1, p + 1), (p + 1, q + 1, q + 2, p + 2)]
            if r0 > 0:
                faces.append((p, p + 3, q + 3, q))
        # 一周する板は始まりと終わりの頂点が重なるので、端の面を張らない
        last = 4 * segments
        if a1 - a0 < 2 * math.pi - 1e-6:
            faces += [(0, 1, 2, 3), (last, last + 3, last + 2, last + 1)]
        if r0 == 0:
            faces = [f for f in faces if len(set(verts[i] for i in f)) > 2]
        self.add(mat, verts, faces)

    def build(self, name):
        root = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(root)
        for mat_name, (verts, faces) in sorted(self.parts.items()):
            mesh = bpy.data.meshes.new(f"{name}_{mat_name}")
            mesh.from_pydata([tuple(round(c, 4) for c in v) for v in verts], [], faces)
            mesh.validate()
            mesh.update()
            obj = bpy.data.objects.new(mesh.name, mesh)
            bpy.context.collection.objects.link(obj)
            mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
            mat.diffuse_color = km.MATERIAL_PREVIEW_COLORS.get(mat_name, (0.5, 0.5, 0.5, 1))
            obj.data.materials.append(mat)
            obj.parent = root
        return root


def straight(road, side):
    """Blender の Y が道の向き（Unity の Z）。"""
    b = MeshBuilder()
    half, length = road / 2 + side, STRAIGHT_LENGTH / 2
    b.box("Asphalt", -half, -length, half, length, 0, ROAD_TOP)
    if side:
        for x0 in (-half, road / 2):
            b.box("Sidewalk", x0, -length, x0 + side, length, ROAD_TOP, SIDEWALK_TOP)
    return b


def cross(road, side):
    b = MeshBuilder()
    half = road / 2 + side
    b.box("Asphalt", -half, -half, half, half, 0, ROAD_TOP)
    if side:
        for sx in (-1, 1):
            for sy in (-1, 1):
                x0 = road / 2 if sx > 0 else -half
                y0 = road / 2 if sy > 0 else -half
                b.box("Sidewalk", x0, y0, x0 + side, y0 + side, ROAD_TOP, SIDEWALK_TOP)
    return b


def t_junction(road, side):
    """Blender の -Y（Unity の +Z）側を歩道で閉じる。"""
    b = MeshBuilder()
    half = road / 2 + side
    b.box("Asphalt", -half, -half, half, half, 0, ROAD_TOP)
    if side:
        b.box("Sidewalk", -half, -half, half, -half + side, ROAD_TOP, SIDEWALK_TOP)
        for x0 in (-half, road / 2):
            b.box("Sidewalk", x0, road / 2, x0 + side, half, ROAD_TOP, SIDEWALK_TOP)
    return b


def rotary():
    """中の島と外周の歩道は、4 方向の大通りの車道の幅だけ切り欠く。車道は外周まで円盤で敷き、大通りの車道の幅で正方形の端まで延ばす。"""
    b = MeshBuilder()
    road, _ = WIDTHS["Main"]
    full = 2 * math.pi
    b.sector("Asphalt", 0, ROTARY_RADIUS, 0, full, 0, ROAD_TOP, ROTARY_SEGMENTS)
    b.sector("Sidewalk", 0, ROTARY_ISLAND, 0, full, ROAD_TOP, SIDEWALK_TOP, ROTARY_SEGMENTS // 2)
    gap = math.asin(road / 2 / ROTARY_SIDEWALK)
    for k in range(4):
        a0 = k * math.pi / 2 + gap
        a1 = (k + 1) * math.pi / 2 - gap
        b.sector("Sidewalk", ROTARY_SIDEWALK, ROTARY_RADIUS, a0, a1, ROAD_TOP, SIDEWALK_TOP, ROTARY_SEGMENTS // 4)
    # 円盤の外周と大通りの車道の端のすき間を埋める短い車道
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if sx:
            x0, x1 = sorted((sx * ROTARY_SIDEWALK, sx * ROTARY_RADIUS))
            b.box("Asphalt", x0, -road / 2, x1, road / 2, 0, ROAD_TOP)
        else:
            y0, y1 = sorted((sy * ROTARY_SIDEWALK, sy * ROTARY_RADIUS))
            b.box("Asphalt", -road / 2, y0, road / 2, y1, 0, ROAD_TOP)
    return b


models = {"Stage_Road_Rotary_01": rotary}
for kind, (road, side) in WIDTHS.items():
    models[f"Stage_Road_{kind}_Straight_01"] = lambda r=road, s=side: straight(r, s)
    models[f"Stage_Road_{kind}_Cross_01"] = lambda r=road, s=side: cross(r, s)
    # 歩道のない細い道の T 字路は十字路と同じ形になるので作らない
    if side:
        models[f"Stage_Road_{kind}_T_01"] = lambda r=road, s=side: t_junction(r, s)

result = {}
for name, make in models.items():
    km.clear_scene()
    root = make().build(name)
    result[name] = {c.name: sum(len(p.vertices) - 2 for p in c.data.polygons) for c in root.children}
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
