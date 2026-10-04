# 区間11：ステージ制・インゲームの流れ・HUD

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2027/01/18〜01/31 |
| 前提となる区間 | マイルストーンB（区間6まで） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

ステージごとのデータを持たせ、ステージ選択からリザルトまでのインゲームの流れを作る。HUD とポーズを作り、アウトゲームとデータを受け渡す。

## 関連する仕様

- ステージ構成・クリア条件：https://app.notion.com/p/3e91ea2aa7fa81e682c6cfbe4f5a11c2
- 失敗条件：https://app.notion.com/p/3e91ea2aa7fa813fb405e07c700a75b4
- アウトゲーム・通貨：https://app.notion.com/p/3e91ea2aa7fa81e3b271e0fc71189ea6
- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70

## 既存の資産

| クラス | 内容 |
|---|---|
| [GameSceneController](../../../Code/Scripts/Application/Scene/GameSceneController.cs) | アウトゲームとインゲームの単位でシーン遷移を要求する。操作面 `IGameSceneController` は DI で受け取る |
| [GameSceneInitializer](../../../Code/Scripts/Initialization/Scene/GameSceneInitializer.cs) | ビルドモードごとのシーングループを組み立てる。区間0で常駐シーンに配線済みの想定 |
| 区間0の仮のアウトゲーム | キー入力でインゲームへ進むだけの仮の仕組み。この区間で本物のアウトゲームの流れに置き換える |
| `IPlayerHealthState` / `PlayerHealthService` | 区間1で作った HP の State と Service。HUD の HP 表示はこの State を読む。ステージ開始時に HP を戻す処理はまだない |
| `PauseBoard` / `IPausable` | UsefulToolkit.ProgramTools のポーズ用の Board とインターフェース（`IsPaused` / `Pause` / `Resume`）。Board は常駐シーンに登録済みだが、中身はまだない |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 11-1 | ステージデータ | ExternalLayer | シーン、敵の出現の上限と間隔、破壊対象、クリアに必要な割合、失敗条件 |
| 11-2 | インゲームの流れ | BlackBoard / Application | 開始 → プレイ中 → クリアまたは失敗 → リザルト、の状態 |
| 11-3 | HUD | EngineAdapter | HP、チャージ、スキルの 3 枠、破壊の進み具合、ランチャーの装填状態 |
| 11-4 | リザルト | EngineAdapter | クリアと失敗の画面、リトライ、アウトゲームへ戻る |
| 11-5 | アウトゲームとの受け渡し | Application | 装備しているスキルを受け取り、初回クリアの通貨を返す |
| 11-6 | ポーズ | Application / EngineAdapter | ポーズ中の時間の止め方と、プレイヤーのアニメーションの停止 |
| 11-7 | ステージごとの失敗条件 | Application | 基本の「HP が 0」以外の失敗条件を、ステージデータから差し替えられるようにする |

## 完了条件

- アウトゲームでステージを選ぶとインゲームに入り、クリアか失敗でリザルトに進み、アウトゲームへ戻る
- 装備したスキルがインゲームで使え、初回クリアで通貨が増える
- ポーズでゲームが止まる

## 詳細仕様で決めること

- ステージ数
- ステージごとの失敗条件の違い
- ポーズを `timeScale = 0` で作るか。その場合、倍率の変更は区間0の `ITimeScaleController` を通し、UnscaledTime の Animator を `Animator.speed = 0` で止める
- ポーズに `PauseBoard` / `IPausable` を使うか（止める対象を `IPausable` で揃えるか）
- アウトゲームとの受け渡しの形と、セーブデータ（アウトゲーム側の作業と調整する）

## 他プラットフォームへの対応

- HUD はプラットフォームごとに配置が変わる。VR では、画面に貼り付ける UI ではなく、ワールド空間の UI にする必要がある
- 表示する値は State から読み、見せ方の違いは EngineAdapter 側に閉じ込める
