"""建物 5 アパートを、VoxelBuilding で積む 3 つのモデルに分けて作る。

16 × 10.4 m、高さ 12 m の 3 階建て。南に外廊下、東に折り返しの外階段がある。1 階（Ground）・2 階（Middle）・建設途中の 3 階（Roof）。
床は各階の天井側に張る。1 階は地面の上に立ち、上の階はその下の階の床の上に載る。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Apartment"
# 平面の幅・奥行き（セル数）
WIDTH, DEPTH = 40, 26
# 1 階の高さと床の厚さ（セル数）
STOREY, SLAB = 10, 2
# 住戸の棟の東端と、外廊下の奥行き（セル数）
BODY_X, CORRIDOR = 34, 5
# 壁の厚さ（セル数）。妻壁は真ん中の層に鉄筋を通す
WALL = 3
# 住戸を分ける間仕切りの範囲（X、セル数）
PARTITION = (16, 18)
# 住戸の西端（X、セル数）
UNIT_STARTS = (WALL, PARTITION[1])
UNIT_WIDTH = PARTITION[0] - WALL
# 外廊下の手すり壁の厚さと高さ（セル数）
PARAPET_DEPTH, PARAPET_HEIGHT = 2, 3
# 外階段の 2 本の段の X の範囲と、踊り場の Y の始まり（セル数）
FLIGHT_UP, FLIGHT_DOWN, LANDING_Y = (34, 37), (37, 40), 15
# 建設途中の 3 階で、妻壁の打ち終えた高さ（セル数）。鉄筋はその上、階の天端まで突き出す
ROOF_GABLE_TOP = 6

WALL_TOP = STOREY - SLAB


def gable_walls(boxes, top, rebar_top):
    """東西の妻壁。外と内の層をコンクリート、真ん中の層を鉄筋にし、両端はコンクリートで覆う。"""
    y0, y1 = CORRIDOR, DEPTH
    for outer in (0, BODY_X - WALL):
        mid = outer + 1
        boxes += [
            ("Concrete", (outer, y0, 0), (outer + 1, y1, top)),
            ("Concrete", (outer + 2, y0, 0), (outer + 3, y1, top)),
            ("Concrete", (mid, y0, 0), (mid + 1, y0 + 1, top)),
            ("Concrete", (mid, y1 - 1, 0), (mid + 1, y1, top)),
            ("Rebar", (mid, y0 + 1, 0), (mid + 1, y1 - 1, rebar_top)),
        ]


def front_wall(boxes, s):
    """外廊下側の壁の 1 住戸分。玄関の開口と、ガラスの入った窓。"""
    y0, y1 = CORRIDOR, CORRIDOR + WALL
    door, window = (s + 2, s + 5), (s + 7, s + 11)
    boxes += [
        ("Concrete", (s, y0, 0), (door[0], y1, WALL_TOP)),
        ("Concrete", (door[0], y0, 6), (door[1], y1, WALL_TOP)),
        ("Concrete", (door[1], y0, 0), (window[0], y1, WALL_TOP)),
        ("Concrete", (window[0], y0, 0), (window[1], y1, 3)),
        ("Glass", (window[0], y0 + 1, 3), (window[1], y1, 6)),
        ("Concrete", (window[0], y0, 6), (window[1], y1, WALL_TOP)),
        ("Concrete", (window[1], y0, 0), (s + UNIT_WIDTH, y1, WALL_TOP)),
    ]


def back_wall(boxes, s):
    """北の壁の 1 住戸分。大きな掃き出し窓。"""
    y0, y1 = DEPTH - WALL, DEPTH
    window = (s + 3, s + 10)
    boxes += [
        ("Concrete", (s, y0, 0), (window[0], y1, WALL_TOP)),
        ("Concrete", (window[0], y0, 0), (window[1], y1, 2)),
        ("Glass", (window[0], y0 + 1, 2), (window[1], y1, 7)),
        ("Concrete", (window[0], y0, 7), (window[1], y1, WALL_TOP)),
        ("Concrete", (window[1], y0, 0), (s + UNIT_WIDTH, y1, WALL_TOP)),
    ]


def partition(boxes, top):
    boxes.append(("Concrete", (PARTITION[0], CORRIDOR + WALL, 0), (PARTITION[1], DEPTH - WALL, top)))


def slab(boxes):
    """住戸と外廊下の上の床。階段の上は外廊下の幅だけ張る。"""
    boxes += [
        ("Concrete", (0, 0, WALL_TOP), (BODY_X, DEPTH, STOREY)),
        ("Concrete", (BODY_X, 0, WALL_TOP), (WIDTH, CORRIDOR, STOREY)),
    ]


def stairs(boxes):
    """折り返しの外階段。外廊下から北へ 5 段上がって踊り場、南へ 5 段上がって上の階の外廊下に出る。段の厚さは 2 セル。"""
    for i in range(1, 6):
        y = CORRIDOR + 2 * (i - 1)
        boxes.append(("Concrete", (FLIGHT_UP[0], y, max(0, i - 2)), (FLIGHT_UP[1], y + 2, i)))
    boxes.append(("Concrete", (FLIGHT_UP[0], LANDING_Y, 3), (FLIGHT_DOWN[1], DEPTH, 5)))
    for j in range(6, 11):
        y = LANDING_Y - 2 * (j - 5)
        boxes.append(("Concrete", (FLIGHT_DOWN[0], y, j - 2), (FLIGHT_DOWN[1], y + 2, j)))


def parapet(boxes):
    """下の階の床の上に立つ、外廊下の手すり壁。"""
    boxes.append(("Concrete", (0, 0, 0), (WIDTH, PARAPET_DEPTH, PARAPET_HEIGHT)))


def finished_storey(with_parapet):
    boxes = []
    gable_walls(boxes, WALL_TOP, WALL_TOP)
    for s in UNIT_STARTS:
        front_wall(boxes, s)
        back_wall(boxes, s)
    partition(boxes, WALL_TOP)
    slab(boxes)
    stairs(boxes)
    if with_parapet:
        parapet(boxes)
    return boxes


def roof():
    """建設途中の 3 階。西の住戸は壁を立て終え、東の住戸は壁の根元だけ。妻壁の上から鉄筋が突き出す。床はまだない。"""
    boxes = []
    gable_walls(boxes, ROOF_GABLE_TOP, STOREY)
    west, east = UNIT_STARTS
    front_wall(boxes, west)
    boxes.append(("Concrete", (west, DEPTH - WALL, 0), (west + UNIT_WIDTH, DEPTH, 4)))
    partition(boxes, WALL_TOP)
    boxes += [
        ("Concrete", (east, CORRIDOR, 0), (east + UNIT_WIDTH, CORRIDOR + WALL, 2)),
        ("Concrete", (east, DEPTH - WALL, 0), (east + UNIT_WIDTH, DEPTH, 2)),
    ]
    parapet(boxes)
    return boxes


# モデルの名前の部分・箱を作る関数・高さ（セル数）
PARTS = (
    ("Ground", lambda: finished_storey(False), STOREY),
    ("Middle", lambda: finished_storey(True), STOREY),
    ("Roof", roof, STOREY),
)

result = {}
for part, make, height in PARTS:
    km.clear_scene()
    root = km.build_model_from_boxes(f"{MODEL_PREFIX}_{part}_01", make(), (WIDTH, DEPTH, height))
    result[root.name] = km.report(root)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
