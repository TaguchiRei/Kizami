# 区間5：破壊対象・崩落・クリア判定

| 項目 | 内容 |
|---|---|
| 状態 | 着手（2026-10-07） |
| 目安の時期 | 2026/11/17〜12/07（最速の推定 10/18〜10/23） |
| 前提となる区間 | 4C |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

ボクセルでできた破壊対象とマップオブジェクトを置き、重要パーツを一定の割合まで削るとクリアになるようにする。
マップを壊して起こした崩落に敵を巻き込んで撃破し、そのエネルギーをチャージにする。
ダメージタイプのデータと判定の窓口は、利用者が揃う区間6へ持ち越す（決定 1）。

## 関連する仕様

- ダメージタイプ：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8
- 破壊対象：https://app.notion.com/p/3e91ea2aa7fa81b2a6ecd57b9542b589
- マップオブジェクト：https://app.notion.com/p/3e91ea2aa7fa818c89d0f11075894db5
- ステージ構成・クリア条件：https://app.notion.com/p/3e91ea2aa7fa81e682c6cfbe4f5a11c2
- 崩落による撃破：https://app.notion.com/p/3f01ea2aa7fa8194b526ce8422c1d7b6
- チャージ（崩落で撃破した敵のエネルギー）：https://app.notion.com/p/3e91ea2aa7fa81249000ca51326add0f
- 敵の大量描画と体の貸し出し（崩落に巻き込まれたかの判定）：https://app.notion.com/p/3f01ea2aa7fa818fb0ebc1f6ea509324
- 議論の記録：[EnemyCrowdDiscussion.md](../EnemyCrowdDiscussion.md)（7 章 崩落、8 章 エネルギー）

着手時の計画は、Notion を読めない環境で、リポジトリの計画書と議論の記録をもとに立てた。Notion の記述との突き合わせは済んでいない。

## 計画書と今のコードの食い違い（着手時に確認）

2026-10-07 に確認した。

| # | 内容 | 根拠 | 対応 |
|---|---|---|---|
| 1 | 剣はボクセルを切らない。破壊対象・マップに剣が効かないことは、絞り込みを足さなくても成り立っている | `MeleeCutAdapter.CollectTargets` は `CuttableObject` を持つコライダーだけを集める。`VoxelPiece` は `CuttableObject` を持たない | 5-3（剣の切断を窓口に通す）は行わない。完了条件の「剣では削れない」は、プレイモードで確かめるだけにする |
| 2 | この区間でダメージを受ける組は「デバッグの破壊攻撃 → ボクセル」だけ。判定の窓口の利用者は 1 つになる | 崩落による撃破は `EnemySpawnAdapter` の中で完結する。装甲は区間8。Application がダメージを出すのは区間6のスキルから | ダメージのデータと判定の窓口（5-1・5-2）は区間6へ持ち越す（決定 1） |
| 3 | 削られて本体から分離した塊は、パーツの体積に含まれない | `VoxelModelLoader.Volume` は「切り離されたピースは含まない」。パーツの `RelativeVolume` は、分離のあとに残った内側のサンプル数で計算する | 分離した塊は破壊済みに数える（決定 3）。追加の処理は要らない |
| 4 | 格子より下まで落ちた敵は、既に `IsAlive = false` で消える。撃破の穴詰めと体の返却は、この経路に乗っている | `EnemyMoveJob` の落下の処理（`agent.Position.y < Grid.Origin.y`）、`EnemySpawnAdapter.ReturnBodies` | 落下による撃破は、同じ経路で `IsAlive` を落とし、崩落による撃破として数える（決定 7） |
| 5 | 体を貸している敵の部位のコライダーと、ボクセルのモデルは衝突する | `DynamicsManager.asset` の衝突の表で、Enemy の行は Player とだけ衝突しない。TestStage のボクセル（`CrowdVoxelTerrain`）は Default レイヤー | 落ちてきた塊が体の上に乗って止まると、体の中心の SDF で判定できない。コミット 3 で、塊と Enemy の衝突を切るか、判定する点を体の上の方にも足すかを決める（決定 8） |

