# 区間26A：ボスの体と武装

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-20） |
| 目安の時期 | 未定 |
| 前提となる区間 | 7, 10, 18 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section26_Boss.md](Section26_Boss.md)（共通の確定事項） |

## 目的

ボクセルのボスを、部位ごとに動かし、武装 4 種で攻撃させる。武装は削ると壊れ、壊れた武装では撃たない。戦闘の流れ（12B）が呼び出す部品をそろえる区間。

## 関連する仕様

- ボス：https://app.notion.com/p/3e91ea2aa7fa81cbb8c6cb10382ac972

## 既存コードの確認結果（2026-10-17）

| 対象 | 状態 | 対応 |
|---|---|---|
| `Assets/Data/Voxel/Baked/MachineEnemy_Boss_Voxel.asset` | 部位ごとにベイク済み。パスは `MachineEnemy_Boss_Body/...`（例：`.../MachineEnemy_Boss_TurretRing/MachineEnemy_Boss_Core`）。脚は `Hip → UpperLeg_Outer → UpperLeg_Inner → UnderLeg_Outer → UnderLeg_Rod → Pad` の 6 段。`Shield_FL/FR` は前脚の `UnderLeg_Outer` の子 | 変更なし |
| Town.unity の `BossVoxelPreview` | FBX に `VoxelModelLoader` を付けた確認用の配置 | 変更なし |
| `VoxelPiece`（`Voxel/Piece/VoxelPiece.cs`） | ボクセルは Transform のローカル空間。`ApplyEdit` はワールド空間でも削れる。Transform を動かしたときの当たり判定の追従は、どの区間でも確かめていない | 12A-1 で確かめる |
| `DestructionTarget` | 重要パーツの体積の割合を測る（`RelativeVolume`、`InitialSampleCount`）。切り離した塊も削れた分に数える | 武装の 5% の判定に同じ測り方を使う |
| `EnemyLegs` / `EnemyLeg` | 2 本の骨の IK（自前）。腿が体の根の直下・脛が腿の直下の階層が前提（`EnemyLegs.cs` の Awake）で、足場は `EnemyNavigationGrid` から読む | ボスの脚は間に部位が挟まり、格子にも乗らないので流用しない（決めること #1） |
| `EnemyBeam` | 予兆のデカールと円柱の見た目。当たり判定はない | 下部レーザーの見た目に使う |
| `EnemyShooter` | アタッカー専用（体のプレハブから銃口を読む、Job で動かす） | 使わない。ボスの弾は数が少ないので別に書く |
| `PlayerMovementAdapterBase` | 速度は State（`TargetVelocity`）から書く。外から速度を足す口はない | 12A-5 で口を足す |
| `PlayerHealthService.ApplyDamage(int)` | ダメージの窓口 | 変更なし |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 12A-1 | ボスのプレハブ | Level | ボスのボクセルのプレハブを作り、確認用のシーンに置く。Transform を動かしながら削れること、当たり判定がついてくることを確かめる |
| 12A-2 | 移動と脚 | EngineAdapter | 胴体の移動・旋回と、4 脚の IK（`UpperLeg_Outer` と `UnderLeg_Outer` を 2 本の骨にする案） |
| 12A-3 | 武装の破壊 | EngineAdapter | 武装ごとの部位から、体積の 5% 以上が削れたら破壊とする。壊れた武装の番ではクールタイムだけを発生させる |
| 12A-4 | 武装 4 種 | EngineAdapter | 上部タレット（緩やかに追尾する射撃）、下部レーザー（コアの露出と、ゆっくり回転する、飛び越えられる細いレーザー）、脚のミサイル（プレイヤーの地点の地面を狙う。4 脚を順番に使う）、アウトリガー（足踏みで広がる衝撃波の輪。小さいダメージと外側への弾き） |
| 12A-5 | プレイヤーを弾く口 | Application / EngineAdapter | 衝撃波で外側へ少し弾く為に、外から速度を足す口を作る |

## 完了条件

- 確認用のシーンで、ボスが歩き、デバッグキーで武装 4 種をそれぞれ撃つ。プレイヤーに当たるとダメージを受け、衝撃波では外側へ弾かれる
- 武装の部位を 5% 以上削ると、その武装は撃たなくなる。ミサイルは壊れた脚の番でクールタイムだけが過ぎる
- ボスが動いている間も、剣とスキルでボスの部位を削れる

## 実装の状況(2026-10-18、クラウドでの静的確認)

12A-2〜12A-5 のコードは書き終え、参照先・asmdef の依存・命名・メンバー順・コメントを静的に確認した。残りは 12A-1 と、コンパイル・プレイでの確認で、どちらもローカルで行う。

### ローカルで行うこと

1. `Kizami.EngineAdapter.Runtime` と `Kizami.Initialization.Runtime` をコンパイルする
2. 12A-1:Town.unity の `BossVoxelPreview`(`VoxelModelLoader` 付き)を元に、ボスのプレハブを作る
   - 根に `BossBody`、`BossWeapons`、`BossShockwaves`、`BossMissiles`、`BossLaser`、`BossTurret`、`BossInitializer`、`BossDebugInitializer` を付ける
   - `BossBody` の脚 4 本に、各脚の `UpperLeg_Outer`(腿)・`UnderLeg_Outer`(脛)・`Pad`(足先)を割り当てる。前後の脚で `Pair` を分ける
   - `BossWeapons` に武装を設定する(上部タレット 2:`Armor_L/R`・`Barrel_L/R`、下部レーザー:`Core` の `BeamGun_Outer/Inner`、ミサイル 4:各脚の `Hatch_A/B`、アウトリガー 4:各脚の `LiftCylinder`・`Pad`)。パスは `MachineEnemy_Boss_Body/...` の形
   - 各武装の `_target` にプレイヤー、`BossLaser` の `_core` に `Core` を割り当てる。マテリアルとプレハブ(輪・レーザー・ミサイル・弾)も設定する
3. 確認:ボスが歩く(足が地面に着く)/ 6〜9 キーで武装 4 種を撃つ / 武装の部位を 5% 以上削ると撃たなくなる / ボスが動いている間も剣とスキルで削れる(Transform を動かしたときの当たり判定の追従、12A-1)

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 脚の IK に `EnemyLegs` を流用するか | 流用しない（2026-10-17）。階層の前提が合わない為。`BossBody` に、階層を問わない 2 本の骨の IK と、真下へのレイで足場を決める処理を書いた |
| 2 | 新しく作る型 | `BossBody`（移動と脚）、`BossWeapons`（武装の発射・破壊・順番）。State は作らない（使う側は 12B で出てくる） |
| 3 | 各武装の値（弾の速さ、追尾の強さ、レーザーの回転の速さと高さ、衝撃波の広がる速さとダメージ、弾く強さ、クールタイム） | Inspector の値にし、仮の値で作ってプレイで調整する |

## 見つけた問題（今回は扱わない）

- なし
