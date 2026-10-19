# 区間1：ホームとステージ選択

| 項目 | 内容 |
|---|---|
| 状態 | 完了 |
| 目安の時期 | 未定 |
| 前提となる区間 | 0 |
| 全体計画 | [OutGameOverallPlan.md](../OutGameOverallPlan.md) |

## 目的

仮の OnGUI のアウトゲームを uGUI に置き換え、ホームからステージ選択へ移ってステージを始められるようにする。

## 既存の資産

| 資産 | 内容 |
|---|---|
| `OutGameStartInitializer` | 仮の画面。この区間で消す（装備の部分は区間2まで残すかを決める） |
| `GameSceneController.GoToStageAsync` | ステージのインゲームへ遷移する。ロック中のステージは `GameProgressService.SelectStage` が弾く（区間0） |
| `IGameProgressState.IsStageUnlocked` / `IsStageCleared` | ロックとクリア済みの判定（区間0） |
| `UiBuilder`（EngineAdapterLayer/UI） | uGUI の Canvas・Text・Button をコードで組み立てる共通の部品。インゲームの画面はこれで作っている |
| `StageResultAdapter`、`StagePauseAdapter` | uGUI の画面の手本。初期化で Canvas を組み立て、ボタンの処理は Initializer から `Action` で受け取る |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 1-1 | ホームの Canvas | EngineAdapter / シーン | ステージ選択・スキル装備・コンフィグへのボタン |
| 1-2 | 画面の切り替え | Application / EngineAdapter | どの画面を出しているかの State と、戻る操作 |
| 1-3 | ステージ選択 | EngineAdapter | 5 ステージのボタン。ロック中は押せない見た目にし、クリア済みに印を付ける |

## 完了条件

- ホームからステージ選択へ移り、ステージを選ぶとそのインゲームが始まる
- インゲームから戻ると、アウトゲームのホーム（またはステージ選択）に戻る
- ロック中のステージは押せない

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | ホームのボタン | ステージ選択・スキル装備・コンフィグ・ゲームの終了 |
| 2 | インゲームから戻ったときの画面 | ステージ選択 |
| 3 | ステージ選択で出す情報 | 名前、クリア済みか、解放されるスキル（`StageData.UnlockSkill` の名前。空なら出さない） |
| 4 | UI の作り方 | 既存の画面に合わせて `UiBuilder` でコードから組み立てる。見た目を作り込む段階で、プレハブにするかを決め直す |
| 5 | 戻ったときにステージ選択を出す判定 | `IGameProgressState.SelectedStageIndex` が `NO_STAGE` 以外ならステージ選択から始める |
| 6 | 装備の仮の画面 | 区間2までの間、ホームの「スキル装備」から仮の装備枠（今の `CycleEquippedSkill` のボタン）を出す |

## 既存コードの確認結果

| 対象 | 今の状態 |
|---|---|
| `OutGame.unity` | 置いてあるのは `OutGameStartInitializer`、ライト、`OutGameCompositor` だけ。Canvas と EventSystem はない |
| `OutGameStartInitializer` | `IInjectable<IGameSceneController>` と `IInjectable<GameProgressService>` で受け取り、OnGUI でステージのボタン（ロック中は押せない、(cleared)・(locked) の印）と装備枠のボタン（`CycleEquippedSkill`）を出す。遷移は `destroyCancellationToken` を渡さずに `GoToStageAsync` を待つ（理由はコードのコメント） |
| インゲームから戻る経路 | `StageResultAdapter` の「Back to Menu」と `StagePauseAdapter` から `IGameSceneController.GoToOutGameAsync` を呼ぶ。アウトゲームは毎回読み込み直されるので、画面の状態は残らない |
| 戻ったときの判定に使えるもの | `IGameProgressState.SelectedStageIndex` は、インゲームから戻ったときだけ `NO_STAGE` 以外になっている（起動直後は `NO_STAGE`） |
| UI の作り方 | インゲームの画面は、どれも `UiBuilder` でコードから組み立てていて、プレハブやシーンに置いた Canvas はない。`UiBuilder.EnsureEventSystem` で EventSystem も作れる |
| ステージの一覧 | 常駐シーンの `GameSceneInitializer._stages` にある 5 つ（チュートリアル→殲滅→崩壊→ボス戦→到達）。`StageData.UnlockSkill` は区間5まで空 |

作業のときの注意

- 新しい Initializer を置いた・外したら、`UsefulToolkit/Generate/Scene Compositor` で `OutGameCompositor` を作り直す（クラウドではできないので、報告にローカルでの手順として書く）
- 画面の切り替えは、まず 1 つの Adapter の中に閉じて作り、State は足さない（planning-criteria の基準1。ほかに読む利用者がいない為）。どの画面を出すかを別の Adapter が読む必要が出たときに State にする

## 実装結果

決めたこと

| # | 項目 | 決定 |
|---|---|---|
| 1 | ホームのボタン | ステージ選択・スキル装備・コンフィグ（区間4まで押せない）・ゲームの終了 |
| 2・5 | 戻ったときの画面 | `SelectedStageIndex` が `NO_STAGE` 以外ならステージ選択から始める。起動直後はホーム |
| 3 | ステージ選択の情報 | 名前、`[CLEARED]` / `[LOCKED]`、解放されるスキル名（空なら出さない） |
| 4 | UI の作り方 | `UiBuilder` でコードから組み立てる |
| 6 | 装備の仮の画面 | ホームの「Skill Equip」から、枠のボタン（`CycleEquippedSkill`）を出す |

作ったもの

- `OutGameMenuAdapter`（EngineAdapterLayer/OutGame）：ホーム・ステージ選択・スキル装備の 3 画面を 1 つの Canvas に組み立てて切り替える。どの画面を出しているかはこの中だけで持ち、State は足していない。EngineAdapterLayer は External と Application を参照できないので、ステージの表示内容は `StageEntry`、装備枠は文字列で受け取る
- `OutGameStartInitializer`：OnGUI を消し、`gameObject.AddComponent` で Adapter を足してつなぐ。Adapter はシーンに置かず、Initializer が実行時に足すので、`OutGame.unity` と `OutGameCompositor` は変わらない
- ステージ開始中は `CanvasGroup.interactable` を切ってボタンを押せなくする

完了条件の確認結果

- ローカルのプレイモードで確認した（2026-10-19）。ホーム → ステージ選択 → ステージ開始 → リザルトの Back to Menu でステージ選択に戻る（クリア・失敗の両方）。ロック中のボタンを押しても画面は変わらない。Quit でエディタの再生が止まる
- 確認で直したこと：EventSystem を常駐シーンに置いた。`UiBuilder.EnsureEventSystem` はアウトゲームのメニューの下に作るので、ステージの Adapter の初期化の時点ではまだ残っていて作られず、その後アウトゲームのシーンと一緒に消え、リザルトのボタンが押せなかった

次の区間へ持ち越すこと

- 装備の画面は仮のまま（区間2）。コンフィグのボタンは押せないまま（区間4）
- 見た目の作り込みと、プレハブへの置き換え（`UiBuilder` を消す）

## 見つけた問題（今回は扱わない）