## 既存の資産

| クラス | 内容 |
|---|---|
| [VoxelModelLoader](../../../Code/Scripts/EngineAdapterLayer/Voxel/Model/VoxelModelLoader.cs) | ボクセルモデルのアセットを読み込み、パーツごとに `VoxelPiece` を作る。`TryGetPart`（パスでパーツを引く）、読み込み・形状の変化・分離・破棄の通知をモデル単位で受け取れる |
| [VoxelPiece](../../../Code/Scripts/EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs) | `ApplyEdit`（ワールド空間でも指定できる）で削る・盛る。`RelativeVolume`、`Root`、`PartPath`、`Generation`、`SampleDistance`、`WorldBounds` を持つ。分離した塊は Rigidbody と凸包のコライダーを持ち、一定の高さより下へ落ちると破棄される |
| [IVoxelShape](../../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/IVoxelShape.cs) | 削る形状（球・箱・カプセル） |
| `VoxelModelBaker` | エディタで、置いた箱などをボクセルモデルのアセットへベイクする。区間4C の TestStage の壁と橋はこれで作った |
| ボクセルの説明 | [VoxelOverview.md](../../Voxel/VoxelOverview.md) |
| 区間4C の成果 | 敵の距離マップのボクセルへの追従（`EnemyDistanceField.Watch`。購読するのは `EnemySpawnAdapter` の初期化のときにシーンにある `VoxelModelLoader` だけ）。敵の落下と着地（`EnemyAgent` の `VerticalSpeed`・`IsGrounded`）。TestStage のボクセルの壁と橋（`CrowdVoxelTerrain`） |
| チャージ | `ChargeService.AddFragments`（かけら 1 個あたり `PlayerParameterData.ChargePerFragment`）、`IChargeState`。上限は 100、かけら 1 個で 1 |
| VFX Graph | `com.unity.visualeffectgraph` 17.3.0 は導入済み。`Assets/Art/Particles/EnemyDead.vfx` は旧構成のシーンだけが使っている |

## 既存コードの確認結果（2026-10-07）

| 対象 | 状態 | 対応 |
|---|---|---|
| [MeleeCutAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/MeleeCutAdapter.cs) | 切る対象は `CuttableObject` だけ | 変更なし（食い違い #1） |
| [EnemyAgent](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyAgent.cs) | 落ち始めた高さを持たない | 落ち始めた高さと、崩落で撃破されたかを足す（決定 7） |
| [EnemyMoveJob](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyMoveJob.cs) | 着地しても落ちた高さを見ない。格子より下まで落ちたら消す | 落ちた高さが一定以上なら、着地したときに撃破する（決定 7） |
| [EnemySpawnAdapter](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs) | 撃破は切断（核）と、格子より下への落下だけ | 潰された敵の判定（`EnemyCollapseDetector`）を持ち、崩落で撃破した敵の位置と数を、エネルギーの演出とチャージへ渡す |
| [ChargeService](../../../Code/Scripts/Application/Player/ChargeService.cs) | かけらの数だけを受け取る | 崩落で撃破した敵の数を受け取るメソッドを足す（決定 9） |
| [PlayerDebugInitializer](../../../Code/Scripts/Initialization/Player/PlayerDebugInitializer.cs) | HP のダメージのボタンだけ | デバッグの破壊攻撃のキーを足す（決定 6） |
| `TestStage.unity` | ボクセルの壁と橋がある。破壊対象と、崩して敵を潰せる高い物はない | 破壊対象と、崩す塔を置く |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 5-1 | ダメージのデータ | ― | 区間6へ持ち越す（決定 1） |
| 5-2 | 判定の窓口 | ― | 区間6へ持ち越す（決定 1） |
| 5-3 | 既存の攻撃をつなぐ | ― | 行わない（食い違い #1） |
| 5-4 | 破壊対象の定義データ | EngineAdapter | ステージシーンの `VoxelModelLoader` に付ける `DestructionTarget`。重要パーツの `PartPath` の一覧と、クリアに必要な割合（決定 2・4） |
| 5-5 | 破壊対象の進み具合 | EngineAdapter | `DestructionTarget` が、重要パーツごとの削れた割合をボクセルの形状が変わったときに計算する。State は作らない（決定 5） |
| 5-6 | マップオブジェクト | Level | 区間4C の壁と橋に加えて、崩して敵を潰せる塔を TestStage に置く。削れた片はチャージにならない（今のボクセルのまま） |
| 5-7 | クリア判定 | EngineAdapter | `StageClearAdapter` が破壊対象を探して数え、すべて破壊済みになったら仮のクリア表示を出す（決定 5） |
| 5-8 | デバッグ用の破壊攻撃 | EngineAdapter / Initialization | `VoxelDestructionAdapter` がカメラの向きで狙った所を球で削る。`PlayerDebugInitializer` がキーで呼ぶ（決定 6） |
| 5-9 | 崩落による撃破 | EngineAdapter | 足場ごと一定以上の高さを落ちた敵と、落ちてくる塊に潰された敵を撃破する。かけらも切断も出さない（決定 7・8・10・11） |
| 5-10 | エネルギーの演出 | EngineAdapter | 崩落で撃破した敵の位置から、VFX Graph の粒をカメラへ吸い込ませる。チャージは撃破したときに足す（決定 9）。`EnemyDebris` の置き換えは区間13へ持ち越す（決定 12） |
| 5-11 | 地形の変化への追従の確認 | ― | デバッグの破壊攻撃で壁と床を壊し、区間4C の距離マップが追従することを確かめる（コミット 1） |

