# 区間4E：スポーン位置と編成

| 項目 | 内容 |
|---|---|
| 状態 | 計画済み（着手の GO 待ち） |
| 目安の時期 | 2027/02/09〜02/15（最速の推定 2026/10/11〜10/12） |
| 前提となる区間 | 9 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

区間の番号は識別用。区間4E は 2026-10-10 に追加した区間で、区間9のあと、区間4F の前に行う。

## 目的

敵のスポーンを「スポーン位置 1 つにつき 1 グループ」にする。

- スポーン位置ごとに、属する区画と、出すグループの編成（ScriptableObject）を指定する
- 初期生成の範囲と、間隔での生成をなくす
- ほかのグループとの合流と、グループごとの交互の前進をなくす
- 区画とスポーン位置を、シーンビューで見て選べるようにする

撤退・補充・全滅後の出し直しは区間4G で作る。この区間では、開始時に出したグループが減っていくだけになる。

## 関連する仕様

- 仕様の決定の一覧：[EnemySquadRedesign.md](../EnemySquadRedesign.md)（「決めたこと」1〜5、12、17、18）
- 敵の出現：https://app.notion.com/p/3e91ea2aa7fa81de9e78d74221c216c8
- 敵の群衆の動き：https://app.notion.com/p/3f01ea2aa7fa81b79394e168026e83e7
- 敵の群衆 AI：https://app.notion.com/p/3f01ea2aa7fa81faad84f46c32a3b860
- 仕様検討リストの経緯：https://app.notion.com/p/3f41ea2aa7fa81998cf7e56fa0361917

## 既存の資産

| 資産 | 内容 |
|---|---|
| `EnemySpawnSystem` | ステージシーンの生成の設定。同時に存在する数の上限、生成情報（`EnemySpawnInfo`）、格子の範囲、区画の大きさ |
| `EnemySpawnPoint` | 実行中の生成位置。半径、有効か |
| `EnemyInitialSpawnArea` | 初期生成の範囲。数と編成 |
| `EnemySpawnAdapter` | `SpawnInitial`、`SpawnByInterval`、`TrySpawn`、`TryGetNextSpawnPoint`、`ReturnStrandedAgents`、`OverlapCountJob` |
| `EnemyGroups` | `TryMerge`（合流）、`MaintainNext`（穴詰め・合流・並べ替え） |
| `EnemyGroupJob` | `UpdatePhase`・`IsBlockedByGroupAhead`・`GetFormationHalfWidth`（交互の前進とレーンの待ち） |
| `EnemyDistanceField` | 区画（範囲の中心が区画の中心）と追跡範囲。追跡範囲の Gizmo |
| ScriptableObject の手本 | `VoxelQualitySettings`（EngineAdapter の ScriptableObject。`CreateAssetMenu` は `Kizami/〜`） |
| Unity の機能 | `Gizmos.DrawIcon`（`Assets/Gizmos/` の画像を、距離によらない大きさで描く。色を付けられ、クリックで選べる） |

## 既存コードの確認結果（2026-10-10）

