"""建物 6 アミューズメント施設を、VoxelBuilding で積む階ごとのモデルに分けて作る。

40 × 32 m、高さ 16 m の天井の高い大きな箱で、1 階 8 m の 2 階建て。各階は下と上の 2 パーツに分けて作る（kizami_model.storey_parts）。南の正面にガラスの入口、北に搬入口がある。中は柱が少なく、北側の 2 段（高さ 4 m と 8 m）に中 2 階の床がある。
屋上の正面側に、看板を掛ける鉄骨の骨組みが立つ（建設途中で看板はまだない）。床は各階の床側（下）に張り、屋根は最上階の天井側に張る。
区間の並び：Ground ×1、Top ×1。各区間は Lower と Upper の組を積む。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Amusement"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 100, 80
# 1 階の高さと床の厚さと壁の厚さ（セル数）
STOREY, SLAB, WALL = 20, 2, 3
# 中の柱の一辺と位置（セル数）
COLUMN = 4
COLUMN_X, COLUMN_Y = (32, 64), (26, 52)
# 南の正面の入口（X の範囲、セル数）と高さ
ENTRANCES, ENTRANCE_TOP = ((20, 40), (60, 80)), 8
# 北の搬入口（X の範囲、セル数）と高さ
SERVICE_DOOR, SERVICE_TOP = (45, 57), 8
# 中 2 階の床の Y の始まり（セル数）。そこから北の壁まで。中の柱より北に置く
MEZZANINE_Y = 57
# 1 階の中 2 階の床の高さ（セル数）
MEZZANINE_Z = 10
# 屋上の看板の骨組み。柱を立てる X の位置・柱と梁の太さ・高さ（セル数）
SIGN_POSTS, SIGN_BEAM, SIGN_HEIGHT = (6, 34, 62, 90), 2, 10

ROOF_TOP = STOREY
TOP_HEIGHT = ROOF_TOP + SIGN_HEIGHT


def outer_walls(boxes, z0, z1, south_openings=(), north_openings=(), cap_bottom=False):
    """外周の 4 枚の壁。南北の壁は角まで、東西の壁はその間に入る。"""
    km.wall_with_openings(boxes, "x", 0, WIDTH, 0, z0, z1, south_openings, cap_bottom)
    km.wall_with_openings(boxes, "x", 0, WIDTH, DEPTH - WALL, z0, z1, north_openings, cap_bottom)
    km.wall(boxes, "y", WALL, DEPTH - WALL, 0, z0, z1, cap_bottom=cap_bottom)
    km.wall(boxes, "y", WALL, DEPTH - WALL, WIDTH - WALL, z0, z1, cap_bottom=cap_bottom)


def columns(boxes, z0, z1):
    for x in COLUMN_X:
        for y in COLUMN_Y:
            boxes.append(("Concrete", (x, y, z0), (x + COLUMN, y + COLUMN, z1)))


def mezzanine(boxes, z):
    """北側の中 2 階の床。外周の壁の内側に張る。"""
    boxes.append(("Concrete", (WALL, MEZZANINE_Y, z), (WIDTH - WALL, DEPTH - WALL, z + SLAB)))


def ground():
    """1 階。南の入口と北の搬入口、高さ 4 m の中 2 階。"""
    boxes = [("Concrete", (0, 0, 0), (WIDTH, DEPTH, SLAB))]
    entrances = [(a, b, SLAB, ENTRANCE_TOP, True) for a, b in ENTRANCES]
    service = [(SERVICE_DOOR[0], SERVICE_DOOR[1], SLAB, SERVICE_TOP, False)]
    outer_walls(boxes, SLAB, STOREY, entrances, service)
    columns(boxes, SLAB, STOREY)
    mezzanine(boxes, MEZZANINE_Z)
    return boxes


def top():
    """最上階と屋根。床の高さに中 2 階の床を張る。正面側の屋根の上に、看板を掛ける鉄骨の柱と梁を立てる。"""
    wall_top = ROOF_TOP - SLAB
    boxes = [("Concrete", (0, 0, wall_top), (WIDTH, DEPTH, ROOF_TOP))]
    mezzanine(boxes, 0)
    outer_walls(boxes, 0, wall_top)
    columns(boxes, 0, wall_top)
    b = SIGN_BEAM
    for x in SIGN_POSTS:
        boxes.append(("Metal", (x, 1, ROOF_TOP), (x + b, 1 + b, TOP_HEIGHT)))
    for x0, x1 in zip(SIGN_POSTS, SIGN_POSTS[1:]):
        boxes.append(("Metal", (x0 + b, 1, TOP_HEIGHT - b), (x1, 1 + b, TOP_HEIGHT)))
        boxes.append(("Metal", (x0 + b, 1, ROOF_TOP + 4), (x1, 1 + b, ROOF_TOP + 4 + b)))
    return boxes


SIZE = (WIDTH, DEPTH, STOREY)
result = km.build_floor_models(MODEL_PREFIX, [
    *km.storey_parts("Ground", ground(), SIZE),
    *km.storey_parts("Top", top(), (WIDTH, DEPTH, TOP_HEIGHT)),
])
