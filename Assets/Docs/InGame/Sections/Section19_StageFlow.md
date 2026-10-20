# 区間19：ステージ制・インゲームの流れ・HUD

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-15） |
| 目安の時期 | 2027/04/13〜04/26（最速の推定 2026/10/29〜11/01） |
| 前提となる区間 | マイルストーンB（区間8まで） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

ステージごとのデータを持たせ、ステージ選択からリザルトまでのインゲームの流れを作る。HUD、スコア、リトライ、ポーズを作り、アウトゲームとデータを受け渡す。
ステージは 4 つ（チュートリアル、一般戦闘、ギミック戦闘、ボス戦）。この区間では流れを作るところまでで、各ステージの中身は区間22と区間26で作る。

## 関連する仕様

- ステージ構成・クリア条件：https://app.notion.com/p/3e91ea2aa7fa81e682c6cfbe4f5a11c2
- 失敗条件：https://app.notion.com/p/3e91ea2aa7fa813fb405e07c700a75b4
- アウトゲーム・通貨：https://app.notion.com/p/3e91ea2aa7fa81e3b271e0fc71189ea6
- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70
- スコア：https://app.notion.com/p/3f01ea2aa7fa812c94f6e302a2bf270d（倒した敵の数、クリアタイム、合計被ダメージなどから計算して表示する。報酬には関わらない）
- プレイヤーの HP：https://app.notion.com/p/3f01ea2aa7fa8110b22de3c533e228c2（ステージは HP 満タンで始まる）

## 既存の資産

| クラス | 内容 |
|---|---|
| [GameSceneController](../../../Code/Scripts/Application/Scene/GameSceneController.cs) | アウトゲームとインゲームの単位でシーン遷移を要求する。操作面 `IGameSceneController` は DI で受け取る。場面ごとに 1 つの SceneGroup を持つ（区間3で変更）。インゲームにいるときに `GoToInGameAsync` を呼んでも、ロード済みのシーンは読み直されない（区間4 の食い違い #6） |
| [GameSceneInitializer](../../../Code/Scripts/Initialization/Scene/GameSceneInitializer.cs) | 常駐シーンにあり、場面ごとの SceneGroup を `GameSceneController` に渡す |
| 仮のアウトゲーム | OnGUI のボタン（`OutGameStartInitializer`）でインゲームへ進むだけ。この区間で本物のアウトゲームの流れに置き換える |
| 失敗の流れ | まだない。区間4から移した（2026-10-05）。敵の攻撃は区間13で入る |
| ステージシーン | 区間4 で、ライト・地面・生成システムなどをステージシーン（`TestStage`）に分けた。`InGameGroup` は「ステージシーン（アクティブ）＋ InGame」 |
| `IPlayerHealthState` / `PlayerHealthService` | 区間1で作った HP の State と Service。HUD の HP 表示はこの State を読む。State は InGame の SceneState なので、インゲームに入るたびに満タンに戻る。合計被ダメージを数える処理はまだない |
| `PauseBoard` / `IPausable` | UsefulToolkit.ProgramTools のポーズ用の Board とインターフェース（`IsPaused` / `Pause` / `Resume`）。Board は常駐シーンに登録済みだが、中身はまだない |
| 仮の HUD | 区間8の `PlayerHudAdapter`（InGame の `PlayerHud`、OnGUI）。画面の下端に HP、その上にチャージのゲージを出し、減ると中央へ縮む。チャージのゲージに、装備したスキルの消費量の位置の線を引く（`ISkillSlotState`）。この区間の HUD で置き換える |
| スキルの装備 | 区間8では、`PlayerInitializer` の Inspector の `_equippedSkills`（`SkillData` の 3 枠）に仮で置いている（TODO）。`SkillService` が受け取り、`SkillSlotState` に消費量を書く |

## 前提の変化（2026-10-07、区間7 の完了時）