| 対象 | 状態 | 対応 |
|---|---|---|
| `EnemyGroups.MaintainNext`・`TryMerge`（[EnemyGroups.cs:237](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroups.cs)、[298](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroups.cs)）、`MERGE_DISTANCE`、`EnemyFormationSettings._mergeSize`（既定 4） | `MergeSize` 以下のグループを、40m 以内で空きのあるグループへ移す。区間4C の 4C-5 で入り、区間4D で扱いを決めずに残った | 消す。穴詰めと並べ替えは残す |
| `EnemyGroupJob.UpdatePhase`・`IsBlockedByGroupAhead`・`GetFormationHalfWidth`（[EnemyGroupJob.cs:104](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroupJob.cs)、[712](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroupJob.cs)、[742](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroupJob.cs)）、`EnemyGroup.PhaseTimer`・`IsAdvancing`、`EnemyFormationSettings` の `_usesAlternatingAdvance`・`_advanceDuration`・`_holdDuration`・`_groupSpacing` | 交互の前進とレーンの待ち。InGame では切ってある（`_usesAlternatingAdvance: 0`）。区間4D で、入れても切っても重なる組の数に差がなかった（約 556 と約 560） | 消す（決めたことの 17） |
| `EnemySpawnAdapter.OverlapCountJob`・`_countsOverlaps`・`_overlapCounts`・`MovingOverlapCount`・`ArrivedOverlapCount`、`EnemyInitializer.GetOverlapText` | 交互の前進を戻すかを判断する為の、重なる組の数のデバッグ表示 | 交互の前進と一緒に消す |
| `EnemySpawnAdapter.SpawnInitial`（[EnemySpawnAdapter.cs:749](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)） | 初期生成の範囲ごとに、`GroupSize` 人ずつ範囲の中のランダムな中心へ置く | スポーン位置ごとに、編成の 1 グループを置く形に書き換える |
| `EnemySpawnAdapter.SpawnByInterval`（[789](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)）、`_spawnTimers`、`TryGetNextSpawnPoint`（[895](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)） | 生成情報ごとの間隔で、スポーン位置を順に回して新しいグループを出す | 消す |
| `EnemySpawnSystem.MaxAliveCount`（[EnemySpawnSystem.cs:30](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnSystem.cs)）、`new EnemyGroups(_agents.Length)` | 敵の状態の配列の長さ。グループの配列も同じ長さ（1000 体なら 1000 グループ分の道筋 128 点・帰りの道筋 64 点） | 決めたことの 3 |
| `EnemyGroups.TryAdd`（[EnemyGroups.cs:173](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroups.cs)） | グループの空きがなければ false で、敵はグループなしのまま出る。`TrySpawn` は結果を見ていない | グループの数をスポーン位置の数にするので、空きがないことはない。グループなしの経路を消す（決めたことの 5） |
| `EnemySpawnAdapter.ReturnStrandedAgents`・`TryGetReturnHome`（[678](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)、[729](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)） | 戻れない敵を元のグループから抜き、持ち場ごとに新しいグループにする。グループなしの敵は次の生成位置へ移す | 決めたことの 5 |
| `EnemyMoveJob.Walk`（[EnemyMoveJob.cs:127](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyMoveJob.cs)）の最後の分岐 | 隊列の位置を持たない敵（グループなし）は、距離マップを下る | 消す（決めたことの 5） |
| `EnemyGroupJob.IsTracked`（[EnemyGroupJob.cs:156](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroupJob.cs)） | 持ち場の位置の列が追跡範囲の矩形に入っているかで、追跡を始める | そのまま使う（決めたことの 2） |
| `EnemyDistanceField` の区画の原点（[EnemyDistanceField.cs:266](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyDistanceField.cs)） | `bounds.center.xz - sectionSize / 2` をコンストラクタで求める | 計算を `EnemySpawnSystem` に置き、`EnemyDistanceField` と区画の色分けの両方で使う |
| `EnemyFormationSettings.GroupSize`（12） | `TryAdd` の人数の上限、`OpenGroup` の道筋の点の数（[EnemyGroups.cs:409](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroups.cs)）、生成の区切りで使う | 編成の長さが人数になるので、設定から消す。道筋の点の数は `MAX_GROUP_SIZE`（16）で求める |
| 編成（`EnemySpawnInfo._composition`、`EnemyInitialSpawnArea._composition`） | `EnemyKind` の配列。並びより後ろのメンバーは Attacker | 編成のアセットに移す（決めたことの 4） |
| `EnemyKind`（[EnemyKind.cs](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyKind.cs)） | EngineAdapter にある。ExternalLayer は EngineAdapter を参照できず、EngineAdapter も ExternalLayer を参照できない | 編成のアセットは EngineAdapter に置く |
| `EnemyInitializer.GetSpawnPointText`（[EnemyInitializer.cs:115](../../../Code/Scripts/Initialization/Enemy/EnemyInitializer.cs)） | デバッグ表示に生成位置の有効・無効を並べる | そのまま使う |
| TestStage | `EnemySpawnSystem_Few`（上限 10、初期生成の範囲 2 つ、5 体ずつ。先頭が Defender のものと Finisher のもの）、`EnemySpawnSystem_Crowd`（上限 1000、範囲 4 つ、250 体ずつ）。どちらも生成情報 1 件（3 秒ごとに 1 体）。生成位置は (±25, 25)、(0, −35) | 置き直す（決めたことの 7） |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 4E-1 | 合流・交互の前進をなくす | EngineAdapter / Initialization | `TryMerge`、交互の前進とレーンの待ち、重なる組の数のデバッグ表示と、その設定を消す |
| 4E-2 | 編成の ScriptableObject | EngineAdapter | グループの編成（`EnemyKind` の並び。長さが人数）を持つアセット |
| 4E-3 | スポーン位置の 1 グループ | EngineAdapter | スポーン位置に区画と編成を持たせる。開始時にスポーン位置ごとに 1 グループを出す。敵の状態とグループの配列の長さを、スポーン位置から決める。初期生成の範囲と生成情報を消す。戻れない敵はグループに残す |
| 4E-4 | デバッグ表示 | EngineAdapter | 区画を色分けして描く。スポーン位置をアイコンで描き、遠くからクリックで選べるようにする |
| 4E-5 | TestStage の配置 | Level | `EnemySpawnSystem_Few` と `EnemySpawnSystem_Crowd` を、スポーン位置と編成の形に置き直す |

