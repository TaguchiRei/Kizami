# 区間22：体力制の破壊対象と、殲滅・崩壊の条件

| 項目 | 内容 |
|---|---|
| 状態 | 計画済み（詳細仕様の確定待ち。2026-10-20） |
| 目安の時期 | 未定 |
| 前提となる区間 | 20, 14B, 11 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

破壊対象に体力を持たせる。体力が尽きるまで、その対象はボクセルの破壊も切断も受けない。体力は画面上部にゲージで出す。崩壊ステージの対象をこの形にし、殲滅ステージに撃破数の条件を足す。ボス（区間23）も同じ仕組みに載せる。

## 関連する仕様

- 破壊対象：https://app.notion.com/p/3e91ea2aa7fa81b2a6ecd57b9542b589
- ステージ（殲滅・崩壊）：Notion のメインシナリオ

## 確定した仕様（2026-10-19、ユーザー）

| # | 内容 |
|---|---|
| 1 | 体力制の対象は、体力が尽きるまでメッシュ切断もボクセル破壊も受けない。体力は画面上部に出す |
| 2 | 崩壊の対象はボクセルにし、形は半球にする。今の通常のオブジェクトのコアシステム（部位がボクセル片に当たると分離）を置き換える |
| 3 | 崩壊の対象の体力は、倒壊の巻き込みで大きく減る。通常攻撃やスキルでも減るが、巻き込みとの差を大きく付ける |
| 4 | 崩壊の対象の体力が尽きると、ボクセルの破壊が効くようになる。通常攻撃も、この対象だけはゼロ厚の切り分けで疑似的に切れる（区間20の印を使う） |
| 5 | 殲滅のクリア条件に「敵を N 体以上倒す」を足す。N は仮に 50 |
| 6 | 到達は変えない |

## 既存コードの確認結果（2026-10-20）

| 対象 | 今の状態 | 対応 |
|---|---|---|
| `VoxelPiece.SetProtected`（287 行） | 編集・攻撃（`ApplyStrike` は 0 を返す）・切り分け（空を返す）を入口で止める | 体力が尽きるまでの保護に使う |
| `VoxelDestructionAdapter.Carve`（355 行） | 範囲のピースすべてに `ApplyStrike` を当てる。保護されたピースは黙って 0 を返す | 保護されたピースが体力の対象のものなら、攻撃 1 回につき 1 回、当たったことを報告する |
| `MeleeCutAdapter.CollectTargets` | 破壊タイプのときだけピースを集める | 区間20の変更（印の付いたピースは常に集める）に加え、体力の対象のピースに刃が入ったら報告する |
| `BombThrowAdapter`（崩爆） | `VoxelDestructionAdapter.CarveExplosionAt` で削る | `Carve` の変更で一緒に報告される |
| 崩落の判定（`EnemyCollapseDetector`） | 敵だけが対象。条件は下向きの速さと体積の下限 | 体力の対象は、自分の範囲に入った落ちる塊を自分で調べる（決めること #3） |
| `CoreSystem` / `CorePart` | 崩壊の対象。使っているのは `Level/Prefabs/Stage/Town/Buildings/Town_CollapseSite.prefab` だけ | プレハブのコアを半球のボクセルの対象に置き換える。`CoreSystem` と `CorePart` のコードは残す（決めること #8） |
| `DestructionTarget` | `VoxelModelLoader` の重要パーツの割合で破壊済みになる | 半球の対象はベイクしたモデルにして、これをそのまま使う |
| `StageFlowService` | 撃破数は `AddDefeats` で数えている。クリアは `TryClear`（破壊済みの数が必要数以上）で、破壊対象の報告のときだけ判定する | `StageData` の必要撃破数を足し、`TryClear` で撃破数も見る。`AddDefeats` からも `TryClear` を呼ぶ |
| `StageHudAdapter` | 破壊の進み具合、制限時間（上の中央、mm:ss）などを uGUI で組み立てる。撃破数は出していない（区間12C の決定 15） | 上の中央に体力のゲージを足す。制限時間があるステージでは、時間の下に置く。必要撃破数があるステージだけ、撃破数を出す |
| `StageInitializer` / `BossInitializer` | どちらも InGame にあり、ステージの物を `FindFirstObjectByType` で探す。`StageInitializer` は DI へ何も登録していない | `StageInitializer` が体力の Service を作って DI に登録し、区間23 の `BossInitializer` が受け取る |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 22-1 | 体力の持ち方 | Application / BlackBoard | `TargetHealthService` が、体力・最大値・表示の状態（通常・無敵・減り続ける）・名前を `TargetHealthState`（`StageBoard`）に書く |
| 22-2 | ダメージの入口 | EngineAdapter | 対象の根に置く `HealthTarget` が、通常攻撃・スキル・落ちる塊の当たりを受け、種類ごとの量を Service へ報告する |
| 22-3 | 保護と解除 | EngineAdapter | `HealthTarget` が、初期化で対象のピースをすべて保護し、体力が尽きたら保護を外して、通常攻撃で切れる印を付ける（解除するかは Inspector の設定。ボスは解除しない） |
| 22-4 | 体力のゲージ | EngineAdapter | `StageHudAdapter` に、上部中央の名前とゲージを足す。灰色と「無敵」、赤の点滅を持つ（区間23で使う） |
| 22-5 | 崩壊の対象 | Level / Art | 半球のモデルを作ってベイクし、`Town_CollapseSite.prefab` のコアと置き換える |
| 22-6 | 殲滅の撃破数 | External / Application / BlackBoard / EngineAdapter | `StageData._requiredDefeatCount`、`IStageFlowState.RequiredDefeatCount`、`TryClear`、HUD の撃破数。`AnnihilationStageData` を 50 にする |

