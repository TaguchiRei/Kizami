"""建物 1 大型ビル A を、VoxelBuilding で積む 3 つのモデルに分けて作る。

1 階（Ground）・中間階（Middle、8 回積む）・屋上（Roof、柱と床だけの 3 階分）で、積むと 32 × 32 × 48 m の 12 階建てになる。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_HighRiseA"
# 平面の一辺（セル数）
SIZE = 80
# 1 階の高さ（セル数）
STOREY = 10
# 中間階を積む回数と、屋上に含める階の数
MIDDLE_COUNT, ROOF_STOREYS = 8, 3
# 床の厚さ・腰壁の高さ・腰壁の奥行き・ガラスの奥行き（セル数）
SLAB, SPANDREL, SPANDREL_DEPTH, GLASS_DEPTH = 2, 3, 3, 2
# 柱の一辺（セル数）と、柱を立てる位置（各軸の最小のセル）
COLUMN = 4
COLUMN_POSITIONS = (0, 19, 38, 57, 76)
# 中央の階段・エレベーターのコアの範囲と壁の厚さ（セル数）
CORE_LO, CORE_HI, CORE_WALL = 30, 50, 3
# 屋上のコアの壁の上端。鉄筋はその上、屋上の天端まで突き出す
ROOF_CORE_TOP = 26
# 屋上の最上階で床を張り終えていない側（Y がこの値以上）
TOP_SLAB_UNFINISHED_Y = CORE_HI


def rotate(lo, hi, side):
    """南面（Y=0 側）基準の箱を、平面の中心のまわりに side × 90° 回す。"""
    for _ in range(side):
        lo, hi = (SIZE - hi[1], lo[0], lo[2]), (SIZE - lo[1], hi[0], hi[2])
    return lo, hi


def in_core(x, y):
    return CORE_LO <= x < CORE_HI and CORE_LO <= y < CORE_HI


def slab(boxes, z, skip_north=False):
    """コアの穴を空けた床を 4 枚の箱で張る。"""
    top = z + SLAB
    parts = [
        ((0, 0, z), (SIZE, CORE_LO, top)),
        ((0, CORE_LO, z), (CORE_LO, CORE_HI, top)),
        ((CORE_HI, CORE_LO, z), (SIZE, CORE_HI, top)),
    ]
    if not skip_north:
        parts.append(((0, CORE_HI, z), (SIZE, SIZE, top)))
    boxes += [("Concrete", lo, hi) for lo, hi in parts]


def core(boxes, concrete_top, rebar_bottom, rebar_top):
    """コアの 4 枚の壁。真ん中の層に鉄筋を通す。鉄筋は上下の階のコアとつながる。"""
    lo, hi, w = CORE_LO, CORE_HI, CORE_WALL
    mid = lo + w // 2
    # 南の壁を基準に、回して 4 面にする。南北の壁は角まで、東西の壁はその間に入る
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
            boxes.append((mat,) + rotate(p, q, side))


def inner_columns(boxes, z0, z1):
    for x in COLUMN_POSITIONS[1:-1]:
        for y in COLUMN_POSITIONS[1:-1]:
            if not in_core(x, y):
                boxes.append(("Concrete", (x, y, z0), (x + COLUMN, y + COLUMN, z1)))


def ground():
    """1 階。腰壁のないロビーで、床から天井までガラスにする。地面に接する鉄筋の下端は覆う。"""
    boxes = []
    core(boxes, STOREY, 1, STOREY)
    slab(boxes, 0)
    for side in range(4):
        local = [
            ("Concrete", (0, 0, SLAB), (COLUMN, COLUMN, STOREY)),
            ("Glass", (COLUMN, GLASS_DEPTH, SLAB), (SIZE - COLUMN, GLASS_DEPTH * 2, STOREY)),
        ]
        local += [("Concrete", (x, 0, SLAB), (x + COLUMN, GLASS_DEPTH, STOREY)) for x in COLUMN_POSITIONS[1:-1]]
        for mat, p, q in local:
            boxes.append((mat,) + rotate(p, q, side))
    inner_columns(boxes, SLAB, STOREY)
    return boxes


def middle():
    """外装まで仕上がった階。四隅の柱、腰壁、ガラス、ガラスの前の縦のフィン、内側の柱。"""
    boxes = []
    core(boxes, STOREY, 0, STOREY)
    slab(boxes, 0)
    window = SLAB + SPANDREL
    for side in range(4):
        local = [
            ("Concrete", (0, 0, SLAB), (COLUMN, COLUMN, STOREY)),
            ("Concrete", (COLUMN, 0, SLAB), (SIZE - COLUMN, SPANDREL_DEPTH, window)),
            ("Glass", (COLUMN, GLASS_DEPTH, window), (SIZE - COLUMN, GLASS_DEPTH * 2, STOREY)),
        ]
        local += [("Concrete", (x, 0, window), (x + COLUMN, GLASS_DEPTH, STOREY)) for x in COLUMN_POSITIONS[1:-1]]
        for mat, p, q in local:
            boxes.append((mat,) + rotate(p, q, side))
    inner_columns(boxes, SLAB, STOREY)
    return boxes


def roof():
    """柱と床だけの 3 階分。最上階は北側の床がまだなく、その側の柱は短い。柱とコアの上から鉄筋が突き出す。"""
    boxes = []
    height = ROOF_STOREYS * STOREY
    core(boxes, ROOF_CORE_TOP, 0, height)
    for k in range(ROOF_STOREYS):
        z = k * STOREY
        top_storey = k == ROOF_STOREYS - 1
        slab(boxes, z, skip_north=top_storey)
        for x in COLUMN_POSITIONS:
            for y in COLUMN_POSITIONS:
                if in_core(x, y):
                    continue
                if not top_storey:
                    boxes.append(("Concrete", (x, y, z + SLAB), (x + COLUMN, y + COLUMN, z + STOREY)))
                    continue
                # 打ちかけの柱。床のない側は床の高さから低く立ち上げる
                if y >= TOP_SLAB_UNFINISHED_Y:
                    column_lo, column_hi = z, z + 4
                else:
                    column_lo, column_hi = z + SLAB, z + STOREY - 4
                boxes.append(("Concrete", (x, y, column_lo), (x + COLUMN, y + COLUMN, column_hi)))
                boxes.append(("Rebar", (x + 1, y + 1, column_hi), (x + 3, y + 3, column_hi + 4)))
    return boxes


# モデルの名前の部分・箱を作る関数・高さ（セル数）
PARTS = (
    ("Ground", ground, STOREY),
    ("Middle", middle, STOREY),
    ("Roof", roof, ROOF_STOREYS * STOREY),
)

result = {}
for part, make, height in PARTS:
    km.clear_scene()
    root = km.build_model_from_boxes(f"{MODEL_PREFIX}_{part}_01", make(), (SIZE, SIZE, height))
    result[root.name] = km.report(root)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