## 完了条件

- 開始時に、スポーン位置ごとに、指定した編成の 1 グループが出る。実行中に新しいグループは出ない
- 減ったグループがほかのグループへ合流しない
- 戻れない敵は、元のグループのまま持ち場の周りへ移る。グループの数はスポーン位置の数から増えない
- 区間4D・9 の挙動（待機・追跡・帰還、外側と内側の螺旋、弾・バリア・吸収）を壊さない
- シーンビューで区画が色分けされ、スポーン位置のアイコンを遠くからクリックして選べる。区画の番号と位置が食い違うスポーン位置は赤く描かれ、警告が出る
- （4B の決定 6）敵の処理が合計 4ms 以下（`_Crowd`、約 1000 体、32 体に体を貸す、エディタ、安全チェックなし）

## 詳細仕様で決めること

2026-10-10 にユーザーと決めた（確定）。どれも推奨の案を採った。

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | 区画の指定の仕方 | スポーン位置に区画の番号（x, z）を持たせる。置いた直後は位置から求めた番号を入れ（`Reset` と、区画の番号が未設定のときの `OnValidate`）、作者が変えられる |
| 2 | 指定した区画と位置が食い違うとき | 追跡を始める判定は、今と同じく持ち場の位置で行う。指定した区画は置き場所の確認に使い、位置と食い違えば初期化のときに警告を出し、シーンビューでそのアイコンを赤くする。距離マップは追跡範囲の矩形の中しか計算しない（[EnemyDistanceField.cs:620](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyDistanceField.cs)）ので、指定した区画で追跡を始めると、持ち場の位置が範囲の外のときに距離がなく、グループが動けない為 |
| 3 | 同時に存在する数の上限とグループの数 | `MaxAliveCount` をなくす。敵の状態の配列の長さは、有効なスポーン位置の編成の人数の合計にする。グループの配列の長さは、有効なスポーン位置の数にする。1 グループ 1 スポーン位置で、補充も満員までなので、どちらも超えない。初期化のあとに有効にしたスポーン位置の分は確保しないので、実行中の有効・無効の切り替えは「出さない」方向だけに使う |
| 4 | 編成のアセットの中身 | `EnemyKind` の並びだけを持ち、並びの長さをそのグループの人数にする（1〜16、`EnemyFormationSettings.MAX_GROUP_SIZE`）。「並びより後ろは Attacker」の規則はなくす。`EnemyFormationSettings.GroupSize` は設定から消す。アセットは EngineAdapter に置く（`EnemyKind` が EngineAdapter にある為。区間4F で Application が編成を読む必要が出たら、そのときに置き場を見直す） |
| 5 | 戻れない敵 | グループから抜かず、持ち場の周りへ移すだけにする。移った敵は、グループの隊列の位置へ歩いて戻る。グループなしの敵の経路（次の生成位置へ移す、`EnemyMoveJob.Walk` の距離マップを下る分岐）は消す |
| 6 | 区画の色分けとアイコン | 区画の色分けは `EnemySpawnSystem` の `OnDrawGizmos` で、格子の範囲の底面に区画ごとの半透明の面と枠を描く。色は区画の番号から決める（隣どうしが同じ色にならない 4 色の繰り返し）。常に描くか、選択中だけ描くかは Inspector で切り替える。スポーン位置は `Gizmos.DrawIcon` で、指定した区画の色を付けたアイコンを描く（`Assets/Gizmos/` に画像を 1 枚足す）。半径の円も同じ色で描く |
| 7 | TestStage の配置 | `_Few`：今の初期生成の範囲 2 つ（北と南）を、同じ場所・同じ編成（5 体）のスポーン位置 2 つにする。今の生成位置 3 つ（A・B・C）は消す。`_Crowd`：負荷を見る用途なので約 1000 体を保つ。今の 4 つの範囲の周りに、12 体の編成のスポーン位置を 21 個ずつ（計 84 個・1008 体）、uloop の動的コードで格子に並べて置く |

