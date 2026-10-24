# 区間40：アーキテクチャのリファクタリング

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 未定 |
| 前提となる区間 | 39（2026-10-24、ユーザー） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

区間39 までは、区間ごとに細部だけを確かめてきた。この区間では、コード全体を State-Centrism Architecture の原則に照らし、層の責務を整理する。区間41 以降の作業を、整理したあとの構造の上に乗せる。

挙動は基本的に変えない。各コミットの前後で、同じ操作に同じ結果が出ることを確かめる。挙動を変えた方が構造がきれいになる、またはコードが短くなるときは、その都度ユーザーに聞く。仕様判断と設計判断はユーザーが行う。

## 確定した方針（2026-10-24、ユーザー）

| # | 内容 |
|---|---|
| 1 | ボス戦の行動の順番と、体力を減らす量の判断は Application に移す。行動や体力に応じた演出を付けやすくし、ボスの状態を見た演出を強めやすくする為 |
| 2 | 敵の群れのルール（攻撃を始める条件、冷却、同時に攻撃する数の上限、隊列）は、ホットパスの最適化の為に EngineAdapterLayer に置いたままにする。最適化の為にやむを得ない、ごく一部の判断に限って EngineAdapterLayer に置いてよい |
| 3 | Application が `Time.*` や `Physics.gravity` のようなエンジンの static な値を読むのは許容する。許容しないのは、MonoBehaviour の継承やライフサイクルのように EngineAdapterLayer の責務を脅かすものだけ。層を分けるのは、コードが固まって仕様変更に耐えなくなるのを防ぐ為 |

## 既存コードの確認結果（2026-10-24 時点。着手時に見直す）

| 場所 | 今の状態 | 原則との関係 |
|---|---|---|
| `StageInitializer.OnWordLanded`（263 行） | 投げた言葉の着弾で、削る・敵を倒すを組み立て、`_isResolvingWordImpact` を持って Brilliant Skill に伝えるかを決める | Initialization にロジックがある |
| `StageInitializer.OnDefeated`（252 行） | 倒した数を `StageFlowService`・`StageGuideService`・`BrilliantSkillService` の 3 つに配る。ボスでの撃破を除く判定もここ | ロジックがある。直接配線の相手が 3 つ |
| `StageInitializer.OnBuildingCollapsed`（352 行） | `GetComponentsInChildren` で倒れた階の範囲を計算する | エンジンを叩く処理が Initialization にある |
| `PlayerInitializer.Rush` / `Fly`（183・198 行） | 突進で先に削る形と、飛行の上昇・下降の分け方を計算する | Initialization にロジックがある |
| `PlayerInitializer.OnSkillUsed` / `DistributeCutResults` | スキルの種類で Adapter を呼び分ける。切断の結果を配る | 着手時に、配線だけかを確かめる |
| `OutGameStartInitializer.QueueUnlockNotices` / `EquipSkill` | 解放の演出の順番待ちと「出したものとして記録する」時機を決める。装備の結果で表示を更新する | Initialization にロジックがある |
| `BossBattleService` / `BossBattleAdapter` | 段階の進みは Service。段階の中の行動の順番（下部レーザー → 降下フィニッシャー → 塔投げ → 待機、最終の繰り返し）は Adapter が決める | 方針1 で Application に移す |
| `HealthTarget` | 体力の値は `TargetHealthService` が持つ（Single Writer は守られている）。通常攻撃・スキル・落ちる塊で減らす量は Adapter が持つ | 方針1 で減らす量の判断を Application に移す |
| `EnemyGroup.State` / `CommandedState` | public フィールド。命令は Application の命令を EnemySpawnAdapter が写し、今の状態は EnemyGroupJob が書く | 方針2 の範囲。書き手が 2 つに見えないか、区間39 の変更のあとに確かめる |
| `SwordFormManagement`（EngineAdapter） | 中身が空で、MonoBehaviour でもない | 置き場所を決めるか消す |

asmdef の参照、Board のキーの型、Application・BlackBoardLayer・ExternalLayer にライフサイクルがないことは確認済みで、問題はない。

## 作業一覧

| # | 作業 | 内容 |
|---|---|---|
| 40-1 | ステージの配線の整理 | `OnWordLanded`・`OnDefeated`・`OnVoxelsCarved` の判断を、Application の Usecase 1 つにまとめる。Initializer はその Usecase へ 1 本ずつつなぐだけにする。倒れた階の範囲の計算は `VoxelBuilding` 側へ移す |
| 40-2 | プレイヤーの配線の整理 | `Rush` / `Fly` の形の計算を、移動の Service（決める部分）と Adapter（体の範囲を読む部分）へ分ける |
| 40-3 | アウトゲームの配線の整理 | 解放の演出の判断を `GameProgressService` 側へ移す |
| 40-4 | ボスの行動の順番 | 段階の中の行動の順番を `BossBattleService` へ移す。Adapter は「この行動を始める」「終わった」の実行と報告だけにする |
| 40-5 | 体力を減らす量 | 攻撃の種類ごとの減らす量を Application に移す。`HealthTarget` は当たりの検出と報告だけにする |
| 40-6 | 小さい整理 | `EnemyGroup` の書き手の確認、`SwordFormManagement` の整理 |
| 40-7 | 設計の正本への反映 | 方針2・3 を Notion の設計ページとスキルに書き足す案を出す。書き戻しはユーザーが確かめてから |

40-1 と 40-4 は、作業の中で新しい Usecase を作るかを決める。作るときは、今の区間で使う利用者を書く（planning-criteria の基準1）。

## 完了条件

- Initializer に、生成・登録・配線のほかの判断が残っていない（デバッグ用の Initializer は除く）
- 直接配線の相手が、原則どおり 1 クラスになっている
- ボスの段階の中の行動の順番と、体力を減らす量の判断が Application にある
- 全ステージとボス戦を通して遊び、整理の前と同じ挙動になる

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 40-1 で、撃破を EventBoard に載せるか、Usecase 1 つにまとめるか | Brilliant Skill はスイッチで無効になっている（コミット 6861d12）。受け取るのが Usecase 1 つで済むなら、EventBoard は使わない |
| 2 | 40-4 で、Adapter に残す単位 | 「攻撃 1 つを最後まで実行する」単位を Adapter に残し、次に何をするかだけを Service が決める |
| 3 | 40-5 で、減らす量をどこに持つか | `TargetHealthService` に攻撃の種類を渡す形にするか、別のデータに持つか |

## 見つけた問題（今回は扱わない）

- なし（着手時の見直しで足す）
