# 区間0：基盤整備

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-04。完了条件 3 の体感確認のみレビュー時に実施） |
| 目安の時期 | 2026/09/29〜10/05 |
| 前提となる区間 | なし |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

以降の区間が乗る土台を作る。

- 時間制御の TimeScale State と Adapter
- 常駐シーンからインゲームまでのシーン遷移と、インゲームのシーン
- PC 用の入力マップ
- デバッグ手段

## 関連する仕様

- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70
- スローモード：https://app.notion.com/p/3e91ea2aa7fa81a3914be1ccc47b86aa

## 既存コードの確認結果

### 時間制御

| 対象 | 時間制御の設計 | 既存コード | 対応 |
|---|---|---|---|
| プレイヤーの Rigidbody | 補間を有効にする | PlayerMoveTest の PlayerRoot が `m_Interpolate: 0`（None） | 直す |
| プレイヤーの移動 | 物理に任せて遅くする | [PlayerMovementAdapterBase](../../../Code/Scripts/EngineAdapterLayer/Player/PlayerMovementAdapterBase.cs) が FixedUpdate の中で `Time.fixedDeltaTime` を使う | 変更なし |
| 視点操作（PC） | 等速 | [StandardPlayerCameraAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/StandardPlayerCameraAdapter.cs) は入力の移動量をそのまま加算し、時間を使わない | 変更なし |
| 視点操作（VR） | `Time.unscaledDeltaTime` にする | [VrPlayerMovementAdapter.cs:51](../../../Code/Scripts/EngineAdapterLayer/Player/VrPlayerMovementAdapter.cs) が `Time.deltaTime` を使う | 下の「決めること」の 4 |
| Cinemachine Brain | Smart Update か Late Update | `UpdateMethod: 2`（Smart Update） | 変更なし |
| Input System の Update Mode | Dynamic Update | 設定アセットがなく、既定値（Dynamic） | 変更なし |
| VoxelMeltSystem / VoxelPiece | 対応済み | `Time.deltaTime` を Job に渡す。分離片は補間を設定済み | 変更なし |

### シーン遷移

| 対象 | 状態 |
|---|---|
| [GameSceneInitializer](../../../Code/Scripts/Initialization/Scene/GameSceneInitializer.cs) | どのシーンにも置かれていない。常駐シーンの Compositor（[UsefulToolkitPersistentCompositor](../../../Code/Scripts/Initialization/UsefulToolkit/UsefulToolkitPersistentCompositor.cs)）にも入っていない |
| SceneGroup アセット | 1 つもない。`GameSceneInitializer` はアウトゲームとインゲームの各 3 枠（PC / スマホ / VR）、計 6 枠をすべて要求し、1 つでも空だとエラーを出して遷移しない |
| 起動時の遷移 | `_transitionOnStart` が有効だと、起動時に**アウトゲーム**のグループへ遷移する。インゲームへは `IGameSceneController.GoToInGameAsync` を誰かが呼ぶ必要がある |
| SceneGroup で指定できるシーン | `GameSceneGroupData` は `BuildScenes` の enum で指定する。そのため、使うシーンは Build Settings に登録し、`UsefulToolkit/Generate/Scene Enum` で enum を作り直す必要がある |
| Build Settings | 常駐シーンと、旧構成の `Test/InGame.unity` だけが登録されている |
| PlayerMoveTest | PC とスマホが共用する操作系（`StandardPlayerInputRouteInitializer`、`PlayerInitializer`、`StandardPlayer〜Adapter`、PlayerRoot、Cinemachine）が入っている。Compositor は `PlayerMoveTestCompositor` |

### 入力（Player マップ）

| アクション | 今のキー（Keyboard&Mouse） |
|---|---|
| Move / Look | WASD / マウスの移動量 |
| Attack | 左クリック、Enter |
| Jump | Space |
| Sprint | 左 Shift |
| Interact | E |
| Crouch | C |
| Previous / Next | 1 / 2 |

