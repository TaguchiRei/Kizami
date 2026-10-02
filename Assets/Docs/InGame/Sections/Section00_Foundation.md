# 区間0：基盤整備

| 項目 | 内容 |
|---|---|
| 状態 | 計画済み（詳細仕様の確定待ち） |
| 目安の時期 | 2026/09/29〜10/05 |
| 前提となる区間 | なし |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

以降の区間が乗る土台を作る。

- 時間制御の TimeScale State と Adapter
- PC 用のインゲームシーン
- PC 用の入力マップ
- デバッグ手段

## 関連する仕様

- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70
- スローモード：https://app.notion.com/p/3e91ea2aa7fa81a3914be1ccc47b86aa

## 時間制御の設計と既存コードの確認結果

| 対象 | 時間制御の設計 | 既存コード | 対応 |
|---|---|---|---|
| プレイヤーの Rigidbody | 補間を有効にする | PlayerMoveTest の PlayerRoot が `m_Interpolate: 0`（None） | 直す |
| プレイヤーの移動 | 物理に任せて遅くする | [PlayerMovementAdapterBase](../../../Code/Scripts/EngineAdapterLayer/Player/PlayerMovementAdapterBase.cs) が FixedUpdate の中で `Time.fixedDeltaTime` を使う | 変更なし |
| 視点操作（PC） | 等速 | [StandardPlayerCameraAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/StandardPlayerCameraAdapter.cs) は入力の移動量をそのまま加算し、時間を使わない | 変更なし |
| 視点操作（VR） | `Time.unscaledDeltaTime` にする | [VrPlayerMovementAdapter.cs:51](../../../Code/Scripts/EngineAdapterLayer/Player/VrPlayerMovementAdapter.cs) が `Time.deltaTime` を使う | 下の「決めること」の 3 |
| Cinemachine Brain | Smart Update か Late Update | `UpdateMethod: 2`（Smart Update） | 変更なし |
| Input System の Update Mode | Dynamic Update | 設定アセットがなく、既定値（Dynamic） | 変更なし |
| VoxelMeltSystem / VoxelPiece | 対応済み | `Time.deltaTime` を Job に渡す。分離片は補間を設定済み | 変更なし |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 0-1 | TimeScale State | BlackBoardLayer | `ITimeScaleState`（倍率の読み取りと、変化の購読）と、その具象クラス |
| 0-2 | 倍率を書き込む Service | Application | 具象の State を持つ唯一のクラス。倍率を変える操作は `ITimeScaleController` として DI で渡す（使うのはスローモード、ポーズ、デバッグ） |
| 0-3 | TimeScale Adapter | EngineAdapterLayer | State の変化を受けて `Time.timeScale` と `Time.fixedDeltaTime`（基準値 × 倍率。倍率が 0 のときは変更しない）に反映する。この 2 つに書き込むのは、プロジェクト全体でこのクラスだけ |
| 0-4 | Initializer の配線 | Initialization | 常駐シーンに置き、RootGameCompositor を作り直す |
| 0-5 | インゲーム用のシーン | Level | PC 用のインゲームシーンを用意し、プレイヤー一式を移す。PlayerRoot の Rigidbody の補間を有効にする。SceneGroup アセットを作る |
| 0-6 | PC 用の入力マップ | ExternalLayer / 生成 | Player マップにアクションを追加し、`UsefulToolkit/Input/Generate Action Enums` で enum を作り直す |
| 0-7 | デバッグ手段 | Debug | 倍率を実行中に変える操作と、State のログ表示 |

## 完了条件

- 常駐シーンから再生するとインゲームシーンに入り、プレイヤーが動かせる
- デバッグ操作で倍率を下げると、`Time.timeScale` と `Time.fixedDeltaTime` の両方が変わる
- 倍率を下げると、プレイヤーの物理はカクつかずに遅くなり、視点操作は等速のまま
- 追加した入力アクションがすべて State まで届く（ログで確認）

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | TimeScale State の置き場所と寿命 | AppBoard の GameState（常駐）。インゲームから出るときに倍率を 1 に戻す |
| 2 | インゲームシーンの作り方 | PC 用の新しいシーンを作る。旧コードが入った `Test/InGame.unity`（Build Settings と `BuildScenes.InGame` に登録済み）の扱いを決める。SceneGroup アセットを作り、GameSceneInitializer が要求する VR とスマホの枠には、専用の操作シーンができるまで PC と同じグループを入れる |
| 3 | VR アダプタの `Time.deltaTime` | 時間制御の設計どおり、`Time.unscaledDeltaTime` に直す |
| 4 | 入力の割り当て | 下の表（仮）。使っていない Player マップの Interact / Crouch / Previous / Next を消すかも決める。壁走りは専用のキーを作らず、区間1で入る条件として決める |

入力の割り当て（仮）

| 操作 | キー |
|---|---|
| 切断 | 左クリック |
| 切断面の回転 | ホイール |
| ジャンプ | Space |
| ダッシュ | 左 Shift |
| 短距離ワープ | 左 Ctrl |
| スローモード | F |
| つかむ | E |
| 投げる | 右クリック |
| ランチャーに装填 | R |
| ランチャーを撃つ | 中クリック |
| スキル 1〜3 | 1 / 2 / 3 |

## 他プラットフォームへの対応

- 入力アクションは Player マップ（PC）に追加する。Smartphone マップと VRControllers マップには、そのプラットフォームに対応するときに同じ意味のアクションを足す
- TimeScale State と Service はプラットフォームに依存しない。VR では、視点操作とプレイヤーの手の動きが等速になっていることを、対応するときに確認する
