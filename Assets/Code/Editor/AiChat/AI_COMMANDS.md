# AI Command Reference (Extended)

AIは以下の形式でコマンドを発行することで、Unityエディタを高度に操作できます。
形式: `<CommandName("arg1", "arg2", ...)>`

## ファイル・プロジェクト操作
- `<ReadFile("path")>` : ファイルを読み込みログ出力。
- `<WriteFile("path", "content")>` : ファイル作成/上書き。
- `<ChangeFile("path", "content")>` : ファイル内容更新。
- `<PatchFile("path", "content")>` : ファイルの特定部分を置換/修正。
- `<DeleteFile("path")>` : ファイル削除。
- `<DeletePath("path")>` : ファイルまたはディレクトリを削除。
- `<MoveFile("from", "to")>` : アセットの移動。
- `<CopyFile("from", "to")>` : アセットのコピー。
- `<ReadDirectory("path")>` : ディレクトリ内容表示。
- `<CreateDirectory("path")>` : ディレクトリ作成。
- `<Exists("path")>` : ファイル/フォルダの存在確認。
- `<ListFiles("path")>` : 指定ディレクトリ内のファイル一覧。
- `<FindAssets("filter")>` : アセット検索 (例: "t:Prefab Enemy")。

## コンパイル・コンソール
- `<GetCompileErrors()>` : 現在のコンパイルエラー一覧。

## シーン・ヒエラルキー
- `<GetLoadedScenes()>` : ロード中シーン一覧。
- `<GetHierarchy()>` : ヒエラルキー構造をテキスト取得。
- `<FindGameObjects("name")>` : 名前でオブジェクト検索。
- `<CreateGameObject("name")>` : 空のGameObject作成。
- `<DeleteGameObject("name")>` : オブジェクト削除。

## プレハブ操作
- `<CreatePrefab("goname", "path")>` : GameObjectをプレハブとして保存。
- `<ApplyPrefabInstance("name")>` : プレハブインスタンスの変更をアセットに適用。

## コンポーネント・トランスフォーム
- `<GetComponentScriptPath("name", "comp")>` : コンポーネントのソーススクリプトのパスを取得。
- `<AddComponent("name", "type")>` : コンポーネント追加。
- `<GetComponentInspectorFields("name", "type")>` : インスペクターで表示されるフィールド名、型、値の一覧。
- `<SetComponentValue("name", "comp", "prop", "val")>` : プロパティ/フィールド値の変更（private/SerializeFiled含む）。
- `<AttachComponentReference("go", "comp", "prop", "targetGo", "targetComp")>` : コンポーネント間の参照を設定。

## マテリアル操作
- `<GetMaterialProperties("path")>` : マテリアルのシェーダープロパティ一覧を取得。
- `<SetMaterialProperty("path", "prop", "val")>` : マテリアルのプロパティ値を設定。

## 実行・その他
- `<EnterPlayMode()>` : Unityの再生モードを開始。
- `<ExitPlayMode()>` : Unityの再生モードを終了。
- `<IsPlaying()>` : 現在再生モード中かどうかを確認。
- `<CaptureGameView()>` : GameViewのキャプチャ撮影。
- `<InvokeMenuItem("path")>` : メニュー項目実行 (例: "File/Save")。
- `<GetSelection()>` : 選択中オブジェクト名。
- `<Clear()>` : AIのコンテキスト（履歴）をクリア。
