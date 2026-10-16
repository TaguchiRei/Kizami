"""T-1 積みかけの壁（Stage_Wall_Block_01〜03）を作る。幅 4 × 高さ 4 × 厚さ 1.2 m に、上端から鉄筋が突き出す。"""
import importlib
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kizami_model as km

importlib.reload(km)

# 壁の大きさ（セル数）。X が幅、Y が厚さ、Z が高さ
WIDTH, DEPTH, HEIGHT = 10, 3, 10
# ブロック 1 段の高さと、段の上に載る目地の段を合わせた周期（セル数）
COURSE = 3
# ブロックの幅と縦目地を合わせた周期（セル数）
BOND = 4
# 突き出した鉄筋の長さ（セル数）
REBAR_STICK_OUT = 2
# 横に通す鉄筋の高さ（セル）
REBAR_ROWS = (4, 7)

# 模様ごとの、列ごとの高さ（セル数）・縦の鉄筋を通す列・縦目地のずらし
VARIANTS = {
    "Stage_Wall_Block_01": ([10, 10, 10, 10, 10, 10, 8, 8, 5, 5], (1, 4, 8), 0),
    "Stage_Wall_Block_02": ([10, 10, 10, 8, 5, 5, 8, 10, 10, 10], (1, 4, 8), 1),
    "Stage_Wall_Block_03": ([5, 8, 10, 10, 10, 10, 10, 8, 8, 5], (1, 5, 8), 3),
}


def masonry(x, z, phase):
    """表と裏の層の素材。段の上の 1 セルと、段ごとに半分ずらした縦目地を Mortar にする。"""
    course = z // COURSE
    if z % COURSE == COURSE - 1:
        return "Mortar"
    shift = phase + (BOND // 2) * (course % 2)
    return "Mortar" if (x + shift) % BOND == BOND - 1 else "Brick"


def build_grid(heights, rebar_columns, phase):
    grid = {}
    middle = DEPTH // 2
    for x, h in enumerate(heights):
        for z in range(h):
            for y in range(DEPTH):
                grid[(x, y, z)] = masonry(x, z, phase)

        # 鉄筋は真ん中の層だけに置き、表・裏・上・下・両端を 1 セル以上の組積で覆う
        if 0 < x < WIDTH - 1:
            for z in REBAR_ROWS:
                if z < h - 1:
                    grid[(x, middle, z)] = "Rebar"
        if x in rebar_columns:
            # 天端まで積んだ列は中に収め、積みかけの列は上端から突き出す
            top = h - 1 if h == HEIGHT else h + REBAR_STICK_OUT
            for z in range(1, top):
                grid[(x, middle, z)] = "Rebar"
    return grid


km.clear_scene()
result = {}
for name, (heights, rebar_columns, phase) in VARIANTS.items():
    km.clear_scene()
    root = km.build_model(name, build_grid(heights, rebar_columns, phase), (WIDTH, DEPTH, HEIGHT))
    result[name] = km.report(root)
    shot_dir = os.path.join(os.environ.get("KIZAMI_SHOT_DIR", km.BLEND_DIR))
    km.render_ortho(os.path.join(shot_dir, name + "_persp.png"), rot_x=70, rot_z=-30)
    km.render_ortho(os.path.join(shot_dir, name + "_front.png"), rot_x=90, rot_z=0)
    if os.environ.get("KIZAMI_EXPORT") == "1":
        km.save_and_export(root)
