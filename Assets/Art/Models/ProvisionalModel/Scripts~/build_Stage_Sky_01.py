"""奈落のフォグの板（Stage_Terrain_AbyssFog_01）とバリアのドーム（Stage_Far_BarrierDome_01）を作る。どちらもシェーダーで描く為の面だけのメッシュで、UV は使わない。

フォグの板は、地面の外周から外壁の内側までを覆う水平な環で、上向きの面だけを張る。原点はマップの中心で、置く高さは Unity 側で決める。
ドームは外壁の上まで覆う半球で、面は外向き。シェーダーは裏面を描くので、内側から見える。
"""
import importlib
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# フォグの板の内側・外側の半径（m）と、円周の分割数
FOG_INNER, FOG_OUTER, FOG_SEGMENTS = 340.0, 610.0, 64
# ドームの半径（m）と、方位・仰角の分割数
DOME_RADIUS, DOME_AZIMUTH, DOME_ELEVATION = 620.0, 48, 12


def abyss_fog():
    verts, faces = [], []
    for i in range(FOG_SEGMENTS):
        a = 2 * math.pi * i / FOG_SEGMENTS
        c, s = math.cos(a), math.sin(a)
        verts += [(FOG_INNER * c, FOG_INNER * s, 0), (FOG_OUTER * c, FOG_OUTER * s, 0)]
    for i in range(FOG_SEGMENTS):
        p, q = 2 * i, 2 * ((i + 1) % FOG_SEGMENTS)
        faces.append((p, p + 1, q + 1, q))
    b = km.MeshBuilder()
    b.add("AbyssFog", verts, faces)
    return b


def barrier_dome():
    """仰角 0（地平線）から頂点までを張る。頂点は 1 点にまとめる。"""
    verts, faces = [], []
    for j in range(DOME_ELEVATION):
        e = 0.5 * math.pi * j / DOME_ELEVATION
        for i in range(DOME_AZIMUTH):
            a = 2 * math.pi * i / DOME_AZIMUTH
            verts.append((DOME_RADIUS * math.cos(e) * math.cos(a), DOME_RADIUS * math.cos(e) * math.sin(a), DOME_RADIUS * math.sin(e)))
    top = len(verts)
    verts.append((0, 0, DOME_RADIUS))
    for j in range(DOME_ELEVATION):
        for i in range(DOME_AZIMUTH):
            n = (i + 1) % DOME_AZIMUTH
            if j + 1 < DOME_ELEVATION:
                faces.append((j * DOME_AZIMUTH + i, j * DOME_AZIMUTH + n, (j + 1) * DOME_AZIMUTH + n, (j + 1) * DOME_AZIMUTH + i))
            else:
                faces.append((j * DOME_AZIMUTH + i, j * DOME_AZIMUTH + n, top))
    b = km.MeshBuilder()
    b.add("BarrierDome", verts, faces)
    return b


models = {"Stage_Terrain_AbyssFog_01": abyss_fog, "Stage_Far_BarrierDome_01": barrier_dome}

result = {}
for name, make in models.items():
    km.clear_scene()
    root = make().build(name)
    result[name] = sum(len(p.vertices) - 2 for c in root.children for p in c.data.polygons)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