入力アクションの定義は [InputSystem_Actions.inputactions](../../../Level/Data/InputSystem_Actions.inputactions)。

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 0-1 | TimeScale State | BlackBoardLayer | `ITimeScaleState`（倍率の読み取りと、変化の購読）と、その具象クラス |
| 0-2 | 倍率を書き込む Service | Application | 具象の State を持つ唯一のクラス。倍率を変える操作は `ITimeScaleController` として DI で渡す（使うのはスローモード、ポーズ、デバッグ） |
| 0-3 | TimeScale Adapter | EngineAdapterLayer | State の変化を受けて `Time.timeScale` と `Time.fixedDeltaTime`（起動時の基準値 × 倍率。倍率が 0 のときは変更しない）に反映する。この 2 つに書き込むのは、プロジェクト全体でこのクラスだけ |
| 0-4 | 常駐シーンの配線 | Initialization | TimeScale の Initializer と `GameSceneInitializer` を常駐シーンに置き、`UsefulToolkit/Generate/Scene Compositor` で常駐シーンの Compositor を作り直す |
| 0-5 | シーンの用意 | Level | 「決めること」の 2 に従って、インゲームの場面シーン、PC とスマホが共用する操作シーン、仮のアウトゲームの場面シーンを作り、Build Settings に登録して `BuildScenes` を作り直す。PlayerRoot の Rigidbody の補間を有効にする |
| 0-6 | SceneGroup アセット | Level | アウトゲーム（場面＋操作）とインゲーム（場面＋操作）の 2 つを作り、`GameSceneInitializer` の 6 枠に設定する。スマホと VR の枠には、PC と同じグループを入れる |
| 0-7 | アウトゲームからインゲームへの仮の入り方 | Application / EngineAdapter | 「決めること」の 3 に従う |
| 0-8 | PC 用の入力マップ | 入力アセット / 生成 | Player マップのアクションを「決めること」の 5 に合わせて足す・直し、`UsefulToolkit/Input/Generate Action Enums` で enum を作り直す |
| 0-9 | デバッグ手段 | Debug | 倍率を実行中に変える操作と、倍率の画面表示。表示は `DebugGUI.ObserveVariable` を使う（`DebugGUI` がシーンになければ `UsefulToolkit/ProgramTools/DebugGUI Setup` で置く） |

## 完了条件

- 常駐シーンから再生すると、アウトゲームを経てインゲームに入り、プレイヤーが動かせる
- デバッグ操作で倍率を下げると、`Time.timeScale` と `Time.fixedDeltaTime` の両方が変わる
- 倍率を下げると、プレイヤーの物理はカクつかずに遅くなり、視点操作は等速のまま
- 追加した入力アクションがすべて Application まで届く（ログで確認）

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | TimeScale State の置き場所と寿命 | AppBoard の GameState（常駐）。インゲームから出るときに倍率を 1 に戻す |
| 2 | シーンの作り方と置き場所 | 既存の分け方（場面シーン ＋ 操作シーン）に合わせる。① インゲームの場面シーン：地面などのステージだけを置く。② PC とスマホが共用する操作シーン：PlayerMoveTest からプレイヤー一式（PlayerRoot、カメラ、入力の配線、Initializer）を移す。③ 仮のアウトゲームの場面シーン：ほぼ空。置き場所は `Assets/Level/Scenes/Master/` の下、SceneGroup アセットは `Assets/Level/Data/SceneGroup/` を想定する。PlayerMoveTest は開発用として残すか消すかを決める。旧構成の `Test/InGame.unity` は Build Settings から外す（`BuildScenes.InGame` の名前が新しいシーンとぶつからないようにする） |
| 3 | アウトゲームからインゲームへの仮の入り方 | 仮のアウトゲームに、キー入力（またはボタン）で `GoToInGameAsync` を呼ぶだけの仕組みを置く。区間11で本物のアウトゲームの流れに置き換える |
| 4 | VR アダプタの `Time.deltaTime` | 時間制御の設計どおり、`Time.unscaledDeltaTime` に直す |
| 5 | 入力の割り当て | 下の表（仮） |

入力の割り当て（仮）

| 操作 | キー | アクション |
|---|---|---|
| 切断 | 左クリック | 既存の Attack を使う（Enter の割り当ては外してよい） |
| 切断面の回転 | ホイール | 新規 |
| ジャンプ | Space | 既存の Jump |
| ダッシュ | 左 Shift | 既存の Sprint |
| 短距離ワープ | 左 Ctrl | 新規 |
| スローモード | F | 新規 |
| つかむ | E | 既存の Interact と同じキーになる。Interact を「つかむ」に名前を変えるか、Interact を消して新規に作る |
| 投げる | 右クリック | 新規 |
| ランチャーに装填 | R | 新規 |
| ランチャーを撃つ | 中クリック | 新規 |
| スキル 1〜3 | 1 / 2 / 3 | 新規。既存の Previous（1）/ Next（2）とぶつかるので、Previous / Next は消す |
| （使わない） | C | 既存の Crouch は消してよい |

- 壁走りは専用のキーを作らず、区間1で入る条件として決める
- アクションを消すと、`PlayerActions` の enum から消える。2026-10-02 時点で、`Assets/Code/Scripts/` から Previous / Next / Interact / Crouch を参照している箇所はない

## 実装結果（2026-10-04）

### 決めたこと

