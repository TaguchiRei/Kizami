# 区間4A：敵の体と切断

| 項目 | 内容 |
|---|---|
| 状態 | 計画済み（詳細仕様の確定待ち） |
| 目安の時期 | 2026/10/06〜10/19（最速の推定 10/06〜10/09） |
| 前提となる区間 | 1, 3 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

区間4は 2026-10-05 に 3 つに分けた。4A（この区間）で敵の体と切断を作り、[4B](Section04B_CrowdPrototype.md) で群衆 AI の試作と計測、[4C](Section04C_CrowdAI.md) で群衆 AI の本実装を行う。4A の敵は、その場に立つか、プレイヤーへまっすぐ進むだけにする。

## 目的

ステージシーンの生成システムから敵を出し、切ると部位を失い、核を切ると倒れる敵の体を作る。
敵の体は最初にすべてプールに用意し、実行中は Instantiate しない。
その前に、今の InGame にあるライト・地面・テスト用の壁をステージシーンへ分ける。

## 関連する仕様

- 敵：https://app.notion.com/p/3e91ea2aa7fa81f3a713c0a6e64d438e（部位の役割、切られた部位は接続部側が残ること、核を壊すまで倒れないこと、倒れたときの扱い、部位ごとの切断回数の上限）
- 敵の出現：https://app.notion.com/p/3e91ea2aa7fa81de9e78d74221c216c8（生成システム、同時に存在する数の上限）
- チャージ：https://app.notion.com/p/3e91ea2aa7fa81249000ca51326add0f（かけらの切り直しは、部位ごとの切断回数の上限まで）
- 敵の大量描画と体の貸し出し：https://app.notion.com/p/3f01ea2aa7fa818fb0ebc1f6ea509324（4A の体は、4B で近くの敵に貸す体になる）

## 前提

- 仮モデルは `Assets/Art/Models/AttakkerEnemy.fbx`（四足歩行）。SkinnedMeshRenderer は使わず、部位ごとに分かれたメッシュを動かす
- 敵の動きは `Time.deltaTime` を使う（スロー中は一緒に遅くなる）
- 移動の仕組み（群衆 AI）は 4B・4C で作る。固有のアクション（攻撃を含む）は区間9で作る
- 失敗（HP 0）とリトライは区間11で作る（敵の攻撃は区間9までないので、それまでは HP のデバッグ操作でしか 0 にならない為）

## 仮モデルの確認結果（2026-10-05）

| 項目 | 内容 |
|---|---|
| 階層 | `Body`、脚 4 本（上の部位 `〜UpperLeg` の子に下の部位 `〜UnderLeg`）、`GunTurret`（子に `Gun`）、`Light` |
| 三角形の数 | 合計 4308（Body 164、上の部位 236 × 4、下の部位 430 × 4、GunTurret 716、Gun 764）。SkinnedMeshRenderer はない |
| Transform | `Light` 以外は回転 0、拡大率 1 |
| 部位の役割（決定） | 核 = `Body`、攻撃部位 = `GunTurret`（子の `Gun` を含む）、移動部位 = 脚の各部位（上 4・下 4） |
| 問題 | ① Blender のライト `Light`（拡大率 100）が書き出されている ② 後ろ脚の名前が `FrontLeftUpperLeg.001` などの複製名のままで、左右も逆（`FrontLeftUpperLeg.001` は右後ろ） ③ マテリアルが `Body` だけ違う ④ `Assets/Art/` は `.gitignore` の対象で、モデルがリポジトリに入らない（決めること #9） |

## 計画書と今のコードの食い違い（着手時に確認）

