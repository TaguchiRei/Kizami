"""外殻エリアの遠景のビル 4 種（Stage_Far_Building_<A〜D>_01）、外殻の地面（Stage_Far_Ground_01）、外壁（Stage_Far_Wall_01）を作る。どちらもボクセルにしない。

遠景はシルエットだけを見せるので、窓や部材の厚みの決まりは適用せず、外形の箱と、建設途中を表す骨組みだけを作る。ビルの原点は底面の中心。
外壁は外周の 1/16 の弧で、原点はマップの中心。22.5° ずつ回して 16 個並べると一周になる。内側（マップ側）の面に縦の控え壁と横の帯を付ける。
外殻の地面も 1/16 の弧で、内側の面が奈落の向こう岸の崖になる。上面は高さ 0。
外壁と外殻の地面の下端は奈落の底より深く下ろし、下から見上げても切れ目が見えないようにする。
"""
import importlib
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# 外壁の内側の半径・厚さ・上端と下端の高さ（m）と、弧の分割数
WALL_RADIUS, WALL_THICKNESS, WALL_TOP, WALL_BOTTOM, WALL_SEGMENTS = 600.0, 8.0, 200.0, -300.0, 8
# 控え壁の数（1 モジュールあたり）・張り出し・幅の角度（度）と、横の帯の高さと張り出し（m）
RIBS, RIB_DEPTH, RIB_ANGLE = 4, 6.0, 0.3
BANDS, BAND_HEIGHT, BAND_DEPTH = (60.0, 140.0), 6.0, 3.0
MODULES = 16
# 外殻の地面の内側の半径（m）
FAR_GROUND_RADIUS = 450.0


def frame(b, half_x, half_y, z0, z1, beam_z, columns=2, size=1.6):
    """建設途中の鉄骨。外周に柱を立て、beam_z の高さに外周の梁を回す。"""
    xs = [-half_x + (2 * half_x - size) * i / columns for i in range(columns + 1)]
    ys = [-half_y + (2 * half_y - size) * i / columns for i in range(columns + 1)]
    for x in xs:
        for y in ys:
            if x in (xs[0], xs[-1]) or y in (ys[0], ys[-1]):
                b.box("Metal", x, y, x + size, y + size, z0, z1)
    for z in beam_z:
        b.box("Metal", -half_x, -half_y, half_x, -half_y + size, z - size, z)
        b.box("Metal", -half_x, half_y - size, half_x, half_y, z - size, z)
        b.box("Metal", -half_x, -half_y + size, -half_x + size, half_y - size, z - size, z)
        b.box("Metal", half_x - size, -half_y + size, half_x, half_y - size, z - size, z)


def building_a():
    """30 × 20 × 150 m の板状の高層ビル。上の 12 m は骨組みだけ。"""
    b = km.MeshBuilder()
    b.box("FarConcrete", -15, -10, 15, 10, 0, 138)
    frame(b, 15, 10, 138, 150, (150,))
    return b


def building_b():
    """40 m → 32 m → 24 m と段々に細くなる 120 m のビル。最上段の上 6 m は骨組みだけ。"""
    b = km.MeshBuilder()
    b.box("FarConcrete", -20, -20, 20, 20, 0, 60)
    b.box("FarConcrete", -16, -16, 16, 16, 60, 96)
    b.box("FarConcrete", -12, -12, 12, 12, 96, 114)
    frame(b, 12, 12, 114, 120, (120,))
    return b


def building_c():
    """24 × 24 × 84 m の塔の屋上に、クライミングクレーンを載せる。クレーンの腕は +X へ張り出す。"""
    b = km.MeshBuilder()
    b.box("FarConcrete", -12, -12, 12, 12, 0, 84)
    b.box("Metal", -1.5, -1.5, 1.5, 1.5, 84, 102)
    b.box("Metal", -10, -1, 30, 1, 98, 100)
    b.box("Metal", -9, -2, -5, 2, 94, 98)
    return b


def building_d():
    """36 × 28 m の 36 m までの躯体の上に、60 m まで鉄骨の骨組みだけが立つビル。"""
    b = km.MeshBuilder()
    b.box("FarConcrete", -18, -14, 18, 14, 0, 36)
    frame(b, 18, 14, 36, 60, (48, 60), columns=3)
    return b


def far_ground():
    b = km.MeshBuilder()
    half = math.pi / MODULES
    b.sector("Rock", FAR_GROUND_RADIUS, WALL_RADIUS, -half, half, WALL_BOTTOM, 0, WALL_SEGMENTS)
    return b


def wall():
    b = km.MeshBuilder()
    half = math.pi / MODULES
    b.sector("FarWall", WALL_RADIUS, WALL_RADIUS + WALL_THICKNESS, -half, half, WALL_BOTTOM, WALL_TOP, WALL_SEGMENTS)
    rib = math.radians(RIB_ANGLE) / 2
    for i in range(RIBS):
        a = -half + 2 * half * (i + 0.5) / RIBS
        b.sector("FarWall", WALL_RADIUS - RIB_DEPTH, WALL_RADIUS, a - rib, a + rib, WALL_BOTTOM, WALL_TOP, 1)
    for z in BANDS:
        b.sector("FarWall", WALL_RADIUS - BAND_DEPTH, WALL_RADIUS, -half, half, z, z + BAND_HEIGHT, WALL_SEGMENTS)
    return b


models = {
    "Stage_Far_Building_A_01": building_a,
    "Stage_Far_Building_B_01": building_b,
    "Stage_Far_Building_C_01": building_c,
    "Stage_Far_Building_D_01": building_d,
    "Stage_Far_Ground_01": far_ground,
    "Stage_Far_Wall_01": wall,
}

result = {}
for name, make in models.items():
    km.clear_scene()
    root = make().build(name)
    result[name] = sum(len(p.vertices) - 2 for c in root.children for p in c.data.polygons)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
