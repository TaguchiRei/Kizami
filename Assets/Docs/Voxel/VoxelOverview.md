# ボクセル表現 全体像

## 何ができる仕組みか

普通のメッシュで作られたモデルを「削れる・割れる・溶ける」物体として扱う仕組み。
見た目はカクカクした箱の集まりではなく、滑らかな表面のまま削れる。

大まかには次の仕事に分かれる。

| 段階 | いつ | 何をする | 詳細 |
|---|---|---|---|
| ベイク | エディタ上（事前） | 既存メッシュを「表面までの距離の格子」に変換して保存する | [Bake.md](Detailed/Bake.md) |
| 読み込み | 実行時の開始時 | 保存した距離の格子を、実行時の細かさに合わせて取り込む | [Bake.md](Detailed/Bake.md) |
| データの保持と編集 | 実行中ずっと | 距離の格子を持ち、形状を足したり引いたりする | [Core.md](Detailed/Core.md) |
| 表示と当たり判定 | 編集されるたび | 距離の格子から表面メッシュとコライダーを作り直す | [Meshing.md](Detailed/Meshing.md) |
| 分離 | 削られた後 | 離れ離れになった塊を別の物体として切り離し、落とす | [Piece.md](Detailed/Piece.md) |
| 加熱・融解 | 加熱されたとき | 融点に達した部分を固体から取り除き、溶けた粒にする | [Thermal.md](Detailed/Thermal.md) |
| 溶けた粒 | 実行中ずっと | 粒を落として流し、冷えたら止め、液面として表示する | [Melt.md](Detailed/Melt.md) |

温度は常温を 0、融点を 1 とした単位の無い値で扱う。

## 基本となる考え方：SDF（符号付き距離場）

このシステムは物体を「ポリゴン」ではなく「空間の各点から表面までの距離」で持っている。

- 空間を等間隔の格子に区切り、各格子点（サンプル）に「一番近い表面までの距離」を記録する
- 物体の **内側なら負**、**外側なら正**、ちょうど表面上なら 0
- 表示するときは「距離が 0 になる面」を探してポリゴンを張る

```mermaid
flowchart LR
    subgraph 格子の1列を横から見た例
    A["+0.08<br/>外"] --- B["+0.03<br/>外"] --- C["-0.02<br/>内"] --- D["-0.07<br/>内"] --- E["-0.04<br/>内"] --- F["+0.01<br/>外"]
    end
```

B と C の間、E と F の間で符号が変わる ＝ そこに表面がある。

使うのは符号（内か外か）だけではなく、**距離の大きさも使う**。
大きさは、表面の頂点をどこに置くか・どの向きにするか（法線）を決めるのに使われ、これにより格子の段差ではなく滑らかな面になる。

この持ち方の利点は、**削る操作が「距離の値の書き換え」だけで済む** こと。
球で削りたければ、球の内側に入る格子点の値を「外側」に書き換えるだけで、ポリゴンの切り貼りは不要になる。

## 全体のデータの流れ

```mermaid
flowchart TD
    subgraph Editor["エディタ（事前）"]
        M[元のモデル<br/>MeshFilter群] -->|GPUで距離を計算| SDF[パーツごとの距離の格子<br/>16bitに圧縮]
        SDF -->|保存| Asset[(ボクセルモデルアセット)]
    end

    subgraph Runtime["実行時"]
        Asset -->|開始時に読み込み| Load[パーツごとに<br/>実行時の細かさへ変換]
        Load --> Vol[距離の格子<br/>ボリューム]
        Edit[削る/盛る 指示<br/>球・箱・カプセル] -->|距離を書き換え| Vol
        Vol -->|変わったチャンクだけ| Mesh[表面メッシュ生成]
        Mesh --> Render[見た目<br/>チャンクごとのMeshRenderer]
        Mesh --> Col[当たり判定<br/>MeshCollider]
        Vol -->|削った後| Split{塊が<br/>分かれた?}
        Split -->|Yes| New[小さい塊を<br/>別の物体として切り離し<br/>Rigidbodyで落下]
        New --> Vol2[新しい物体の<br/>ボリューム]
        Heat[加熱の指示] -->|温度を上げ<br/>融点で外側に| Vol
        Vol -->|溶けた分| Drop[溶けた粒]
        Drop -->|落下・滑り・凝固| Liquid[液面の表示]
    end
```

## 登場するおもなクラスの関係