| # | 内容 | 根拠 | 対応 |
|---|---|---|---|
| 1 | `MeshDataCache` は、`Start` の時点で**アクティブな**子孫の `CuttableObject` しか登録しない。非アクティブで待たせておくプールの敵は登録されない | `MeshDataCache.Initialize` の `GetComponentsInChildren<CuttableObject>()`（`includeInactive` なし） | 敵を出すたびに、パーツを登録し直す（決定 2） |
| 2 | 切られたパーツは、ストアの作り直し（`Rebuild`）で登録から外れ、`MeshId` が別のメッシュを指すようになる | `MeshDataCache.Rebuild` が `!user.IsCuttable` を外す | 出すたびに登録し直すので、保持していた `MeshId` には頼らない |
| 3 | 切られたパーツは `SetActive(false)` されるので、その子もまとめて消える。仮モデルは上の部位の子に下の部位がある | `MultiCutBlade.ExecuteCut` の `target.gameObject.SetActive(false)` | 接続部側を残す処理（決定 4）で、元のパーツを表示し直す。遠い側にある子の部位は、体から外して落とす |
| 4 | 4-3「敵の State（生きている敵の一覧と数）」は、区間4A に読む利用者がいない | 区間3の `FragmentOrbAdapter` は、かけら数・オーブ数を Adapter のプロパティとしてデバッグ表示に出している | 作らない。数は Adapter のプロパティにする。敵の状態は 4B で NativeArray に持つ |
| 5 | 骨子の 4-5「`NavMeshAgent` でプレイヤーに近づく」は、Notion の群衆 AI（距離マップと簡易物理。NavMesh は不採用寄り）と合わない | Notion「敵の群衆 AI（目的地・経路・移動）」 | 4A では作らない。移動は 4B・4C |
| 6 | インゲームにいるときに `GoToInGameAsync` を呼んでも、インゲームは読み直されない（リトライにならない） | `SceneLoadRequester.CollectLoadTargets` はロード済みのシーンを除く | 区間11で、UsefulToolkit にシーンを読み直す経路を足して扱う（決定 10） |

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間2・3の成果 | `MeleeCutAdapter` が範囲内の切れる `CuttableObject` を集めて切り、結果（`MultiCutResult[]`）を `PlayerInitializer` が渡した関数へ渡す。今の渡し先は `FragmentOrbAdapter.ReceiveCutResults` だけ。かけらはオーブになり、吸収されるとチャージが増える |
| `RecycleBuffer<T>` | UsefulToolkit.framework（`UsefulToolkit` の Utility）。固定長のリングバッファで、空きがないと最も古い物を使い回す。要素は `IRecyclable` |
| `SubclassSelectorAttribute` | UsefulToolkit.framework。`[SerializeReference]` のフィールドに、Inspector でサブクラスを選ぶ表示を付ける |
| MeshCut の公開 API | `MeshDataCache.Instance`、`CompleteStoreReaders()`、`Store.Add(Mesh)`、`RegisterUser(CuttableObject)`、`CuttableObject.SetRegisteredMesh(int)`。組み合わせれば、実行中に 1 つのパーツを登録できる。切断の設定 `CanMultiCut` は、`ExecuteCut` の中でかけらへ引き継がれる（`InheritCutSettings`） |
| `IGameSceneController` | 常駐シーンの `GameSceneInitializer` が Root スコープに登録している |
| `SceneGroup` | `HasMainScene` が有効なら、配列の先頭がアクティブなシーンになる。今の `InGameGroup` は `HasMainScene` 有効で `[InGame]` |
| 物理レイヤー | `Enemy`（7）がある。Player と Enemy は衝突しない。Shard と Enemy は衝突する |
| エフェクト | VFX Graph（導入済み）。`Assets/Art/Particles/EnemyDead.vfx` がある |

## MeshCut の制約（敵に関わるもの）

