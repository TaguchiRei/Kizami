# 区間40：アーキテクチャのリファクタリング

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 未定 |
| 前提となる区間 | 39（2026-10-24、ユーザー） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

区間39 までは、区間ごとに細部だけを確かめてきた。この区間では、コード全体を State-Centrism Architecture の原則に照らし、層の責務を整理する。区間41 以降の作業を、整理したあとの構造の上に乗せる。

挙動は基本的に変えない。各コミットの前後で、同じ操作に同じ結果が出ることを確かめる。挙動を変えた方が構造がきれいになる、またはコードが短くなるときは、その都度ユーザーに聞く。仕様判断と設計判断はユーザーが行う。

## 確定した方針（2026-10-24、ユーザー）

| # | 内容 |
|---|---|
| 1 | ボス戦の行動の順番と、体力を減らす量の判断は Application に移す。行動や体力に応じた演出を付けやすくし、ボスの状態を見た演出を強めやすくする為 |
| 2 | 敵の群れのルール（攻撃を始める条件、冷却、同時に攻撃する数の上限、隊列）は、ホットパスの最適化の為に EngineAdapterLayer に置いたままにする。最適化の為にやむを得ない、ごく一部の判断に限って EngineAdapterLayer に置いてよい |
| 3 | Application が `Time.*` や `Physics.gravity` のようなエンジンの static な値を読むのは許容する。許容しないのは、MonoBehaviour の継承やライフサイクルのように EngineAdapterLayer の責務を脅かすものだけ。層を分けるのは、コードが固まって仕様変更に耐えなくなるのを防ぐ為 |

## 既存コードの確認結果（2026-10-24 時点。着手時に見直す）

| 場所 | 今の状態 | 原則との関係 |
|---|---|---|
| `StageInitializer.OnWordLanded`（263 行） | 投げた言葉の着弾で、削る・敵を倒すを組み立て、`_isResolvingWordImpact` を持って Brilliant Skill に伝えるかを決める | Initialization にロジックがある |
| `StageInitializer.OnDefeated`（252 行） | 倒した数を `StageFlowService`・`StageGuideService`・`BrilliantSkillService` の 3 つに配る。ボスでの撃破を除く判定もここ | ロジックがある。直接配線の相手が 3 つ |
| `StageInitializer.OnBuildingCollapsed`（352 行） | `GetComponentsInChildren` で倒れた階の範囲を計算する | エンジンを叩く処理が Initialization にある |
| `PlayerInitializer.Rush` / `Fly`（183・198 行） | 突進で先に削る形と、飛行の上昇・下降の分け方を計算する | Initialization にロジックがある |
| `PlayerInitializer.OnSkillUsed` / `DistributeCutResults` | スキルの種類で Adapter を呼び分ける。切断の結果を配る | 着手時に、配線だけかを確かめる |
| `OutGameStartInitializer.QueueUnlockNotices` / `EquipSkill` | 解放の演出の順番待ちと「出したものとして記録する」時機を決める。装備の結果で表示を更新する | Initialization にロジックがある |
| `BossBattleService` / `BossBattleAdapter` | 段階の進みは Service。段階の中の行動の順番（下部レーザー → 降下フィニッシャー → 塔投げ → 待機、最終の繰り返し）は Adapter が決める | 方針1 で Application に移す |
| `HealthTarget` | 体力の値は `TargetHealthService` が持つ（Single Writer は守られている）。通常攻撃・スキル・落ちる塊で減らす量は Adapter が持つ | 方針1 で減らす量の判断を Application に移す |
| `EnemyGroup.State` / `CommandedState` | public フィールド。命令は Application の命令を EnemySpawnAdapter が写し、今の状態は EnemyGroupJob が書く | 方針2 の範囲。書き手が 2 つに見えないか、区間39 の変更のあとに確かめる |
| `SwordFormManagement`（EngineAdapter） | 中身が空で、MonoBehaviour でもない | 置き場所を決めるか消す |

### 着手時の見直し（2026-10-24）

