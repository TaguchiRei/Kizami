"""建物 7 商店街のアーケードの 1 モジュールを、VoxelBuilding で積む階ごとのモデルに分けて作る。

モジュールは通りの真ん中で東西の 2 棟に分ける。片側の店舗だけを壊したとき、その上の階が反対側の店舗に支えられて宙に残らないようにする為。
通りに架ける鉄骨の梁と屋根板も、通りの真ん中で切れる。

幅 30.4 m × 通りに沿った長さ 12 m。東西に奥行き 8 m の 2 階建ての店舗、真ん中に幅 14.4 m の通り、その上に鉄骨の屋根を架ける。
モジュールは通り（Y）の向きにつないで延ばす。店舗の間仕切りは各モジュールの南端にだけ置き、隣のモジュールと続いた店並みにする。
屋根の鉄骨は、通りに面した店舗の壁の上に柱を立てて架ける。屋根は建設途中で、屋根板は南半分だけ張ってある。
端のモジュールは、北端を店舗の壁で閉じ、看板を掛ける門の骨組みを立てる。床は各階の床側（下）に張る。
1 棟の区間は、直線のモジュールが <West/East>Ground ×1、<West/East>Upper ×1、<West/East>Roof ×1。端のモジュールは末尾に End が付く。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Arcade"
# モジュールの幅（X）と、通りに沿った長さ（Y）（セル数）
WIDTH, LENGTH = 76, 30
# 1 階の高さと床の厚さと壁の厚さ（セル数）
STOREY, SLAB, WALL = 10, 2, 3
# 店舗の奥行き（セル数）。西の店舗は X 0..SHOP、東の店舗は X WIDTH-SHOP..WIDTH
SHOP = 20
# 店先の開口（Y の範囲）と、1 階の開口の上端・2 階の窓の上下（セル数）
FRONT, SHOPFRONT_TOP, WINDOW = (5, 27), 8, (5, 9)
# 屋根の鉄骨。柱を立てる Y の位置・柱と梁の太さ・梁の高さ・屋根板を張り終えた Y の範囲（セル数）
POSTS_Y, STEEL, BEAM_Z, ROOFED_Y = (2, 16), 2, 12, (0, 16)
# 端のモジュールの門の骨組みの Y の位置と、梁の高さ（セル数）
GATE_Y, GATE_Z = 27, 18

EAST = WIDTH - SHOP
# 屋根の柱を立てる X（通りに面した店舗の壁の上）
POST_X = (SHOP - STEEL, EAST)
ROOF_HEIGHT = BEAM_Z + STEEL * 2


def shop_walls(boxes, z0, z1, front_openings, end, cap_bottom=False):
    """東西の店舗の壁。外側の壁と南の間仕切りは鉄筋入り、通りに面した壁は開口を開ける。end なら北端も壁で閉じる。"""
    for x0 in (0, EAST):
        street_x = SHOP - WALL if x0 == 0 else EAST
        outer_x = 0 if x0 == 0 else WIDTH - WALL
        km.wall(boxes, "y", 0, LENGTH, outer_x, z0, z1, cap_bottom=cap_bottom)
        km.wall_with_openings(boxes, "y", 0, LENGTH, street_x, z0, z1, front_openings, cap_bottom)
        inner0 = WALL if x0 == 0 else EAST + WALL
        km.wall(boxes, "x", inner0, inner0 + SHOP - 2 * WALL, 0, z0, z1, cap_bottom=cap_bottom)
        if end:
            km.wall(boxes, "x", inner0, inner0 + SHOP - 2 * WALL, LENGTH - WALL, z0, z1, cap_bottom=cap_bottom)


def slabs(boxes):
    boxes += [
        ("Concrete", (0, 0, 0), (SHOP, LENGTH, SLAB)),
        ("Concrete", (EAST, 0, 0), (WIDTH, LENGTH, SLAB)),
    ]


def ground(end):
    boxes = []
    slabs(boxes)
    shop_walls(boxes, SLAB, STOREY, [(FRONT[0], FRONT[1], SLAB, SHOPFRONT_TOP, True)], end)
    return boxes


def upper(end):
    boxes = []
    slabs(boxes)
    shop_walls(boxes, SLAB, STOREY, [(FRONT[0], FRONT[1], SLAB + WINDOW[0], SLAB + WINDOW[1], True)], end)
    return boxes


def roof(gate):
    """店舗の屋根と、通りに架ける鉄骨の屋根。gate なら北端に門の骨組みを立てる。"""
    boxes = []
    slabs(boxes)
    s = STEEL
    for y in POSTS_Y:
        for x in POST_X:
            boxes.append(("Metal", (x, y, SLAB), (x + s, y + s, BEAM_Z)))
        boxes.append(("Metal", (POST_X[0], y, BEAM_Z), (POST_X[1] + s, y + s, BEAM_Z + s)))
    for x in POST_X:
        boxes += [
            ("Metal", (x, 0, BEAM_Z), (x + s, POSTS_Y[0], BEAM_Z + s)),
            ("Metal", (x, POSTS_Y[0] + s, BEAM_Z), (x + s, POSTS_Y[1], BEAM_Z + s)),
            ("Metal", (x, POSTS_Y[1] + s, BEAM_Z), (x + s, LENGTH, BEAM_Z + s)),
        ]
    boxes.append(("Metal", (POST_X[0], ROOFED_Y[0], BEAM_Z + s), (POST_X[1] + s, ROOFED_Y[1], BEAM_Z + 2 * s)))
    if gate:
        for x in POST_X:
            boxes.append(("Metal", (x, GATE_Y, SLAB), (x + s, GATE_Y + s, BEAM_Z)))
            boxes.append(("Metal", (x, GATE_Y, BEAM_Z + s), (x + s, GATE_Y + s, GATE_Z)))
        boxes.append(("Metal", (POST_X[0] + s, GATE_Y, GATE_Z - s), (POST_X[1], GATE_Y + s, GATE_Z)))
    return boxes


def split(boxes):
    """モジュールの箱を、通りの真ん中で西の棟と東の棟に分ける。東の棟の箱は、棟の西端が原点になるようずらす。"""
    middle = WIDTH // 2
    west, east = [], []
    for mat, lo, hi in boxes:
        if lo[0] < middle:
            west.append((mat, lo, (min(hi[0], middle), hi[1], hi[2])))
        if hi[0] > middle:
            east.append((mat, (max(lo[0], middle) - middle, lo[1], lo[2]), (hi[0] - middle, hi[1], hi[2])))
    return west, east


HALF = WIDTH // 2
parts = []
for name, boxes, height in (
    ("Ground", ground(False), STOREY),
    ("Upper", upper(False), STOREY),
    ("Roof", roof(False), ROOF_HEIGHT),
    ("GroundEnd", ground(True), STOREY),
    ("UpperEnd", upper(True), STOREY),
    ("RoofEnd", roof(True), GATE_Z),
):
    west, east = split(boxes)
    parts += [("West" + name, west, (HALF, LENGTH, height)), ("East" + name, east, (HALF, LENGTH, height))]
result = km.build_floor_models(MODEL_PREFIX, parts)
