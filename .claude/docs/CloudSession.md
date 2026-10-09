# クラウドセッションでの制約

環境変数 `CLAUDE_CODE_REMOTE` が `true` のときに当てはまる。

- Unity エディタは動かない。そのため uloop（コンパイル・プレイモード・テスト・スクリーンショット）と Blender MCP は使えない
- クラウドでは実装と静的な確認（参照先、asmdef の依存、命名・メンバー順、コメントの基準）までを行う。コンパイルと実機の確認は、ローカルに引き取って（`claude --teleport`）から行う。報告では、未確認であることと、ローカルで確かめる内容（コンパイルする asmdef、開くシーン、操作の手順）を書く
- `.csproj` と `.sln` は Unity が生成するもので、リポジトリにない。asmdef の依存は `.asmdef` ファイルを読んで確かめる
- UPM パッケージの中身（`Library/PackageCache`）はない。UsefulToolkit の使い方は usefultoolkit スキルを先に読み、足りなければ GitHub の TaguchiRei/UsefulToolkit（public）を読む
- commit・push・PR の決まりは `.claude/docs/GitWorkflow.md` にある