| 場所 | 今の状態 | 扱い |
|---|---|---|
| `StageInitializer.OnDefeated` | 撃破を `StageFlowService`・`StageGuideService`・`BrilliantSkillService` の 3 つへ配る。`StageGuideService.AddDefeats` は「プレイ中だけ」の判定を `StageFlowService` と別に持つ | 40-1 |
| `StageInitializer.OnWordLanded` / `OnVoxelsCarved` / `OnBuildingCollapsed` | Brilliant Skill はスイッチ（`_isBrilliantSkillEnabled`）で無効だが、コードは残っている | 40-1。確認はスイッチを一時的に入れて行う |
| `PlayerInitializer.Swing` / `OnSkillUsed` | 1 つの呼び出しを、切断の Adapter と腕の Adapter の 2 つへ配る | 40-2 |
| `PlayerInitializer.DistributeCutResults` | 切断の結果を、敵の Adapter → かけらの Adapter の順に渡す。Application を通らない EngineAdapter 同士のデータの受け渡し | 残す。ゲームのルールではなく、エンジン側のデータの扱いの順番の為 |
| `OutGameStartInitializer.Quit` | `Application.Quit` / `EditorApplication.isPlaying` を直接呼ぶ | 40-3 |
| `BossBattleAdapter` | コア露出の行動の順番（`MeleeAction`）、最終の足踏み → レーザーの繰り返し、水晶の段階の攻撃、ダウン中に武装を 1 つ壊す判定、飛来と着地直後の無敵・落ちる塊を数える期間を決めている。行動の回数・間隔・待機の値は Adapter の SerializeField | 40-4 |
| `HealthTarget` | 減らす量（通常攻撃・スキル・落ちる塊の 1m³ あたり）を持つ。無敵を `_isInvulnerable` で持ち、`TargetHealthService` の表示の状態（`TargetHealthMode.Invincible`）と二重になっている | 40-5 |
| `EnemyGroup.State` / `CommandedState` | `CommandedState` を書くのは `EnemySpawnAdapter`（Application の命令の写し）、`State` を書くのは `EnemyGroupJob` だけ。フィールドごとの書き手は 1 つ | 残す。方針2 の範囲で、Single Writer も守られている |
| `SwordFormManagement` | ファイルがなくなっている | 作業なし |
| `UsefulToolkitPersistentCompositor` | ChildBoard の登録を行う生成物（code-refactoring の対象外） | Board を足すときは、生成で作り直す |

## 作業計画（2026-10-24 案。ユーザーの確認待ち）

各コミットのあとにコンパイルし、プレイモードで変えた経路を確かめる。挙動を変えた方がきれいになる箇所は、その都度聞く。

| コミット | 作業 | 内容 | 確かめること |
|---|---|---|---|
| 1 | 40-1 撃破の通知 | 敵を倒したことを EventBoard の出来事にする（`EnemyEventBoard` と `IEnemyDefeatEvents`。`PlayerEventBoard` と同じ形）。`EnemySpawnAdapter` が流し、流れ・案内・Brilliant Skill の 3 つの Service が購読する。ボスでの撃破を除く判定は `BrilliantSkillService` へ移す。`StageInitializer.OnDefeated` を消す | 殲滅のステージで撃破数・スコア・案内のログが前と同じに増える。クリアの判定が働く |
| 2 | 40-1 Brilliant Skill の配線 | 言葉の着弾（削る・倒す）と「着弾の間は判定に伝えない」を `BrilliantSkillService` へ移す（削る処理と倒す処理は関数で受け取る）。倒れた階の範囲の計算を `VoxelBuilding` へ移し、通知で中心と半径を渡す | スイッチを一時的に入れ、連続キル・倒壊の巻き込み・一撃の破壊が成立し、着弾の撃破が数えられない |
| 3 | 40-2 プレイヤーの配線 | `Rush` を `SkillService`、`Fly` を `FlightService` へ移す（体の範囲は関数で受け取る）。`Swing` / `OnSkillUsed` は決めること #5 による | 攻走で経路を削って通り抜ける。飛び移りで上昇 → 下降する。腕が振られる |
| 4 | 40-3 アウトゲームの配線 | 解放の演出の判断（順番待ちに加え、出したものとして記録する）を `GameProgressService` へ移す。`Quit` を `OutGameMenuAdapter` へ移す | ステージを初めてクリアして戻ると、解放の演出が 1 回だけ出る。終了ボタンでプレイモードが止まる |
| 5 | 40-4 ボスの行動の順番 | 今の行動（足踏み、下部レーザー、降下フィニッシャー、塔投げ、待機、水晶を投げる 等）を `BossBattleState` に持たせ、`BossBattleService` が次の行動を決める。Adapter は行動を 1 つ実行し、終わったら報告するだけにする。行動の回数・間隔・待機の値は `BossInitializer` へ移す（値はそのまま写す） | ボス戦を最初から最後まで通し、段階と行動の順番が前と同じ |
| 6 | 40-4 ボスの無敵とダウン | 飛来・着地直後の無敵、落ちる塊を数える期間、ダウン中に武装を 1 つだけ壊す判定を `BossBattleService` へ移す | 飛来中に体力が減らない。ダウン中の攻撃で武装が 1 つだけ壊れる |
| 7 | 40-5 体力を減らす量 | `HealthTarget` は当たりの種類（通常攻撃・スキル・落ちる塊の体積・量を決めたダメージ）を報告するだけにし、量は `TargetHealthService` が決める。量の値は対象ごとに違うので、`Setup` で対象の設定として受け取る。無敵は決めること #7 による | 殲滅・崩壊・ボスの各対象で、同じ攻撃で前と同じだけ体力が減る |
| 8 | ドキュメント | 全体計画書の「手本にする既存コード」と「現状」を直す | ― |

### 新しく作る型と、今の区間で使う利用者

