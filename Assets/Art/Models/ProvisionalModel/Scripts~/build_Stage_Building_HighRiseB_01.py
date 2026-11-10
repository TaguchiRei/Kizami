"""建物 2 大型ビル B を、VoxelBuilding で積む階ごとのモデルに分けて作る。

36 m → 28 m → 20 m と段々に細くなる 15 階建て。36 m と 28 m の区間は外装まで仕上がり、20 m の区間は柱と床だけ。
段差の階は、下の区間の大きさの床を張って外側をテラスにし、縁に手すり壁を立てる。床は各階の床側（下）に張る。
区間の並び：Ground ×1、Tier36 ×5、Setback28 ×1、Tier28 ×4、Setback20 ×1、Frame20 ×2、Top20 ×1。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_HighRiseB"
# 区間ごとの平面の一辺（セル数）
WIDE, MIDDLE, NARROW = 90, 70, 50
# 1 階の高さ（セル数）
STOREY = 10
# 床の厚さ・腰壁の高さ・腰壁の奥行き・ガラスの奥行き（セル数）
SLAB, SPANDREL, SPANDREL_DEPTH, GLASS_DEPTH = 2, 3, 3, 2
# 柱の一辺と、柱の間の数（セル数）
COLUMN, SPANS = 4, 4
# 中央のコアの一辺と壁の厚さ（セル数）。どの区間でも平面の中心にそろえる
CORE, CORE_WALL = 20, 3
# 最上階のコアの壁の上端。鉄筋はその上、階の天端まで突き出す
TOP_CORE_TOP = 6
# テラスの縁の手すり壁の厚さと高さ（セル数）
PARAPET_DEPTH, PARAPET_HEIGHT = 2, 3


def rotate(lo, hi, side, size):
    """南面（Y=0 側）基準の箱を、平面の中心のまわりに side × 90° 回す。"""
    for _ in range(side):
        lo, hi = (size - hi[1], lo[0], lo[2]), (size - lo[1], hi[0], hi[2])
    return lo, hi


def column_positions(size):
    return [round(i * (size - COLUMN) / SPANS) for i in range(SPANS + 1)]


def core_range(size):
    lo = (size - CORE) // 2
    return lo, lo + CORE


def in_core(x, y, size):
    """(x, y) に立つ柱がコアと重なるか。"""
    lo, hi = core_range(size)
    return x + COLUMN > lo and x < hi and y + COLUMN > lo and y < hi


def slab(boxes, size, z, offset=0, outer=None):
    """コアの穴を空けた床を張る。outer を渡すと、外側に outer の大きさまで広げる（テラス）。offset は平面の中での位置のずれ。"""
    lo, hi = core_range(size)
    top = z + SLAB
    span = outer if outer is not None else size
    shift = (span - size) // 2
    o = offset - shift
    parts = [
        ((0, 0), (span, lo + shift)),
        ((0, lo + shift), (lo + shift, hi + shift)),
        ((hi + shift, lo + shift), (span, hi + shift)),
        ((0, hi + shift), (span, span)),
    ]
    boxes += [("Concrete", (a[0] + o, a[1] + o, z), (b[0] + o, b[1] + o, top)) for a, b in parts]


def core(boxes, size, offset, concrete_top, rebar_bottom, rebar_top):
    """コアの 4 枚の壁。真ん中の層に鉄筋を通し、上下の階のコアとつなげる。"""
    lo, hi = core_range(size)
    w = CORE_WALL
    mid = lo + w // 2
    for side in range(4):
        a, b = (lo, hi) if side % 2 == 0 else (lo + w, hi - w)
        local = [
            ("Concrete", (a, lo, 0), (b, lo + 1, concrete_top)),
            ("Concrete", (a, lo + w - 1, 0), (b, lo + w, concrete_top)),
            ("Concrete", (a, mid, 0), (a + 1, mid + 1, concrete_top)),
            ("Concrete", (b - 1, mid, 0), (b, mid + 1, concrete_top)),
            ("Rebar", (a + 1, mid, rebar_bottom), (b - 1, mid + 1, rebar_top)),
        ]
        if rebar_bottom > 0:
            local.append(("Concrete", (a + 1, mid, 0), (b - 1, mid + 1, rebar_bottom)))
        for mat, p, q in local:
            lo_r, hi_r = rotate(p, q, side, size)
            boxes.append((mat, (lo_r[0] + offset, lo_r[1] + offset, lo_r[2]), (hi_r[0] + offset, hi_r[1] + offset, hi_r[2])))


def finished_storey(boxes, size, offset, z0, lobby=False):
    """外装まで仕上がった階の、床より上。四隅の柱、腰壁、ガラス、ガラスの前の縦のフィン、内側の柱。lobby なら腰壁なしで床から天井までガラス。"""
    positions = column_positions(size)
    inner = positions[1:-1]
    window = z0 if lobby else z0 + SPANDREL
    top = z0 + STOREY - SLAB
    for side in range(4):
        local = [
            ("Concrete", (0, 0, z0), (COLUMN, COLUMN, top)),
            ("Glass", (COLUMN, GLASS_DEPTH, window), (size - COLUMN, GLASS_DEPTH * 2, top)),
        ]
        if not lobby:
            local.append(("Concrete", (COLUMN, 0, z0), (size - COLUMN, SPANDREL_DEPTH, window)))
        local += [("Concrete", (x, 0, window), (x + COLUMN, GLASS_DEPTH, top)) for x in inner]
        for mat, p, q in local:
            lo_r, hi_r = rotate(p, q, side, size)
            boxes.append((mat, (lo_r[0] + offset, lo_r[1] + offset, lo_r[2]), (hi_r[0] + offset, hi_r[1] + offset, hi_r[2])))
    for x in inner:
        for y in inner:
            if not in_core(x, y, size):
                boxes.append(("Concrete", (x + offset, y + offset, z0), (x + COLUMN + offset, y + COLUMN + offset, top)))


def frame_columns(boxes, size, offset, z0, top, rebar=False, short_side_y=None):
    """柱と床だけの階の柱。rebar なら柱の上から鉄筋を突き出す。short_side_y より北の柱は低い打ちかけにする。"""
    for x in column_positions(size):
        for y in column_positions(size):
            if in_core(x, y, size):
                continue
            column_top = top
            if short_side_y is not None and y >= short_side_y:
                column_top = z0 + 2
            boxes.append(("Concrete", (x + offset, y + offset, z0), (x + COLUMN + offset, y + COLUMN + offset, column_top)))
            if rebar:
                boxes.append(("Rebar", (x + 1 + offset, y + 1 + offset, column_top), (x + 3 + offset, y + 3 + offset, column_top + 4)))


def parapet(boxes, outer, inner):
    """テラスの縁の手すり壁。outer の大きさの床の外周に、床の上から立てる。"""
    z0, z1 = SLAB, SLAB + PARAPET_HEIGHT
    for side in range(4):
        lo_r, hi_r = rotate((0, 0, z0), (outer - PARAPET_DEPTH, PARAPET_DEPTH, z1), side, outer)
        boxes.append(("Concrete", lo_r, hi_r))


def ground():
    boxes = []
    core(boxes, WIDE, 0, STOREY, 1, STOREY)
    slab(boxes, WIDE, 0)
    finished_storey(boxes, WIDE, 0, SLAB, lobby=True)
    return boxes


def tier(size):
    boxes = []
    core(boxes, size, 0, STOREY, 0, STOREY)
    slab(boxes, size, 0)
    finished_storey(boxes, size, 0, SLAB)
    return boxes


def setback(lower, size, finished):
    """段差の階。下の区間の大きさの床を張り、縁に手すり壁、内側に size の大きさの階を立てる。モデルの平面は下の区間の大きさ。"""
    boxes = []
    offset = (lower - size) // 2
    core(boxes, size, offset, STOREY, 0, STOREY)
    slab(boxes, size, 0, offset, outer=lower)
    parapet(boxes, lower, size)
    if finished:
        finished_storey(boxes, size, offset, SLAB)
    else:
        frame_columns(boxes, size, offset, SLAB, STOREY)
    return boxes


def frame():
    boxes = []
    core(boxes, NARROW, 0, STOREY, 0, STOREY)
    slab(boxes, NARROW, 0)
    frame_columns(boxes, NARROW, 0, SLAB, STOREY)
    return boxes


def top():
    """最上階。北側の柱は打ちかけで低く、柱とコアの上から鉄筋が突き出す。"""
    boxes = []
    core(boxes, NARROW, 0, TOP_CORE_TOP, 0, STOREY)
    slab(boxes, NARROW, 0)
    _, core_hi = core_range(NARROW)
    frame_columns(boxes, NARROW, 0, SLAB, STOREY - 4, rebar=True, short_side_y=core_hi)
    return boxes


result = km.build_floor_models(MODEL_PREFIX, [
    ("Ground", ground(), (WIDE, WIDE, STOREY)),
    ("Tier36", tier(WIDE), (WIDE, WIDE, STOREY)),
    ("Setback28", setback(WIDE, MIDDLE, True), (WIDE, WIDE, STOREY)),
    ("Tier28", tier(MIDDLE), (MIDDLE, MIDDLE, STOREY)),
    ("Setback20", setback(MIDDLE, NARROW, False), (MIDDLE, MIDDLE, STOREY)),
    ("Frame20", frame(), (NARROW, NARROW, STOREY)),
    ("Top20", top(), (NARROW, NARROW, STOREY)),
])