- 区間7で、クリアの判定を EngineAdapter の `StageClearAdapter` に置き、仮の「STAGE CLEAR」を画面に出す形にした（区間7の決定 5。読むのが仮の表示だけだった為）。リザルトとリトライがクリアを読むので、この区間で Application の Service と BlackBoard の State に移し、仮の表示を置き換える
- 破壊対象は、ステージシーンの `DestructionTarget` が持つ（重要パーツのパスと必要な割合）。ステージデータに破壊対象の情報を持たせるかを決める

## 前提の変化（2026-10-08、区間の順番の変更）

- 区間11（リファクタリング）、区間13（敵の固有アクション）、区間18を、この区間の前に置いた。区間13は、この区間を前提にしなくなった。「既存の資産」に書いたクラス名と置き場所は、区間11 で変わっている可能性がある。着手時に今のコードで見直す
- 区間13で敵の攻撃が入る。失敗（11-3a）は、HP のデバッグ操作だけでなく、敵の実際の攻撃で HP を 0 にして確かめられる。それまでは HP が 0 になっても何も起きない
- 区間13で、吸収型の敵がほかの敵を吸収して消す予定（区間13の案の 9）。スコアの「倒した敵の数」に、吸収された敵を数えるかを決める
- 区間13で敵の種類が増える。ステージデータ（11-1）に敵の出し方を持たせるときは、区間13で生成情報に足す「グループの編成」（区間13の案の 2）を含める

## 前提の変化（2026-10-14、区間19の着手時）

- 区間17（`feature/alpha/big-voxel`）と区間18は進行中。区間19は区間8までを前提にしているので、並べて進める。区間17で、倒れた建物の塊も崩落の判定（`EnemyCollapseDetector.WatchFalling`）に入るようになった。崩落で倒した敵の数え方は変わらない
- 仕様書（Notion）を読んだ。決まっていることは次のとおり
  - 失敗条件は「HP が 0」だけ。ステージごとの変化は「検討中」
  - 初回クリアの通貨は、スコアに関わらず得られ、量はステージによって変わらない。通貨でスキルを買い、3 枠に装備して挑む
  - スコアの計算式・重み、失敗したときに出すか、最高スコアを保存するかは「検討中」
- 時間制御（スローモード）の仕様書のリンク（`3ea1ea2aa7fa8183bbfac10bfafd4d70`）は、Notion のコネクタから見つからなかった（404）

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 11-1 | ステージデータ | ExternalLayer | 4 ステージ分。ステージシーン、敵の出現の上限と間隔、破壊対象、クリアに必要な割合、失敗条件 |
| 11-2 | インゲームの流れ | BlackBoard / Application | 開始 → プレイ中 → クリアまたは失敗 → リザルト、の状態 |
| 11-3 | HUD | EngineAdapter | HP、チャージ、スキルの 3 枠、破壊の進み具合、ランチャーの装填状態 |
| 11-3a | 失敗 | Application / EngineAdapter | HP が 0 になったら失敗にする（区間4から移した）。ステージごとの失敗条件は 11-7 |
| 11-4 | リザルト | EngineAdapter | クリアと失敗の画面、アウトゲームへ戻る |
| 11-4a | スコア | BlackBoard / Application | 倒した敵の数とクリアの基礎スコアからスコアを計算してリザルトに出す（2026-10-14 に変更。決めたこと 5） |
| 11-4b | リトライ | UsefulToolkit / Application | 同じステージをもう一度始める。同じ SceneGroup を読み直せないので、UsefulToolkit にシーンを読み直す経路を足す（2026-10-05 決定。ダミーのシーングループを経由する案より、こちらを採る）。区間2と同じく、要件定義を書いて UsefulToolkit のリポジトリで作る |
| 11-5 | アウトゲームとの受け渡し | Application | 装備しているスキルを受け取り、初回クリアの通貨を返す |
| 11-6 | ポーズ | Application / EngineAdapter | ポーズ中の時間の止め方と、プレイヤーのアニメーションの停止 |
| 11-7 | ステージごとの失敗条件 | External / Application | 失敗条件をステージデータから差し替えられるようにし、「HP が 0」も外せるようにする（決めたこと 7） |

