"""窓のガラスの板（Stage_Glass_Pane_01）と、あらかじめ割った破片を作る。幅 1.6 × 高さ 2.0 × 厚さ 0.05 m。

ガラスはボクセル化しないので、グリッドと厚みの決まりは適用しない。割れ方は、板の中心を起点にした放射状のボロノイ分割で固定する。
ルートの下に、割れる前の板 `Intact` と、破片をまとめた `Shards` を置く。破片は Rigidbody で飛ばすので、原点をそれぞれの重心に置く。
"""
import importlib
import math
import os
import random
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_NAME = "Stage_Glass_Pane_01"
# 板の幅・高さ・厚さ（m）
WIDTH, HEIGHT, THICKNESS = 1.6, 2.0, 0.05
# 割れの起点（板の中心からのずれ、m）
IMPACT = (0.0, 0.0)
# 起点のまわりに種を置く輪の半径（m）と、輪ごとの種の数
RINGS = ((0.12, 5), (0.4, 7), (0.8, 9), (1.3, 11))
# 種の角度と半径をばらつかせる割合
JITTER = 0.35
# 割れ方を固定する乱数の種
RANDOM_SEED = 7


def seeds():
    rng = random.Random(RANDOM_SEED)
    cx, cy = IMPACT[0], HEIGHT / 2 + IMPACT[1]
    points = [(cx, cy)]
    for radius, count in RINGS:
        offset = rng.random() * math.tau
        for i in range(count):
            a = offset + (i + rng.uniform(-JITTER, JITTER) * 0.5) * math.tau / count
            r = radius * (1 + rng.uniform(-JITTER, JITTER))
            x, y = cx + math.cos(a) * r, cy + math.sin(a) * r
            if -WIDTH / 2 < x < WIDTH / 2 and 0 < y < HEIGHT:
                points.append((x, y))
    return points


def clip(polygon, site, other):
    """凸多角形を、site 側に残る、site と other の垂直二等分線の半平面で切る。"""
    nx, ny = other[0] - site[0], other[1] - site[1]
    mx, my = (site[0] + other[0]) / 2, (site[1] + other[1]) / 2

    def side(p):
        return (p[0] - mx) * nx + (p[1] - my) * ny

    result = []
    for i, p in enumerate(polygon):
        q = polygon[(i + 1) % len(polygon)]
        sp, sq = side(p), side(q)
        if sp <= 0:
            result.append(p)
        if (sp < 0) != (sq < 0) and sp != sq:
            t = sp / (sp - sq)
            result.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return result


def voronoi_cells(points):
    rect = [(-WIDTH / 2, 0.0), (WIDTH / 2, 0.0), (WIDTH / 2, HEIGHT), (-WIDTH / 2, HEIGHT)]
    cells = []
    for i, site in enumerate(points):
        cell = rect
        for j, other in enumerate(points):
            if i != j:
                cell = clip(cell, site, other)
        if len(cell) >= 3:
            cells.append(cell)
    return cells


def prism(name, polygon, parent):
    """XZ 平面の多角形を Y 方向に厚さ分押し出した、閉じたメッシュ。原点を多角形の重心に置く。"""
    n = len(polygon)
    cx = sum(p[0] for p in polygon) / n
    cz = sum(p[1] for p in polygon) / n
    half = THICKNESS / 2
    verts = [(x - cx, -half, z - cz) for x, z in polygon] + [(x - cx, half, z - cz) for x, z in polygon]
    # 多角形は XZ で反時計回り。-Y の面は外から見て反時計回りになる順、+Y の面はその逆
    faces = [list(range(n)), list(range(2 * n - 1, n - 1, -1))]
    faces += [[i, n + i, n + (i + 1) % n, (i + 1) % n] for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = (cx, 0.0, cz)
    obj.parent = parent
    mat = bpy.data.materials.get("Glass") or bpy.data.materials.new("Glass")
    mat.diffuse_color = km.MATERIAL_PREVIEW_COLORS["Glass"]
    mesh.materials.append(mat)
    return obj


def make_empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    return obj


km.clear_scene()
root = make_empty(MODEL_NAME)
rect = [(-WIDTH / 2, 0.0), (WIDTH / 2, 0.0), (WIDTH / 2, HEIGHT), (-WIDTH / 2, HEIGHT)]
prism(f"{MODEL_NAME}_Intact", rect, root)
shards_root = make_empty(f"{MODEL_NAME}_Shards", root)
cells = voronoi_cells(seeds())
for i, cell in enumerate(cells):
    prism(f"{MODEL_NAME}_Shard_{i:02d}", cell, shards_root)

result = {
    "shards": len(cells),
    "tris": sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'),
    "area_error": round(sum(
        abs(sum(c[k][0] * c[(k + 1) % len(c)][1] - c[(k + 1) % len(c)][0] * c[k][1] for k in range(len(c)))) / 2
        for c in cells) - WIDTH * HEIGHT, 6),
}
if os.environ.get("KIZAMI_EXPORT") == "1":
    km.save_and_export(root, km.STAGE_DIR)
