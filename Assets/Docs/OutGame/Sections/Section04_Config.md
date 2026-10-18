# 区間4：コンフィグ

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（区間0の実装後に見直し済み。着手するときは「既存コードの確認結果」が今のコードと合っているかだけ確かめる） |
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

区間を終えるときに書く。

## 見つけた問題（今回は扱わない）

