---
paths:
  - "Assets/**/*.cs"
---

# C# を編集したあとのコンパイル（ローカルのみ）

クラウドセッションでは Unity が動かないので、この手順は使わず `.claude/docs/CloudSession.md` に従う。

- プレイモード中なら止める
- uloop の `execute-dynamic-code` で `UnityEditor.AssetDatabase.Refresh()` を呼んでからコンパイルする。uloop の `compile` は、Unity が変更を検知していないと古いソースのまま成功を返すことがある
- 報告の前に `Library/ScriptAssemblies/<asmdef 名>.dll` の更新時刻がソースより新しいことを確かめる