## 完了条件

- デバッグ用の破壊攻撃で、破壊対象とマップオブジェクトが削れる
- 剣では破壊対象とマップオブジェクトが削れない
- すべての重要パーツを必要な割合まで削ると、クリアになる
- マップを壊して起こした崩落に巻き込まれた敵と、足場ごと落ちた敵が撃破され、エネルギーがプレイヤーに吸い込まれてチャージが増える

## 詳細仕様

### 決定（2026-10-07）

| # | 項目 | 決定 |
|---|---|---|
| 1 | ダメージのデータと判定の層（5-1〜5-3） | この区間では作らず、区間6へ持ち越す。この区間でダメージを受ける組は 1 つだけ（食い違い #2）。Application がダメージを出し始めるのは区間6のスキル、種類によって結果が変わる相手（装甲）は区間8なので、層はそのときの利用者を見て決める。作るときは、Application と EngineAdapter の両方から参照できる BlackBoard に `[Flags]` の種類を置く案がある |
| 2 | 重要パーツの指定方法 | `VoxelModelLoader` と同じ GameObject に付ける `DestructionTarget` に、`PartPath`（`VoxelModelLoader` からの相対パス）の一覧で指定する。読み込みが終わったら `TryGetPart` で引き、見つからないパスはエラーを出す |
| 3 | 本体から分離した塊を破壊済みに数えるか | 数える。パーツの `RelativeVolume` は分離した塊を含まないので、そのまま使う（食い違い #3） |
| 4 | クリアに必要な割合の単位と値 | 破壊対象ごとに持つ。重要パーツが 1 つずつ「1 − `RelativeVolume`」が割合以上になったら、その破壊対象は破壊済み。値は仮に 0.7。すべての破壊対象が破壊済みになったらクリア |
| 5 | クリア判定と表示を置く層 | EngineAdapter の `StageClearAdapter` が数え、仮のクリア表示も出す。今クリアを読むのは仮の表示だけなので、Service と State は作らない。区間11でリザルトやリトライがクリアを読むときに、Application の Service と State に移す |
| 6 | デバッグの破壊攻撃 | `PlayerDebugInitializer` が `Keyboard.current` を直接読む（キーは仮に G、押している間は一定の間隔で削る）。カメラの中心から最大 50m のレイを撃ち、当たった所を半径 1.5m の球で削る（仮）。デバッグ専用の Action を入力マップに足さない。破壊ダメージの量と形状の大きさの対応は区間6で決める |
| 7 | 落下で撃破になる高さ | 3m（仮）。敵が自分で降りるのは 2m までなので、3m 以上落ちるのは足場が壊れたときだけ。橋（4〜5.25m）が崩れれば撃破になる。格子より下まで落ちて消える敵も、崩落による撃破に数える |
| 8 | 潰されて撃破になる条件 | 分離した塊のうち、下向きの速さが 3m/s 以上、体積が 0.5m³ 以上のもの。塊の `WorldBounds` に入った敵について、体の中心（根から 1.5m 上）の SDF が 0.3m 以下なら撃破する（値はすべて仮）。体を貸している敵で塊が体の上に止まる場合の扱いは、コミット 3 で決める（食い違い #5）。コミット 3 で、Enemy と Default のレイヤーの衝突を切った（落ちてくる塊は体をすり抜け、体の中心の SDF で判定する。「実装中に確かめたこと」） |
| 9 | 崩落で撃破した敵 1 体あたりのチャージ量と、足すタイミング | かけら 1 個分（仮）。撃破したときに足す。VFX Graph の粒は届いたことを CPU に返しにくい為 |
| 10 | 潰されたかの判定の方式 | 格子の索引は作らない。落ちている塊は少ないので、毎フレーム、塊の範囲と全部の敵を総当たりで比べ、範囲に入った敵だけ `SampleDistance` で確かめる。コミット 3 で計測し、重ければ索引か Burst の Job にする |
| 11 | 体を貸していた敵が崩落で撃破されたとき | すぐに体をプールへ返す。かけらとディゾルブは出さず、エネルギーの演出だけを出す |
| 12 | 散らばる部位（`EnemyDebris`）を VFX Graph に置き換えるか | 置き換えない。完了条件に関わらないので区間13へ持ち越す |