| 型 | 層 | 利用者 |
|---|---|---|
| `EnemyEventBoard` / `IEnemyDefeatEvents` / `EnemyDefeatEvents` | BlackBoardLayer | 発行：`EnemySpawnAdapter`。購読：`StageFlowService`・`StageGuideService`・`BrilliantSkillService`（3 つ） |
| ボスの行動の種類（enum） | BlackBoardLayer | 書く：`BossBattleService`。読む：`BossBattleAdapter`。層を分ける為に要る（planning-criteria 基準1 の例外） |
| 体力の対象ごとの減らす量（struct） | BlackBoardLayer か ExternalLayer | 受け取る：`TargetHealthService`。渡す：`StageInitializer`（`HealthTarget` の値を読んで渡す） |

## 詳細仕様で決めること（作業計画の分）

| # | 項目 | 案 |
|---|---|---|
| 4 | 撃破を EventBoard に載せるか（#1 の答え） | 載せる。受け取る Service が 3 つあり、Usecase 1 つにまとめると流れ・案内・Brilliant Skill を 1 クラスが知ることになる。代わりの案は、案内が `IStageFlowState` の撃破数の変化を購読し、配線を流れと Brilliant Skill の 2 本にする形 |
| 5 | 腕のアニメーションの配線 | 腕の Adapter が `IPlayerActionEvents` を購読する。振りの角度とスキルの腕のアニメーション名を通知に足す必要がある。足さない場合は、今の配り分けを残す |
| 6 | ボスの行動の値の置き場所 | `BossInitializer` の SerializeField（`_stompsPerRound` と同じ並び）。ボスのプレハブの値を写す |
| 7 | 無敵を 1 つにまとめるか | `HealthTarget._isInvulnerable` を消し、`TargetHealthMode.Invincible` にまとめる。ただし飛来中と着地直後の HUD のゲージの見た目が「無敵」の表示に変わる（挙動の変更） |
| 8 | 生成物の Compositor | `EnemyEventBoard` の登録は、生成で作り直すのを基本とする。生成する前に手で足すなら、生成される形と同じにする |

### 決めたこと（2026-10-24、ユーザー）

- #4・#6・#7・#8 は案のとおり
- #5：腕のアニメーションは出来事の購読に分けない。分けてもクラスが増えるだけで、分ける理由がない為。通常攻撃の腕の振りは `MeleeCutAdapter` が腕の Adapter を持って自分で振らせ、スキルの腕のアニメーションは `SkillService` の発動の通知を腕の Adapter へ直接つなぐ。どちらも配線の相手は 1 つになる
  - 2026-10-25 に見直した：スキルの腕のアニメーションは、ほかのスキルの効果と合わせて、発動の出来事を購読する形にする（[後半の計画書](Section40_ArchitectureRefactoring_Part2.md)の決定 #17）。スキルの配線相手が 5 クラスになっていた為。通常攻撃の腕の振りは今のまま

## コミット5（ボスの行動の順番）の詳細計画（2026-10-24）

### 今の分担

| 判断 | 今の置き場所 |
|---|---|
| 段階の進み（飛来 → 足踏み → コア露出 → ダウン → 水晶 / 最終 → 停止）、足踏みの回数、体力の表示の状態 | `BossBattleService` |
| 段階に入ったときの切り替え（円形の空間を開く・閉じる、先読みの円、武装の赤、レーザーのコアを出したまま にする 等） | `BossBattleAdapter.Enter` |
| コア露出の行動の順番（下部レーザー → 降下フィニッシャーを 3 回・1.5 秒おき → 出したビームが終わるのを待つ → 塔投げ → 10 秒待機 → 報告） | `BossBattleAdapter.TickCoreExposed` / `TickStrikes`（`MeleeAction`） |
| 最終の繰り返し（足踏み 3 回 → 下部レーザーを撃っている間だけ降下フィニッシャーも撃つ → 足踏みへ戻る） | `BossBattleAdapter.TickFinal` |
| 水晶の段階の流れ（退く所へ飛ぶ → 水晶を投げる → 壊れるまでタレット・降下フィニッシャー・頭部のレーザーをそれぞれのクールタイムで撃つ） | `BossBattleAdapter.TickCrystal` |
| 飛来で着いてから足踏みを始めるまでの 1 秒 | `BossBattleAdapter.TickArriving` |
| 武装 1 つずつのクールタイム、壊れた番を飛ばす | 武装ごとの Adapter（`BossLaser.TryFire` など） |
| 無敵・落ちる塊を数える期間・ダウン中に武装を壊す判定 | `BossBattleAdapter`（コミット6 で扱う） |

行動の値（`_landingPause`・`_meleeStrikeCount`・`_meleeStrikeInterval`・`_towerRest`・`_finalStomps`）は、ボスのプレハブとシーンでは `_landingPause: 1` のほかは保存されておらず、すべてコードの既定値で動いている（プレハブに古い `_allOutDuration` が残っている）。

### 移したあとの形