## 完了条件

- アウトゲームでステージを選ぶとインゲームに入り、クリアか失敗でリザルトに進み、アウトゲームへ戻る
- 装備したスキルがインゲームで使え、初回クリアで通貨が増える
- ポーズでゲームが止まる

## 既存コードの確認結果（2026-10-14）

| 対象 | 今の状態 | 区間19での扱い |
|---|---|---|
| `GameSceneController` | SceneGroup はアウトゲームとインゲームの 2 つ（`OUT_GAME_INDEX` / `IN_GAME_INDEX`）。`InGameGroup` は `[TestStage, InGame]` に固定 | ステージごとの SceneGroup を `StageData` に持たせ、選んだステージのグループを読む |
| `StageClearAdapter` / `StageInitializer` | クリアの判定（`IsCleared`・`DestroyedTargetCount`）と仮の「STAGE CLEAR」を EngineAdapter が持つ（TODO） | Adapter は破壊済みの数を報告するだけにし、判定は `StageFlowService` に移す。仮の表示は消す |
| `PlayerHealthService` / `PlayerHealthState` | HP の変化の通知（`RegisterOnHealthChanged`）がある。HP が 0 になっても何も起きない。合計被ダメージは数えていない | HP 0 で失敗にする。合計被ダメージは HP の変化の通知（減った分）から数える |
| 倒した敵の数 | 数えているのは `EnemySpawnAdapter` の `CollapseDefeatCount`・`CrushDefeatCount`（崩落・圧死）だけ。核で倒した数と、吸収（`EnemyFinisherAttack`）は数えていない | `EnemySpawnAdapter` に撃破の通知（`Action<int>`）を足し、`StageFlowService` が数える。`EnemyEnergyAdapter` → `ChargeService.AddCollapsedEnemies` と同じ直接の配線 |
| `TimeScaleState` / `ITimeScaleController` | 範囲は 0〜1。0 にできる | ポーズは `SetScale(0)` で止める |
| `SlowModeService` | 継続の消費は `Time.unscaledDeltaTime` で減る（`DrainAsync`）。チャージが 0 になると `End()` が `ResetScale()`（等速）を呼ぶ | ポーズ中もスローのゲージが減り、0 になると等速に戻ってポーズが解けてしまう。ポーズ中は消費を止める（決めたこと 6） |
| 入力 | `InputSystem_Actions` にポーズの Action がない | Player マップに Pause（Esc）を足し、生成コードを作り直す（Unity が要るのでローカル） |
| `PauseBoard` | 常駐の Compositor に登録してあるだけで、使う側がない | 使わない（決めたこと 6） |
| スキルの装備 | `PlayerInitializer` の Inspector の `_equippedSkills`（`SkillData` 3 枠）に仮で置き、`SkillService` に渡す（TODO） | 常駐の進行の State から受け取る |
| HUD | `PlayerHudAdapter`（OnGUI）が HP とチャージ、スキルの消費量の線を出す | 置き換える |
| ランチャー | `FragmentThrowAdapter.IsLoaded`（装填済みか）がある | HUD が読む |
| `DebugGUI` | `ObserveVariable` に登録を外す API がなく、インゲームに入り直すと表示が重複する（区間3の「見つけた問題」） | シーンを読み直す経路と一緒に、UsefulToolkit に登録を外す API を足す |
| asmdef | BlackBoard は External を参照できない | State にはステージとスキルの番号を持たせ、`StageData`・`SkillData` への変換は Application が行う |

## 詳細仕様で決めること