| 制約 | 影響 |
|---|---|
| `MeshDataCache` は `Start` の時点でアクティブな子孫の `CuttableObject` だけを登録する | 敵を出すたびに、パーツを登録し直す |
| 切られたパーツは `DisableCutting()` で切れない状態になり、非アクティブになる。子も一緒に消える | 接続部側を残すときは、元のパーツを表示し直して登録し直す（決定 4） |
| ストアを変えるときは、先に `CompleteStoreReaders()` で切断の Job の完了を待つ（README「ストアを読む Job との関係」） | 登録し直す処理で必ず守る |
| 刃をローカル空間へ移すときの拡大率は `localScale` を使う（区間2で確認） | パーツの親に拡大率を持たせない。仮モデルは拡大率がすべて 1 なので問題ない |
| 近接切断の刃はカメラの位置を通る（区間2の決定） | 水平（0°）に振ると、カメラより低い部分は切れない |
| `MeleeCutAdapter` は、パーツ自身のコライダーで切る対象を探す（`Physics.OverlapBoxNonAlloc`） | 各パーツにコライダーを付ける |
| かけらは `MeshCutObjectPool`（固定長のリングバッファ）のもので、空きがなくなると古い物から回収される | 体に残す接続部側を、かけらのまま持たない。メッシュを元のパーツに写してから、かけらはプールへ返す（決定 4） |

## 既存コードの確認結果

| 対象 | 状態 | 対応 |
|---|---|---|
| InGame シーン | `Directional Light`、`Ground`、`TestWalls`（`WallFront` / `WallLeft` / `WallRight` / `StageBounds`）、`MeshCut System`（`MeshDataCache` / `FragmentPool` / `CutBlade`）、`DummyEnemy`、`PlayerRoot`、`CameraPivot`、`Main Camera`、`MeleeCut`、`MeleeCutPreview`、`FragmentOrb`、`Compositor` | ライト・地面・`TestWalls` をステージシーンへ移す（4-0）。`DummyEnemy` は敵が出るようになったら削除する |
| [PlayerInitializer](../../../Code/Scripts/Initialization/Player/PlayerInitializer.cs) | `meleeCutAdapter.Initialize(fragmentOrbAdapter.ReceiveCutResults)` で、切断の結果を `FragmentOrbAdapter` だけに渡している | 受け取り手に敵を足す（決定 9） |
| [FragmentOrbAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/FragmentOrbAdapter.cs) | 切断の結果の `Original` が管理中のかけらでなければ何もしない。オーブは自分の子として `Instantiate` する | 体に残った切断済みの部分を、倒れたときにオーブにする操作を足す（決定 6） |
| `MeshCutObjectPool` | かけらを自分の子として `Instantiate` する | 変更なし |
| `PlayerInitializer` の State の登録先 | `gameObject.scene.buildIndex`（InGame）で SceneState を登録している | 変更なし。ステージシーンをアクティブにしても影響しない |
| asmdef | EngineAdapter と Initialization は `UsefulToolkit.MeshCut.Runtime` を参照済み | 変更なし |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 4-0 | ステージシーンの分離 | Level | ステージシーンを作り、今の InGame にあるライト・地面・`TestWalls`（`StageBounds` を含む）とライティングの設定を移す。`InGameGroup` を「ステージシーン（先頭、アクティブ）＋ InGame」にする。Build Settings に足し、`BuildScenes` を作り直す |
| 4-1 | UsefulToolkit.MeshCut の拡張 | UsefulToolkit | 実行中に 1 つのパーツを登録する操作と、部位の系統ごとの切断回数（決定 2・5）。区間2と同じく、要件定義を書いて UsefulToolkit のリポジトリで作る |
| 4-2 | 敵の体のプレハブ | Level | AttakkerEnemy から作る。各部位にコライダーと `CuttableObject` を付け、役割を設定する |
| 4-3 | 部位の役割 | EngineAdapter | 役割をサブクラスセレクターで選ぶ（決定 7）。4A で使うのは核・移動・攻撃（攻撃は「失ったか」を持つだけ）・役割なし |
| 4-4 | 体のプール | EngineAdapter | 初期化のときに、同時に存在する数の上限の分だけ体を作る。実行中は Instantiate しない。空きがなければ出さない |
| 4-5 | 生成システム | EngineAdapter / Level | ステージシーンに置く生成システム（親）と、その子の、実行中の生成位置・初期生成情報・生成情報（決定 8） |
| 4-6 | 切断の受け取り | EngineAdapter / Initialization | 切断の結果から、どの体のどの部位が切られたかを引き、接続部側を残す処理と、子の部位を落とす処理を行う（決定 4） |
| 4-7 | 倒れる処理と再利用 | EngineAdapter | 核を切ったら倒れる。体に残った切断済みの部分はオーブにし、切っていない部位は見た目用の物で散らばらせてディゾルブで消す（決定 6・決めること #1）。体は全部位を戻してすぐプールへ返す |
| 4-8 | 仮の移動 | EngineAdapter | プレイヤーへまっすぐ進む（4B で置き換える）。移動部位が N 個壊されたら止まる |
| 4-9 | デバッグ表示 | Debug | 出ている敵の数、プールの空き、生成位置の有効・無効 |

