# Bake：既存メッシュのベイクと実行時の読み込み

[← 全体像へ](../VoxelOverview.md)

## 1. なぜ2段階なのか

メッシュから「表面までの距離の格子」を正確に計算するのは重いので、**エディタ上で事前に計算して保存**（ベイク）しておき、
実行時はそれを読み込むだけにしている。

```mermaid
flowchart LR
    subgraph Editor["エディタ（事前・1回だけ）"]
        M[元のモデル] --> B[ベイク] --> A[(ボクセルモデル<br/>アセット)]
    end
    subgraph Runtime["実行時（毎回）"]
        A --> L[読み込み] --> P[パーツごとのピース]
    end
```

## 2. ベイク（エディタ）

### 使い方

メニュー `Kizami/Voxel/Bake Model` でウィンドウを開き、モデルと設定を選んで「ベイク」を押す。
出力先は既定で `Assets/Data/Voxel/Baked/モデル名_Voxel.asset`。同じパスのアセットがあれば中身だけ差し替える（参照は切れない）。

> ウィンドウは [`VoxelBakeWindow`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelBakeWindow.cs)<sup>[L12](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelBakeWindow.cs#L12)</sup>、処理本体は [`VoxelModelBaker`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelModelBaker.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelModelBaker.cs#L14)</sup> で行っている。

### 処理の流れ

```mermaid
flowchart TD
    A[モデルの階層から<br/>メッシュを持つものを全部集める] --> B{全部まとめて<br/>1パーツにする?}
    B -->|まとめない：既定| C[メッシュごとに1パーツ<br/>そのメッシュのローカル空間でベイク]
    B -->|まとめる| D[全メッシュをルートの空間へ移し<br/>1パーツとしてベイク]
    C --> E
    D --> E[範囲に余白を足した箱を決める]
    E --> F[解像度を決める<br/>最大辺 ÷ ボクセルの大きさ<br/>上限 384]
    F --> G[GPUで距離を計算<br/>VFX Graph の MeshToSDFBaker]
    G --> H[結果の3Dテクスチャを<br/>CPU側へ読み戻す]
    H --> I[距離をローカル空間の長さに直し<br/>16bit整数に圧縮]
    I --> J[パーツのパス・原点・寸法と一緒に保存]
```

ポイント：

- 距離の計算は Unity の VFX Graph に含まれる [GPU ベイカーをそのまま使っている](../../../Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelModelBaker.cs)<sup>[L120](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelModelBaker.cs#L120)</sup>（自前実装ではない）
- **パーツごとにベイクした場合**、各パーツは「元のモデル階層の中のどの Transform か」を相対パスで覚えておく。読み込み時はこのパスで同じ場所の Transform を探す
- 距離は ±最大距離（既定 0.3）を [16bit 整数の全範囲に割り当てて圧縮する](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfEncoding.cs)<sup>[L13](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfEncoding.cs#L13)</sup>。これより遠い値は丸められる
- 解像度には上限（384）がある。GPU ベイカーは総ボクセル数が一定を超えると例外を投げるため

- ボクセルより細い・薄い部分は格子で表現しきれず、先が丸くなったり途切れたりする。元メッシュが閉じていない・面が裏返っている場合は内外判定を誤ることがある

> 圧縮と復元は [`VoxelSdfEncoding`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfEncoding.cs)<sup>[L8](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfEncoding.cs#L8)</sup>、1パーツ分のデータは [`VoxelSdfData`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfData.cs)<sup>[L12](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelSdfData.cs#L12)</sup>、保存先のアセットは [`VoxelModelAsset`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelAsset.cs)<sup>[L14](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelAsset.cs#L14)</sup> で扱っている。

### ベイク設定

| 項目 | 既定値 | 意味 |
|---|---|---|
| ボクセルの大きさ | 0.02 | ベイクの細かさ。**実行時の品質設定より細かく** する |
| 余白（ボクセル数） | 2 | メッシュの外側に取る余白 |
| 最大距離 | 0.3 | 保持する距離の上限。**実行時のボクセルの大きさの 2 倍以上** にする（短いと法線が荒れる。読み込み時に警告が出る） |
| 階層をまとめる | オフ | オンで全メッシュを1パーツに |
| 内外判定の補正回数 / 閾値 | 1 / 0.5 | GPU ベイカーの内外判定の調整値（そのまま渡している） |

> 設定は [`VoxelBakeSettings`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelBakeSettings.cs)<sup>[L10](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Editor/Bake/VoxelBakeSettings.cs#L10)</sup> 構造体で定義している。

## 3. 実行時の読み込み

### 使い方

モデルのルートにモデル読み込み役のコンポーネントを付け、ベイク済みアセットと品質設定を指定する。
既定では Start で自動的に読み込む。

> この役割は [`VoxelModelLoader`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L16](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L16)</sup> クラスが担っている。

### 処理の流れ

```mermaid
sequenceDiagram
    participant Unity
    participant 読み込み役
    participant ピース as パーツのピース
    participant ボリューム

    Unity->>読み込み役: Awake
    読み込み役->>読み込み役: 元のモデルの MeshRenderer を控えておく<br/>（チャンク用の MeshRenderer ができる前に）
    Unity->>読み込み役: Start
    loop アセット内のパーツごと
        読み込み役->>読み込み役: 相対パスで同じ場所の Transform を探す
        alt 見つからない
            読み込み役->>読み込み役: 警告を出して飛ばす
        else 見つかった
            読み込み役->>ピース: ピースが無ければ追加
            読み込み役->>ピース: 品質・マテリアル・分離設定・融解の送り先を設定
            読み込み役->>ピース: 所属モデルとパスを記録
            読み込み役->>ピース: 距離データを渡す
            ピース->>ボリューム: 実行時のボクセルの大きさで空の格子を作る
            ピース->>ボリューム: ベイク済みの距離を補間しながら写す
            ピース->>ピース: 全チャンクをダーティにし、体積を測る
        end
    end
    読み込み役->>読み込み役: 控えておいた元の MeshRenderer を非表示にする
    読み込み役->>読み込み役: 読み込み完了を通知
```

- ベイク時の細かさと実行時の細かさは別でよい。実行時の品質設定（PC / モバイル）に合わせて、読み込み時に補間して変換する
- マテリアルは「上書き指定 → パーツの元の MeshRenderer → 階層内で最初の元の MeshRenderer」の順に探す
- [元の見た目は非表示にするだけで、削除はしない](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L217](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L217)</sup>
- 全チャンクをダーティにするので、表示は数フレームかけて出そろう（1フレームの作り直し上限に従う）

> 読み込みは [`VoxelModelLoader.Load`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L179](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L179)</sup>、パーツ側の受け取りは [`VoxelPiece.LoadSdf`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs)<sup>[L235](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs#L235)</sup> メソッドで行っている。

## 4. モデル単位でのまとめ役

読み込み役は、読み込んだ後も「このモデルに属するピース全部」を追跡する。

```mermaid
flowchart TD
    L[読み込み役] -->|読み込んだ| P1[パーツA]
    L -->|読み込んだ| P2[パーツB]
    P1 -->|分離| C1[破片A-1]
    C1 -->|さらに分離| C2[破片A-1-1]
    P1 -. 通知を転送 .-> L
    C1 -. 通知を転送 .-> L
    C2 -. 通知を転送 .-> L
    P2 -. 通知を転送 .-> L
```

- **パーツ**：読み込み時からあるピース（分離前の本体）
- **ピース全体**：パーツ ＋ そこから切り離された全ての破片（破棄されたものは除く）
- 切り離された破片にも「所属モデル」が引き継がれるので、何世代分離しても通知は読み込み役に集まる
- 体積（今 / 最初 / 割合）はパーツの合計で計算する（切り離された破片は含まない）。「モデル本体がどれだけ削られたか」の指標になる

| 通知 | 内容 |
|---|---|
| 読み込み完了 | 読み込み・読み込み直しが終わった |
| いずれかのピースの形状変化 | 各ピースの形状変化通知をまとめたもの |
| いずれかのピースの分離 | 各ピースの分離通知をまとめたもの（新しい破片は追跡対象に加わる） |
| いずれかのピースの破棄 | 各ピースの破棄通知をまとめたもの（追跡対象から外れる） |

読み込み直した場合、既に切り離された破片はそのまま残る。

> これらは [`VoxelModelLoader`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L16](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L16)</sup> の [`RegisterOnLoaded`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L142](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L142)</sup> / [`RegisterOnPieceShapeChanged`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L151](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L151)</sup> / [`RegisterOnPieceSplit`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L161](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L161)</sup> / [`RegisterOnPieceDestroyed`](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs)<sup>[L170](https://github.com/TaguchiRei/Kizami/blob/main/Assets/Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs#L170)</sup> メソッドで登録する。