2026-10-14 にユーザーと決めた。5・7・8・10・13・14 は最初の推奨の案から変えたもの。値は区間33で調整する。

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | アウトゲームをどこまで作るか | 仮の OnGUI を広げる。ステージ選択、スキルの装備（3 枠）、通貨の表示だけ。スキルの購入、本物の UI、セーブは作らない（アウトゲームの作業に回す）。通貨は常駐の State に持ち、ゲームを終えると消える |
| 2 | ステージデータの中身 | `StageData`（ScriptableObject、External）に表示名、SceneGroup、クリアの基礎スコア、失敗条件（決めたこと 7）を持たせる。敵の出し方と破壊対象はステージシーンに置いた物（`EnemySpawnSystem`・`DestructionTarget`）が持っているので、データには持たせない。初回クリアの通貨は全ステージ同じ（仕様）なので、ステージごとには持たせない |
| 3 | 選んだステージと装備の受け渡し | 常駐の State（AppBoard）に、選んだステージの番号、装備したスキルの番号 3 つ、通貨、クリア済みのステージを持たせる。番号と `StageData`・`SkillData` の対応は、常駐の Initializer の Inspector に並べた配列で持つ |
| 4 | インゲームの流れの状態 | Ready → Playing → Cleared / Failed。Ready は開始の表示（1 秒）だけ。InGame の SceneState に置き、`StageFlowService`（Application）が書く。クリアの判定もここへ移す |
| 5 | スコアの計算式 | スコア ＝ 倒した敵の数 × 1 ＋ クリアの基礎スコア（クリアしたときだけ）＋ Brilliant Skill の追加スコア。クリアタイムと被ダメージはスコアに使わない（時間をかけて準備して壊す遊び方を損なわない為）。Brilliant Skill（よい行動をしたとき、空間にボクセルの文字を出してほめる仕組み。例：短時間に敵を大量に倒す、大きな構造物を崩す）の扱いは決めたこと 13 |
| 6 | ポーズ | Esc で切り替える。`ITimeScaleController.SetScale(0)` で止め、解くときにポーズ前の倍率に戻す。ポーズ中は Player マップの入力を止め、スローモードの継続の消費を止める。`PauseBoard` / `IPausable` は止める対象が 1 つなので使わない |
| 7 | ステージごとの失敗条件（11-7） | 差し替える仕組みを区間19で作る。`StageData` に失敗条件の一覧を持たせ、「プレイヤーの HP が 0」を外せるようにする（チュートリアル用）。新しく作った `StageData` は、初めから「HP が 0」が入った状態にする。ほかの条件の中身は区間22で決める |
| 8 | UsefulToolkit の問題（同じ SceneGroup を読み直せない、`DebugGUI` の登録を外せない） | ユーザーが UsefulToolkit 側で直す。kizami 側はそれを使ってリトライを作り、重複を止める |
| 9 | HUD とリザルトの作り方 | uGUI（Screen Space の Canvas）にする。値は State から読み、Canvas を持つ Adapter を PC 用として作る。VR のワールド空間の UI は、VR に対応するときに Adapter を足す |
| 10 | 失敗したときのリザルト | 失敗した理由と倒した敵の数を出す。基礎スコアは入らない。最高スコアは保存しない |
| 11 | 倒した敵の数 | 核で倒した敵、崩落・圧死で倒した敵を数える。吸収された敵は、プレイヤーが倒していないので数えない |
| 12 | 出す敵の総数 | 区間16 の出し直しで敵は尽きないので、総数は持たせない（区間22で必要になったら決める） |

| 13 | Brilliant Skill をどこで作るか | 区間19とは別の区間で作る（判定とボクセルの文字の演出）。区間19は倒した数と基礎スコアまでで、追加スコアを受け取る口も Brilliant Skill の区間で足す。仕様は Notion の「Brilliant Skill」 |
| 14 | 失敗条件の持ち方 | `StageData` に `[SerializeReference]` で失敗条件の一覧を持たせる。区間19で作る条件は `PlayerDeathFailCondition`（HP が 0）だけ。新しく作った `StageData` は、この条件が 1 つ入った状態にする（フィールドの初期値）。判定は `StageFlowService` が条件の型ごとに行う |