### 決めること

| # | 項目 | 案 |
|---|---|---|
| 13 | デバッグの破壊攻撃で、敵が通れる穴を開けやすくするか（コミット 1 の確認で発見。「実装中に確かめたこと」） | 案A：Inspector で削る球の半径を 2.5m くらいに上げる（コードは変えない）。案B：球の中心を、レイの向きに半径の半分だけ奥へずらす（深く掘れるが、コードを変える） |

## 作業計画

### 新しく作る型と、区間5 での利用者

| 型 | 層・置き場所 | 区間5 での利用者 |
|---|---|---|
| `VoxelDestructionAdapter`（狙った所のボクセルを球で削る） | EngineAdapter、InGame | `PlayerDebugInitializer`（区間6でスキルも使う） |
| `DestructionTarget`（重要パーツの指定と、削れた割合） | EngineAdapter、ステージシーン | `StageClearAdapter` |
| `StageClearAdapter`（破壊対象を探して数え、仮のクリア表示を出す） | EngineAdapter、InGame | `StageInitializer` |
| `StageInitializer`（`StageClearAdapter` の初期化と、破壊対象のデバッグ表示） | Initialization、InGame | `InGameCompositor`。既存の Initializer はプレイヤーと敵のもので、ステージ全体のものを置く所がない為に作った（コミット 2） |
| `EnemyCollapseDetector`（落ちてくる塊に潰されたかの判定） | EngineAdapter のプレーンなクラス | `EnemySpawnAdapter`（`EnemyGroups` と同じく持ち主になる） |
| エネルギーの VFX Graph のアセットと、撃破位置を GraphicsBuffer で渡すコンポーネント | EngineAdapter、InGame | `EnemySpawnAdapter` |

既存の型の拡張：`EnemyAgent`（落ち始めた高さ）、`EnemyMoveJob`（落下の撃破）、`ChargeService`（崩落で撃破した敵の分）、`PlayerParameterData`（1 体あたりのチャージ量）、`PlayerDebugInitializer`（破壊攻撃のキー）。

