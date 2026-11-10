"""建物 9 小さい店舗の長屋を、VoxelBuilding で積む階ごとのモデルに分けて作る。

幅 16 × 奥行き 8 m の 2 階建てに、3 軒の店が並ぶ。1 階の店先はガラスで、上半分に鉄のシャッターが半分下りている。
2 階は住まいで、南に窓がある。東端の 1 軒は屋根がまだなく、壁の鉄筋が上へ突き出す。床は各階の床側（下）に張り、屋根は 2 階の天井側に張る。
区間の並び：Ground ×1、Top ×1。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Rowhouse"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 40, 20
# 1 階の高さと床の厚さと壁の厚さ（セル数）
STOREY, SLAB, WALL = 10, 2, 3
# 店の間仕切りの X の位置（セル数）。厚さは WALL
PARTITIONS = (13, 24)
# 1 階の店先の上端と、シャッターが下りている下端（床の上からのセル数）
SHOPFRONT_TOP, SHUTTER_BOTTOM = 7, 4
# 2 階の窓の下端・上端（床の上からのセル数）
WINDOW = (3, 6)
# 屋根のない東端の店の X の始まり（セル数）
UNROOFED_X = PARTITIONS[1]

WALL_TOP = STOREY - SLAB


def shop_ranges():
    """店ごとの、正面の壁の内側の X の範囲。"""
    edges = [WALL] + [p for x in PARTITIONS for p in (x, x + WALL)] + [WIDTH - WALL]
    return list(zip(edges[::2], edges[1::2]))


def walls(boxes, z0, z1, openings, rebar_top=None):
    """外周の壁と店の間仕切り。南の正面だけに開口を開ける。"""
    km.wall_with_openings(boxes, "x", 0, WIDTH, 0, z0, z1, openings)
    km.wall(boxes, "x", 0, WIDTH, DEPTH - WALL, z0, z1, rebar_top)
    for x in (0, WIDTH - WALL) + PARTITIONS:
        km.wall(boxes, "y", WALL, DEPTH - WALL, x, z0, z1, rebar_top)


def ground():
    boxes = [("Concrete", (0, 0, 0), (WIDTH, DEPTH, SLAB))]
    openings = [(a + 1, b - 1, SLAB, SLAB + SHOPFRONT_TOP, True) for a, b in shop_ranges()]
    walls(boxes, SLAB, STOREY, openings)
    # 半分下りたシャッター。開口のガラスの上半分を鉄に置き換える
    shutters = []
    for i, box in enumerate(boxes):
        mat, lo, hi = box
        if mat == "Glass":
            boxes[i] = (mat, lo, (hi[0], hi[1], SLAB + SHUTTER_BOTTOM))
            shutters.append(("Metal", (lo[0], lo[1], SLAB + SHUTTER_BOTTOM), hi))
    return boxes + shutters


def top():
    """2 階と屋根。東端の店は屋根がなく、壁の鉄筋が上へ突き出す。"""
    boxes = [("Concrete", (0, 0, 0), (WIDTH, DEPTH, SLAB))]
    openings = [(a + 2, b - 2, SLAB + WINDOW[0], SLAB + WINDOW[1], True) for a, b in shop_ranges()]
    walls(boxes, SLAB, WALL_TOP, openings)
    boxes.append(("Concrete", (0, 0, WALL_TOP), (UNROOFED_X + WALL, DEPTH, STOREY)))
    for x in range(UNROOFED_X + WALL + 2, WIDTH - WALL, 4):
        for y in (1, DEPTH - 2):
            boxes.append(("Rebar", (x, y, WALL_TOP), (x + 1, y + 1, STOREY)))
    return boxes


SIZE = (WIDTH, DEPTH, STOREY)
result = km.build_floor_models(MODEL_PREFIX, [
    ("Ground", ground(), SIZE),
    ("Top", top(), SIZE),
])
