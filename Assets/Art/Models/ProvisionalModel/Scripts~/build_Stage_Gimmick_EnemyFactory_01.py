"""敵の生産施設 2 種（発注書の K-1・K-2）を、中の装置（K-3）ごと 1 階だけの VoxelBuilding のモデルとして作る。

施設は敵の生産の起点で、敵が中に出て出口から歩いて出てくる。中は敵が歩ける空間を空ける。
外壁は厚さ 0.8 m のコンクリート。外周の 1 セルの帯に、機械が取りついた配管とケーブルを鉄で立てる。
中の装置は鉄の台と Machine の本体に分け、壊れ具合が分かるようにする。床は床側（下）、屋根は天井側に張る。
"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_PREFIX = "Stage_Gimmick_EnemyFactory"
# 床・屋根の厚さと外壁の厚さ（セル数）。外壁は外周の帯の 1 セル内側に立てる
SLAB, WALL = 2, 2
# 出口の幅の範囲は施設ごと、出口の高さは共通（床の上からのセル数）
EXIT_HEIGHT = 8
# 外周の帯に立てる配管の間隔（セル数）
PIPE_STEP = 5


def shell(width, height, exits_south, exits_north):
    """床・外壁・屋根。exits_* は出口の X の範囲の並び。"""
    w, top = width - 1, height - SLAB
    boxes = [
        ("Concrete", (1, 1, 0), (w, w, SLAB)),
        ("Concrete", (1, 1, top), (w, w, height)),
    ]
    for y0, exits in ((1, exits_south), (w - WALL, exits_north)):
        cursor = 1
        for a, b in sorted(exits) + [(w, w)]:
            if a > cursor:
                boxes.append(("Concrete", (cursor, y0, SLAB), (a, y0 + WALL, top)))
            if b > a:
                boxes.append(("Concrete", (a, y0, SLAB + EXIT_HEIGHT), (b, y0 + WALL, top)))
            cursor = b
    for x0 in (1, w - WALL):
        boxes.append(("Concrete", (x0, 1 + WALL, SLAB), (x0 + WALL, w - WALL, top)))
    return boxes


def erosion(width, height, exits_south, exits_north):
    """外周の帯に立てる配管。出口の前と、その両脇 1 セルは空ける。"""
    boxes = []
    top = height - SLAB
    for a in range(3, width - 3, PIPE_STEP):
        boxes.append(("Metal", (0, a, 0), (1, a + 1, top)))
        boxes.append(("Metal", (width - 1, a, 0), (width, a + 1, top)))
        for y0, exits in ((0, exits_south), (width - 1, exits_north)):
            if all(a < e0 - 1 or a > e1 for e0, e1 in exits):
                boxes.append(("Metal", (a, y0, 0), (a + 1, y0 + 1, top)))
    return boxes


def device(x0, y0, size_x, size_y):
    """中の装置。鉄の台の上に Machine の本体と煙突を載せる。高さは床から 2.8 m。"""
    return [
        ("Metal", (x0, y0, SLAB), (x0 + size_x, y0 + size_y, SLAB + 1)),
        ("Machine", (x0 + 1, y0 + 1, SLAB + 1), (x0 + size_x - 1, y0 + size_y - 1, SLAB + 5)),
        ("Machine", (x0 + size_x // 2 - 1, y0 + size_y // 2 - 1, SLAB + 5), (x0 + size_x // 2 + 1, y0 + size_y // 2, SLAB + 7)),
    ]


def small():
    """K-1。8 × 8 × 6.4 m、南に出口 1 か所。"""
    width, height, south = 20, 16, [(6, 14)]
    boxes = shell(width, height, south, []) + erosion(width, height, south, [])
    return boxes + device(6, 10, 8, 5), (width, width, height)


def large():
    """K-2。14.4 × 14.4 × 8 m、南北に出口 2 か所。装置 2 台の間を、出口をつなぐ鉄のコンベアが通る。"""
    width, height, exits = 36, 20, [(14, 22)]
    boxes = shell(width, height, exits, exits) + erosion(width, height, exits, exits)
    boxes += device(5, 13, 9, 10) + device(22, 13, 9, 10)
    boxes.append(("Metal", (16, 3, SLAB), (20, width - 3, SLAB + 1)))
    return boxes, (width, width, height)


parts = []
for name, (boxes, size) in (("Small", small()), ("Large", large())):
    parts.append((name, boxes, size))
result = km.build_floor_models(MODEL_PREFIX, parts)