Notion の「スコア」「失敗条件」に反映し、「Brilliant Skill」のページを仕様リストに作った（2026-10-14）。

## 作業計画（2026-10-14）

### 新しく作る型

| 型 | 層 | 今の区間で使うもの |
|---|---|---|
| `StageData` | External | アウトゲームのステージ選択、`GameSceneController`、基礎スコア、失敗条件 |
| `StageFailCondition` / `PlayerDeathFailCondition` | External | `StageData` の失敗条件の一覧（`[SerializeReference]`）。`StageFlowService` が判定する。基底の型は、`StageData` が一覧を持つために要る |
| `GameProgressState` / `IGameProgressState` | BlackBoard（AppBoard、常駐） | アウトゲームの表示、インゲームの初期化（ステージと装備） |
| `GameProgressService` | Application（常駐、DI で渡す） | アウトゲームの選択と装備、`StageFlowService` の初回クリア |
| `StageBoard` | BlackBoard | `StageFlowState` の置き場。プレイヤーにも敵にも属さない為 |
| `StageFlowState` / `IStageFlowState` | BlackBoard（StageBoard、InGame） | 流れの状態、失敗の理由、倒した数、破壊済みの数、スコア。HUD、リザルト、ポーズが読む |
| `StageFlowService` | Application | 流れの状態を進め、失敗条件を判定し、数え、スコアを計算する |
| `StageHudAdapter` | EngineAdapter | HUD（`PlayerHudAdapter` を置き換える） |
| `StageResultAdapter` | EngineAdapter | リザルト。ボタンからリトライとアウトゲームへ戻るを要求する |
| `PauseService` | Application | ポーズの切り替え。倍率と入力とスローの消費を止める |

スコアの値は State を分けず、`StageFlowState` に入れる（読む側が同じ為）。

### コミットの分け方

| # | 内容 | 確かめ方 |
|---|---|---|
| 1 | `StageData`、`GameProgressState` / `Service`、`GameSceneController` で選んだステージのグループを読む。仮のアウトゲームにステージ選択を足す | ステージを選んでインゲームに入れる。TestStage で今までどおり遊べる |
| 2 | `StageBoard`、`StageFlowState` / `Service`。クリアの判定を `StageClearAdapter` から移す。失敗条件の一覧と「HP が 0」 | クリアと失敗で状態が変わる。「HP が 0」を外した `StageData` では HP 0 で失敗しない |
| 3 | 倒した数を数え、スコアを計算する。`StageResultAdapter`、アウトゲームへ戻る | リザルトに値が出て、アウトゲームへ戻れる |
| 4 | `StageHudAdapter`（HP、チャージ、スキル 3 枠、破壊の進み具合、ランチャーの装填状態）。`PlayerHudAdapter` を消す | 表示を目で確かめる |
| 5 | Pause の Action、`PauseService` | 止める・戻す。スロー中にポーズして待ってもスローが解けない。ポーズ中に切断やスキルが出ない |
| 6 | 装備を `GameProgressState` から受け取り、仮のアウトゲームで装備を選べるようにする。初回クリアで通貨を返す | 装備したスキルが使える。初回クリアでだけ通貨が増える |
| 7 | リトライと `DebugGUI` の重複の修正（ユーザーが UsefulToolkit 側を直したあと） | 同じステージに何度か入り直して、状態と表示が初めからになる |
| 8 | 完了条件の確認、実装結果、全体計画書の更新 | 完了条件すべて |

クラウドでは Unity が動かないので、コンパイルとプレイモードの確認は、各コミットのあとにローカルで行う。

## 実装の途中経過（2026-10-14、クラウド）

コミット 1〜6 をクラウドで実装した。Unity を動かせないので、コンパイルも動作もまだ確かめていない。コミット7（リトライ）は、UsefulToolkit 側でシーンの読み直しと `DebugGUI` の登録を外す API が入るのを待つ。