- `BossBattleState` に「今の行動」と、行動を出し直した回数を足す。同じ行動（足踏み → 足踏み）が続いても、Adapter が新しい指示だと分かるようにする為
- `BossBattleService` が段階ごとに次の行動を決め、`Tick` で待機の時間を数える。Adapter は今の行動を 1 つ実行し、終わったら `ReportActionFinished` で報告する
- 行動の種類（案）

| 行動 | Adapter がすること | 終わり |
|---|---|---|
| 待機 | 何もしない（Service が時間を数える） | Service が決める |
| 退く | 退く所へ飛ぶ | 着いたら。退く所や飛来がなければすぐ |
| 水晶を投げる | 水晶を投げる | 投げたらすぐ |
| 遠距離の攻撃 | タレット・降下フィニッシャー・頭部のレーザーを、それぞれのクールタイムで撃ち続ける | 水晶がすべて壊れたら（今の `ReportCrystalsCleared`） |
| 飛来 | 着地点へ飛ぶ | 着いたら |
| 足踏み | アウトリガーで 1 回足踏みする | 足踏みしたら。アウトリガーがなければすぐ |
| 下部レーザー | 次の番のレーザーを撃つ | 撃ち終えたら。レーザーがなければすぐ |
| 降下フィニッシャー | 1 つ出す | 出したらすぐ |
| 降下フィニッシャーを待つ | 何もしない | 出したビームがすべて終わったら |
| 塔投げ | 塔を投げる | 投げたらすぐ |
| 最終の一斉攻撃 | 下部レーザーを撃ち、撃っている間は降下フィニッシャーも撃つ | レーザーを撃ち終えたら |

- `ReportArrived`・`ReportStomp`・`ReportCoreAttackFinished` は Service の中の判断になるので、Adapter からの報告は `ReportActionFinished` と `ReportCrystalsCleared` だけにする（`ReportWeaponBroken` はコミット6 で見直す）
- 行動の値は `BossInitializer` へ移し、Service に渡す。値は今の既定値のまま写す
- 段階に入ったときの切り替え（`Enter`）は Adapter に残す。State の段階をエンジン側へ写すだけで、ゲームのルールではない為
- 武装ごとのクールタイムと、遠距離の攻撃でそれぞれを撃つ間隔（`_crystalStrikeCooldown`）は Adapter に残す。攻撃 1 つの実行の中身の為（決めること #2 の案）

### 挙動が変わりうる箇所

- 行動の切り替わりが 1 フレーム遅れる。今は同じフレームで次の行動に入る箇所（レーザーを撃ち終えた → 降下フィニッシャー）が、報告 → Service が次を決める → Adapter が次のフレームで読む、の順になる。見た目では分からない程度の想定
- 降下フィニッシャーがない（全部壊れた）ときの扱い。今はフィニッシャーを 0 回にしてすぐ塔投げへ進む。新しい形では、Adapter が「出せなかった」ことを報告に載せ、Service が残りの回数と間隔を飛ばす

### 新しく作る型と利用者

| 型 | 層 | 利用者 |
|---|---|---|
| ボスの行動の種類（`BossAction`、enum） | BlackBoardLayer | 書く：`BossBattleService`。読む：`BossBattleAdapter` |
| ボス戦の行動の値（`BossBattleSettings`、Serializable、決めること #9 による） | ExternalLayer | 持つ：`BossInitializer`。読む：`BossBattleService` |

### 決めること

| # | 項目 | 案 |
|---|---|---|
| 9 | 行動の値の渡し方 | `BrilliantSkillSettings` と同じ形の `BossBattleSettings` にまとめ、`BossInitializer` が持って Service に渡す。今 `BossInitializer` にある `_stompsPerRound`・`_finalDuration` は、名前を変えずにそのまま残す（シーンの値を保つ為）。まとめない場合は、Service のコンストラクタの引数が 5 つ増える |
| 10 | 「出せなかった」の報告 | `ReportActionFinished(bool isPerformed)` にする。今は降下フィニッシャーでだけ使う |
| 11 | 行動が変わったことの通知 | 今回は足さない。Adapter は毎フレーム State を読む（今の段階の読み方と同じ）。行動に合わせた演出を足すときに、State の変化の通知を足す |

#9〜#11 は案のとおり（2026-10-24、ユーザー）

## コミット6（ボスの無敵とダウン）の詳細計画（2026-10-24。#12 は案のとおり）

### 今の分担

| 判断 | 今の置き場所 |
|---|---|
| 飛来の間と、足踏みを始めてから猶予（`_collapseGrace` = 2 秒）が過ぎるまでは無敵 | `BossBattleAdapter.UpdateCollapseCounting` → `HealthTarget.SetInvulnerable` |
| 足踏みとコア露出の間（猶予のあと）だけ、落ちる塊のダメージを数える | 同上 → `HealthTarget.SetCollapseCounting` |
| ダウン中に攻撃が当たったら、1 回だけ武装を壊す | `BossBattleAdapter.OnHitWhileDepleted`（`_isWeaponBrokenInDown`） |
| 壊れた武装の数がダウンに入ったときより増えたら、または壊せる武装が残っていなければ報告 | `BossBattleAdapter.TickDown`（`_brokenBaseline`）→ `ReportWeaponBroken` |
| 壊す武装を選ぶ（画面の中央に近く、プレイヤーに近いもの） | `BossWeapons.BreakBest` |

