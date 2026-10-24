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

asmdef の参照、Board のキーの型、Application・BlackBoardLayer・ExternalLayer にライフサイクルがないことは確認済みで、問題はない。

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
