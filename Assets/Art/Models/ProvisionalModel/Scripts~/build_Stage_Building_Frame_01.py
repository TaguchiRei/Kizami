"""建物 10 建設途中の骨組みを、VoxelBuilding で積む 3 つのモデルに分けて作る。

鉄骨の柱と梁だけの 8 階建てで、平面は 24 × 24 m。南と東の面に足場を組み、屋上にクライミングクレーンを載せる。
1 階（Ground）・中間階（Middle、6 回積む）・最上階とクレーン（Roof）。梁は各階の天井側に架ける。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Building_Frame"
# 平面の一辺（セル数）。骨組みは X 0..FRAME、Y SCAFFOLD..SIZE に置き、南と東の残りに足場を組む
SIZE, FRAME, SCAFFOLD = 60, 56, 4
# 1 階の高さ・梁の高さ・梁の幅・基礎の厚さ（セル数）
STOREY, BEAM, BEAM_WIDTH, FOUNDATION = 10, 3, 2, 2
# 柱の一辺（セル数）と、骨組みの中で柱を立てる位置（各軸の最小のセル）
COLUMN = 4
COLUMN_POSITIONS = (0, 26, 52)
# 足場の床の厚さ・柱の位置（足場の辺に沿った最小のセル）・手すりの高さの範囲（セル数）
PLANK = 2
SCAFFOLD_POSTS = (0, 14, 28, 42, 58)
RAIL = (4, 6)
# クレーンのマストの一辺・マストの上端・ジブの太さ（セル数）
MAST, MAST_TOP, JIB = 4, 32, 2
# クレーンの運転室の大きさ（セル数）と、カウンターウエイトの長さ（セル数）
CAB, COUNTERWEIGHT = 4, 6

COLUMN_TOP = STOREY - BEAM


def frame_xy(x, y):
    """骨組みの中の座標を、モデルのセルの座標にする。"""
    return x, y + SCAFFOLD


def columns(boxes, z0, z1, skip=()):
    for cx in COLUMN_POSITIONS:
        for cy in COLUMN_POSITIONS:
            if (cx, cy) in skip:
                continue
            x, y = frame_xy(cx, cy)
            boxes.append(("Metal", (x, y, z0), (x + COLUMN, y + COLUMN, z1)))


def beams(boxes, x_lines=COLUMN_POSITIONS, y_lines=COLUMN_POSITIONS):
    """柱の頭をつなぐ梁。X 方向の梁を通しで架け、Y 方向の梁はその間に入れる。"""
    z0, z1 = COLUMN_TOP, STOREY
    inset = (COLUMN - BEAM_WIDTH) // 2
    for cy in y_lines:
        x0, y = frame_xy(0, cy + inset)
        boxes.append(("Metal", (x0, y, z0), (FRAME, y + BEAM_WIDTH, z1)))
    for cx in x_lines:
        for a, b in zip(COLUMN_POSITIONS, COLUMN_POSITIONS[1:]):
            x, y_lo = frame_xy(cx + inset, a + inset + BEAM_WIDTH)
            _, y_hi = frame_xy(0, b + inset)
            boxes.append(("Metal", (x, y_lo, z0), (x + BEAM_WIDTH, y_hi, z1)))


def scaffold(boxes):
    """南と東の面の足場。天井側の床、外側の柱、腰の高さの手すり。"""
    z_plank = STOREY - PLANK
    boxes += [
        ("Wood", (0, 0, z_plank), (SIZE, SCAFFOLD, STOREY)),
        ("Wood", (FRAME, SCAFFOLD, z_plank), (SIZE, SIZE, STOREY)),
    ]
    # 南の面は Y 0..2、東の面は X 58..60 の外側の列に柱と手すりを並べる
    for a, b in zip(SCAFFOLD_POSTS, SCAFFOLD_POSTS[1:]):
        boxes.append(("Metal", (a, 0, 0), (a + 2, 2, z_plank)))
        boxes.append(("Metal", (a + 2, 0, RAIL[0]), (b, 2, RAIL[1])))
    boxes.append(("Metal", (SCAFFOLD_POSTS[-1], 0, 0), (SIZE, 2, z_plank)))
    # 東の面の柱は、南東の角の柱の次から並べる
    east, rail_start = SIZE - 2, 2
    for y in SCAFFOLD_POSTS[1:]:
        boxes.append(("Metal", (east, rail_start, RAIL[0]), (SIZE, y, RAIL[1])))
        boxes.append(("Metal", (east, y, 0), (SIZE, y + 2, z_plank)))
        rail_start = y + 2


def ground():
    """1 階。骨組みの下にコンクリートの基礎を打つ。"""
    boxes = []
    x0, y0 = frame_xy(0, 0)
    boxes.append(("Concrete", (x0, y0, 0), (FRAME, SIZE, FOUNDATION)))
    columns(boxes, FOUNDATION, COLUMN_TOP)
    beams(boxes)
    scaffold(boxes)
    return boxes


def middle():
    boxes = []
    columns(boxes, 0, COLUMN_TOP)
    beams(boxes)
    scaffold(boxes)
    return boxes


def roof():
    """最上階とクレーン。北東の柱はまだ立てておらず、梁は南と西の 2 本だけ。中央の柱の上にクレーンのマストを立てる。"""
    boxes = []
    center = COLUMN_POSITIONS[1]
    north_east = COLUMN_POSITIONS[-1]
    columns(boxes, 0, COLUMN_TOP, skip={(north_east, north_east), (center, center)})
    # 立てかけの柱の根元
    x, y = frame_xy(north_east, north_east)
    boxes.append(("Metal", (x, y, 0), (x + COLUMN, y + COLUMN, 2)))
    beams(boxes, x_lines=COLUMN_POSITIONS[:1], y_lines=COLUMN_POSITIONS[:1])

    # クレーン。マストは中央の柱の位置から立ち上げ、ジブは X 方向に骨組みの幅いっぱいに張る
    mx, my = frame_xy(center, center)
    boxes.append(("Metal", (mx, my, 0), (mx + MAST, my + MAST, MAST_TOP)))
    jib_y = my + (MAST - JIB) // 2
    boxes += [
        ("Metal", (0, jib_y, MAST_TOP), (mx, jib_y + JIB, MAST_TOP + JIB)),
        ("Metal", (mx, my, MAST_TOP), (mx + CAB, my + CAB, MAST_TOP + CAB)),
        ("Metal", (mx + CAB, jib_y, MAST_TOP), (FRAME, jib_y + JIB, MAST_TOP + JIB)),
        ("Concrete", (FRAME - COUNTERWEIGHT, jib_y, MAST_TOP - 3), (FRAME, jib_y + JIB, MAST_TOP)),
    ]
    return boxes


# モデルの名前の部分・箱を作る関数・高さ（セル数）
PARTS = (
    ("Ground", ground, STOREY),
    ("Middle", middle, STOREY),
    ("Roof", roof, MAST_TOP + CAB),
)

result = {}
for part, make, height in PARTS:
    km.clear_scene()
    root = km.build_model_from_boxes(f"{MODEL_PREFIX}_{part}_01", make(), (SIZE, SIZE, height))
    result[root.name] = km.report(root)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
