# 要件定義：切断後の形の当たり判定に隙間を作らない

区間4A（[Section04_Enemy.md](Section04_Enemy.md)）のコミット 4 で見つかった問題への対応。作業するのは UsefulToolkit のリポジトリ（https://github.com/TaguchiRei/UsefulToolkit）。[Section04_MeshCutRequirements.md](Section04_MeshCutRequirements.md)（`Register`・切断回数・`AdoptCutShape`・`RestoreInitialShape`）の続き。

| 項目 | 内容 |
|---|---|
| 対象のパッケージ | `com.rei.usefultoolkit.meshcut`（`Packages/com.rei.usefultoolkit.meshcut/`） |
| 対象のクラス | `UsefulToolkit.MeshCut.CuttableObject`（必要なら `ColliderClusterJob`） |
| ブランチ | `feature/meshcut/adopt-collider`（案） |
| 作成日 | 2026-10-06 |

## 背景

刻断の近接切断は、刃の範囲（厚さ 0.2m の薄い直方体）に重なるコライダーを `Physics.OverlapBoxNonAlloc` で集め、その `CuttableObject` を切る。

敵の部位を切ると、接続部の側のかけらの形を `AdoptCutShape` で元の部位に移す。このとき、部位にはかけらの球コライダー（`ColliderClusterJob` の k-means による近似）が写り、部位が元から持っていたコライダーは無効になる。

長い部位では、この球の並びに隙間ができる。刃の範囲が隙間に入ると、メッシュは刃の平面をまたいでいるのに、対象として見つからず切れない。

実測（2026-10-06、刻断の仮モデル AttackerEnemy の上の脚）は次のとおり。

| 項目 | 値 |
|---|---|
| 部位の大きさ | 0.35 × 0.57 × 2.48m の脚を付け根から 1.5m で切り、残した長さは約 1.7m |
| 球の数 | 10 個中 7 個が有効。半径は 0.08〜0.29m |
| 隙間 | 部位の長さ方向で、-0.24〜0.49m（ローカルではなくワールドの x）の約 0.7m に、どの球もない |
| 結果 | その範囲を通る刃では、2 回目の切断ができない |

同じ理由で、かけらそのものを切り直すときにも、隙間を通る刃では切れない。

## 要件

### 機能

| # | 要件 |
|---|---|
| F1 | `AdoptCutShape` で形を移した先のパーツについて、移したメッシュのどこを通る平面でも（メッシュの三角形と交わる平面なら）、有効なコライダーのどれかと交わるようにする。方法は作業者が決めてよい。例：移した先が元から持っていたコライダー（`BoxCollider` など）を無効にせず、移したメッシュの bounds に合わせて有効のまま残す選択肢を設ける |
| F2 | F1 で元から持っていたコライダーの中心や大きさを変える場合は、`RestoreInitialShape` で初期化の時点の値に戻す |
| F3 | F1 の動作を選べるようにする場合は、選び方（引数、Inspector の設定など）を作業者が決める。刻断の敵の部位では、F1 を満たす動作を使う |
| F4（任意） | かけらそのもの（プールの破片）の球コライダーにも、長い形で隙間ができないようにする。対応が大きくなる場合は対象外にしてよい。その場合は README の制約に書く |
| F5（任意） | `MeshDataCache.Register` で、メッシュの Read/Write が無効（`mesh.isReadable` が false）なときは、原因が分かるエラーログを出して false を返す。今は `NativeMeshDataStore.Add` で「source and destination length must be the same」という `ArgumentException` になり、原因が分かりにくい（刻断の区間4A のコミット 3 で発生） |

### 互換性

| # | 要件 |
|---|---|
| C1 | 今の `AdoptCutShape(CuttableObject)` の呼び出しは、書き換えなしでコンパイルできる |
| C2 | F1 の動作を選べるようにした場合、選ばないときの動作は今と同じ |
| C3 | 切断の結果、`Register`、切断回数、`Rebuild` の動作は変えない |

### 文書

| # | 要件 |
|---|---|
| D1 | README の `AdoptCutShape` の説明に、移した先の当たり判定がどうなるかを書く |
| D2 | CHANGELOG の `[Unreleased]` に追記する |
| D3 | リポジトリの `CLAUDE.md` の「Mesh cut package」の節を更新する |

## 対象外

- 刻断の `MeleeCutAdapter` の、対象を集める方法の変更
- 切断のアルゴリズムと、切断の Job の変更（F4 で `ColliderClusterJob` を直す場合を除く）

## 受け入れの確認

UsefulToolkit のサンドボックスで、次のことを確かめる。

| # | 確認すること |
|---|---|
| 1 | 0.35 × 0.57 × 2.5m 程度の細長いパーツ（`BoxCollider` 付き）を長さ方向の 1.5m の所で切り、付け根側のかけらの形を `AdoptCutShape`（F1 の動作）で移す。残った形の長さ方向のどこに、厚さ 0.2m の薄い直方体の `OverlapBox` を置いても、そのパーツのコライダーが見つかる |
| 2 | 1 のパーツを `RestoreInitialShape` で戻すと、元のコライダーの中心・大きさ・有効無効が初期化の時点と同じになり、球コライダーは無効になる |
| 3 | F1 の動作を選ばない `AdoptCutShape` は、今と同じ（球コライダーを写し、元のコライダーを無効にする） |
| 4 | 今の呼び出し元と、右クリックメニュー「切断」が、変更なしで動く |

## 刻断の側の作業（参考）

UsefulToolkit 側のマージの後で、刻断の `Packages/packages-lock.json` の meshcut の参照を更新する。`EnemyBody.ReceiveCut` で F1 の動作を使うようにし、区間4A の完了条件 5（同じ部位の系統を上限まで切れる）を確かめる。
