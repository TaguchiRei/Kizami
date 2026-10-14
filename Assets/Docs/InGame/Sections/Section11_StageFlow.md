# 区間11：ステージ制・インゲームの流れ・HUD

| 項目 | 内容 |
|---|---|
| 状態 | 着手（2026-10-14） |
| 目安の時期 | 2027/04/13〜04/26（最速の推定 2026/10/29〜11/01） |
| 前提となる区間 | マイルストーンB（区間6まで） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

ステージごとのデータを持たせ、ステージ選択からリザルトまでのインゲームの流れを作る。HUD、スコア、リトライ、ポーズを作り、アウトゲームとデータを受け渡す。
ステージは 4 つ（チュートリアル、一般戦闘、ギミック戦闘、ボス戦）。この区間では流れを作るところまでで、各ステージの中身は区間14と区間12で作る。

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
| [GameSceneController](../../../Code/Scripts/Application/Scene/GameSceneController.cs) | アウトゲームとインゲームの単位でシーン遷移を要求する。操作面 `IGameSceneController` は DI で受け取る。場面ごとに 1 つの SceneGroup を持つ（区間3で変更）。インゲームにいるときに `GoToInGameAsync` を呼んでも、ロード済みのシーンは読み直されない（区間4A の食い違い #6） |
| [GameSceneInitializer](../../../Code/Scripts/Initialization/Scene/GameSceneInitializer.cs) | 常駐シーンにあり、場面ごとの SceneGroup を `GameSceneController` に渡す |
| 仮のアウトゲーム | OnGUI のボタン（`OutGameStartInitializer`）でインゲームへ進むだけ。この区間で本物のアウトゲームの流れに置き換える |
| 失敗の流れ | まだない。区間4から移した（2026-10-05）。敵の攻撃は区間9で入る |
| ステージシーン | 区間4A で、ライト・地面・生成システムなどをステージシーン（`TestStage`）に分けた。`InGameGroup` は「ステージシーン（アクティブ）＋ InGame」 |
| `IPlayerHealthState` / `PlayerHealthService` | 区間1で作った HP の State と Service。HUD の HP 表示はこの State を読む。State は InGame の SceneState なので、インゲームに入るたびに満タンに戻る。合計被ダメージを数える処理はまだない |
| `PauseBoard` / `IPausable` | UsefulToolkit.ProgramTools のポーズ用の Board とインターフェース（`IsPaused` / `Pause` / `Resume`）。Board は常駐シーンに登録済みだが、中身はまだない |
| 仮の HUD | 区間6の `PlayerHudAdapter`（InGame の `PlayerHud`、OnGUI）。画面の下端に HP、その上にチャージのゲージを出し、減ると中央へ縮む。チャージのゲージに、装備したスキルの消費量の位置の線を引く（`ISkillSlotState`）。この区間の HUD で置き換える |
| スキルの装備 | 区間6では、`PlayerInitializer` の Inspector の `_equippedSkills`（`SkillData` の 3 枠）に仮で置いている（TODO）。`SkillService` が受け取り、`SkillSlotState` に消費量を書く |

## 前提の変化（2026-10-07、区間5 の完了時）

- 区間5で、クリアの判定を EngineAdapter の `StageClearAdapter` に置き、仮の「STAGE CLEAR」を画面に出す形にした（区間5の決定 5。読むのが仮の表示だけだった為）。リザルトとリトライがクリアを読むので、この区間で Application の Service と BlackBoard の State に移し、仮の表示を置き換える
- 破壊対象は、ステージシーンの `DestructionTarget` が持つ（重要パーツのパスと必要な割合）。ステージデータに破壊対象の情報を持たせるかを決める

## 前提の変化（2026-10-08、区間の順番の変更）

- 区間8R（リファクタリング）、区間9（敵の固有アクション）、区間10を、この区間の前に置いた。区間9は、この区間を前提にしなくなった。「既存の資産」に書いたクラス名と置き場所は、区間8R で変わっている可能性がある。着手時に今のコードで見直す
- 区間9で敵の攻撃が入る。失敗（11-3a）は、HP のデバッグ操作だけでなく、敵の実際の攻撃で HP を 0 にして確かめられる。それまでは HP が 0 になっても何も起きない
- 区間9で、吸収型の敵がほかの敵を吸収して消す予定（区間9の案の 9）。スコアの「倒した敵の数」に、吸収された敵を数えるかを決める
- 区間9で敵の種類が増える。ステージデータ（11-1）に敵の出し方を持たせるときは、区間9で生成情報に足す「グループの編成」（区間9の案の 2）を含める

## 前提の変化（2026-10-14、区間11の着手時）

- 区間15（`feature/alpha/big-voxel`）と区間10は進行中。区間11は区間6までを前提にしているので、並べて進める。区間15で、倒れた建物の塊も崩落の判定（`EnemyCollapseDetector.WatchFalling`）に入るようになった。崩落で倒した敵の数え方は変わらない
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