### 移したあとの形

- 無敵と落ちる塊を数えるか
  - `BossBattleService.Tick` が、足踏みとコア露出が続いている時間を数え、`BossBattleState` の `IsInvulnerable`・`CountsCollapse` に書く
  - Adapter は毎フレーム、その 2 つを `HealthTarget` に写すだけにする
  - 猶予の値は `BossBattleSettings.CollapseGrace` へ移す（プレハブ・シーンに保存された値はなく、既定値 2 秒のまま）
- ダウン中の武装の破壊は、コミット5 の行動の形に乗せる
  - ダウンに入ったら、Service が行動を `AwaitHit`（攻撃を待つ）にする。Adapter は、壊せる武装が残っていなければ `ReportActionFinished(false)` で報告する
  - 体力が尽きたあとの当たりは、`BossInitializer` が `HealthTarget.SetOnHitWhileDepleted` を `BossBattleService.ReportHitWhileDown` へ直接つなぐ（今は Adapter が受けている）
  - Service は、行動が `AwaitHit` のときだけ行動を `BreakWeapon`（武装を壊す）にする。これで「ダウン中に 1 回だけ」が Service の判断になる
  - Adapter は `BreakWeapon` で `BreakBest` を呼び、壊せたら赤を消して `ReportActionFinished(true)`、壊せなかったら（カメラがない等）`false` で報告する
  - Service は、`BreakWeapon` が true、または `AwaitHit` が false で終わったら、今の `ReportWeaponBroken` の処理（水晶 / 最終へ進む）を行う。`BreakWeapon` が false なら `AwaitHit` に戻す
- 消えるもの：`ReportWeaponBroken`（public から private へ）、Adapter の `TickDown`・`OnHitWhileDepleted`・`_brokenBaseline`・`_isWeaponBrokenInDown`・`_fightTime`・`_collapseGrace`、`Initialize` の `reportWeaponBroken` 引数
- 壊す武装の選び方（`BreakBest`）と、全パーツの保護（`UpdateProtection`）は Adapter 側に残す。前者は画面とプレイヤーの位置から選ぶ実行の中身、後者は State の「止まったか」を写すだけの為

### 挙動が変わりうる箇所

- ダウンの終わりが 1〜2 フレーム遅れる（当たり → Service → Adapter が壊す → 報告 → Service が進める）
- 今は「壊れた武装の数が増えたら」進むので、`BreakBest` 以外で武装が壊れても進む。新しい形では `BreakWeapon` を通したときだけ進む。ボスのパーツは止まるまで保護されていて、ダウン中に `BreakBest` 以外で壊れることはない為、実際の挙動は変わらない想定

### 新しく作る型と利用者

新しい型は作らない。`BossAction` に `AwaitHit`・`BreakWeapon` を、`BossBattleState` に `IsInvulnerable`・`CountsCollapse` を、`BossBattleSettings` に `CollapseGrace` を足す。

### 決めること

| # | 項目 | 案 |
|---|---|---|
| 12 | 無敵をこのコミットで State に置くか | 置く。コミット7 で `TargetHealthMode.Invincible` にまとめるときに、`BossBattleService` が `TargetHealthService` へ無敵を指示する形へ書き換える。先にコミット7 を行えば書き換えは 1 回で済むが、コミット7 は対象すべての体力に関わり大きいので、ボスの判断を移すことを先に済ませる |

## コミット7（体力を減らす量）の詳細計画（2026-10-24。#13 は案のとおり）

### 今の分担

| 判断 | 今の置き場所 |
|---|---|
| 通常攻撃 1 回・スキル 1 回・落ちる塊 1m³ で減る量 | `HealthTarget` の SerializeField（ボス：0.5 / 1 / 1、`Town_CollapseSite`：0.5 / 1 / 2） |
| 落ちる塊を探し、同じ塊を 1 回だけ数える | `HealthTarget.FixedUpdate` |
| 落ちる塊を数えるか（ボスは足踏み・コア露出の猶予のあとだけ） | `BossBattleService` → `BossBattleState.CountsCollapse` → Adapter が `HealthTarget` へ写す |
| 無敵 | 2 か所。`HealthTarget._isInvulnerable`（飛来・着地直後、`BossBattleState.IsInvulnerable` を写す）と、`TargetHealthMode.Invincible`（水晶の段階） |
| 量を決めたダメージ（羽天轟号の毎秒の量、倒れた塔の 30） | 攻撃側の Adapter（`SkyBeamAdapter`・`BossTowerThrow`） |
| 体力を引く、尽きたか | `TargetHealthService.Damage` |
| 尽きたら保護を外す、尽きたあとの当たりを知らせる | `HealthTarget` |