### 計画から変えたこと

- `PauseService` は作らず、ポーズを `StageFlowService` に持たせた。プレイ中にだけポーズでき、クリア・失敗と同じく Player マップを止める処理を共有する為。ポーズ中かは `StageFlowState.IsPaused`
- ポーズの入力は Player マップに Action を足さず、UI マップの `Cancel`（Esc）を使う。ポーズ中は Player マップを止めるので、Player マップの Action では解けない為。プレイを始めたときに UI マップを有効にする
- スローモードの継続の消費は、時間の倍率（`ITimeScaleState.Scale`）が 0 の間は止める。ポーズを知らせずに済む
- ポーズの画面は `StagePauseAdapter`。プレイに戻るボタンとアウトゲームへ戻るボタンを置く
- HUD とリザルトとポーズの画面の uGUI は、シーンに置かず、初期化のときにコードで組み立てる（`UiBuilder`）。クラウドからシーンの UI を組めない為。見た目を作り込むときにプレハブへ置き換える（TODO）。`PlayerHudAdapter` は `StageHudAdapter` に名前を変え（スクリプトの GUID はそのまま）、初期化を `StageInitializer` へ移した
- ランチャーの装填状態は State がないので、HUD が `FragmentThrowAdapter.IsLoaded` を直接読む
- 選べるステージの一覧は `IGameSceneController.Stages` で渡す。`GameProgressService` は `GameSceneInitializer` が組み立て、常駐のコンテナへ登録する
- クリアの基礎スコアは `StageData.ClearScore`（既定 1000）。初回クリアの通貨は `GameSceneInitializer` の Inspector（既定 100）
- アウトゲームを通さずにインゲームを始めたときは、既定の値の `StageData`（失敗条件は HP 0）と、`PlayerInitializer` の Inspector の装備で始める

### ローカルで確かめること

1. コンパイル（Kizami の Runtime の asmdef すべて）
2. 生成された Compositor を手で直した（`UsefulToolkitPersistentCompositor` の `StageBoard` の登録、`InGameCompositor` と `OutGameCompositor` の注入）。UsefulToolkit の生成を実行し直して、差分が出ないこと
3. シーンとアセットを手で書き換えた。参照が切れていないこと
   - `UsefulToolkitPersistent` の `GameSceneInitializer`：`_stages`（TestStageData）、`_skills`（Skill_Beam、Skill_Explosion）、`_firstClearReward`
   - `InGame` の `Stage`：`StageInitializer` の参照（`StageClearAdapter`、`EnemySpawnAdapter`、`StageResultAdapter`、`StageHudAdapter`、`StagePauseAdapter`、`FragmentThrowAdapter`）、追加した `StageResultAdapter` と `StagePauseAdapter`
   - `InGame` の `StageHud`（旧 `PlayerHud`）が `StageHudAdapter` になっていること。`PlayerInitializer` から `_hudAdapter` を外した
   - `TestStageData.asset` を手で作った。失敗条件は書いていないので、読み込んだときにフィールドの初期値（HP 0）が入ること。Inspector で一度保存すると書き出される
4. 常駐シーンから再生して、次を確かめる
   - アウトゲームに通貨（0）、スキルの装備枠 3 つ（Beam、Explosion、空き）、TestStage のボタンが出る。枠のボタンでスキルが替わる
   - TestStage に入ると、HUD（HP、チャージと線、スキルの枠、Targets、Defeated、Launcher）と START が出る。装備したスキルが使える
   - 敵を核で倒す、崩落で倒すと Defeated が増える。フィニッシャーに吸収された敵は増えない
   - 門を 2 つ壊すとクリアのリザルト（倒した数、スコア ＝ 倒した数 ＋ 1000）が出て、カーソルが出る。Back to Menu でアウトゲームへ戻り、通貨が 100 増え、TestStage に (cleared) が付く。もう一度クリアしても通貨は増えない
   - HP を 0 にすると失敗のリザルト（HP reached 0、倒した数）が出る。`TestStageData` の失敗条件を空にすると、HP が 0 でも失敗しない
   - Esc でポーズの画面が出て、時間と操作が止まる。Esc か Resume で戻る。スロー中にポーズして待っても、チャージが減らずスローが解けない。ポーズから Back to Menu で戻ると、アウトゲームの時間が等速
   - リザルトのあとは、プレイヤーを操作できない
