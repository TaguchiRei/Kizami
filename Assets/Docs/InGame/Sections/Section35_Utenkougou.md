# 区間35：火術奥義・羽天轟号

| 項目 | 内容 |
|---|---|
| 状態 | 実装済み（2026-10-21。手触りと、敵・バリア・ボスへの効きの確認はユーザーが行う） |
| 目安の時期 | 未定 |
| 前提となる区間 | 34、アウトゲームの区間5（2026-10-21、ユーザーの優先度で並べた順。アウトゲームの区間6 から移した） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

到達のクリアで手に入るご褒美のスキル、羽天轟号を作る。フィニッシャーの極太ビームを強化した、上空から撃つビームにする。

## 関連する仕様

- アウトゲーム全体計画書の 7 章「スキル」の羽天轟号（[OutGameOverallPlan.md](../../OutGame/OutGameOverallPlan.md)）

## 既存の資産

| 資産 | 内容 |
|---|---|
| `EnemyFinisherBeams`、`EnemyFinisherBeams.Shot` | フィニッシャーの極太ビーム（インゲーム区間23）の見た目。`BeginContinuous` と `Stop` があり、ボスの円形の空間（`BossArena`）が止めるまで撃ち続ける形で使っている |
| `BossHeadLaser.DefeatAlong` | 射線に沿って `EnemySpawnAdapter.DefeatInRadius` を並べて敵を倒す手本 |
| `ArmorPanel.ApplyDamage` | 装甲とバリアの受け手。粉砕を含めば一撃で壊れる |
| `HealthTarget`、`TargetHealthService` | 体力制の対象（区間31）。ボスの体力は `TargetHealthService` が持つ |
| `PlayerMoveMode.Aiming` | この間は `PlayerHealthService.ApplyDamage` がダメージを無視し、`PlayerMovementAdapterBase` が移動を止める。視点は `PlayerLookService` が別に受ける |
| `ChargeService` | エネルギーの消費 |

## 確定した仕様（2026-10-21、ユーザー）

| # | 内容 |
|---|---|
| 1 | 発動地点は、基本的にビルなどより高い位置にする。上昇先はワールドの高度で指定し、`max(今の高さ, 指定高度)` へ上がる（指定より高い所からは上がらない）。発動地点が指定より高くなることは基本ない |
| 2 | 上から撃ち下ろすので、1 回の判定で壊れるのは軸に沿った最初の当たりの 1 か所になり、複数の物を一度に壊す量に上限がかかる |
| 3 | ボクセルに対しては、0.5 秒ごとの判定で、軸に沿った `SphereCast` の最初の当たりを基準に壊す。判定のたびにエネルギーを 15 使い、足りなければその回は撃たずに止める。照射の開始時に 1 回目の判定を行う |
| 4 | 装甲とバリアは、一撃で粉砕する（破壊判定のみ。SE は区間36 で付ける） |
| 5 | 体力制の対象（ボスなど）には、秒間 12 ダメージを与える |
| 6 | 雑魚敵は一撃で倒す |
| 7 | 通常攻撃は、使用中は禁止する |
| 8 | 水晶への効き方は後回し。今は特別な扱いをしない |
| 9 | 無敵は移動モード `Aiming` を流用する（使用中は静止し、視点は動かせる） |
| 10 | 止めるのは、判定でエネルギーが足りないとき、または同じ枠をもう一度押したとき。止めたら発動した地点へ戻し、戻り終えるまで無敵のままにする。再発動の停止はエネルギーを使わない |
| 11 | スキルの解放は、スキルの実体ができてから設定する（コミット 4） |

## 既存コードの確認結果（2026-10-21）