- 基準1：ダメージのデータ、判定の窓口、クリアの Service と State は、区間5 での利用者が 1 つ以下なので作らない（決定 1・5）。潰されたかの判定は、格子の索引を作る前に総当たりで計測する（決定 10）
- 基準2：計画当初の「判定とクリアは Application」と、区間2〜4の「利用者が 1 つなら EngineAdapter」が食い違っていたので、決定 1・5 でユーザーが EngineAdapter 側を選んだ
- 基準4：剣とボクセル、分離した塊、落ちて消える敵、敵とボクセルの衝突は、コードと設定で確かめてから決めた（食い違い #1・#3〜#5）

### コミットの分け方

| # | 内容 | 確かめ方 |
|---|---|---|
| 0 | 区間計画書の更新 | ― |
| 1 | デバッグの破壊攻撃（決定 6）。5-11 もここで確かめる | 壁と橋が削れる。剣では削れない。壁に開けた穴を敵が通り、削った床を避ける |
| 2 | 破壊対象とクリア（決定 2〜5）。TestStage に破壊対象を置く | 重要パーツを全部削るとクリアの表示が出る。重要パーツ以外を削っても進まない。塊を切り離しても進む |
| 3 | 落下と潰されたことによる撃破（決定 7・8・10・11）。TestStage に崩す塔を置く | 橋を壊すと上にいた敵が落ちて撃破される。塔を崩すと下にいた敵が撃破される。かけらは出ない。判定にかかる時間 |
| 4 | エネルギーの演出とチャージ（決定 9） | 崩落で撃破すると粒がカメラへ吸い込まれ、チャージが撃破した数だけ増える |
| 5 | 計測、実装結果、全体計画書の更新 | 敵の処理の合計が 4ms 以下（4B の決定 6）に収まる |

## 次の区間へ持ち越すこと（計画の時点）

- 区間6：ダメージのデータ（種類の組み合わせ・量・形状・発生源）と判定の窓口、破壊ダメージの量と削る形状の大きさの対応（決定 1・6）
- 区間11：クリアを Application の Service と State に移す（決定 5）
- 区間13：散らばる部位（`EnemyDebris`）を VFX Graph に置き換えるか（決定 12）

## 他プラットフォームへの対応

- プラットフォームによる違いはない。スマホでは、ボクセルの品質設定（`VoxelQualitySettings`）を別の値にする可能性がある
- VFX Graph は compute shader が必要。動かない機種では Particle System で代わりにする（[EnemyCrowdDiscussion.md](../EnemyCrowdDiscussion.md) 8 章）

## 見つけた問題（今回は扱わない）

