"""地形 2 種（Stage_Terrain_Ground_01・Stage_Terrain_Cliff_01）を作る。地形はボクセルにしない。

地面は半径 350 m・厚さ 2 m の平らな円盤で、上面が高さ 0。円周を 128 分割する。
崖は外周の 1/16 の弧の断面で、原点はマップの中心。22.5° ずつ回して 16 個並べると一周になる。
崖の上端の頂点は地面の円周の頂点と同じ位置に置き、すき間を作らない。崖の面は下へ行くほど内側へ下がり、外へ張り出す段を作らない（落ちたプレイヤーが途中に乗らない為）。
左右の端の列は同じ形にし、隣の崖とつなぐ。崖は外から見る面だけを張る。
"""
import importlib
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# 地面の半径と厚さ・円周の分割数
RADIUS, GROUND_THICKNESS, SEGMENTS = 350.0, 2.0, 128
# 崖のモジュールの数・深さ・縦の分割数・地面の 1 区切りあたりの横の分割数
CLIFF_MODULES, CLIFF_DEPTH, CLIFF_ROWS, CLIFF_SUBDIV = 16, 80.0, 10, 3
# 崖が深さ 1 m ごとに内側へ下がる量と、岩肌の凹凸の最大の深さ（m）
CLIFF_SLOPE, CLIFF_ROUGHNESS = 0.08, 3.0


def make_object(name, mat_name, verts, faces):
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    mesh = bpy.data.meshes.new(f"{name}_{mat_name}")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.collection.objects.link(obj)
    mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
    mat.diffuse_color = km.MATERIAL_PREVIEW_COLORS.get(mat_name, (0.5, 0.5, 0.5, 1))
    obj.data.materials.append(mat)
    obj.parent = root
    return root


def ground():
    """上面と底面の中心から扇形に三角形を張り、側面を四角形でつなぐ。"""
    verts = [(0, 0, 0), (0, 0, -GROUND_THICKNESS)]
    for i in range(SEGMENTS):
        a = 2 * math.pi * i / SEGMENTS
        x, y = RADIUS * math.cos(a), RADIUS * math.sin(a)
        verts += [(x, y, 0), (x, y, -GROUND_THICKNESS)]
    faces = []
    for i in range(SEGMENTS):
        top, bottom = 2 + 2 * i, 3 + 2 * i
        top_n, bottom_n = 2 + 2 * ((i + 1) % SEGMENTS), 3 + 2 * ((i + 1) % SEGMENTS)
        faces += [(0, top, top_n), (1, bottom_n, bottom), (top, bottom, bottom_n, top_n)]
    return make_object("Stage_Terrain_Ground_01", "Ground", verts, faces)


def hash01(*keys):
    """整数の組から決まる 0〜1 の値。"""
    h = 0
    for k in keys:
        h = (h * 73856093 ^ (k + 0x9E3779B9) * 19349663) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 1274126177) & 0xFFFFFFFF
    return (h & 0xFFFF) / 0xFFFF


def cliff():
    columns = SEGMENTS // CLIFF_MODULES * CLIFF_SUBDIV
    half = math.pi / CLIFF_MODULES
    verts = []
    for row in range(CLIFF_ROWS + 1):
        t = row / CLIFF_ROWS
        depth = CLIFF_DEPTH * t
        for col in range(columns + 1):
            # 端の列は左右で同じ値にする。縦の筋が出るよう、凹凸は列ごとの値を主にする
            key = 0 if col in (0, columns) else col
            rough = 0.7 * hash01(key, 7) + 0.3 * hash01(key, row)
            recess = 0.0 if row == 0 else depth * CLIFF_SLOPE + CLIFF_ROUGHNESS * rough * min(1.0, t * 4)
            a = -half + 2 * half * col / columns
            r = RADIUS - recess
            verts.append((r * math.cos(a), r * math.sin(a), -depth))
    faces = []
    width = columns + 1
    for row in range(CLIFF_ROWS):
        for col in range(columns):
            v = row * width + col
            faces.append((v, v + width, v + width + 1, v + 1))
    return make_object("Stage_Terrain_Cliff_01", "Rock", verts, faces)


result = {}
for make in (ground, cliff):
    km.clear_scene()
    root = make()
    mesh = root.children[0].data
    result[root.name] = {"tris": sum(len(p.vertices) - 2 for p in mesh.polygons), "verts": len(mesh.vertices)}
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root, km.STAGE_DIR)
