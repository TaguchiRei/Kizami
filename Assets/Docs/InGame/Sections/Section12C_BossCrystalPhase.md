# 区間12C：水晶の段階とボス戦のステージ

| 項目 | 内容 |
|---|---|
| 状態 | 実装中（コードはクラウドで先に書いた。Unity での作業と確認はローカルでまとめて行う） |
| 目安の時期 | 未定 |
| 前提となる区間 | 12B, 18, 14B |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section12_Boss.md](Section12_Boss.md)（共通の確定事項） |

## 目的

水晶の段階を作り、ボスステージ（`Stage_Boss`）の開始からボス戦が始まり、撃破でクリアになるまでを通す。

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間12A・12B の成果 | ボスの体と武装、直接戦闘の流れ |
| 区間18の成果 | ボクセルを溶かして進むビーム（`EnemyFinisherBeams.Melt`、確定 9） |
| `EnemyFinisherAttack` | 吸収の演出（VFX Graph の粒） |
| `EnemyEnergyAdapter` | 倒した敵からエネルギーを出す |
| `EnemySpawnAdapter.DefeatInRadius` | 球の中の敵を倒し、エネルギーを出して撃破数に数える。頭部のレーザーで使う |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 12C-1 | 水晶 | EngineAdapter / Level | ボスがボクセルの水晶を投げてマップに置く。すべて壊すと段階が終わる |
| 12C-2 | 遠距離の攻撃 | EngineAdapter | 上部タレットとミサイル。ユニットを吸収して頭部を展開し、ボクセルを壊すレーザーを撃つ。巻き込まれた雑魚は一撃で倒れ、エネルギーになる |
| 12C-3 | 飛来のムービー | Level | Timeline と Cinemachine で、ボスが水晶のあった所へ飛んでくる |
| 12C-4 | ボスステージ | Level | `Stage_Boss` の開始でボス戦を始める。水晶 → 直接戦闘 → 水晶 → 直接戦闘 → 撃破でクリア |

## 完了条件

- ボスステージの最初から、ボスの撃破によるクリアまで通して遊べる

## 確定した詳細仕様(2026-10-18、ユーザー)

| # | 内容 |
|---|---|
| 1 | ボス戦は独立したボスステージ(`Stage_Boss`)で行う。崩壊ステージには組み込まない |
| 2 | 段階に `Crystal`(水晶)と `Arriving`(飛来)を足す。1 周は Crystal → Arriving → Stomping → CoreExposed → Opening。最後の周の Opening で武装を壊したら AllOutAttack → Stopped |
| 3 | 水晶は置き場所の目印の数だけ投げる(仮に 3)。見た目は、`VoxelModelLoader` と `DestructionTarget` を付けたプレハブ(仮の形) |
| 4 | 頭部の展開は、Inspector で部位ごとの位置と回転の差を決める(案:`Armor_L/R` を外へ開く)。吸収はボスから半径 60m の敵 |
| 5 | 吸収した敵はエネルギーを出さず、撃破数にも数えない(`EnemySpawnAdapter.AbsorbInRadius` を足した)。レーザーで倒した敵は `DefeatInRadius` でエネルギーになる |
| 6 | レーザーの溶かし方は区間18と共通にする(`VoxelBeamMelt` に切り出した) |

## 実装の状況(2026-10-18、クラウドでの静的確認まで)

| # | 作ったもの |
|---|---|
| 12C-1 | `BossCrystals`:目印ごとに水晶を放物線で投げて置く。すべての `DestructionTarget.IsDestroyed` で段階を終える。次の周で前の水晶を消す |
| 12C-2 | `BossHeadLaser`:吸収 → 展開 → 予兆の線で狙う → 固定した向きへ撃つ → 畳む。ボクセルを溶かし、射線上の敵を倒し、プレイヤーにダメージ。上部タレットとミサイルは既存のものを水晶の段階で撃つ |
| 12C-3 | `BossArrival`:`PlayableDirector` があれば再生して終わりを待ち、なければ放物線で飛ぶ。着く所は水晶を置いた場所の中心で、地面からの高さを保つ |
| 12C-4 | 新しいコードはなし。`BossInitializer._beginOnInitialize` でステージ開始と同時に始め、クリアはボスの `DestructionTarget`(`Core`)で決まる |
| 共通 | `BossBattleState` / `BossBattleService` / `BossBattleAdapter` に水晶と飛来の段階を足した。`VoxelBeamMelt`(区間18の `Melt` の切り出し)、`EnemySpawnAdapter.AbsorbInRadius` |

### ローカルで行うこと

1. `Kizami.EngineAdapter.Runtime`・`Kizami.Application.Runtime`・`Kizami.Initialization.Runtime`・`Kizami.BlackBoard.Runtime` をコンパイルする
2. 区間18の確認:`EnemyFinisherBeams` の溶かし方を `VoxelBeamMelt` に切り出したので、フィニッシャーのビームがこれまでどおり建物を溶かすかを見る
3. `Stage_Boss` を Build Settings に戻す(今の `BuildScenes` に入っていない)
4. 水晶のプレハブを作る:根に `VoxelModelLoader`(仮の形のボクセル)と `DestructionTarget`(重要パーツと割合)。`Stage_Boss` に置き場所の目印を 3 つ置く
5. ボスのプレハブに `BossCrystals`・`BossHeadLaser`・`BossArrival` を付け、`BossBattleAdapter` の `_turret`・`_headLaser`・`_crystals`・`_arrival` を割り当てる。`BossInitializer` に `_headLaser` と `_enemySpawnAdapter` を割り当てる
6. `BossHeadLaser` の `_muzzle` と展開する部位を決める。`TurretRing` は `Core` の親なので、展開の部位にすると下部レーザーとコアも一緒に動く点に注意する
7. 飛来のムービー:Timeline と Cinemachine で作り、`BossArrival._director` に割り当てる(なくても放物線で動く)
8. 確認:開始 → 水晶 3 つが飛ぶ → 遠くから撃たれる(頭部のレーザーで建物が溶け、敵が倒れてエネルギーになる)→ 水晶をすべて壊すと飛来 → 直接戦闘 → 武装を 1 つ壊すと 2 周目の水晶 → … → 総攻撃 → 停止 → `Core` を 0.7 削ってクリア

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 水晶のモデル | 仮の形で作り、プレイで見て決める |
| 2 | 頭部の展開で動く部位と量、レーザーの太さ・時間・ダメージ | Inspector の仮の値で作り、プレイで調整する |
| 3 | 区間14C（街）へ載せ替えるときの扱い | 14C の進み方を見て決める |