### 移したあとの形

- `HealthTarget` は当たりの種類と大きさを報告するだけにする
  - `Initialize(Func<TargetHitKind, float, bool> reportHit)`。戻り値は今と同じく「この当たりで尽きたか」
  - 種類（`TargetHitKind`、BlackBoardLayer の enum）：`Melee`・`Skill`・`Collapse`（大きさは塊の体積 m³）・`Direct`（大きさは減らす量）
  - `_meleeDamage`・`_skillDamage`・`_collapseDamagePerCubicMeter`・`_isInvulnerable`・`_countsCollapse` と、`SetInvulnerable`・`SetCollapseCounting` を消す
  - 落ちる塊を探すこと、同じ塊を 1 回だけ数えること、尽きたら保護を外すこと、尽きたあとの当たりを知らせることは残す（エンジン側の検出と反応の為）
- `TargetHealthService` が量を決める
  - `Setup(name, max, damage)` で、対象ごとの量（決めること #13）を受け取る
  - `ReportHit(kind, size)` で、種類ごとの量に直して引く。`Collapse` は、落ちる塊を数えないときは引かない
  - `SetCollapseCounting(bool)` を足す。既定は数える
- 無敵は `TargetHealthMode.Invincible` だけにする（決めること #7）
  - `BossBattleService` の `UpdateDamageRules` は、飛来・猶予の間は `Invincible`、猶予のあとは `Normal` を `TargetHealthService` に指示し、落ちる塊を数えるかも `TargetHealthService` に指示する
  - `BossBattleState.IsInvulnerable`・`CountsCollapse`・`SetDamageRules` と、Adapter の `ApplyDamageRules` を消す（コミット6 で足したものを戻す形）
- 量を決めたダメージ（羽天轟号・倒れた塔）は、量を攻撃側が持ったまま `Direct` で報告する。量が攻撃ごとの設定で、対象ごとの設定ではない為
  - 2026-10-25 に見直した：この 2 つの量も ExternalLayer の設定へ移す（[後半の計画書](Section40_ArchitectureRefactoring_Part2.md)の決定 #23）。手本の「ルールの値は ExternalLayer」と揃える為

### 挙動が変わる箇所

- 飛来の間と着地直後の約 2 秒、HUD の体力のゲージが「無敵」の表示（色と名前の後ろの「無敵」）になる（#7 で了承済み）

### 新しく作る型と利用者

| 型 | 層 | 利用者 |
|---|---|---|
| 当たりの種類（`TargetHitKind`、enum） | BlackBoardLayer | 報告する：`HealthTarget`。量に直す：`TargetHealthService` |
| 対象ごとの量（#13 による） | ExternalLayer | #13 による |

### 決めること

| # | 項目 | 案 |
|---|---|---|
| 13 | 対象ごとの量の置き場所 | `TargetDamageSettings`（Serializable、通常攻撃・スキル・落ちる塊 1m³ の 3 つ）を作り、`StageData` に持たせる。体力の対象は 1 ステージに 1 つなので、ステージの設定として置ける。`StageInitializer.ConnectHealthTarget` が `Setup` に渡す。値はボスのステージに 0.5 / 1 / 1、崩壊のステージに 0.5 / 1 / 2 を写す。別案は、値を `HealthTarget` に残して Initializer が読んで渡す形で、アセットは触らずに済むが、ルールの値が EngineAdapterLayer に残る |

asmdef の参照、Board のキーの型、Application・BlackBoardLayer・ExternalLayer にライフサイクルがないことは確認済みで、問題はない。

## 続きの作業計画（2026-10-25 案。ユーザーの確認待ち）

コミット1〜8 のあとで、ユーザーの指摘により、完了条件に照らして残っている問題が 4 つ見つかった。4 つとも次の案で直すことに決まった（2026-10-25、ユーザー）。

| # | 問題 | 直し方 |
|---|---|---|
| A | `StageInitializer` に判断が残っている（分岐 35 個。ほかの Initializer は 13 個以下） | 体力制の対象の選び方は `StageClearAdapter`、デバッグの文字列はデバッグ用の Initializer、開始位置へ置く処理は `PlayerStartPoint` へ移す。参照が取れないときのエラーログと、既定のステージへの切り替えは残す |
| B | Service が別の Service を具象型で持つ（7 つの Service、11 か所） | 使う操作だけの狭いインターフェースを BlackBoardLayer に定義し、Initializer がそれを渡す |
| C | EngineAdapter の検索とシングルトン | 同じシーンにある物は Initializer から渡す。別のシーンにある物の検索と、実行中に作られる物が使う `Instance` は残し、理由をサマリーに書く |
| D | ボスの Adapter が、武装の数で行動を飛ばすかを決めている | 武装の残りの数を `BossWeaponState` に Adapter が書き、飛ばすかは `BossBattleService` が決める |

### コミットの分け方