| 対象 | 今の状態 | 区間11での扱い |
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

2026-10-14 にユーザーと決めた。5・7・8・10 は推奨の案から変えた。値は区間13で調整する。

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | アウトゲームをどこまで作るか | 仮の OnGUI を広げる。ステージ選択、スキルの装備（3 枠）、通貨の表示だけ。スキルの購入、本物の UI、セーブは作らない（アウトゲームの作業に回す）。通貨は常駐の State に持ち、ゲームを終えると消える |
| 2 | ステージデータの中身 | `StageData`（ScriptableObject、External）に表示名、SceneGroup、クリアの基礎スコア、失敗条件（決めたこと 7）を持たせる。敵の出し方と破壊対象はステージシーンに置いた物（`EnemySpawnSystem`・`DestructionTarget`）が持っているので、データには持たせない。初回クリアの通貨は全ステージ同じ（仕様）なので、ステージごとには持たせない |
| 3 | 選んだステージと装備の受け渡し | 常駐の State（AppBoard）に、選んだステージの番号、装備したスキルの番号 3 つ、通貨、クリア済みのステージを持たせる。番号と `StageData`・`SkillData` の対応は、常駐の Initializer の Inspector に並べた配列で持つ |
| 4 | インゲームの流れの状態 | Ready → Playing → Cleared / Failed。Ready は開始の表示（1 秒）だけ。InGame の SceneState に置き、`StageFlowService`（Application）が書く。クリアの判定もここへ移す |
| 5 | スコアの計算式 | スコア ＝ 倒した敵の数 × 1 ＋ クリアの基礎スコア（クリアしたときだけ）＋ Brilliant Skill の追加スコア。クリアタイムと被ダメージはスコアに使わない（時間をかけて準備して壊す遊び方を損なわない為）。Brilliant Skill（よい行動をしたとき、空間にボクセルの文字を出してほめる仕組み。例：短時間に敵を大量に倒す、大きな構造物を崩す）の扱いは「詳細仕様で決めること（残り）」 |
| 6 | ポーズ | Esc で切り替える。`ITimeScaleController.SetScale(0)` で止め、解くときにポーズ前の倍率に戻す。ポーズ中は Player マップの入力を止め、スローモードの継続の消費を止める。`PauseBoard` / `IPausable` は止める対象が 1 つなので使わない |
| 7 | ステージごとの失敗条件（11-7） | 差し替える仕組みを区間11で作る。`StageData` に失敗条件の一覧を持たせ、「プレイヤーの HP が 0」を外せるようにする（チュートリアル用）。新しく作った `StageData` は、初めから「HP が 0」が入った状態にする。ほかの条件の中身は区間14で決める |
| 8 | UsefulToolkit の問題（同じ SceneGroup を読み直せない、`DebugGUI` の登録を外せない） | ユーザーが UsefulToolkit 側で直す。kizami 側はそれを使ってリトライを作り、重複を止める |
| 9 | HUD とリザルトの作り方 | uGUI（Screen Space の Canvas）にする。値は State から読み、Canvas を持つ Adapter を PC 用として作る。VR のワールド空間の UI は、VR に対応するときに Adapter を足す |
| 10 | 失敗したときのリザルト | 失敗した理由と倒した敵の数を出す。基礎スコアは入らない。最高スコアは保存しない |
| 11 | 倒した敵の数 | 核で倒した敵、崩落・圧死で倒した敵を数える。吸収された敵は、プレイヤーが倒していないので数えない |
| 12 | 出す敵の総数 | 区間4G の出し直しで敵は尽きないので、総数は持たせない（区間14で必要になったら決める） |

### 詳細仕様で決めること（残り）

| # | 項目 | 案 |
|---|---|---|
| 13 | Brilliant Skill をどこで作るか | 判定（何をよい行動とするか）とボクセルの文字の演出は、区間11とは別の区間にする。区間11は、倒した数と基礎スコアまで |
| 14 | 失敗条件の持ち方 | `StageData` に `[SerializeReference]` で失敗条件の一覧を持たせる（`PlayerDeathFailCondition` など。条件ごとに値を持てる）。判定は `StageFlowService` が条件の型ごとに行う。もう一つの案は、フラグの列挙型（`PlayerDeath` などを並べる）。値を持てないが、作りは小さい |

## 作業計画（2026-10-14）

### 新しく作る型

| 型 | 層 | 今の区間で使うもの |
|---|---|---|
| `StageData` | External | アウトゲームのステージ選択、`GameSceneController`、基礎スコア、失敗条件 |
| 失敗条件の型 | External | `StageData` の失敗条件の一覧。形は「詳細仕様で決めること（残り）」 |
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

## 見つけた問題（今回は扱わない）

- なし

## 他プラットフォームへの対応

- HUD はプラットフォームごとに配置が変わる。VR では、画面に貼り付ける UI ではなく、ワールド空間の UI にする必要がある
- 表示する値は State から読み、見せ方の違いは EngineAdapter 側に閉じ込める