```mermaid
classDiagram
    direction LR
    class ボクセルモデルアセット {
        パーツの距離データ一覧
    }
    class モデル読み込み役 {
        アセットを読み込む
        モデル単位で通知をまとめる
    }
    class ピース {
        1つの物体
        編集の受付・分離・再メッシュ化
    }
    class ボリューム {
        距離の格子と温度
    }
    class 格子の寸法 {
        位置・ボクセルの大きさ・チャンク分割
    }
    class 形状 {
        球・箱・カプセル
    }
    class 融解システム {
        溶けた粒の保持・動き・表示
        シーン全体の加熱の入口
    }
    class 熱の設定 {
        蒸発点・凝固点・冷却速度
        粒の動き
    }
    class 品質設定 {
        ボクセルの大きさ
        チャンクの大きさ
        1フレームの再メッシュ上限
    }

    モデル読み込み役 --> ボクセルモデルアセット : 読む
    モデル読み込み役 "1" --> "*" ピース : パーツごとに作る
    ピース --> ボリューム : 持つ
    ピース --> 品質設定 : 参照
    ボリューム --> 格子の寸法 : 持つ
    ピース ..> 形状 : 編集で受け取る
    ピース ..> ピース : 分離で新しく生む
    ピース --> 融解システム : 溶けた分を渡す
    融解システム --> ピース : 登録されたピースを加熱
    融解システム --> 熱の設定 : 参照
```

| 図中の呼び名 | 実際のクラス |
|---|---|
| ボクセルモデルアセット | [`VoxelModelAsset`](../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelAsset.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelAsset.cs#L14)</sup>（中身は [`VoxelSdfData`](../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfData.cs)<sup>[L12](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfData.cs#L12)</sup> の配列） |
| モデル読み込み役 | [`VoxelModelLoader`](../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L16](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L16)</sup> |
| ピース | [`VoxelPiece`](../../Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs)<sup>[L25](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs#L25)</sup> |
| ボリューム | [`VoxelVolume`](../../Code/Scripts/EngineAdapterLayer/Voxel/Core/VoxelVolume.cs)<sup>[L17](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Core/VoxelVolume.cs#L17)</sup> |
| 格子の寸法 | [`VoxelGridLayout`](../../Code/Scripts/EngineAdapterLayer/Voxel/Core/VoxelGridLayout.cs)<sup>[L13](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Core/VoxelGridLayout.cs#L13)</sup> |
| 形状 | [`IVoxelShape`](../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/IVoxelShape.cs)<sup>[L10](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Shapes/IVoxelShape.cs#L10)</sup> を実装した [`SphereShape`](../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/SphereShape.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Shapes/SphereShape.cs#L14)</sup> / [`BoxShape`](../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/BoxShape.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Shapes/BoxShape.cs#L14)</sup> / [`CapsuleShape`](../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/CapsuleShape.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Shapes/CapsuleShape.cs#L14)</sup> |
| 品質設定 | [`VoxelQualitySettings`](../../Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelQualitySettings.cs)<sup>[L10](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelQualitySettings.cs#L10)</sup> |
| 融解システム | [`VoxelMeltSystem`](../../Code/Scripts/EngineAdapterLayer/Voxel/Melt/VoxelMeltSystem.cs)<sup>[L24](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Melt/VoxelMeltSystem.cs#L24)</sup>（液面の表示は [`VoxelMeltSurface`](../../Code/Scripts/EngineAdapterLayer/Voxel/Melt/VoxelMeltSurface.cs)<sup>[L21](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Melt/VoxelMeltSurface.cs#L21)</sup>） |
| 熱の設定 | [`VoxelThermalSettings`](../../Code/Scripts/EngineAdapterLayer/Voxel/Thermal/VoxelThermalSettings.cs)<sup>[L11](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Thermal/VoxelThermalSettings.cs#L11)</sup> |

## 用語

| 用語 | 意味 |
|---|---|
| サンプル | 格子点。距離（と温度）の値を持つ |
| セル | 隣り合う 8 つのサンプルに囲まれた小さな立方体 |
| チャンク | セルを各軸 N 個（既定 32）ずつ区切ったまとまり。メッシュとコライダーはこの単位で作り直す |
| 切り詰め距離 | 保持する距離の上限（ボクセル 4 つ分）。表面から遠い場所の正確な距離は不要なので、この値で頭打ちにする |
| 塊（連結成分） | 内側のサンプルが上下左右前後でつながった集まり。2つ以上あれば「分かれた」ことになる |
| ダーティ | 値が変わったので作り直しが必要、という印 |