- 【ステージ】区間4C で記録済みの、生成位置 C がボクセルの壁（`CrowdVoxelTerrain/VoxelWall`）の中にある件（[Section04C](Section04C_CrowdAI.md) の「見つけた問題」）
- 【アセット】`Assets/Art/Particles/EnemyDead.vfx` は、旧構成の `Test/InGame.unity` だけが使っている。エネルギーの演出には使わず、新しく作る
- 【ボクセル】`VoxelModelBaker`（VFX Graph の `MeshToSDFBaker`）は、大きな三角形でできたメッシュの内側を外側と判定することがある。Unity の Cube を拡大したメッシュを 0.1m でベイクすると、2m・1.5m・3m の立方体は中身がほぼ空（2m で 8m³ のうち 0.58m³）になり、細長い箱（8×1×3、1.5×5×1.5）は一部だけ欠けた。面を 0.25m の格子に分けたメッシュなら正しく埋まる（0.5m の格子では 4.40m³）。符号を決める回数（`SignPassCount`）を 4 にしても直らない。区間5の破壊対象は、分けたメッシュで作って避けた（コミット 2）。今後ボクセルにする物（区間12・14）で同じことが起きる。ベイクの側で面を細かく分けるか、元のメッシュの側で分けるかは未定。TestStage の壁と橋は、厚みが 1m 以下で、体積が想定どおりなので影響はない
- 【ボクセル】`VoxelModelBaker` の余白（`PaddingVoxels`）が既定の 2 だと、複数のメッシュをまとめてベイクしたとき（`CombineHierarchy`）に、ベイクする箱の端に厚さ約 0.8m の板が内側として残った（コミット 3 の張り出しで、想定 220m³ に対して 262.7m³）。余白を 4 にすると 220.4m³ になった。張り出しは余白 4 でベイクした（コミット 4 で、最大距離 0.6m に合わせて余白 6 でベイクし直した）。既定の値（余白 2、最大距離 0.3m）を変えるかは未定
- 【ボクセル】塊が分かれると、最も大きい塊が元のピースに残り、それ以外が Rigidbody で切り離される。支えの判定はないので、塔を根元で切ると上の大きい方が宙に浮いたまま残り、下の小さい方が落ちる。崩して敵を潰すマップオブジェクトは、落とす側が小さくなる形にする必要がある（コミット 3 の張り出しは、太い柱 160m³ に細い梁 60m³ を付けた）。区間14でステージを作るときに、支えの判定が要るかを決める
- 【UsefulToolkit】`DebugGUI` のログを受け取る処理（`OnLogReceived`）が `EditorPrefs.GetBool` を呼ぶので、アセットの読み込み中（シリアライズ中）に警告が出ると、`UnityException: GetBool is not allowed to be called during serialization` のエラーになる。コミット 4 で、VFX Graph の雛形に入っていた HDRP 用の設定（URP のプロジェクトにはスクリプトがない）の警告から起きた。HDRP 用の設定を消したので今は起きないが、ほかの警告でも起きうる。直すのは UsefulToolkit の側

## 実装中に確かめたこと