## 作業計画

### 新しく作る型と、区間4E での利用者

| 型 | 層 | 区間4E での利用者 |
|---|---|---|
| 編成のアセット（`EnemySquadComposition`。ScriptableObject） | EngineAdapter | `EnemySpawnPoint` が参照し、`EnemySpawnAdapter` が生成のときに読む |

消す型：`EnemyInitialSpawnArea`、`EnemySpawnInfo`。

拡張する型：`EnemySpawnPoint`（区画の番号、編成、アイコン）、`EnemySpawnSystem`（区画の原点・番号の計算、区画の色分け。上限と生成情報を消す）、`EnemyDistanceField`（区画の原点を受け取る）、`EnemySpawnAdapter`（スポーン位置ごとの生成、配列の長さ、戻れない敵、重なる組の数を消す）、`EnemyGroups`（合流を消す、人数の上限を `MAX_GROUP_SIZE` に）、`EnemyGroupJob`・`EnemyGroup`（交互の前進を消す）、`EnemyMoveJob`（グループなしの分岐を消す）、`EnemyFormationSettings`（人数・合流・交互の前進の設定を消す）、`EnemyInitializer`（重なる組の数の表示を消す）

設定を消すので、InGame.unity の `EnemySpawnAdapter` と、TestStage.unity の `EnemySpawnSystem` に保存された値は、保存し直して消す。

アイコンの画像は `Assets/Gizmos/EnemySpawnPoint.png`（白い図形。色はコードで付ける）を作る。

### コミットの分け方

| # | 内容 | 確かめること |
|---|---|---|
| 0 | 区間計画書の更新（決めたことの答え、作業計画）。[EnemySquadRedesign.md](../EnemySquadRedesign.md) の決めたことの 17・18 | ― |
| 1 | 合流・交互の前進・重なる組の数をなくす（4E-1） | コンパイル。`_Crowd` で、動的コードでメンバーを倒して 4 体以下にしたグループが、ほかのグループへ移らない。追跡・螺旋・帰還が今と同じに動く |
| 2 | 編成のアセットとスポーン位置の 1 グループ（4E-2・4E-3）。`_Few` の置き直し（4E-5 の前半）。`EnemyInitialSpawnArea` を消す前に、`_Few` と `_Crowd` の範囲のオブジェクトをシーンから外す | `_Few` で、北と南に 5 体のグループが 1 つずつ出て、先頭が Defender・Finisher になる。実行中に増えない。敵の状態の数が 10、グループの数が 2。戻れない敵（動的コードで台地の上へ移す）が、グループのまま持ち場の周りへ移る |
| 3 | デバッグ表示（4E-4） | 区画が色分けされる。スポーン位置のアイコンを遠くからクリックして選べる。区画の番号を書き換えると赤くなり、再生時に警告が出る |
| 4 | `_Crowd` の置き直し（4E-5 の後半） | 84 グループ・1008 体が出る。追跡範囲の外のグループは待機する。敵の処理が 4ms 以下 |
| 5 | 実装結果、全体計画書の更新 | 完了条件すべて |

### 基準の当てはめで見直した点（2026-10-10）

- 基準1：新しい型は編成のアセットだけにした。区画の番号は `Vector2Int` で持ち、区画を表す型や MonoBehaviour は作らない。区画の計算は `EnemySpawnSystem` のメソッドにして、`EnemyDistanceField` に原点を渡す
- 基準1：満員の人数を `EnemyGroup` に持たせる案は、使うのが区間4G の撤退の割合だけなので、区間4G で足す
- 基準3：追跡範囲の外の部隊の処理（ユーザーの提案、2026-10-10。[EnemySquadRedesign.md](../EnemySquadRedesign.md) の決めたことの 19・20）は、区間4E の完了条件に関わらないので、区間4F（4F-4）に入れた

## 他プラットフォームへの対応

- プラットフォームによる違いはない

## 次の区間へ持ち越すこと（計画の時点）

- 区間4F：部隊の意思決定を Application へ移す。追跡範囲の外の部隊の処理（4F-4）
- 区間4G：撤退・補充・全滅後の出し直し、スポーン位置の破壊。満員の人数を `EnemyGroup` に持たせる
