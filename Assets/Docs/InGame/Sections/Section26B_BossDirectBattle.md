# 区間26B：ボスとの直接戦闘

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-20） |
| 目安の時期 | 未定 |
| 前提となる区間 | 26A, 13, 19 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section26_Boss.md](Section26_Boss.md)（共通の確定事項） |

## 目的

直接戦闘の流れを最初から撃破まで通す。水晶の段階（12C）はまだ作らないので、直接戦闘を 2 回続けて行う形で確かめる。

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間26A の成果 | ボスの体、武装 4 種、武装の破壊 |
| `EnemyFinisherBeams` / `EnemyBeam` | 吸収型ユニットのビームの見た目。円形の空間のレーザーに使う |
| 遠距離アタッカーのビームのダメージ | 円形の空間の境界のダメージに合わせる（確定 8） |
| `MeleeCutService`（90 行） | `EnhanceDestroy` のときに `DamageType.Destroy` を足している。ボスが止まったあとは常に足す |
| `StageFlowService` | 破壊対象がすべて破壊済みになったらクリア |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 12B-1 | 戦闘の段階 | Application / BlackBoard | 足踏み 3 回 → コアの露出と攻撃 → 隙（武装を 1 つ壊すまで）→ 次へ。2 回目のあとに総攻撃 10 秒 → 停止 |
| 12B-2 | 円形の空間 | EngineAdapter | 吸収型ユニットを複数出し、上空からのレーザーで円形の空間を作る。境界に触れるとダメージ |
| 12B-3 | 総攻撃 | EngineAdapter | ミサイルはクールタイムを無視して撃ち切る、衝撃波は消えるたびに出す、下部レーザーは撃ち続ける |
| 12B-4 | 停止と破壊属性 | Application | 停止したら通常攻撃に `DamageType.Destroy` を自動で付ける |
| 12B-5 | クリア | EngineAdapter / Level | 止まったボスの `Core` を破壊対象にし、0.7 削ったらクリア |

## 完了条件

- 直接戦闘を 2 回通し、総攻撃 → 停止 → 剣でコアを削ってクリアになる

## 既存コードの確認結果と食い違い(2026-10-18)

| 対象 | 状態 | 対応 |
|---|---|---|
| 遠距離アタッカーのビーム(確定 8) | 遠距離アタッカーは `EnemyShooter`(弾、ダメージ 5)で、ビームは吸収型の `EnemyFinisherBeams`(追跡ビーム 10 / 0.5 秒、特大ビーム 40)にある | 円形の空間の境界は、追跡ビームの 10 / 0.5 秒に合わせた(`BossArena._damage`) |
| `EnemyFinisherBeams` | 真下の特大ビームと周回する分身の一連の攻撃で、円を作る用途には合わない | 部品の `EnemyBeam`(円柱)だけを使った |
| 停止まで武装以外を壊せなくする仕組み | なかった。当たり判定を切ると、プレイヤーがボスの体をすり抜ける | `VoxelPiece.SetProtected` を足し、編集・切断・加熱の入口で何もせず返すようにした |
| `StageClearAdapter` | シーンの `DestructionTarget` を自動で集める | ボスの根の `DestructionTarget` に重要パーツ `Core` を設定すれば、クリア判定は新規コードなしで使える |

## 確定した詳細仕様(2026-10-18、ユーザーが提案どおりでよいとした)

| # | 内容 |
|---|---|
| 1 | 直接戦闘 1 周:足踏み 3 回 → コアを出してレーザーとミサイルで攻撃 → 隙(周の始めより武装が 1 つ多く壊れるまで)。2 周したら総攻撃(10 秒)→ 停止 |
| 2 | 円形の空間の境界のダメージは追跡ビームに合わせる(10 / 0.5 秒) |
| 3 | 武装以外のパーツは、止まるまで `VoxelPiece.SetProtected` で保護する |
| 4 | 戦闘の段階は `BossBattleState`(EnemyBoard)を正本にし、`BossBattleService` が進める |
| 5 | 直接戦闘の間、ボスは動かさない(`BossBody.SetMoving(false)`)。円形の空間の中心はボスの位置 |

## 実装の状況(2026-10-18、クラウドでの静的確認まで)

| # | 作ったもの |
|---|---|
| 12B-1 | `BossBattleState` / `BossBattleService` / `BossBattleAdapter`。段階は Idle → Stomping → CoreExposed → Opening →(周の数だけ繰り返し)→ AllOutAttack → Stopped |
| 12B-2 | `BossArena`。円周に見た目だけのユニット(仮)を浮かべ、真下へレーザーを撃つ。柵に触れるか外へ出るとダメージ |
| 12B-3 | `BossMissiles.FireAll`、`BossShockwaves.ForceStomp` / `HasActiveRing`、`BossLaser.FireFor` / `Cancel` / `HoldCoreExposed` |
| 12B-4 | `MeleeCutService` が `IBossBattleState.IsStopped` を読んで `DamageType.Destroy` を足す。`BossBattleAdapter` が停止で保護を外す |
| 12B-5 | コードの追加なし。ボスの根に `DestructionTarget`(重要パーツ `Core`、0.7)を付ける |

### ローカルで行うこと

1. `Kizami.EngineAdapter.Runtime`、`Kizami.Application.Runtime`、`Kizami.Initialization.Runtime` をコンパイルする
2. `BossEnemy.prefab` / `Stage_Boss` に配線する:ボスの根に `BossBattleAdapter` と `DestructionTarget`(重要パーツのパスは `Core` の `MachineEnemy_Boss_Body/...` の形、0.7)を付け、`BossInitializer` に `_arena` と `_battleAdapter` を割り当てる。`BossArena` は別の GameObject に置いてプレイヤーを `_target` に、レーザーのマテリアルを設定する(ユニットのプレハブは任意)。`BossDebugInitializer._battle` にも割り当てる
3. 確認:Stage_Boss を開始すると足踏み 3 回 → コアが出てレーザーとミサイル → 隙(武装を削る)→ 2 周目 → 総攻撃 10 秒 → 停止、と進む / 止まるまで武装以外が削れない / 止まったあと、強化なしの通常攻撃でボクセルが割れる / `Core` を 0.7 削るとクリア / 円の柵に触れるとダメージ

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 吸収型ユニットの数と、円形の空間の半径 | 仮の値(12 体、半径 30m)で作ってプレイで決める |
| 2 | ユニットを吸収型の敵そのものにするか | 今は見た目だけの仮のプレハブ。敵の仕組みに乗せるかは、プレイで見て決める |
