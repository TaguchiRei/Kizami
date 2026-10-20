# 要件定義：ロード済みのシーングループを読み直す経路と、DebugGUI の監視の登録を外す手段

区間19（[Section19_StageFlow.md](Section19_StageFlow.md)）のコミット7（リトライ）の前提。作業するのは UsefulToolkit のリポジトリ（https://github.com/TaguchiRei/UsefulToolkit）。

| 項目 | 内容 |
|---|---|
| 対象のパッケージ | `com.rei.usefultoolkit.framework`、`com.rei.usefultoolkit.debugging` |
| 対象のクラス | `UsefulToolkit.Application.Scene.SceneLoadService`（と `SceneLoadRequester` / `SceneState`）、`UsefulToolkit.Debugging.DebugGUI` |
| 基にした版 | `24c4a9c` |
| 作成日 | 2026-10-15 |

API の名前と形は、UsefulToolkit 側で決めてよい。この文書は、刻断が必要としていることと守ってほしい制約を書く。

## 背景

刻断では、インゲームのリザルトから同じステージをもう一度始める（リトライ）。インゲームは SceneGroup（ステージシーン（アクティブ）＋ InGame）で、刻断の `GameSceneController` は `SceneLoadService.LoadGroupAsync(groupIndex, overwriteLoadedScenes: true)` で遷移している。

### 問題 1：ロード済みのグループを読み直せない

`SceneLoadRequester.RequestOverwriteLoadAsync` は、`CollectLoadTargets` で未ロードのシーンだけをロードの対象にする。インゲームにいるときに同じグループを要求しても、どのシーンも読み直されず、シーンの State（`RegisterSceneState` で登録した State）も作り直されない。

アウトゲームを経由すれば読み直せるが、リトライのたびにアウトゲームのシーンを読む無駄がある。ダミーのグループを経由する案は、刻断の側で採らないことにした（2026-10-05）。

### 問題 2：DebugGUI の監視の登録を外せない

`DebugGUI.ObserveVariable(name, func)` はリストに足すだけで、外す手段がない。刻断はインゲームの Initializer から 17 か所で登録しているので、インゲームに入り直すたびに表示が重複する。破棄されたシーンのオブジェクトを読む `func` が残るので、例外の原因にもなりうる。

## 要件

### 機能

| # | 要件 |
|---|---|
| F1 | ロード済みのシーングループを、もう一度読み込める。グループのシーンはいったんアンロードされ、新しくロードされる（Awake・Initializer・Compositor が初めから動く） |
| F2 | F1 で読み直したシーンに紐づくシーンの State は、アンロードのときに登録が外れ、ロードのあとに新しく登録できる。通常のシーン遷移と同じライフサイクルになる |
| F3 | F1 で、常駐シーンはアンロードしない |
| F4 | F1 のあと、アクティブシーンはグループのメインシーンになる |
| F5 | 読み直しの最中に別のロード・アンロードを要求すると、今の `TryBeginLoad` と同じく失敗を返す |
| F6 | `DebugGUI` に登録した監視を外せる。外したあとは表示されず、`func` も呼ばれない |
| F7 | F6 は、登録したオブジェクトのシーンがアンロードされたときに外れる形でも、明示的に外す形でもよい。前者なら、刻断の側の解除の処理が要らないので望ましい |

### 制約

- `ObserveVariable` には `[Conditional("UNITY_EDITOR")]` が付いている。Conditional のメソッドは戻り値を返せないので、`IDisposable` を返して解除する形にするなら、Conditional の扱いを含めて決める
- 既存の `LoadGroupAsync`・`ObserveVariable(name, func)` の呼び出しは、そのまま動くこと（刻断以外の利用者がいる為）
- 読み直しは、アクティブシーンを一時的に常駐シーンへ移すなど、アクティブシーンをアンロードできない Unity の制約を内部で扱うこと

### 文書

- 追加した API の XML ドキュメントコメントと、パッケージの README（あれば）に使い方を書く

## 対象外

- シーンの読み直しの演出（フェードなど）
- `DebugGUI` の `Log` 系の変更

## 受け入れの確認

刻断の側で、コミット7の実装のあとに確かめる。

1. インゲームのリザルトからリトライすると、同じステージが初めから始まる（流れの状態が Ready、倒した数・スコアが 0、HP が満タン、破壊対象が元どおり）
2. リトライを 3 回繰り返しても、コンソールに State の二重登録の警告やエラーが出ない
3. インゲームとアウトゲームを何度か往復しても、`DebugGUI` の表示が重複しない
4. リトライの前後で、常駐シーンの State（通貨・装備・クリア済み）が保たれる

## 刻断の側の作業（参考）

- `IGameSceneController` に、今のステージを読み直す操作を足す
- `StageResultAdapter` に Retry のボタンを足す
- `DebugGUI.ObserveVariable` の 17 か所（`TimeScaleDebugInitializer`、`EnemyInitializer`、`PlayerDebugInitializer`、`StageInitializer`）を、新しい API に合わせる