## 完了条件

- 崩壊ステージで、対象の体力が上部に出る。倒壊に巻き込むと大きく減り、通常攻撃やスキルでは少しだけ減る
- 体力が尽きるまで、対象は削れも切れもしない。尽きたあとは、スキルで削れ、通常攻撃でゼロ厚の切り分けができる
- 対象を必要な割合まで壊すとクリアになる
- 殲滅ステージで、生産施設をすべて壊しても、50 体倒すまでクリアにならない。撃破数が HUD に出る

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 体力の対象の数 | 1 つのステージに 1 つ（崩壊の対象か、ボス）。State も 1 つにする |
| 2 | ダメージの量 | 体力 100。通常攻撃 1 回で 0.5、スキル 1 回で 1、落ちる塊は体積 1m³ あたり 2（仮）。巻き込みは塊の大きさで決まるので、建物を倒して当てると一度に大きく減る |
| 3 | 落ちる塊の当たりの調べ方 | `HealthTarget` が FixedUpdate ごとに、自分のピースを包む箱へ `Physics.OverlapBox` をかけ、Rigidbody を持つほかのボクセルのピースのうち、下向き 3m/s 以上・0.5m³ 以上（区間5の崩落の条件と同じ値）のものを、塊ごとに 1 回だけ数える。接触のコールバックは Rigidbody の側にしか届かず、落ちる塊は毎回新しく作られる為、コールバックを使わない |
| 4 | 1 つの攻撃で何回減らすか | 1 回の振り・1 回の発動で 1 回。当たったピースの数では数えない |
| 5 | 半球の対象の大きさと素材 | 半径 6m（仮）、ボクセル 0.4m、普通の強度の 1 種類。モデルは Blender か Unity のメッシュで作り、`VoxelModelBaker` でベイクする（ローカル） |
| 6 | 崩壊のクリア条件 | 体力が尽きたあと、重要パーツ（半球全体）を必要な割合（0.7）まで壊す |
| 7 | 撃破数の表示 | 必要撃破数のあるステージだけ、HUD の破壊の進み具合の下に「撃破 n / N」を出す |
| 8 | `CoreSystem` / `CorePart` | 崩壊ステージでは使わなくなる。コードは残し、消すかは区間13 で決める |
| 9 | 崩壊ステージの倒す物 | 今の配置（対象へ向けて斜めに置いた物）のまま、半球に届くかを確かめて置き直す |

## 作業計画（2026-10-20）