## 完了条件

- ステージシーンを分けたあとも、区間3までの操作（移動、視点、切断、チャージ、かけらが外へ落ちない）が動く
- 初期生成情報の場所に敵が出ていて、実行中は生成位置から敵が出る。同時に存在する数は上限を超えない。無効にした生成位置からは出ず、動かした生成位置からは動かした先で出る
- 実行中に敵の体の Instantiate が呼ばれない
- 部位を切ると、接続部側が体に残り、遠い側がかけらになってチャージになる。接続部の近くを切ると、部位全体が落ちる。上の部位を切ると、遠い側にある子の部位も落ちて、ディゾルブで消える
- 同じ部位の系統は、上限の回数を超えて切れない。核は、ほかの部位を何度切ったあとでも切れる
- 核以外をいくら切っても倒れない。核を切ると倒れ、体に残っていた切断済みの部分はチャージになり、切っていない部位はばらばらになってディゾルブで消える
- 移動部位が N 個壊されると（切られる・落ちる）、移動しなくなる
- 倒れた敵の体はすぐプールに戻り、次に出たときに全部位がそろっていて、切断回数も 0 に戻っている

## 詳細仕様

### 決定（2026-10-05）

| # | 項目 | 決定 |
|---|---|---|
| 1 | 体のプール | 最初にすべてプールに用意し、実行中は Instantiate しない。同時に存在する数の上限（プールの大きさ）に達しているときは、新しく出さない。動けない敵がプールを埋めて、新しい敵が出なくなる、という戦略を遊びにする |
| 2 | パーツの登録 | UsefulToolkit.MeshCut に `MeshDataCache.Register(CuttableObject)`（`CompleteStoreReaders` → `Store.Add` → `SetRegisteredMesh` → `RegisterUser` をまとめたもの）を足す。敵を出すたびに、全パーツを登録し直す |
| 3 | 倒れる条件 | 切断では、核を壊すまで倒れない（崩落による撃破は区間5） |
| 4 | 切られた部位 | 接続部に近い側が体に残り、遠い側がかけらになる。接続部の近く（部位の基準点から一定の距離以内）を切ると、位置にかかわらず部位全体が落ちる。残す側は、切断面に対して基準点がある側とする。実装：残す側のかけらのメッシュを元のパーツに写し、元のパーツを表示し直して登録し直す。かけらはプールへ返す（かけらのまま体に付けておくと、リングバッファの回収で消えるおそれがある為）。遠い側にある子の部位は、体から外れて落ちる。切っていない部位なので、ディゾルブで消える |
| 5 | 切断回数の上限 | 敵ごとではなく、部位の系統ごとに数える。かけらが「これまでに切られた回数」を持ち、表と裏のかけらには「元の回数 + 1」を引き継ぐ。上限に達したかけらは切れない。核の系統は別に数えるので、脚を何度切っても核は切れる（敵ごとに数えると、脚だけを切って上限に達し、核とタレットだけが残った倒せない敵ができる為）。1 回の攻撃で同じ系統のかけらを何個切っても、それぞれの回数が 1 ずつ増えるだけなので、自然に「1 回」と数えられる。UsefulToolkit.MeshCut の `CanMultiCut` を受け継ぐ仕組みを広げて、ライブラリ側に入れる |
| 6 | 倒れたとき | 切断済みの部分（体に残っていた接続部側を含む）はチャージになる。切っていない部位はばらばらになり、ディゾルブで消える（チャージにならない）。体は全部位を有効に戻し、元のメッシュに戻し、切断回数を 0 にして、すぐプールへ返す。散らばって消える部位は、体とは別の見た目用の物で表す |
| 7 | 部位の役割の持たせ方 | 体のコンポーネントに「パーツと役割」の一覧を持たせ、役割は `[SerializeReference]` ＋ `SubclassSelector` で選ぶ（攻撃する・ガードする・吸収するなど、敵の種類で処理そのものが変わる為）。特殊部位は、区間9で役割のクラスを足して作る |
| 8 | 生成システム | ステージシーンに親となる生成システムを置き、その子に次の 3 つを置く。InGame 側は、親を `FindAnyObjectByType` で見つける（ステージシーン全体を探さない）。① 実行中の生成位置：MonoBehaviour。実行中に動くことも、無効になることもある。無効化のメソッドを公開する ② 初期生成情報：開始時に敵がいる位置と、そこを中心とした半径。複数置ける。初期化のときだけ使う ③ 生成情報：実行中に出す敵の情報。一度に出す数の上限を持つ |
| 9 | 切断の結果を、かけらと敵の 2 か所へ渡す方法 | 直接配線。`PlayerInitializer` が、`FragmentOrbAdapter` と敵の Adapter の両方へ渡す関数を `MeleeCutAdapter` に渡す（区間3の決定 2 と同じ理由） |
| 10 | 失敗とリトライ | 区間11へ移す。リトライは、UsefulToolkit にシーンを読み直す経路を足して作る |
| 11 | 仮モデル | AttakkerEnemy。核 = `Body`、攻撃部位 = `GunTurret`（子の `Gun` を含む）、移動部位 = 脚の各部位（上 4・下 4） |
| 12 | 動けなくなる条件 | 移動部位が N 個壊されたら動けなくなる。移動部位は脚の各部位（上の部位 4 つと下の部位 4 つ）。切られたとき、または体から外れて落ちたときに「壊れた」と数える（N の値は決めること #3） |
| 13 | 敵のレイヤーと、プレイヤーとの衝突 | Enemy レイヤーにし、今の設定（Player と Enemy は衝突しない）のままにする |
| 14 | ステージシーンの名前と、今のダミーの敵 | `Assets/Level/Scenes/Stage/TestStage/TestStage.unity`。`DummyEnemy` は、敵が出るようになったら削除する |

