# 区間11R-2：コメントの修正

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-15） |
| 前提となる区間 | 11R-1（ソースコードのリファクタリング）が終わっていること。同じブランチ `feature/alpha/refactoring` で続けて行う |
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

2026-10-15 に実装した。区切りごとに 1 コミットで、Shader〜Initialization はユーザー、EngineAdapter（Enemy）以降は Claude がコミットした（ユーザーの指示）。

| コミット | 内容 |
|---|---|
| `5a314dd` | Shader |
| `76c3712` | BlackBoard と External |
| `d857390` | Application |
| `b0a955e` | Initialization |
| `5f63afc` | EngineAdapter（CoreSystem・Input・Player・Stage・UI） |
| `8b02ff0` | EngineAdapter（Enemy） |
| `edb80b9` | EngineAdapter（Voxel） |
| `c46d97d` | `Assets/Docs/Voxel/` の行リンク（C-4） |

- 基準に、ユーザーの指示で「コメントの中の日本語と数字の間にスペースを入れない」を足した（例：`1 つ` → `1つ`）。識別子や英語の語・演算子に続く数字（`UV1 の`、`Pass 0 と`、`d >= 1.0 は`、`× 2 ＋ 左右`）と、箇条書きの番号は対象外。スクラッチパッドのスクリプトで当て、拾えない数か所は手で直した
- 何をどこへ繋ぐか・置くかを列挙していたクラスサマリーを縮めた：`EnemyInitializer`、`StageInitializer`、`StageHudAdapter`、`EnemySpawnAdapter`。アルゴリズムの説明が重なっていた `VoxelBuilding` のクラスサマリーも 2 行を 1 行にした
- 経緯や計画が混ざっていたもの：`EnemyBarriers` の TODO の区間番号、`VoxelModelBaker.MAX_EDGE_VOXELS` の「8 では出た」を、今の事実に直した
- 否定形は、`BuildingPattern.shader` の 2 か所（「ブロックを描かずに」「丸みの無い所ではずらさない」）を直した。残した否定形は、どれも禁止事項・制約（「.w で割ってはいけない」「destroyCancellationToken は渡さない」）か、仕様・設計判断の説明（「クリアタイムと被ダメージは使わない」「壊れたバリアは張り直さない」）
- `// TODO:` は 12 件（Shader の 5 件を含む）。区間10・11 で済んだもの（`StageClearAdapter`）はすでに消えていた。`SSBoolean_Lit.shader` の TODO の「」を外して `ScreenSpaceBooleanFeature.cs` と同じ書き方にした
- 自動生成の Compositor 3 ファイルは対象外にした

完了条件の確認

| 条件 | 結果 |
|---|---|
| 各区切りの差分がコメントだけ | 確認済み。`check_comment_only_diff.py` の出力は、すべての区切りで「差分はコメント行と空行だけ」 |
| 装飾と複数行の変数のサマリーが 0 件（残すものは理由を書く） | 装飾 1 件は区間8R と同じ `ScreenSpaceBooleanFeature.cs` の視線のアスキー図。複数行 6 件は、区間8R の 5 件（`MAX_INSTANCES_PER_DRAW`・`MESHING_READ_MARGIN`・`TRUNCATION_VOXELS`・`PADDING_CELLS`・`VoxelHullDirections.COUNT`）と `VoxelModelBaker.MAX_EDGE_VOXELS`。後者は、割らないと中身が欠け、割りすぎると点が漏れるという両側の制約で、1 行に収まらない |
| 残した否定形が制約か仕様の説明 | 確認済み（上記） |
| コンパイルのエラーと警告が 0 件 | 確認済み。区切りごとに Refresh してからコンパイルし、DLL がソースより新しいことを見た。Refresh で始まったコンパイルと重なると「Compilation is already in progress」がエラー 1 件として返るので、そのときはコンパイルし直した |
| `Assets/Docs/Voxel/` の行リンクが意図した行を指す | 確認済み。`fix_doc_line_links.py` で 55 件を付け直し、決まらなかった 1 件（`SurfaceNetsJob.GenerateQuads`、引数の折り返しが変わった）は手で L149 にした。全 118 件の語句と指す行を並べて見比べた |

## 見つけた問題（今回は扱わない）

作業中に見つけたコードの問題を、内容と場所だけ書く。

- コードの問題は見つからなかった
- `SSEmbedded_Mask.shader` の TODO（reversed-Z で比較が反転する）は前からあるもので、今回は書き方だけを確かめた