- （コミット 1）`VoxelDestructionAdapter` は InGame の `VoxelDestruction` に置き、`PlayerDebugInitializer` が G キーを押している間、0.1 秒ごとに呼ぶ。狙うレイは Player・Shard・Enemy・Blade・Ignore Raycast のレイヤーを無視する。削るのは、当たった所を中心とする半径 1.5m の球の範囲にコライダーを持つすべてのピース
- （コミット 1）G キーで TestStage の壁が削れた（体積が 100% から 88% まで減った）。橋の床も削れることを、ユーザーが確かめた。地面や敵を狙っても何も起きず、エラーも出ない
- （コミット 1）剣では、壁まで 2.78m（剣の届く 3m 以内）で 3 回振っても、壁の体積もかけらの数も変わらなかった（食い違い #1）
- （コミット 1）削ると距離マップが調べ直され、穴の列に立てる層ができて距離の値が付いた。区間4C と同じく `VoxelModelLoader` の通知を通る
- （コミット 1）半径 1.5m では、敵が通れる穴を開けにくい。球の中心が当たった面の上にあり、半分が空中に出るので、1 回で掘れる深さは 1.5m までで、厚さ 1m の壁の裏面では穴の幅が 1〜2m しかない。敵が通るには、幅 1m（`_cellSize`）× 高さ 4m（`_enemyHeight`）の空きが壁の奥まで続く必要がある。裏面まで抜けたあとはレイが穴を通り抜けるので、同じ所を削り続けても穴は広がらない。エディタでの確認では、敵が穴を通るところまでは確かめられなかった（決めること #13）
- （コミット 1 の確認で発見、修正）視点を左右に回すと、ときどき再生を始めたときの向きへ引き戻された。体（PlayerRoot）の左右の向きを `transform.localRotation` に直接書いていて、補間を有効にした Rigidbody の姿勢が Transform へ書き戻されると、Rigidbody に取り込まれなかった向きが元に戻る為（狙った向き 60° に対して、体と Rigidbody が 0° のままのフレームが続いた）。区間1のコードの不具合で、区間5とは別のコミットで直した。左右の向きも上下と同じく `CinemachinePanTilt` の Pan の軸に書き（`StandardPlayerCameraAdapter`）、体の Rigidbody は回転をすべて固定した。移動の向きは、もともとカメラの前方から決めている。直したあと、60° 回して止めても 60° のままで、W で進む向きも 60° だった
- （コミット 2）破壊対象の仮のモデルは門の形（`Assets/Level/Prefabs/Stage/DestructionGate.prefab`）。土台（8×1×3m）、柱 2 本（1.5×5×1.5m）、それぞれの柱の上の核 2 つ（2m の立方体、赤いマテリアル `DestructionCore`）。重要パーツは核 2 つ（`CoreLeft`、`CoreRight`）で、必要な割合は 0.7。パーツごとにベイクする（`CombineHierarchy` なし、ボクセル 0.1m。実行時は `VoxelQuality_Terrain` の 0.2m）ので、拡大率で大きさを変えた箱は使えない（ボクセルが縦横で違う大きさになる為）。実寸の箱のメッシュ（面を 0.25m の格子に分けたもの。「見つけた問題」）を `Assets/Art/Models/Stage/` にアセットとして置いた。TestStage の `DestructionTargets` の下に、(20, -1, -10) と (-20, -1, 10) の 2 か所に置いた
- （コミット 2）InGame の `Stage` に `StageInitializer` と `StageClearAdapter` を置き、`InGameCompositor` を作り直した。DebugGUI の「Destruction」に、破壊済みの数と、破壊対象ごとの重要パーツの削れた割合と必要な割合を出す
- （コミット 2）エディタで `ApplyEdit` を呼んで確かめた。土台と柱を削っても核の割合は 0 のまま。片方の核だけ 99% 削っても破壊済みにならず、両方が 0.7 を超えたら破壊済みになった（1 / 2）。核の中を半径 1m の球でくり抜くと 0.528（球の体積 4.19m³ / 8m³）。核の真ん中を厚さ 0.4m の板で切ると、上の半分（2.4m³）が切り離されて落ち、割合は 0.500 になった（切り離した分も削れた分に数える。決定 3）。2 つ目の門も破壊済みになると、クリアになり、画面の中央に「STAGE CLEAR」が出た。エラーは 0 件
- （コミット 2）読み込んだ直後、体積を測る前のパーツは `RelativeVolume` が 0 になる（削れた割合が 1 に見える）ので、`InitialSampleCount` が 0 の間は破壊済みにしない
- （コミット 3）足場ごとの落下：`EnemyAgent` に落ち始めた高さ（`FallStartHeight`）を足し、立っている敵が足場を失ったときに記録する。着地したときに 3m 以上落ちていれば、崩落で倒す（`IsDefeatedByCollapse`）。格子より下まで落ちた敵も、崩落で倒す。生成と、戻れない敵を戻すときは、出した高さを落ち始めた高さにする
- （コミット 3）落ちてくる塊：`EnemyCollapseDetector`（`EnemySpawnAdapter` が持つ）が、見張るボクセルのモデルから切り離された Rigidbody を持つ塊を集め、下向き 3m/s 以上・体積 0.5m³ 以上の塊の `WorldBounds`（表面の余白 0.3m を足す）に入った敵の、体の中心（根から 1.5m 上）の SDF が 0.3m 以下なら倒す。経路の距離マップ（`EnemyDistanceField`）も切り離された塊を集めているが、役割が違う（止まったら格子に入れる）ので、別に持つ
- （コミット 3）崩落で倒した敵は、同じフレームで数えて記録を消し、体を貸していれば次の `ReturnBodies` で返す（かけらもディゾルブも出さない。決定 11）。DebugGUI の「Collapse Defeats」に、崩落で倒した数の累計と、そのうち潰した数を出す。値は `EnemySpawnAdapter` の Inspector（`_fallDefeatHeight`、`_crush〜`）にある
- （コミット 3）TestStage に、崩す張り出し（`CrowdVoxelTerrain/Overhang`、`Assets/Level/Prefabs/Stage/CollapseOverhang.prefab`）を置いた。(-6, -1, -14) に太い柱（4×10×4m）を立て、その上から +x へ梁（11×2×3m、下面の高さ 8m）を張り出す。梁の根元を切ると、梁（52.8m³）が切り離されて落ちる
- （コミット 3）体を貸している敵の上に梁を落とすと、梁が体のコライダーの上に乗って止まり（中心の高さ 3.7m）、1 体も潰せなかった（食い違い #5）。Enemy と Default のレイヤーの衝突を切ると、梁は体をすり抜けて地面まで落ち、梁の下に置いた 10 体のうち 8 体を潰した（残りの 2 体は切った所の外側にいた）。Enemy の体と衝突していた動く物は、ボクセルの塊だけ（プレイヤーとは元から衝突しない。かけらは Shard レイヤー）
- （コミット 3）橋の上（高さ 3m）に 10 体を置いて橋を切ると、地面（-1m）まで 4m 落ちて倒された。同時に、橋の下の地面を歩いていた敵が、落ちてきた橋に潰された（崩落で倒した 27 体のうち、落下 17 体・潰した 10 体）。エラーは 0 件
- （コミット 3）エディタで、梁の下へ自然に敵が集まるのを 2 分待ったが、1 体も来なかった（包囲の置き場がプレイヤーの周りの螺旋の上にある為）。確認では、敵の位置を書き換えて梁の下に置き、動けない状態にした。プレイでの見え方（敵が通る所の上を崩す）は、区間14でステージを作るときに確かめる
- （コミット 3）潰されたかの判定は、落ちている塊 1 つにつき、全員と範囲を比べて約 0.1ms（エディタ、1000 体）。塊が止まっていれば比べない。10 個が同時に落ちても約 1ms で、落ちている間だけかかるので、格子の索引と Burst の Job は作らない（決定 10）
- （コミット 4）チャージ：`ChargeService.AddCollapsedEnemies` と、1 体あたりの量 `PlayerParameterData.ChargePerCollapsedEnemy`（既定 1）を足した。配線はかけらとオーブと同じ形で、`PlayerInitializer` が `EnemyEnergyAdapter` をチャージを足す関数で初期化し、`EnemyInitializer` が `EnemyEnergyAdapter.Emit` を `EnemySpawnAdapter` に渡す。`EnemySpawnAdapter` は、崩落で倒した敵を数えるときに、体の中心の位置を渡す
- （コミット 4）`EnemyEnergyAdapter`（InGame の `EnemyEnergy`）は、倒した位置をフレームごとに溜め、LateUpdate で、倒した数を 1 回だけチャージに渡し、位置を GraphicsBuffer にまとめて VFX Graph へ渡し、イベント `OnEnergy` を 1 回送る（`spawnCount` に位置の数 × 1 体あたりの粒の数）。吸い込む先は MainCamera の位置を毎フレーム渡す。1 フレームに渡せる位置は 256 個までで、超えた分は粒を出さずにチャージだけ数える
- （コミット 4）VFX Graph のアセット（`Assets/Art/Particles/EnemyEnergy.vfx`）は雛形（01_Minimal_System）を写したもので、グラフはユーザーが [Section05_EnergyVfxGraph.md](Section05_EnergyVfxGraph.md) の手順で組む（グラフをスクリプトから組む公開の方法がない為）。雛形には HDRP 用の出力の設定が入っていて、URP のプロジェクトでは「The referenced script (Unknown) on this Behaviour is missing!」の警告になるので、その部分を消した
- （コミット 4）橋に 10 体を置いて崩すと、崩落で倒した 10 体の分、チャージが 0 から 10 になった。エラーと警告は 0 件。粒の見え方は、グラフを組んでから確かめる
- （コミット 4）コミット 2・3 でベイクした門と張り出しの最大距離（0.3m）が、実行時のボクセル 2 つ分（0.4m）より短く、読み込むときに「法線が荒れます」の警告が出ていた。区間4C の壁と橋と同じ 0.6m、余白 6 でベイクし直した（核 8.00m³、土台 24.00m³、張り出し 220.00m³）