| # | 項目 | 結果 |
|---|---|---|
| 1 | TimeScale State | 案どおり `AppBoard` の GameState。倍率は 0〜1 にクランプ。インゲームから出るときに 1 へ戻す処理は未実装（区間7または11で入れる） |
| 2 | シーン構成 | 案どおり。`Assets/Level/Scenes/Master/` に `OutGame/OutGame`、`InGame/InGame`、`Player/StandardPlayerControl`。インゲームとアウトゲームの両方に地面を置いた（操作シーンが場面をまたいで残る為、地面がないとアウトゲームでプレイヤーが落ちる）。PlayerMoveTest は開発用に残す。旧 `Test/InGame` は Build Settings から外した（`Test/` の旧シーンは未使用のまま残っている） |
| 3 | 仮の遷移 | `OutGameStartInitializer` が OnGUI のボタン「インゲームへ」を出し、`GoToInGameAsync` を呼ぶ |
| 4 | VR アダプタ | `Time.unscaledDeltaTime` に変更（コンパイルのみ確認。VR 実機は未確認） |
| 5 | 入力 | 下記。Interact は改名せず残し、「つかむ」に使う |

入力の変更（Player マップ）

- 削除：Previous、Next、Crouch、Attack の Enter 割り当て
- 追加：CutRotate（ホイール）、Warp（左 Ctrl）、SlowMode（F）、Throw（右クリック）、LoadLauncher（R）、FireLauncher（中クリック）、Skill1〜3（1 / 2 / 3）

### 作った主なもの

| 層 | ファイル |
|---|---|
| BlackBoardLayer | `TimeScaleState`（`ITimeScaleState`） |
| Application | `TimeScaleService`（`ITimeScaleController`） |
| EngineAdapterLayer | `TimeScaleAdapter`（`Time.timeScale` と `Time.fixedDeltaTime` に書き込むのはこのクラスだけ） |
| Initialization | `TimeScaleInitializer`、`TimeScaleDebugInitializer`（OnGUI のスライダーとプリセットボタン、`DebugGUI` への値表示）、`OutGameStartInitializer`、`OutGameCompositor`、`StandardPlayerControlCompositor` |
| Level | 3 シーン、SceneGroup アセット（`Assets/Level/Data/SceneGroup/` の `OutGameGroup`、`InGameGroup`。スマホと VR の枠にも同じものを設定） |

常駐シーンには `TimeScaleInitializer`、`TimeScaleAdapter`、`TimeScaleDebugInitializer`、`GameSceneInitializer`、`DebugGUI` を置いた。常駐シーンの Root Compositor の `_startScene` は空にし、起動時の遷移は `GameSceneInitializer` が行う。

### PlayerMoveTest を使うとき

（区間1で PlayerMoveTest を削除した為、この手順は使えない）

常駐シーンの Root Compositor の `_startScene` に PlayerMoveTest を指定し、`GameSceneInitializer` の `_transitionOnStart` を外す。

### 完了条件の確認結果

| # | 条件 | 結果 |
|---|---|---|
| 1 | 常駐シーンから再生すると、アウトゲームを経てインゲームに入り、プレイヤーが動かせる | 確認済み（遷移はボタンの処理を直接呼んで確認。W キーで前進） |
| 2 | 倍率を下げると `Time.timeScale` と `Time.fixedDeltaTime` の両方が変わる | 確認済み（0.5 で 0.5 / 0.01、0 で 0 / 変更なし、1 で 1 / 0.02） |
| 3 | 倍率を下げると、プレイヤーの物理はカクつかずに遅くなり、視点操作は等速のまま | Rigidbody の補間の有効化までは確認済み。体感はレビュー時に確認する（擬似キー入力では測れなかった） |
| 4 | 追加した入力アクションがすべて Application まで届く | 確認済み（9 アクションと既存の Attack、Interact、Jump、Sprint が Started、Performed、Canceled とも届いた） |

### 次の区間へ持ち越すこと

- 区間7：インゲームから出るときの倍率のリセット
- 区間11：仮のアウトゲーム（`OutGameStartInitializer`）を本来の流れに置き換える
- スマホと VR の入力マップへの追加（対応時に Smartphone マップと VRControllers マップへ同じ意味のアクションを足す）

## 他プラットフォームへの対応

- 入力アクションは Player マップ（PC）に追加する。Smartphone マップと VRControllers マップには、そのプラットフォームに対応するときに同じ意味のアクションを足す
- 作る操作シーンは PC とスマホが共用する（`StandardPlayerInputRouteInitializer` がビルドモードでタッチ操作の有無を切り替える）。VR の操作シーンはまだないので、VR の枠には暫定で PC と同じグループを入れる
- TimeScale State と Service はプラットフォームに依存しない。VR では、視点操作とプレイヤーの手の動きが等速になっていることを、対応するときに確認する
