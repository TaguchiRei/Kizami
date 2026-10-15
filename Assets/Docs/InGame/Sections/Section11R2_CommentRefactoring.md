# 区間11R-2：コメントの修正

| 項目 | 内容 |
|---|---|
| 状態 | 未着手 |
| 前提となる区間 | 11R-1（ソースコードのリファクタリング）がマージ済みであること |
| 続く区間 | 11R-3（計画書の修正） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 前の区間 | [Section11R1_SourceRefactoring.md](Section11R1_SourceRefactoring.md) |

## 目的

11R-1 で構造と名前が決まったコードの、コメントと XML ドキュメントコメント（summary / param / remarks）を、スキル `comment-refactoring` の基準で整える。コードの差分は出さない。

## 前提

- 作業の前に、スキル `comment-refactoring`・`planning-criteria`、`.claude/rules/csharp-conventions.md`・`unity-compile.md` を読む。11R-1 の計画書の「実装結果」で、名前や場所が変わったものを確かめる
- 基準の要点：変更の経緯は書かず今の設計判断・仕様判断を書く、装飾を付けない、変数のサマリーは一行、コードから読み取れる否定形を避ける（禁止事項・制約は否定形でよい）、今後の課題は `// TODO:` にとどめる
- `<remarks>` は「サマリーに入れると役割がぼやけるが、消すと壊されうる制約」だけにする。動作やアルゴリズムの説明は、実装しているメソッド・定数のサマリーへ移す（区間8R で決めたこと）
- ゲーム全体の決まりに従っているだけの説明（例：「Time.deltaTime で数え、スロー中は一緒に遅くなる」）は各クラスに書かない
- コード（コメント以外）は変えない。コードの問題を見つけたら「見つけた問題」に書く
- Git 操作はユーザーが行う。Claude はコミットの分け方とメッセージを提案する（`.claude/docs/GitWorkflow.md`）

## 対象範囲

11R-1 と同じ `Assets/Code/Scripts/` の 5 層（生成された Compositor を除く）と、`Assets/Code/Shader/`。区間8R で整えたあとに変わった部分が中心だが、8R のあとに基準が足されているので、全体を見る。

## 作業一覧

区切りごとに、作業 → 検証 → 変更したファイルと確認結果の報告をして止まる。

| # | 作業 | 内容 |
|---|---|---|
| C-1 | 調べる | `extract_comments.py`・`find_candidates.py` で、装飾・否定形・複数行の変数のサマリーの候補を出す。`// TODO:` の一覧（2026-10-15 時点で 7 件）を作り、区間10・11 で済んだもの（例：`StageClearAdapter` の TODO）がないかを見る |
| C-2 | 提案する | 区切りごとの直し方の方針と、判断が要るもの（残す否定形、消してよいか迷う `<remarks>`、済んだ TODO）を出し、GO をもらう |
| C-3 | 直す | 区切り：Shader → BlackBoard と External → Application → Initialization → EngineAdapter（CoreSystem・Input・Player・Stage） → EngineAdapter（Enemy） → EngineAdapter（Voxel）。区切りごとに 1 コミット |
| C-4 | 行リンクを付け直す | `Assets/Docs/Voxel/` とその `Detailed/` の、コードの行へのリンクを `fix_doc_line_links.py`（スキル `code-refactoring`）で今のコードに合わせる。11R-1 と 11R-2 の両方で行がずれているので、ここでまとめて行う |
| C-5 | 書き戻す | この計画書の「実装結果」と「見つけた問題」を書く |

## 完了条件

- 各区切りのコミットで、`check_comment_only_diff.py` の出力が空（差分がコメントだけ）
- `find_candidates.py` の装飾と複数行の変数のサマリーが 0 件。区間8R で残すと決めたもの（`ScreenSpaceBooleanFeature.cs` のアスキー図、`MAX_INSTANCES_PER_DRAW` など 5 つの定数）と、今回残すと決めたものは、理由を実装結果に書く
- 残した否定形は、どれも禁止事項・制約か仕様の説明であることを実装結果に書く
- コンパイルのエラーと警告が 0 件
- `Assets/Docs/Voxel/` の行リンクが、すべて意図した行を指している

コードを変えないので、プレイモードでの確認は行わない。

## 実装結果

C-5 で書く。

## 見つけた問題（今回は扱わない）

作業中に見つけたコードの問題を、内容と場所だけ書く。