### 決めること

「推奨」は、根拠とあわせて Claude が勧める案。

| # | 項目 | 案 | 推奨 |
|---|---|---|---|
| 1 | 散らばって消える部位の見た目 | A：部位の形の見た目用の物を `RecycleBuffer` のプールから出し、ディゾルブのシェーダーで消す ／ B：VFX Graph で、部位のメッシュを粒として出して消す | 4A は A。見た目用の物はコライダーを持たず、決まった時間で消えるだけなので、作りが小さい。B は、区間5でエネルギーの演出を VFX Graph で作るときに、一緒に置き換えるかを決める |
| 2 | 接続部の近くとみなす距離 | ― | 部位ごとに Inspector で持ち、仮に 0.3m。部位の基準点が接続部（脚なら付け根、下の部位なら膝）にあることは、着手時に仮モデルで確かめる |
| 3 | 動けなくなる移動部位の数 N | ― | 仮に 4（8 つのうち）。体の Inspector に置く |
| 4 | 部位の系統ごとの切断回数の上限 | ― | 仮に 2（元の部位を切ったかけらを、もう 1 回まで切れる）。`CuttableObject` の Inspector に置き、かけらへ引き継ぐ |
| 5 | 生成情報の中身と置き場所 | ― | 生成システムに一覧で持つ（区間9で敵の種類を足せる形）。1 件あたり「一度に出す数の上限」と「出す間隔（仮に 3 秒）」。出す数の合計（無限か有限か）の持たせ方は、Notion の検討中の結論に合わせる |
| 6 | 同時に存在する数の上限の置き場所 | ― | ステージシーンの生成システムに置く（仕様のとおりステージごと）。InGame の体のプールは、初期化のときにその値の分だけ体を作る。4A の仮の値は 10 |
| 7 | 実行中の生成位置から出す順番 | 順番に回す ／ ランダム ／ プレイヤーから遠い順 | 順番に回す。無効な生成位置は飛ばす。区間11でステージごとに見直す |
| 8 | 体に残った切断済みの部分をチャージにする方法 | ― | `FragmentOrbAdapter` に「位置を渡すとオーブを出す」操作を足し、倒れたときに体がその部分ごとに呼ぶ（1 つで 1 チャージ。かけら 1 個と同じ） |
| 9 | 仮モデルの FBX の扱い | A：そのまま `Assets/Art/` に置く（ほかの環境ではモデルが外れる） ／ B：追跡されるフォルダへ移す ／ C：Blender で直して書き出し直す | ユーザーが決める。ライトは FBX の読み込み設定の「Import Lights」を切れば外せる。後ろ脚の名前は、プレハブを展開して付け直すか、Blender で直す |

