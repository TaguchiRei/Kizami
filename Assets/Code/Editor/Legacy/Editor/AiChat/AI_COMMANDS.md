# AI Command Reference (Optimized)

AIは以下の形式でコマンドを発行することで、Unityエディタを高度に操作できます。
形式: `{"name": "CommandName", "arguments": ["arg1", "arg2", ...]}` (JSON形式)

## ファイル・プロジェクト操作 (アセット)
- `FindAssets(filterOrName)` : アセットを検索します。名前のみ、または `t:Prefab` のようなフィルタが使用可能です。
- `BatchReadFile(paths...)` : 指定した複数のファイルを一括で読み込みます。
- `WriteFile(path, content)` : ファイルを作成または上書きします。ディレクトリがない場合は自動作成します。
- `PatchFile(path, content)` : ファイルの特定部分を修正します。
- `DeleteAsset(path)` : プロジェクトからアセット（ファイルまたはフォルダ）を削除します。
- `MoveFile(from, to)` : アセットを移動します。
- `CopyFile(from, to)` : アセットをコピーします。
- `ReadDirectory(path)` : 指定ディレクトリ直下のファイル・フォルダ一覧を取得します。
- `CreateDirectory(path)` : 新しいディレクトリを作成します。
- `Exists(path)` : パスの存在を確認します。
- `SearchCode(keyword, [directory])` : スクリプト内をキーワード検索します。

## シーン・オブジェクト操作 (インスタンス)
- `GetHierarchy([root])` : ヒエラルキー構造をテキスト取得します。
- `FindGameObjects(nameFilter)` : 名前でオブジェクトを検索します。
- `GetObjectDetail(idOrPath)` : オブジェクトの詳細（ID, Component, Transform）を取得します。
- `GetScriptsOnObject(idOrPath)` : アタッチされているスクリプト一覧とパスを取得します。
- `CreateGameObject(name)` : 空のGameObjectを作成します。
- `DestroyGameObject(idOrPath)` : シーン内のGameObjectを削除（Undo可能）します。
- `AddComponent(target, type)` : コンポーネントを追加します。
- `SetComponentValue(target, comp, prop, val)` : プロパティ値を設定します。
- `AttachComponentReference(go, comp, prop, targetGo, targetComp)` : オブジェクト間の参照を設定します。
- `GetComponentInspectorFields(target, comp)` : インスペクターの全フィールドと値を取得します。
- `GetComponentScriptPath(target, comp)` : ソースコードのパスを取得します。

## プレハブ・マテリアル
- `CreatePrefab(go, path)` : オブジェクトをプレハブとして保存します。
- `ApplyPrefabInstance(go)` : プレハブの変更をアセットに適用します。
- `GetMaterialProperties(path)` : マテリアルのプロパティ一覧を取得します。
- `SetMaterialProperty(path, prop, val)` : マテリアル値を設定します。

## その他
- `GetCompileErrors()` : コンパイルエラーを取得します。
- `GetLoadedScenes()` : ロード中シーン一覧。
- `CaptureGameView()` : GameViewをキャプチャします。
- `InvokeMenuItem(path)` : Unityメニューを実行します。
- `GetSelection()` : 選択中オブジェクトを取得します。
- `EnterPlayMode() / ExitPlayMode() / IsPlaying()` : 再生モード操作。
- `Clear()` : 会話履歴のクリア。
