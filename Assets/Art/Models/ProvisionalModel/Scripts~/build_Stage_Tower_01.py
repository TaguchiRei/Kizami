"""天照塔（Stage_Tower_Tensho_01）を、元のモデル big_tower.fbx の裾に道の通り穴を開けて作る。

元のタワーは脚の間の裾が中心から 39 m まで地面を覆い、中央ロータリーの車道と大通りの手前をふさぐ。
ロータリーの環（半径 18〜40 m）と、大通り 4 本の通路（車道と両側の歩道の幅）を、高さ TUNNEL_HEIGHT までブーリアンの差で切り抜く。
切り抜いたあとのタワーは、中の島の上の芯と対角線の 4 本の脚で立ち、ロータリーの上はアーチの天井になる。
元のモデルは残し、切り抜いたものを別の FBX にする。原点は底面の中心。
"""
import importlib
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

MODEL_NAME = "Stage_Tower_Tensho_01"
SOURCE_FBX = os.path.join(km.STAGE_DIR, "big_tower.fbx")
# 通り穴の高さ（m）。大きい敵も通れる 3 階分
TUNNEL_HEIGHT = 12.0
# ロータリーの中の島と外周の半径（m）。道路の部品 Stage_Road_Rotary_01 に合わせる
ROTARY_ISLAND, ROTARY_RADIUS = 18.0, 40.0
# 大通りの車道と両側の歩道を合わせた幅と、通路を切り抜く中心からの範囲（m）
MAIN_ROAD_WIDTH, MAIN_ROAD_RANGE = 27.2, (15.0, 75.0)
# 切り抜く形を、タワーの底面より下まで延ばす長さ（m）。底面と同じ高さの面を残さない為
CUT_BELOW = 1.0


def import_tower():
    """元のタワーを読み込み、回転と縮尺をメッシュに焼き込む。"""
    bpy.ops.import_scene.fbx(filepath=SOURCE_FBX)
    tower = next(o for o in bpy.context.selected_objects if o.type == 'MESH')
    bpy.ops.object.select_all(action='DESELECT')
    tower.select_set(True)
    bpy.context.view_layer.objects.active = tower
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return tower


def cutters():
    """切り抜く形。ロータリーの環は 4 つの扇形に分け、それぞれを閉じた立体にする。"""
    pieces = []
    for k in range(4):
        b = km.MeshBuilder()
        a0 = k * math.pi / 2
        b.sector("Cut", ROTARY_ISLAND, ROTARY_RADIUS, a0 - 0.05, a0 + math.pi / 2 + 0.05, -CUT_BELOW, TUNNEL_HEIGHT, 16)
        pieces.append(b.build(f"Cut_Ring_{k}"))
    half = MAIN_ROAD_WIDTH / 2
    near, far = MAIN_ROAD_RANGE
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        b = km.MeshBuilder()
        if sx:
            x0, x1 = sorted((sx * near, sx * far))
            b.box("Cut", x0, -half, x1, half, -CUT_BELOW, TUNNEL_HEIGHT)
        else:
            y0, y1 = sorted((sy * near, sy * far))
            b.box("Cut", -half, y0, half, y1, -CUT_BELOW, TUNNEL_HEIGHT)
        pieces.append(b.build(f"Cut_Road_{sx}_{sy}"))
    return [p.children[0] for p in pieces]


def carve(tower, pieces):
    for piece in pieces:
        modifier = tower.modifiers.new("Carve", 'BOOLEAN')
        modifier.operation = 'DIFFERENCE'
        modifier.solver = 'EXACT'
        modifier.object = piece
        bpy.context.view_layer.objects.active = tower
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for piece in pieces:
        root = piece.parent
        bpy.data.objects.remove(piece, do_unlink=True)
        bpy.data.objects.remove(root, do_unlink=True)


km.clear_scene()
tower = import_tower()
carve(tower, cutters())

root = bpy.data.objects.new(MODEL_NAME, None)
bpy.context.collection.objects.link(root)
tower.name = tower.data.name = f"{MODEL_NAME}_Tower"
tower.data.materials.clear()
material = bpy.data.materials.get("Tower") or bpy.data.materials.new("Tower")
tower.data.materials.append(material)
tower.parent = root

result = dict(km.report(root), tris=sum(len(p.vertices) - 2 for p in tower.data.polygons))
if os.environ.get("KIZAMI_EXPORT") == "1":
    km.save_and_export(root, km.STAGE_DIR)