| コミット | 作業 | 内容 | 確かめること |
|---|---|---|---|
| 9 | A | `IsStageTarget` と `ConnectHealthTarget` の選び方を `StageClearAdapter` へ移す（`KeepGimmicks` と同じ突き合わせを使う）。Initializer は見つかった `HealthTarget` を `TargetHealthService` へつなぐだけにする。`PlacePlayerAtStartPoint` を `PlayerStartPoint` へ移す | ボス・崩壊のステージで体力のゲージが出て減る。開始位置の目印のあるステージで、その位置と向きから始まる |
| 10 | A | `GetTargetText` を `StageDebugInitializer`（新規）へ移す。`BossDebugInitializer` と同じ形 | デバッグ表示の「Stage」が前と同じに出る |
| 11 | D | `BossWeaponState`（新規）に、種類ごとの残りの数と、壊せる武装が残っているかを持たせる。`BossBattleAdapter` が書き、`BossBattleService` が読んで、足踏み（アウトリガー）・下部レーザー・降下フィニッシャーがないときに行動を飛ばし、壊せる武装がないときにダウンを終える。Adapter の `ReportActionFinished(false)` は、実行してみて行えなかったとき（カメラがない等）だけにする | ボス戦を最初から最後まで通す。武装を壊したあとの周で、壊れた武装の行動が飛ばされる |
| 12 | B | プレイヤーの Service の操作面を作る：チャージの消費（`ChargeService`。使う側 `SkillService`・`SlowModeService`・`SkyBeamService`）、強化（`PlayerEnhanceService`。`SkillService`・`MeleeCutService`）、移動（`PlayerMovementService`。`SkillService`・`SkyBeamService`・`FlightService`・`FragmentThrowService`）、羽天轟号（`SkyBeamService`。`SkillService`） | スキル・スロー・羽天轟号・飛び移り・言葉をつかむ・通常攻撃の強化が前と同じに動く |
| 13 | B | ボスの体力の操作面を作る（`TargetHealthService`。`BossBattleService`） | ボス戦で体力の減り方・無敵・最終の減り方が前と同じ |
| 14 | C | EngineAdapter の `Find〜`（10 ファイル）と `Instance`（`SoundPlayer`・`VoxelAttraction`・`GlassShardPool`）を 1 つずつ調べ、同じシーンにある物は Initializer から渡す。残すものはサマリーに理由を書く | 変えた Adapter のあるステージを一通り遊ぶ |
| 15 | ― | 全体計画書の手本（操作面のインターフェース、Adapter が書く State の例）と、この計画書の状態を直す。40-7（Notion とスキルへの反映案）を出す | ― |

### 新しく作る型と、今の区間で使う利用者

| 型 | 層 | 利用者 |
|---|---|---|
| `StageDebugInitializer` | Initialization | 置く：InGame のシーン。読む：`IStageFlowState`・`StageClearAdapter.Targets` |
| `BossWeaponState`（と Getter） | BlackBoardLayer | 書く：`BossBattleAdapter`。読む：`BossBattleService` |
| チャージの消費の操作面 | BlackBoardLayer | 実装：`ChargeService`。使う：`SkillService`・`SlowModeService`・`SkyBeamService` |
| 強化の操作面 | BlackBoardLayer | 実装：`PlayerEnhanceService`。使う：`SkillService`・`MeleeCutService` |
| 移動の操作面 | BlackBoardLayer | 実装：`PlayerMovementService`。使う：`SkillService`・`SkyBeamService`・`FlightService`・`FragmentThrowService` |
| 羽天轟号の操作面 | BlackBoardLayer | 実装：`SkyBeamService`。使う：`SkillService`（利用者は 1 つだが、規約で分ける） |
| ボスの体力の操作面 | BlackBoardLayer | 実装：`TargetHealthService`。使う：`BossBattleService`（利用者は 1 つだが、規約で分ける） |

### 決めること

| # | 項目 | 案 |
|---|---|---|
| 14 | 操作面に読み取り（`IsActive`・`IsHoldingWord`・`CanRush`・`IsDepleted` など）を含めるか | 対応する State の Getter に同じ値があれば、そちらを読む。なければ操作面に含める。読み取りのためだけに State へ値を足すことはしない |
| 15 | 操作面の分け方 | 実装する Service 1 つにつき 1 つにする。使う側ごとにさらに細かく分けることはしない（型が増えるだけの為） |
| 16 | `StageDebugInitializer` の置き場所 | InGame のシーンの `Stage` と同じ GameObject に置く。置いたあとに `UsefulToolkit/Generate/Scene Compositor` で作り直す（ユーザーの作業） |

## 後半の作業計画

コミット9〜14 のあとにユーザーが改めて調べた結果をもとに、残った原則違反の整理（コミット15〜24）を [Section40_ArchitectureRefactoring_Part2.md](Section40_ArchitectureRefactoring_Part2.md) に分けて書いた。続きの作業計画のコミット15（ドキュメント）は、後半のコミット23 に含める。

## 追加：崩壊ステージの改修と吸い寄せ（2026-10-24、ユーザー）