5. 失敗条件の一覧（`[SerializeReference]`）は、Unity の既定の Inspector では要素を消せるが、型を選んで足すことはできない。消したあとに HP 0 を戻すときは、`StageData` を作り直すか、型を選べる表示を足す（区間22で条件を増やすときに決める）

## ローカルでの確認結果（2026-10-15）

コミット 1〜6 を、上の「ローカルで確かめること」の 1〜4 で確かめた。

| 項目 | 結果 |
|---|---|
| コンパイル | エラー・警告なし |
| .meta・GUID | 問題なし |
| 生成された Compositor | 生成し直すと、`UsefulToolkitPersistentCompositor` は差分なし。`InGameCompositor`・`OutGameCompositor` は注入の順番だけが変わり、注入する型の組み合わせは同じ |
| シーンとアセットの参照 | 切れた参照・Missing Script なし。`TestStageData` は読み込むと失敗条件に `PlayerDeathFailCondition` が 1 つ入る |
| アウトゲーム | 通貨、装備の 3 枠、TestStage のボタンが出て、枠のボタンでスキルが替わる |
| インゲームに入る | HUD と START が出る。装備したスキルが使える |
| 倒した数 | プレイでは確かめていない。コードで、核で倒した数（`EnemyBodyLender.ReceiveCutResults`）と崩落・圧死（`CountCollapseDefeats`）だけを通知し、吸収は通知しないことを確かめた |
| クリア | リザルトに倒した数とスコア（倒した数 ＋ 1000）が出て、カーソルが出る。Player マップは止まる |
| 通貨 | 初回クリアでだけ 100 増え、(cleared) が付く |
| 失敗 | HP 0 で失敗のリザルト（HP reached 0、倒した数）が出る。失敗条件を空にした場合はプレイでは確かめず、空なら何も購読しないことをコードで確かめた |
| ポーズ | 時間と操作が止まり、Esc か Resume で戻る。スロー中にポーズしてもスローが解けない。ポーズから戻るとアウトゲームの時間が等速 |
| 入り直し | 流れと HUD は毎回初めからになる。`DebugGUI` の表示は重複する（コミット7で直す） |

### 確認中に直したこと

- クリア・失敗のあとも敵の攻撃で HP が減り続けていた。`PlayerHealthService` が `StageFlowState` を読み、プレイ中のときだけダメージを適用するようにした。開始の表示中（Ready）も適用しない。`StageFlowState` のないシーンでは今までどおり適用する

## 実装結果（2026-10-15）

### 決めたこと

- ステージは `StageData`（表示名、SceneGroup、クリアの基礎スコア、失敗条件の一覧）で持ち、敵の出し方と破壊対象はステージシーンに置いた物が持つ（決めたこと 2）
- 選んだステージ、装備、通貨、クリア済みのステージは常駐の `GameProgressState` に持ち、ゲームを終えると消える（決めたこと 1・3）
- インゲームの流れは Ready → Playing → Cleared / Failed。クリアの判定、失敗条件の判定、倒した数、スコア、ポーズを `StageFlowService` が持つ（決めたこと 4・6、計画から変えたこと）
- スコア ＝ 倒した敵の数 ＋ クリアの基礎スコア（クリアしたときだけ）。吸収された敵は数えない（決めたこと 5・11）
- プレイヤーがダメージを受けるのはプレイ中だけ。開始の表示中と、クリア・失敗のあとは受けない（確認中に直したこと）
- リトライは、UsefulToolkit の `SceneLoadService.ReloadGroupAsync` で、選んでいるステージのグループを読み直す。`DebugGUI` の監視は持ち主（`this`）を渡して登録し、シーンのアンロードで外れる（[Section19_SceneReloadRequirements.md](Section19_SceneReloadRequirements.md)）