| 対象 | 今の状態 | 対応 |
|---|---|---|
| 無敵 | 計画書は「仕組みがない」としていたが、`Aiming` の間はダメージを受けず移動も止まる（`PlayerHealthService.ApplyDamage`、`PlayerMovementAdapterBase.FixedUpdate`） | `PlayerMovementService` に固定の要求を足し、`HoldStill` を呼ぶ |
| `Shot` | 向きは `BeginContinuous` で固定され、照射中に更新できない。照射中はボクセルを溶かして進み（毎秒 4m）、プレイヤーへのダメージ関数は null でも動く | `SetDirection` と溶かさないフラグを足す |
| 敵・バリア | `EnemySpawnAdapter.DefeatInRadius`、`ArmorPanel.ApplyDamage` がある | 射線に沿って呼ぶ |
| `HealthTarget` | 1 回ごとの `ReportSkillHit` / `ReportMeleeHit` だけで、量を渡す口がない | `ReportDamage(float)` を足す |
| `VoxelDestructionAdapter` | 体力制のボクセルを削ると `ReportSkillHit` を数える（382 行） | 羽天轟号の判定では通知しない形にする |
| `SkillService` | 発動のたびに消費して一度だけ効果を出す。再発動で止まる形はない | `SkyBeamService` を 1 つ受け取り、再発動を渡す |
| `SkillData` | 系統 `FireUltimate` は区間34 で入っている。`SkillEffect` に羽天轟号がない | `SkyBeam` を足す。既存の欄を流用し（`Duration`＝チャージ時間、`Radius`、`Length`）、足すのは `_altitude`・判定ごとの消費・判定の間隔 |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 35-1 | 発動と上昇・戻り | Application / EngineAdapter | `SkyBeamService`（発動・停止の切り替え、無敵と静止の固定、通常攻撃の禁止）、`SkyBeamAdapter`（`PlaceAt` を毎ステップ呼んで上昇・戻り）。時間の進行は Adapter が持ち、判定のたびに「続けるか」を Service へ尋ねる |
| 35-2 | ビームの見た目と向き | EngineAdapter | チャージ 1 秒（`Shot` の予兆）のあと、カメラの正面へビームを向け続ける |
| 35-3 | 判定 | EngineAdapter | 0.5 秒ごとにボクセル・装甲・バリア、0.1 秒ごとに雑魚、毎フレーム体力制の対象。エネルギー切れで止める |
| 35-4 | 解放と配線 | Initialization / ExternalLayer | 到達で解放、`GameSceneInitializer._ultimateSkill` への割り当て、計画書の更新 |

## 完了条件

- 発動に 10、判定ごとに 15 を使い、エネルギーが切れるか再発動で止まる
- 使っている間はダメージを受けず、視点を動かしてビームを向けられ、通常攻撃は出ない
- ビームでボクセルが壊れ、雑魚敵が一撃で倒れ、装甲とバリアが壊れ、体力制の対象に秒間 12 ダメージが入る
- 止めたら発動した地点へ戻る

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 上昇先の高度とチャージの時間 | 高度 100m（着手時に町のビルの最大の高さを測って決める）、上昇 0.7 秒、チャージ 1 秒 |
| 2 | ビームの太さと長さ | フィニッシャーのビームより太く、長さは `Shot` の既定（1500m）。太さは仮に半径 16m から始めてプレイして決める |
| 3 | ビームの出る位置 | カメラの下 2m・前 1m から始める |
| 4 | 撃てる時間 | 満タンで発動 10 ＋判定 6 回（約 3 秒）。短ければ、判定の間隔か消費を見直す |
| 5 | 雑魚を倒す間隔 | 0.1 秒 |
| 6 | 時間の進み方 | `Time.deltaTime`（スロー中は遅くなる。ポーズでは止まる） |

## 作業計画（2026-10-21）

### 方針

- 新しい型は `SkyBeamService`（利用者：`SkillService`）と `SkyBeamAdapter`（利用者：`SkyBeamService`。`PlayerInitializer` が配線）の 2 つだけ。State は作らない（無敵と静止は既存の移動モード、エネルギーは既存の `ChargeService` を使い、読む人が増えない為）
- ボクセルの判定は、`Shot` の溶かす処理を切り、`VoxelDestructionAdapter.CarveExplosionAt` で行う（溶かす処理は毎秒 4m で、100m 上からでは届くのが遅すぎる為）

### コミット

