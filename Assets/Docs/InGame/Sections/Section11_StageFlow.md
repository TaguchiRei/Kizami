# 区間11：ステージ制・インゲームの流れ・HUD

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2027/01/19〜02/01（最速の推定 2026/11/05〜11/08） |
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
| 失敗の流れ | まだない。区間4から移した（2026-10-05）。敵の攻撃は区間9までないので、HP のデバッグ操作で確かめる |
| ステージシーン | 区間4A で、ライト・地面・生成システムなどをステージシーン（`TestStage`）に分けた。`InGameGroup` は「ステージシーン（アクティブ）＋ InGame」 |
| `IPlayerHealthState` / `PlayerHealthService` | 区間1で作った HP の State と Service。HUD の HP 表示はこの State を読む。State は InGame の SceneState なので、インゲームに入るたびに満タンに戻る。合計被ダメージを数える処理はまだない |
| `PauseBoard` / `IPausable` | UsefulToolkit.ProgramTools のポーズ用の Board とインターフェース（`IsPaused` / `Pause` / `Resume`）。Board は常駐シーンに登録済みだが、中身はまだない |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 11-1 | ステージデータ | ExternalLayer | 4 ステージ分。ステージシーン、敵の出現の上限と間隔、破壊対象、クリアに必要な割合、失敗条件 |
| 11-2 | インゲームの流れ | BlackBoard / Application | 開始 → プレイ中 → クリアまたは失敗 → リザルト、の状態 |
| 11-3 | HUD | EngineAdapter | HP、チャージ、スキルの 3 枠、破壊の進み具合、ランチャーの装填状態 |
| 11-3a | 失敗 | Application / EngineAdapter | HP が 0 になったら失敗にする（区間4から移した）。ステージごとの失敗条件は 11-7 |
| 11-4 | リザルト | EngineAdapter | クリアと失敗の画面、アウトゲームへ戻る |
| 11-4a | スコア | BlackBoard / Application | 倒した敵の数、クリアタイム、合計被ダメージを数え、スコアを計算してリザルトに出す |
| 11-4b | リトライ | UsefulToolkit / Application | 同じステージをもう一度始める。同じ SceneGroup を読み直せないので、UsefulToolkit にシーンを読み直す経路を足す（2026-10-05 決定。ダミーのシーングループを経由する案より、こちらを採る）。区間2と同じく、要件定義を書いて UsefulToolkit のリポジトリで作る |
| 11-5 | アウトゲームとの受け渡し | Application | 装備しているスキルを受け取り、初回クリアの通貨を返す |
| 11-6 | ポーズ | Application / EngineAdapter | ポーズ中の時間の止め方と、プレイヤーのアニメーションの停止 |
| 11-7 | ステージごとの失敗条件 | Application | 基本の「HP が 0」以外の失敗条件を、ステージデータから差し替えられるようにする |

## 完了条件

- アウトゲームでステージを選ぶとインゲームに入り、クリアか失敗でリザルトに進み、アウトゲームへ戻る
- 装備したスキルがインゲームで使え、初回クリアで通貨が増える
- ポーズでゲームが止まる

## 詳細仕様で決めること

- ステージごとの失敗条件の違い
- スコアの計算式と、リザルトに出す項目。失敗したときもスコアを出すか
- シーンを読み直す経路の形（UsefulToolkit の API）
- インゲームに入り直すと `DebugGUI` の表示が重複する問題（区間3の「見つけた問題」）を、リトライを作るときに直すか
- ポーズを `timeScale = 0` で作るか。その場合、倍率の変更は区間0の `ITimeScaleController` を通し、UnscaledTime の Animator を `Animator.speed = 0` で止める
- ポーズに `PauseBoard` / `IPausable` を使うか（止める対象を `IPausable` で揃えるか）
- アウトゲームとの受け渡しの形と、セーブデータ（アウトゲーム側の作業と調整する）

## 他プラットフォームへの対応

- HUD はプラットフォームごとに配置が変わる。VR では、画面に貼り付ける UI ではなく、ワールド空間の UI にする必要がある
- 表示する値は State から読み、見せ方の違いは EngineAdapter 側に閉じ込める