### 作った主なもの

| 種類 | もの |
|---|---|
| ステージの定義 | `StageData`、`StageFailCondition`・`PlayerDeathFailCondition`、`TestStageData.asset` |
| 進行（常駐） | `GameProgressState`・`IGameProgressState`、`GameProgressService`、`GameSceneController` のステージごとのグループ・`GoToStageAsync`・`RetryAsync` |
| インゲームの流れ | `StageBoard`、`StageFlowState`・`IStageFlowState`、`StageFlowService`、`StageInitializer` の配線、`EnemySpawnAdapter.SetOnDefeated`、`StageClearAdapter` の報告 |
| 画面 | `StageHudAdapter`（`PlayerHudAdapter` を置き換え）、`StageResultAdapter`（Retry と Back to Menu）、`StagePauseAdapter`、`UiBuilder` |
| アウトゲーム（仮） | `OutGameStartInitializer` の通貨、装備の 3 枠、ステージ選択 |
| その他 | `SlowModeService` の倍率 0 の間の消費の停止、`PlayerHealthService` のプレイ中だけのダメージ、`ObserveVariable` の持ち主 |

### 完了条件の確認結果

| 完了条件 | 結果 |
|---|---|
| アウトゲームでステージを選ぶとインゲームに入り、クリアか失敗でリザルトに進み、アウトゲームへ戻る | 確認済み（エディタ）。TestStage で、門 2 つを壊してクリア、HP 0 で失敗のリザルトが出て、Back to Menu でアウトゲームへ戻った。Retry で同じステージが初めから始まり、3 回繰り返しても警告・エラーは出なかった |
| 装備したスキルがインゲームで使え、初回クリアで通貨が増える | 確認済み（エディタ）。アウトゲームで替えた装備がインゲームの枠に入って使えた。初回クリアで通貨が 100 増え、2 回目は増えなかった |
| ポーズでゲームが止まる | 確認済み（エディタ）。Esc で時間と操作が止まり、Esc か Resume で戻った。スロー中のポーズでスローが解けず、ポーズから戻るとアウトゲームの時間は等速だった |

確かめていないこと：`TestStageData` の失敗条件を空にしたときに HP 0 で失敗しないこと（コードで、空なら何も購読しないことを確かめた）、Retry のあとに通貨とクリア済みが保たれること（常駐の State なので、読み直しの対象ではない）。

### 次の区間へ持ち越すこと

- Brilliant Skill（判定とボクセルの文字の演出、追加スコアを受け取る口）。決めたこと 13 で別の区間にした。全体計画書の区間一覧には、まだ入っていない
- 失敗条件の一覧の、型を選んで足す表示（区間22で条件を増やすときに決める）
- HUD・リザルト・ポーズの画面のプレハブ化と見た目（`UiBuilder` の TODO。区間33）
- アウトゲームの本物の UI、スキルの購入、セーブ（アウトゲームの作業）

## 見つけた問題（今回は扱わない）

- ポーズ中にデバッグのダメージで HP を 0 にすると、ポーズのまま失敗になり、時間の倍率が 0 のまま残る。敵は倍率 0 では攻撃しないので、デバッグの操作でだけ起きる
- HUD のスキルの枠は番号と消費量だけで、どのスキルかが分からない。見た目は区間33で作り込む

## 他プラットフォームへの対応

- HUD はプラットフォームごとに配置が変わる。VR では、画面に貼り付ける UI ではなく、ワールド空間の UI にする必要がある
- 表示する値は State から読み、見せ方の違いは EngineAdapter 側に閉じ込める
