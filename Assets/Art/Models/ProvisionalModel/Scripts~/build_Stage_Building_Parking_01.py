"""建物 4 立体駐車場を、VoxelBuilding で積む階ごとのモデルに分けて作る。

40 × 28 m で、地上・2〜4 階・屋上の 5 層。外周は腰の高さの壁だけで、北側に 1 階分を上がるスロープがある。屋上は腰壁の一部がまだ立っていない。
床は各階の天井側に張る。スロープを 1 つの階のモデルの中で上の階の床までつなぐ為。1 階は地面の上に立ち、上の階はその下の階の床の上に載る。
区間の並び：Ground ×1、Middle ×3、Roof ×1。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Parking"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 100, 70
# 1 階の高さと床の厚さ（セル数）
STOREY, SLAB = 10, 2
# 柱の一辺と、柱を立てる位置（セル数）
COLUMN = 4
COLUMN_X, COLUMN_Y = (0, 32, 64, 96), (0, 33, 66)
# 外周の腰壁の厚さと高さ（セル数）
PARAPET_DEPTH, PARAPET_HEIGHT = 2, 3
# スロープの Y の範囲と X の始まり、段の数と 1 段の長さ（セル数）。段は 1 セルずつ上がり、最後の段が上の階の床の高さになる
RAMP_Y, RAMP_X, RAMP_STEPS, RAMP_RUN = (52, 66), 20, 10, 5
# 屋上で腰壁がまだ立っていない区間（北の面の X の範囲、セル数）。鉄筋だけが立つ
ROOF_UNBUILT_X = (40, 96)

WALL_TOP = STOREY - SLAB
RAMP_END_X = RAMP_X + RAMP_STEPS * RAMP_RUN


def columns(boxes):
    for x in COLUMN_X:
        for y in COLUMN_Y:
            boxes.append(("Concrete", (x, y, 0), (x + COLUMN, y + COLUMN, WALL_TOP)))


def gaps(length, positions):
    """0..length のうち、positions に立つ柱を除いた区間。"""
    result, start = [], 0
    for p in positions:
        if p > start:
            result.append((start, p))
        start = p + COLUMN
    if start < length:
        result.append((start, length))
    return result


def slab(boxes):
    """天井側の床。スロープの上だけ穴を空ける。"""
    y0, y1 = RAMP_Y
    boxes += [
        ("Concrete", (0, 0, WALL_TOP), (WIDTH, y0, STOREY)),
        ("Concrete", (0, y1, WALL_TOP), (WIDTH, DEPTH, STOREY)),
        ("Concrete", (0, y0, WALL_TOP), (RAMP_X, y1, STOREY)),
        ("Concrete", (RAMP_END_X, y0, WALL_TOP), (WIDTH, y1, STOREY)),
    ]


def ramp(boxes):
    """床から上の階の床の高さまで、1 セルずつ上がる段。段の厚さは 2 セル。"""
    y0, y1 = RAMP_Y
    for i in range(1, RAMP_STEPS + 1):
        x = RAMP_X + (i - 1) * RAMP_RUN
        boxes.append(("Concrete", (x, y0, max(0, i - 2)), (x + RAMP_RUN, y1, i)))


def parapet(boxes, skip_north=None):
    """外周の腰壁。下の階の床の上に、外周の柱の間を埋めて立てる。skip_north の区間は根元だけ打ち、鉄筋を立てる。"""
    d, h = PARAPET_DEPTH, PARAPET_HEIGHT
    for a, b in gaps(WIDTH, COLUMN_X):
        boxes.append(("Concrete", (a, 0, 0), (b, d, h)))
        if skip_north is None:
            boxes.append(("Concrete", (a, DEPTH - d, 0), (b, DEPTH, h)))
            continue
        # 腰壁の立っていない区間と重なる所は、根元の 1 セルだけにする
        lo, hi = max(a, skip_north[0]), min(b, skip_north[1])
        if lo >= hi:
            boxes.append(("Concrete", (a, DEPTH - d, 0), (b, DEPTH, h)))
            continue
        if a < lo:
            boxes.append(("Concrete", (a, DEPTH - d, 0), (lo, DEPTH, h)))
        if hi < b:
            boxes.append(("Concrete", (hi, DEPTH - d, 0), (b, DEPTH, h)))
        boxes.append(("Concrete", (lo, DEPTH - d, 0), (hi, DEPTH, 1)))
        boxes += [("Rebar", (x, DEPTH - d, 1), (x + 1, DEPTH - d + 1, h + 3)) for x in range(lo + 2, hi - 1, 6)]
    for a, b in gaps(DEPTH, COLUMN_Y):
        boxes.append(("Concrete", (0, a, 0), (d, b, h)))
        boxes.append(("Concrete", (WIDTH - d, a, 0), (WIDTH, b, h)))


def ground():
    boxes = []
    columns(boxes)
    slab(boxes)
    ramp(boxes)
    return boxes


def middle():
    boxes = []
    parapet(boxes)
    columns(boxes)
    slab(boxes)
    ramp(boxes)
    return boxes


def roof():
    """屋上の駐車階。腰壁と、腰壁の高さまでの柱の根元だけ。北の面の一部は腰壁がまだ立っておらず、鉄筋が出ている。"""
    boxes = []
    parapet(boxes, skip_north=ROOF_UNBUILT_X)
    for x in COLUMN_X:
        for y in COLUMN_Y:
            if x in (COLUMN_X[0], COLUMN_X[-1]) or y in (COLUMN_Y[0], COLUMN_Y[-1]):
                boxes.append(("Concrete", (x, y, 0), (x + COLUMN, y + COLUMN, PARAPET_HEIGHT)))
    return boxes


SIZE = (WIDTH, DEPTH, STOREY)
result = km.build_floor_models(MODEL_PREFIX, [
    ("Ground", ground(), SIZE),
    ("Middle", middle(), SIZE),
    ("Roof", roof(), (WIDTH, DEPTH, PARAPET_HEIGHT + 3)),
])