| # | 内容 | 確認 |
|---|---|---|
| 1 | `SkillEffect.SkyBeam`、`SkillData` の欄、`SkyBeamService`、`PlayerMovementService` の固定の要求、`SkyBeamAdapter` の上昇と戻りだけ、`Skill_Utenkougou.asset`、通常攻撃の禁止 | 発動で消費 10、指定高度まで上昇し、無敵で静止して視点は動かせる。再発動で戻る。使用中は通常攻撃が出ない |
| 2 | `Shot` に `SetDirection` と溶かさないフラグ、プレイヤー側に `EnemyFinisherBeams` を置く | チャージ 1 秒のあと、視点どおりにビームが動く |
| 3 | 判定（ボクセル、装甲・バリア、雑魚、体力制の対象）、`HealthTarget.ReportDamage`、エネルギー切れで止まる | 約 3 秒で止まって戻る。雑魚が倒れ、バリアが割れる。体力制の対象に 12/秒が入る |
| 4 | 到達で解放、`GameSceneInitializer._ultimateSkill` への割り当て、計画書の更新 | アウトゲームの専用枠に出て装備できる |

### 実装時に確かめること

- 町を覆うバリアのドームや外壁が、ビームの射線や上昇の途中を遮らないか
- 発動地点が、建物の崩れた塊などで埋まっていないか（戻るときに押し出されるなら、上から地面を探して置き直す）
- クリア・失敗で入力が止まったあとも、戻りまで進むか

## 実装結果

決めたこと

| # | 項目 | 決定 |
|---|---|---|
| 1 | 上昇先の高度とチャージ | 高度 100m（`Skill_Utenkougou` の `_altitude`）、上昇と戻り 0.7 秒、チャージ 1 秒（`SkyBeam` の `EnemyFinisherBeams._telegraphDuration`。`SkillData.Duration` は使わない） |
| 2 | ビームの太さ | 見た目・射線の球ともに半径 16m、判定で壊す球も半径 16m |
| 3 | ビームの見え方（2026-10-21、ユーザー） | 撃つ所の真後ろから見るので、蓋のない筒で描き、ディザーで透かす（不透明度 0.25、発光は元の 10）。発射口の光は大きさ 6・暗めの色。敵のビームは蓋のある円柱のまま |
| 4 | 撃つ所 | カメラの下 2m・前 1m |
| 5 | 雑魚を倒す間隔 | 0.1 秒。射線に沿ってビームの半径の球を並べて倒す |

作った主なもの

- `SkyBeamService`（Application）：使用中の流れ、静止と無敵（`PlayerMovementService.SetSkyBeamActive` で `Aiming`）、通常攻撃とほかのスキルの禁止（`MeleeCutService` の `isActionBlocked`）、判定ごとの消費（`TryContinue`）
- `SkyBeamAdapter`（EngineAdapter/Player）：上昇・チャージ・照射・戻り。照射中の基準の探し方、破壊、敵の撃破、体力制の対象へのダメージ
- `EnemyFinisherBeams.Shot` に、向きの更新（`SetDirection`）、溶かさない照射（`BeginContinuous(direction, false)`）、蓋のない筒（`_isOpenEnded`）を足した。`FinisherBeam.shader` に `_DitherOpacity`（既定 1）
- `VoxelDestructionAdapter.CarveSkyBeamAt`（装甲とバリアを粉砕、体力制の対象には当たりを数えない）、`HealthTarget.ReportDamage`
- `Skill_Utenkougou.asset`、`SkyBeam.mat`・`SkyBeamInner.mat`、InGame の `SkyBeam`。到達の `_unlockSkill`、常駐シーンの `GameSceneInitializer._skills` と `_ultimateSkill`

完了条件の確認結果（殲滅ステージ、ローカル）

- 発動で 10、判定ごとに 15 を使い、6 回で切れて発動地点へ戻る。再発動でも止まって戻る（止めるときは消費しない）
- 使用中はダメージを受けず（`ApplyDamage(10)` で HP が変わらない）、通常攻撃が出ず、視点でビームの向きが変わる
- 真下へ撃つと地面のボクセルが削れる
- 確かめられなかったこと：雑魚の撃破、バリアと装甲の粉砕、体力制の対象への秒間ダメージ。uloop の視点操作で狙いを合わせられなかった為。手触りと合わせて、ユーザーがプレイで確かめる
- スキル装備の画面に専用の枠が出る（未解放の為 LOCKED）

## 見つけた問題（今回は扱わない）

| # | 内容 | 場所 |
|---|---|---|
| 1 | 到達（`ArrivalStageData`）が `GameSceneInitializer._stages` に入っていない為、到達をクリアしても羽天轟号が解放されない。今は解放する手段がない | 常駐シーン `UsefulToolkitPersistent` の `GameSceneInitializer` |