### 新しく作る型と、区間22 での利用者

| 型 | 層・置き場所 | 区間22 での利用者 |
|---|---|---|
| `TargetHealthState` / `ITargetHealthState`（体力・最大値・表示の状態・名前・尽きたか・尽きてから当たった回数） | BlackBoard（`StageBoard`、InGame の SceneState） | 書くのは `TargetHealthService`。読むのは `StageHudAdapter`、`HealthTarget`、区間23の `BossBattleAdapter` |
| `TargetHealthMode`（Normal / Invincible / Draining） | BlackBoard | `TargetHealthState`、`StageHudAdapter` |
| `TargetHealthService`（体力の初期化・ダメージ・回復・表示の状態・減り続ける体力の Tick） | Application | `StageInitializer` が作る。`HealthTarget` が直接の配線でダメージを報告する。区間23 の `BossBattleService` が状態を変える |
| `HealthTarget`（保護と解除、攻撃と落ちる塊の当たりの受け手） | EngineAdapter、ステージシーンの対象の根（`VoxelModelLoader` と同じ GameObject） | `StageInitializer`（探して配線）、`MeleeCutAdapter` と `VoxelDestructionAdapter`（当たりを報告） |
| `TargetDamageKind`（Melee / Skill / Collapse） | BlackBoard（Application と EngineAdapter の両方が使う為、`DamageType` と同じ置き場） | `HealthTarget`、`TargetHealthService` |

既存の型の拡張：`MeleeCutAdapter`、`VoxelDestructionAdapter`、`StageData`、`StageFlowState` / `IStageFlowState`、`StageFlowService`、`StageHudAdapter`、`StageInitializer`。

- 基準1：対象は 1 ステージに 1 つ（決めること #1）なので、対象ごとの辞書や番号を持たない。`TargetDamageKind` は、種類ごとの倍率を `HealthTarget` の Inspector に持つので分ける
- 基準1の例外：`TargetHealthState` は、Application が書き EngineAdapter の HUD と `HealthTarget` が読むので、層の分離のために State にする
- 基準2：攻撃の Adapter（`MeleeCutAdapter`、`VoxelDestructionAdapter`）から `HealthTarget` を `GetComponentInParent` で探して直接呼ぶ。Event を挟まない（今の `ArmorPanel.ApplyDamage` の呼び方と同じ）
- 基準4：落ちる塊を毎 FixedUpdate に調べる重さは、対象 1 つにつき箱の問い合わせ 1 回なので、リスクに挙げない

### コミットの分け方

| # | 内容 | 確かめ方（ローカル） |
|---|---|---|
| 1 | 22-6：殲滅の撃破数（`StageData`、`StageFlowState`、`StageFlowService`、HUD の撃破数） | 殲滅で、施設を全部壊しても 50 体までクリアにならない。撃破数が出る。ほかのステージは今までどおり |
| 2 | 22-1：`TargetHealthState`・`TargetHealthMode`・`TargetDamageKind`・`TargetHealthService`。`StageInitializer` が作って DI に登録する | コンパイル。デバッグ表示（`GetLog`）に体力が出る |
| 3 | 22-2・22-3：`HealthTarget`、攻撃の Adapter からの報告、落ちる塊の調べ方 | TestStage に仮の対象（ボクセルの物に `HealthTarget`）を置く。通常攻撃・スキル・落とした塊で体力が減り、尽きるまで削れない。尽きると削れて、通常攻撃で切れる |
| 4 | 22-4：HUD の体力のゲージ | 上部中央に名前とゲージ。無敵・点滅の表示はデバッグの操作で確かめる |
| 5 | 22-5：半球の対象と崩壊ステージ（ローカル。モデルとベイク、プレハブ、`DestructionTarget` の重要パーツ） | 崩壊ステージで完了条件 1〜3 |
| 6 | 計画書の実装結果、全体計画書の更新 | 完了条件すべて |

コミット 5 はモデルの作成とベイク、シーンとプレハブの編集なので、ローカルで行う。クラウドではコミット 1〜4 のコードと、5 の手順までを書く。

## 見つけた問題（今回は扱わない）

- なし
