"""建物 3 ガラス張りのビルを、VoxelBuilding で積む階ごとのモデルに分けて作る。

28 × 20 m の 10 階建て。柱を外周から内側に下げ、外周を床から天井までガラスにする（カーテンウォール）。上の 3 階はまだガラスがなく、床の縁が見える。
床は各階の床側（下）に張る。区間の並び：Ground ×1、Curtain ×6、Open ×2、Top ×1。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Glass"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 70, 50
# 1 階の高さと床の厚さ（セル数）
STOREY, SLAB = 10, 2
# ガラスの奥行き（セル数）。床の縁に張る
GLASS_DEPTH = 1
# 柱の一辺と、外周から柱を下げる距離（セル数）
COLUMN, INSET = 4, 5
# 柱の間の数（X、Y）
SPANS = (4, 2)
# 中央のコアの範囲（セル数）と壁の厚さ
CORE_X, CORE_Y, CORE_WALL = (27, 43), (17, 33), 3
# 最上階のコアの壁の上端。鉄筋はその上、階の天端まで突き出す
TOP_CORE_TOP = 6
# 1 階のロビーの、ガラスを外周から下げる距離（セル数）。上の階の床が庇になる
LOBBY_SETBACK = 4


def column_positions():
    xs = [INSET + round(i * (WIDTH - 2 * INSET - COLUMN) / SPANS[0]) for i in range(SPANS[0] + 1)]
    ys = [INSET + round(i * (DEPTH - 2 * INSET - COLUMN) / SPANS[1]) for i in range(SPANS[1] + 1)]
    return [(x, y) for x in xs for y in ys
            if not (CORE_X[0] - COLUMN < x < CORE_X[1] and CORE_Y[0] - COLUMN < y < CORE_Y[1])]


def slab(boxes):
    """コアの穴を空けた床を 4 枚の箱で張る。"""
    (x0, x1), (y0, y1) = CORE_X, CORE_Y
    parts = [
        ((0, 0), (WIDTH, y0)),
        ((0, y0), (x0, y1)),
        ((x1, y0), (WIDTH, y1)),
        ((0, y1), (WIDTH, DEPTH)),
    ]
    boxes += [("Concrete", (a[0], a[1], 0), (b[0], b[1], SLAB)) for a, b in parts]


def core(boxes, concrete_top, rebar_top):
    """コアの 4 枚の壁。南北の壁は真ん中の層に鉄筋を通し、上下の階のコアとつなげる。東西の壁はコンクリートだけ。"""
    (x0, x1), (y0, y1) = CORE_X, CORE_Y
    w = CORE_WALL
    for y in (y0, y1 - w):
        mid = y + 1
        boxes += [
            ("Concrete", (x0, y, 0), (x1, y + 1, concrete_top)),
            ("Concrete", (x0, y + 2, 0), (x1, y + 3, concrete_top)),
            ("Concrete", (x0, mid, 0), (x0 + 1, mid + 1, concrete_top)),
            ("Concrete", (x1 - 1, mid, 0), (x1, mid + 1, concrete_top)),
            ("Rebar", (x0 + 1, mid, 0), (x1 - 1, mid + 1, rebar_top)),
        ]
    for x in (x0, x1 - w):
        boxes.append(("Concrete", (x, y0 + w, 0), (x + w, y1 - w, concrete_top)))


def columns(boxes, top, rebar=False, short_from_y=None):
    for x, y in column_positions():
        column_top = top
        if short_from_y is not None and y >= short_from_y:
            column_top = SLAB + 2
        boxes.append(("Concrete", (x, y, SLAB), (x + COLUMN, y + COLUMN, column_top)))
        if rebar:
            boxes.append(("Rebar", (x + 1, y + 1, column_top), (x + 3, y + 3, column_top + 4)))


def curtain_wall(boxes, setback=0):
    """外周の床から天井までのガラス。南北の面は角まで、東西の面はその間に入る。"""
    z0, z1 = SLAB, STOREY
    s, d = setback, GLASS_DEPTH
    boxes += [
        ("Glass", (s, s, z0), (WIDTH - s, s + d, z1)),
        ("Glass", (s, DEPTH - s - d, z0), (WIDTH - s, DEPTH - s, z1)),
        ("Glass", (s, s + d, z0), (s + d, DEPTH - s - d, z1)),
        ("Glass", (WIDTH - s - d, s + d, z0), (WIDTH - s, DEPTH - s - d, z1)),
    ]


def ground():
    boxes = []
    core(boxes, STOREY, STOREY)
    slab(boxes)
    columns(boxes, STOREY)
    curtain_wall(boxes, LOBBY_SETBACK)
    return boxes


def curtain():
    boxes = []
    core(boxes, STOREY, STOREY)
    slab(boxes)
    columns(boxes, STOREY)
    curtain_wall(boxes)
    return boxes


def open_floor():
    """ガラスを張る前の階。床と柱とコアだけ。"""
    boxes = []
    core(boxes, STOREY, STOREY)
    slab(boxes)
    columns(boxes, STOREY)
    return boxes


def top():
    """最上階。北側の柱は打ちかけで低く、柱とコアの上から鉄筋が突き出す。"""
    boxes = []
    core(boxes, TOP_CORE_TOP, STOREY)
    slab(boxes)
    columns(boxes, STOREY - 4, rebar=True, short_from_y=CORE_Y[1])
    return boxes


SIZE = (WIDTH, DEPTH, STOREY)
result = km.build_floor_models(MODEL_PREFIX, [
    ("Ground", ground(), SIZE),
    ("Curtain", curtain(), SIZE),
    ("Open", open_floor(), SIZE),
    ("Top", top(), SIZE),
])
