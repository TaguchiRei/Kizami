# 区間4：コンフィグ

| 項目 | 内容 |
|---|---|
| 状態 | 完了 |
| 目安の時期 | 未定 |
| 前提となる区間 | 0, 1 |
| 全体計画 | [OutGameOverallPlan.md](../OutGameOverallPlan.md) |

## 目的

今ある操作の設定に、キーコンフィグと音量を足して、アウトゲームの画面で変えられるようにする。

## 既存の資産

| 資産 | 内容 |
|---|---|
| `OperationSettingService` / `OperationSettingState` | ダッシュ・つかむの受け付け方、切断面の回転角度、視点感度。`setting.json` への保存と読み込みは区間0で済んでいる |
| `StagePauseAdapter` | インゲームのポーズの画面（プレイに戻る・アウトゲームへ戻る） |
| `InputSystem_Actions.inputactions`、`ExternalInputDefinition` | 入力アクション |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 4-1 | 既存の設定の画面 | EngineAdapter | トグルとスライダー |
| 4-2 | キーコンフィグ | EngineAdapter / External | `PerformInteractiveRebinding` で割り当てを変え、`SaveBindingOverridesAsJson` の文字列を `OperationSettingSaveData` に載せる。初期値に戻す |
| 4-3 | 音量 | EngineAdapter | AudioMixer（マスター・BGM・SE のグループ）を作り、Exposed Parameter をスライダーで変える |
| 4-4 | 保存 | Application | `OperationSettingSaveData` に項目を足し、起動時に当て直す |

## 完了条件

- キーを割り当て直すと、インゲームの操作がそのキーで動く。再起動しても残る
- 音量を変えると、それぞれのグループの音が変わる。再起動しても残る
- 既存の設定を画面で変えられ、再起動しても残る

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 割り当て直せる操作 | プレイヤーの操作すべて。UI の操作は除く |
| 2 | 重複した割り当て | 入れ替える |
| 3 | 音量の範囲と初期値 | 0〜100、初期値 80 |
| 4 | インゲームのポーズからも開くか | 開けるようにする（`StagePauseAdapter` にボタンを足す） |
| 5 | ダッシュの受け付け方と回転角度を画面に出すか | 出す。`OperationSettingService` に窓口を足す |

## 既存コードの確認結果

| 対象 | 今の状態 |
|---|---|
| 設定の窓口 | `OperationSettingService` にあるのは `SetGrabInputMode`、`SetHorizontalSensitivity`、`SetVerticalSensitivity` の 3 つ。どれも変更のたびに `setting.json` へ保存する。ダッシュの受け付け方と切断面の回転角度を変える窓口はない。窓口はどこからも呼ばれていない |
| 設定の Service の持ち主 | `OperationSettingInitializer`（常駐シーン）が生成して自分で持つだけで、Compositor に登録していない。アウトゲームの画面から呼ぶには `TryRegisterContent` で登録する（`GameSceneInitializer` の `Awake` が手本） |
| 保存データ | `OperationSettingSaveData`（Application/CoreSystem）。項目を足すと、古い `setting.json` を読んだときは足した項目が既定値（0 や null）になる。音量の初期値 80 のように 0 でない初期値を持つ項目は、読み込みのときに補う |
| 入力 | アクションは `Assets/Level/Data/InputSystem_Actions.inputactions`（Player・UI・Smartphone・VR の map）。実行中の `InputActionAsset` は UsefulToolkit の Input パッケージが持ち、このリポジトリのコードからは触っていない。PC の割り当ては Keyboard&Mouse のスキーム |
| 音 | `AudioSource` も `AudioMixer` もプロジェクトにない。音を鳴らすコードもない |

作業のときの注意

- 割り当てを変える `InputActionAsset` のインスタンスをどこから取るかは、usefultoolkit スキルを読み、足りなければ GitHub の TaguchiRei/UsefulToolkit の `com.rei.usefultoolkit.input` を読んで確かめる。取り方が分からないときは、実装に入らずにユーザーに聞く
- 音を鳴らす物がまだないので、音量は AudioMixer とスライダーと保存までを作り、鳴っている音で確かめるのは音を入れる区間（インゲーム区間13）に回してよいかをユーザーに確かめる

