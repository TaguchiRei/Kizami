# 区間0：進行のセーブと解放

| 項目 | 内容 |
|---|---|
| 状態 | 実装済み・ローカルでの確認待ち |
| 目安の時期 | 未定 |
| 前提となる区間 | ― |
| 全体計画 | [OutGameOverallPlan.md](../OutGameOverallPlan.md) |

## 目的

クリアしたステージ、装備、コンフィグをアプリを閉じても残す。通貨をなくし、ステージのロックとスキルの解放を初回クリアで決まるようにする。

## 関連する仕様

- 全体計画書の 7 章「確定した仕様」のステージとスキル

## 既存の資産

| 資産 | 内容 |
|---|---|
| `GameProgressService` / `GameProgressState` | 選んだステージ、装備（スキルの番号）、通貨、クリア済みのステージを持つ。保存はしない |
| `OperationSettingService` | 設定の初期値を Inspector から受け取る。保存した値の読み出しは `// TODO:` |
| `StageData` / `SkillData` | ステージとスキルの定義 |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 0-1 | 保存の窓口 | ExternalLayer | 進行とコンフィグを書き出し・読み込む |
| 0-2 | 通貨の削除 | Application / BlackBoard | `Currency` と初回クリアの報酬を消す |
| 0-3 | ステージのロック | Application / BlackBoard | 前のステージをクリアしたら次を選べる。初めはチュートリアルだけ |
| 0-4 | スキルの解放 | ExternalLayer / Application | ステージの定義に解放するスキルを持たせ、初回クリアで解放する。衝波と攻走は初めから解放 |
| 0-5 | 装備の制約 | Application | 解放済みのスキルだけ、同じスキルは 1 枠だけ |
| 0-6 | セーブの時機 | Application | クリア、装備の変更、コンフィグの変更で保存する |

## 完了条件

- ステージをクリアしてアプリを再起動しても、クリアと解放が残っている
- ロック中のステージは選べない
- 解放していないスキル、ほかの枠と同じスキルは装備できない

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 保存の方式 | `JsonUtility` で `Application.persistentDataPath` にファイルを書く（スマホ・VR でも同じ扱いにできる為）。`PlayerPrefs` も候補 |
| 2 | ステージとスキルの識別 | 並びの位置ではなく、定義に持たせた ID で保存する（並びを変えても壊れない為） |
| 3 | テスト用ステージ | `TestStageData` を一覧から外すか、デバッグのビルドだけに出すか |
| 4 | セーブデータの初期化 | デバッグメニューに消す手段を置くか |

## 既存コードの確認結果

- 通貨を使っていたのは `GameProgressState`、`GameProgressService`、`GameSceneInitializer`（`_firstClearReward`）、`OutGameStartInitializer` の表示の 4 か所
- 常駐シーンのステージ一覧は、テスト・チュートリアル・殲滅・崩壊・到達・ボス戦の順で、仕様の順と違っていた
- 衝波・攻走のスキルの定義はまだない（区間5で作る）

## 実装結果

決めたこと

| # | 項目 | 決定 |
|---|---|---|
| 1 | 保存の方式 | `JsonUtility` で `persistentDataPath/Save/` に書く。進行は `progress.json`、設定は `setting.json` |
| 2 | 識別 | `StageData` と `SkillData` に `_id` を足し、ID で保存する。空ならアセットの名前を使う |
| 3 | テスト用ステージ | 一覧から外した |
| 4 | セーブデータの初期化 | エディタのメニュー `Kizami/Delete Save` だけにした |

作ったもの

- `JsonSaveStore<T>`、`SaveDirectory`（ExternalLayer/Save）。保存データの型の `ProgressSaveData`、`OperationSettingSaveData`（Application/CoreSystem）。設定の enum が BlackBoard 層にあるので、データの型は Application に置いた
- 解放済みのスキルは保存しない。初期の解放とクリア済みのステージから決まる為
- 装備の制約は `CycleEquippedSkill` の中で、解放していないスキルとほかの枠のスキルを飛ばす形で入れた
- 初期解放のスキルは `GameSceneInitializer._initialUnlockedSkills`。衝波・攻走ができるまでは、仮にビームと爆発を入れている
- 各ステージの `_unlockSkill` は空のまま（砕剣などの定義ができる区間5で入れる）

## 見つけた問題（今回は扱わない）

- `OperationSettingService` の設定を変える窓口は、どこからも呼ばれていない。区間4で画面とつなぐ