## 作業計画（推奨案で進めた場合）

### 処理の流れ

```mermaid
sequenceDiagram
    participant G as 生成システム（ステージシーン）
    participant S as 敵の Adapter（InGame）
    participant B as 敵の体（プール）
    participant C as MeshDataCache
    participant M as MeleeCutAdapter
    participant F as FragmentOrbAdapter
    S->>G: 初期化で親を探し、上限・初期生成情報・生成位置を読む
    S->>B: 上限の数だけ体を作る（初期化のときだけ）
    S->>B: 初期生成情報の範囲と、間隔ごとの生成位置から出す
    B->>C: 全パーツを登録し直す（Register）
    M->>S: 切断の結果（FragmentOrbAdapter と並べて直接配線）
    M->>F: 切断の結果
    S->>B: 切られたパーツの持ち主の体へ渡す
    B->>B: 接続部側のメッシュを元のパーツに写して残す。遠い側の子の部位を外して落とす
    B->>B: 役割ごとに処理する（移動部位の数、核なら倒れる）
    B->>F: 倒れたら、体に残った切断済みの部分をオーブにする
    B->>B: 切っていない部位は見た目用の物で散らばらせて消し、体は全部位を戻してプールへ返す
```

### 新しく作る型と、区間4A での利用者

| 型 | 層 | 区間4A での利用者 |
|---|---|---|
| `MeshDataCache.Register` と、`CuttableObject` の切断回数（メソッドとフィールドの追加） | UsefulToolkit.MeshCut | `EnemyBody`（出すたびに登録し直す、接続部側を残す）、`MeleeCutAdapter` 経由の切断（上限に達したかけらを除く） |
| `EnemyBody` | EngineAdapter | `EnemySpawnAdapter`。敵のプレハブに付ける。パーツと役割の一覧、元のメッシュ、接続部側を残す処理、子の部位を落とす処理、倒れる処理、全部位を戻す処理、仮の移動を持つ |
| `EnemyPartRole`（抽象）と、`CorePartRole` / `MovePartRole` / `AttackPartRole` | EngineAdapter | `EnemyBody`。部位を失ったときの処理を役割ごとに持つ |
| `EnemySpawnAdapter` | EngineAdapter | `EnemyInitializer`（初期化）、`PlayerInitializer`（切断の結果を渡す）。体のプール、パーツから体を引く表、生成の間隔、出ている敵の数（デバッグ表示用のプロパティ）を持つ |
| `EnemySpawnSystem` / `EnemySpawnPoint` / `EnemyInitialSpawnArea` | EngineAdapter | `EnemySpawnAdapter`。ステージシーンに置く。親が上限と生成情報の一覧を持ち、子が実行中の生成位置と初期生成情報になる |
| `EnemyDebrisAdapter` | EngineAdapter | `EnemyBody`（倒れたとき・子の部位が落ちたとき）。見た目用の部位のプール（`RecycleBuffer`）とディゾルブ |
| `EnemyInitializer` | Initialization | `InGameCompositor`。敵の Adapter を初期化し、出ている敵の数を `DebugGUI` に出す |
| ディゾルブのシェーダー | Level（Shader Graph） | `EnemyDebrisAdapter` の見た目用の部位 |