## 実装結果

決めたこと

| # | 項目 | 決定 |
|---|---|---|
| 1 | 割り当て直せる操作 | Player マップのキーボード・マウスの割り当て（Move の上下左右の各 2 つ、Attack、Interact、Jump、Sprint、Warp、SlowMode、Throw、LoadLauncher、FireLauncher、Skill1〜3）。Look と CutRotate は動きの入力なので対象外 |
| 2 | 重複した割り当て | 入れ替える（重なった相手に元のキーを譲る） |
| 3 | 音量 | 0〜100、初期値 80（マスター・BGM・SE） |
| 4 | ポーズから開くか | 今回は見送り（インゲームのポーズの画面に手を入れる為）。必要になった区間で足す |
| 5 | ダッシュの受け付け方と回転角度 | 画面に出した（ダッシュ・つかむの切り替え、回転角度 1〜90、視点感度 0.1〜5） |

`InputActionAsset` の取り方：`InputDispatcher`（UsefulToolkit.Input）は、シリアライズされたアセットを複製せずそのまま使う。同じアセットを `OperationSettingInitializer` に割り当てれば、割り当ての変更がインゲームの操作へそのまま効く。

作ったもの

- `IOperationSettingController`（BlackBoardLayer）：設定の書き込み面。`OperationSettingService` が実装し、常駐シーンの Awake で `TryRegisterContent` して配る（`IInputController` と同じ作り）。`OperationSettingState` には音量 3 つを足した
- `OperationSettingSaveData`：音量 3 つ、`HasVolume`（0 も有効な音量の為、保存済みかを別に持つ）、`BindingOverridesJson`
- `InputRebindAdapter`（EngineAdapterLayer/OutGame）：割り当て直し、入れ替え、初期値に戻す、保存と起動時の当て直し
- `ConfigScreen` と `UiBuilder.CreateSlider`：コンフィグの画面。`OutGameMenuAdapter` のホームの Config ボタンから開く
- `AudioVolumeAdapter`（EngineAdapterLayer/CoreSystem）：音量を AudioMixer の Exposed Parameter へ反映する。Mixer が未設定なら何もしない

完了条件の確認結果

- ローカルでの準備は済ませた。`OutGameCompositor` を作り直し、`OperationSettingInitializer` に `InputSystem_Actions` と `Assets/Audio/MainMixer.mixer`（Master の下に BGM・SE。Volume を `MasterVolume`・`BgmVolume`・`SeVolume` で Expose）を割り当てた
- ローカルのプレイモードで確認した（2026-10-19）。切り替えとスライダーが `setting.json` に残り、再生し直しても戻る。音量のスライダーで Mixer の Exposed Parameter が変わる。キーの割り当て直し、Esc での取り消し、重なったキーの入れ替え、再生し直したあとの当て直し、Reset Keys で初期値に戻ること。インゲームの `InputDispatcher` が持つアセットにも割り当てが効いている
- 確かめられなかったこと：割り当てたキーを実際に押してインゲームで動くか（uloop のキー入力は割り当て直しに届かず、`InputSystem.QueueStateEvent` で流した）。鳴る音がないので、音量は Mixer の値まで
- 保存した `BindingOverridesJson` の `action` が `Smartphone/Sprint` のように別のマップ名になることがある。Input System は `id` で照合するので、読み込みは正しく Player マップに戻る

次の区間へ持ち越すこと

- 音量を実際の音で確かめるのは、音を入れるインゲーム区間 13 に回す（ユーザー確認済み）
- ポーズの画面からコンフィグを開く導線
- スライダーは動かしている間、値が変わるたびに `setting.json` へ保存する。重く感じたら、離したときだけ保存に変える
- 見た目の作り込み

## 見つけた問題（今回は扱わない）