整理とは別に、このブランチで続けて行う。

### 崩壊ステージ（ステージ3）の改修

- 時間がたつと元に戻る梁（`RegrowingStructure` と、`Town_CollapseSite` の柱と梁）を消す
- 破壊対象のコア（`Core_Collapse`）を、商店街のアーケードの通りの中に置く。ビル街の中庭の置き場（`TownLayout.PlanCollapseSite`）は消す
- コアは、アーケードの棟を崩して潰す。崩れた棟は、下の吸い寄せでコアの側へ倒れる。案内の文（`CollapseGuideTexts` の 2 段階目）も、アーケードを崩す内容にした

### 吸い寄せ

ボクセルの物が切り離されて Rigidbody になるとき、重心を高くして倒れやすくし、水平方向は引き寄せ対象の側へずらす。

- 対象の物：切断したかけら（`VoxelPiece`）と、倒れた建物のまとまり（`VoxelBuilding`）
- 引き寄せ対象：敵の部隊の中心、崩壊ステージの破壊対象、ボス
- 選び方：届く距離の中で一番近い 1 つ。破壊対象とボスは優先で、届く距離の中にあれば敵の部隊より先に選ぶ。破壊対象とボスの間に優先の差はない
- 崩壊ステージの破壊対象は、`StageData._attractsToTargets` をオンにしたステージ（今は `CollapseStageData` だけ）の、クリアの対象の `DestructionTarget`。`StageClearAdapter` が登録する
- 重心は、Unity が形から求めた重心から、物の大きさに対する割合でずらす（`Rigidbody.centerOfMass`）

| 型 | 層 | 利用者 |
|---|---|---|
| 吸い寄せ（`VoxelAttraction`、MonoBehaviour） | EngineAdapterLayer | 置く：InGame のシーン。重心をずらす：`VoxelPiece`・`VoxelBuilding`。位置を渡す：`EnemySpawnAdapter`・`StageClearAdapter`・`BossBattleAdapter` |
| 優先の引き寄せ対象（`IAttractionTarget`、interface） | EngineAdapterLayer | 実装：`DestructionTarget`・`BossBattleAdapter`。読む：`VoxelAttraction` |

引き寄せ対象の位置は、どれもエンジン側の物の位置で、ゲームのルールの判断を含まない。その為、State を挟まず、EngineAdapterLayer の中で `VoxelAttraction` に渡す。かけらは実行中に作られて Initializer から配線できない為、`SoundPlayer` と同じく `Instance` で参照する。

## 作業一覧

| # | 作業 | 内容 |
|---|---|---|
| 40-1 | ステージの配線の整理 | `OnWordLanded`・`OnDefeated`・`OnVoxelsCarved` の判断を、Application の Usecase 1 つにまとめる。Initializer はその Usecase へ 1 本ずつつなぐだけにする。倒れた階の範囲の計算は `VoxelBuilding` 側へ移す |
| 40-2 | プレイヤーの配線の整理 | `Rush` / `Fly` の形の計算を、移動の Service（決める部分）と Adapter（体の範囲を読む部分）へ分ける |
| 40-3 | アウトゲームの配線の整理 | 解放の演出の判断を `GameProgressService` 側へ移す |
| 40-4 | ボスの行動の順番 | 段階の中の行動の順番を `BossBattleService` へ移す。Adapter は「この行動を始める」「終わった」の実行と報告だけにする |
| 40-5 | 体力を減らす量 | 攻撃の種類ごとの減らす量を Application に移す。`HealthTarget` は当たりの検出と報告だけにする |
| 40-6 | 小さい整理 | `EnemyGroup` の書き手の確認、`SwordFormManagement` の整理 |
| 40-7 | 設計の正本への反映 | 方針2・3 を Notion の設計ページとスキルに書き足す案を出す。書き戻しはユーザーが確かめてから |

40-1 と 40-4 は、作業の中で新しい Usecase を作るかを決める。作るときは、今の区間で使う利用者を書く（planning-criteria の基準1）。

## 完了条件

- Initializer に、生成・登録・配線のほかの判断が残っていない（デバッグ用の Initializer は除く）
- 直接配線の相手が、原則どおり 1 クラスになっている
- ボスの段階の中の行動の順番と、体力を減らす量の判断が Application にある
- 全ステージとボス戦を通して遊び、整理の前と同じ挙動になる

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 40-1 で、撃破を EventBoard に載せるか、Usecase 1 つにまとめるか | Brilliant Skill はスイッチで無効になっている（コミット 6861d12）。受け取るのが Usecase 1 つで済むなら、EventBoard は使わない |
| 2 | 40-4 で、Adapter に残す単位 | 「攻撃 1 つを最後まで実行する」単位を Adapter に残し、次に何をするかだけを Service が決める |
| 3 | 40-5 で、減らす量をどこに持つか | `TargetHealthService` に攻撃の種類を渡す形にするか、別のデータに持つか |

## 見つけた問題（今回は扱わない）

- なし（着手時の見直しで足す）
