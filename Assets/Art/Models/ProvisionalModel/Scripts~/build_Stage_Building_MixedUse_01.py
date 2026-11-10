"""建物 8 中層の雑居ビルを、VoxelBuilding で積む階ごとのモデルに分けて作る。

幅 12 × 奥行き 16 m の 6 階建て。南の正面に 1 階の店先と各階の窓、北に窓があり、東西は隣の建物と接する窓のない壁。
最上階は建設途中で、壁は腰の高さまでしか立っておらず、壁の鉄筋が上へ突き出す。床は各階の床側（下）に張る。
区間の並び：Ground ×1、Middle ×4、Top ×1。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_MixedUse"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 30, 40
# 1 階の高さと床の厚さと壁の厚さ（セル数）
STOREY, SLAB, WALL = 10, 2, 3
# 1 階の店先（X の範囲）と高さ（セル数）
SHOPFRONT, SHOPFRONT_TOP = (5, 25), 9
# 南と北の窓（X の範囲）と、窓の下端・上端（床の上からのセル数）
SOUTH_WINDOWS, NORTH_WINDOWS, WINDOW = ((4, 13), (17, 26)), ((9, 21),), (3, 7)
# 最上階で壁を打ち終えた高さ（床の上からのセル数）と、鉄筋の上端
TOP_WALL, TOP_REBAR = 3, 8


def walls(boxes, z1, south, north, rebar_top=None):
    """外周の壁。南北の壁は角まで、東西の壁はその間に入る。rebar_top を渡すと、東西の壁の鉄筋が上へ突き出す。"""
    km.wall_with_openings(boxes, "x", 0, WIDTH, 0, SLAB, z1, south)
    km.wall_with_openings(boxes, "x", 0, WIDTH, DEPTH - WALL, SLAB, z1, north)
    km.wall(boxes, "y", WALL, DEPTH - WALL, 0, SLAB, z1, rebar_top)
    km.wall(boxes, "y", WALL, DEPTH - WALL, WIDTH - WALL, SLAB, z1, rebar_top)


def windows(ranges):
    return [(a, b, SLAB + WINDOW[0], SLAB + WINDOW[1], True) for a, b in ranges]


def slab():
    return [("Concrete", (0, 0, 0), (WIDTH, DEPTH, SLAB))]


def ground():
    boxes = slab()
    walls(boxes, STOREY, [(SHOPFRONT[0], SHOPFRONT[1], SLAB, SHOPFRONT_TOP, True)], windows(NORTH_WINDOWS))
    return boxes


def middle():
    boxes = slab()
    walls(boxes, STOREY, windows(SOUTH_WINDOWS), windows(NORTH_WINDOWS))
    return boxes


def top():
    boxes = slab()
    walls(boxes, SLAB + TOP_WALL, [], [], rebar_top=TOP_REBAR)
    return boxes


SIZE = (WIDTH, DEPTH, STOREY)
result = km.build_floor_models(MODEL_PREFIX, [
    ("Ground", ground(), SIZE),
    ("Middle", middle(), SIZE),
    ("Top", top(), SIZE),
])