拡張する型：`PlayerInitializer`（切断の結果を敵の Adapter にも渡す）、`FragmentOrbAdapter`（位置を渡すとオーブを出す操作）

作らないもの：敵の State・Board・Event、ステージシーンの Compositor と Initializer、敵の Service（Application）、移動の仕組み（4B・4C）、攻撃（区間9）、失敗とリトライ（区間11）

設計について確認したいこと（基準2）

- 敵の生成・部位・倒れるルールを、Application の Service にせず EngineAdapter に置く案にしている。区間3で、オーブ化と吸収のルールを `FragmentOrbAdapter` に置いたのと同じ考え方。4B で敵の状態を NativeArray に持つときに、どの層に置くかを見直す

### コミットの分け方

| # | 内容 | 確かめ方 |
|---|---|---|
| 0 | ステージシーンの分離（4-0） | 常駐シーンから再生してインゲームに入り、区間3までの操作が動き、見た目（ライティング）が変わっていない。ステージシーンがアクティブになっている |
| 1 | UsefulToolkit.MeshCut の拡張（4-1。UsefulToolkit のリポジトリ。刻断側はパッケージの更新だけ） | ダミーのパーツを切ったあと、表示し直して `Register` すると、もう一度切れる。ストアを作り直したあとでも切れる。上限に達したかけらは切れない |
| 2 | 敵の体のプレハブ、部位の役割、体のプール、生成システム、`EnemyInitializer`、デバッグ表示、仮の移動（4-2〜4-5、4-8 の前半、4-9）。`DummyEnemy` を削除 | 完了条件 2・3。敵を切るとかけらがチャージになる |
| 3 | 切断の受け取り（4-6）。接続部側を残す、接続部の近くなら全体を落とす、子の部位を落とす | 完了条件 4・5 |
| 4 | 倒れる処理と再利用、移動部位で止まる処理（4-7、4-8 の後半）、`EnemyDebrisAdapter` とディゾルブ | 完了条件 6・7・8 |
| 5 | 区間計画書の「実装結果」と全体計画書の更新 | ― |

## 次の区間へ持ち越すこと（計画の時点）

- 4B：敵の状態を NativeArray に持ち、体を近くの敵にだけ貸す。体を返すときも、部位の状態（失った部位、切られて短くなった部位）は敵の状態に持ち続ける
- 区間5：崩落による撃破。エネルギーの演出を VFX Graph で作るときに、散らばる部位の見た目（決めること #1）を一緒に置き換えるかを決める
- 区間9：特殊部位と固有のアクション（攻撃を含む）。攻撃部位を失った敵は攻撃しない
- 区間11：失敗とリトライ。スコアに使う「倒した敵の数」

## 他プラットフォームへの対応

- 敵の処理にプラットフォームによる違いはない。スマホでは、同時に存在する数の上限を別の値にする可能性がある（生成システムの Inspector の値）

## 見つけた問題（今回は扱わない）

- 【UsefulToolkit.MeshCut】README の「重要な制約」に「切断対象は必ず `MeshDataCache` の子に配置」とあるが、`Start` の時点で非アクティブな子は登録されないことが書かれていない（`MeshDataCache.Initialize`）。コミット1で `Register` を足すときに、README にも書き足すかを決める