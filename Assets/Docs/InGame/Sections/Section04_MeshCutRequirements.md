# 要件定義：敵のパーツを実行中に登録し、切断回数と切断後の形を扱う

区間4A（[Section04_Enemy.md](Section04_Enemy.md)）の作業 4-1。作業するのは UsefulToolkit のリポジトリ（https://github.com/TaguchiRei/UsefulToolkit）。

| 項目 | 内容 |
|---|---|
| 対象のパッケージ | `com.rei.usefultoolkit.meshcut`（`Packages/com.rei.usefultoolkit.meshcut/`） |
| 対象のクラス | `UsefulToolkit.MeshCut.MeshDataCache`、`UsefulToolkit.MeshCut.CuttableObject`、`UsefulToolkit.MeshCut.MultiCutBlade`、`UsefulToolkit.MeshCut.MultiMeshCut` |
| ブランチ | `feature/meshcut/enemy-part`（案） |
| 作成日 | 2026-10-06 |

## 背景

刻断（kizami）の敵は、部位ごとに分かれたメッシュ（それぞれが `CuttableObject`）でできている。敵の体は最初にすべてプールに用意して非アクティブで待たせ、出すたびに使い回す。敵の部位を切ると、接続部に近い側が体に残り、遠い側がかけらになる。

今の MeshCut では、次の 4 つができない。

| # | できないこと | 根拠 |
|---|---|---|
| 1 | 実行中に 1 つの `CuttableObject` を登録する。`MeshDataCache` は `Start` の時点でアクティブな子孫しか登録しないので、非アクティブで待たせているプールの敵が切れない | `MeshDataCache.Initialize` の `GetComponentsInChildren<CuttableObject>()`（`includeInactive` なし） |
| 2 | 部位の系統（その部位と、そこから生まれたかけら）ごとに、切断できる回数に上限を設ける。今は `CanMultiCut` で「1 回だけ」か「何回でも」しか選べない | `CuttableObject.CanMultiCut`、`MultiMeshCut` の追加登録（`!breakables[objIndex].CanMultiCut` で除外） |
| 3 | かけらの形を元のパーツに移す。かけらのメッシュはかけらが持ち主になっていて、そのかけらが次の切断で使い回されると Destroy される。元のパーツの `MeshFilter` に写しただけでは、後で形が消える | `CuttableObject.SetCutMesh` が前に持っていたメッシュを Destroy する。`MultiCutBlade.ApplyResult` が毎回呼ぶ |
| 4 | 切られたパーツを、切る前の形に戻す。`SetCutMesh` に共有のメッシュ（プレハブのメッシュ）を渡すと、後でそれごと Destroy される | `CuttableObject.SetCutMesh` の説明 |

## 要件

### 機能

| # | 要件 |
|---|---|
| F1 | `MeshDataCache` に、実行中に 1 つの `CuttableObject` を登録する操作を足す。ストアを読んでいる Job の完了を待ち（`CompleteStoreReaders`）、そのパーツの今のメッシュをストアに加え、`MeshId` を割り振って切れる状態にし、再構築の対象に加える。非アクティブなパーツも登録できる |
| F2 | F1 で、同じ共有メッシュ（プレハブのメッシュなど）を何度も登録しても、ストアが伸び続けないようにする。既に登録済みのメッシュの ID を使い回すか、再構築で回収されるかは作業者が決めてよい |
| F3 | `CuttableObject` に、切断回数の上限（Inspector で設定。0 は上限なし）と、これまでに切られた回数（読み取り用）を持たせる |
| F4 | 切断で生まれた表と裏のかけらは、切断元の上限と「切断元の回数 + 1」を引き継ぐ。`CanMultiCut` を引き継ぐ仕組み（`InheritCutSettings`）を広げて行う |
| F5 | 上限が 1 以上で、引き継いだ回数が上限に達したかけらは、切れない状態にする（`CanMultiCut` が false のかけらと同じ扱い）。そのかけらのメッシュはストアに追加登録しない |
| F6 | `CuttableObject` に、ほかのかけらの切断後の形を自分に移す操作を足す。移すものは、メッシュ（持ち主も移す）、マテリアル、球コライダーの位置と大きさ（有効・無効を含む）、切断回数、切れるかどうか（かけらがストアに登録済みなら、その `MeshId` を使い、再構築の対象にする）。移したあとのかけらはメッシュの持ち主でなくなり、プールへ返して使い回しても、移した先の形は消えない |
| F7 | F6 で形を移した先のパーツは、自分が元から持っていたコライダー（球コライダー以外。例：プレハブに付けた `BoxCollider`）を無効にする。切断前の形より大きい当たり判定が残らないようにする為 |
| F8 | `CuttableObject` に、切断前の形に戻す操作を足す。`Awake` の時点のメッシュとマテリアルを覚えておき、それに戻す。持ち主になっていた切断後のメッシュは破棄し、球コライダーを無効にし、F7 で無効にしたコライダーを有効に戻し、切断回数を 0 にする。共有のメッシュは破棄しない |
| F9 | F6 と F8 は、アクティブ・非アクティブを変えない（呼び出し元が決める）。F8 のあとに切れる状態にするには、F1 で登録し直す |

### 型の形（案）

次の形を想定しているが、名前と形は作業者が決めてよい。F1〜F9 を満たせばよい。

```csharp
public class MeshDataCache
{
    /// <summary> 実行中に 1 つの CuttableObject を登録し、切れる状態にする </summary>
    public void Register(CuttableObject cuttable);
}

public class CuttableObject
{
    /// <summary> 部位の系統ごとの切断回数の上限。0 は上限なし </summary>
    public int MaxCutCount { get; }

    /// <summary> この部位の系統が、これまでに切られた回数 </summary>
    public int CutCount { get; }

    /// <summary> かけらの切断後の形を自分に移す。かけらはメッシュの持ち主でなくなる </summary>
    public void AdoptCutShape(CuttableObject fragment);

    /// <summary> Awake の時点の形に戻し、切断回数を 0 にする </summary>
    public void RestoreInitialShape();
}
```

### 互換性

| # | 要件 |
|---|---|
| C1 | 上限が 0（上限なし）の `CuttableObject` は、今と同じように動く。`CanMultiCut` の意味は変えない |
| C2 | 今の公開 API（`ExecuteCut`、`MultiCutResult`、`MeshCutObjectPool`、`MeshDataCache.Initialize` / `RegisterUser` / `Rebuild`、`CuttableObject.SetCutMesh` など）は、書き換えなしでコンパイルでき、同じように動く |
| C3 | Inspector の右クリックメニュー「切断」（`Test`）は、今までどおり動く |
| C4 | 既存のシーンとプレハブに置いた `CuttableObject` は、設定し直さなくても今と同じように動く（上限の既定値は 0） |

### 性能

| # | 要件 |
|---|---|
| P1 | F1 の登録は、1 つのパーツにつきメッシュ 1 つ分の追加で済む（ストア全体を作り直さない） |
| P2 | F6・F8 は割り当てを増やさない（メッシュとマテリアルの配列は、覚えてある物を使う） |

### 文書

| # | 要件 |
|---|---|
| D1 | README の「重要な制約」に、`Start` の時点で非アクティブな子は登録されないこと、実行中に足したり非アクティブで待たせたりするパーツは F1 で登録することを書く |
| D2 | README の「1回だけ切断 / 何回でも切断」に、切断回数の上限と引き継ぎを書く |
| D3 | README の「API」に、F1・F6・F8 の操作を書く。F6 のあとにかけらをプールへ返してよいことも書く |
| D4 | CHANGELOG の `[Unreleased]` に `### Added` として追記する |
| D5 | リポジトリの `CLAUDE.md` の「Mesh cut package」の節に、F1・F3・F6・F8 を書き足す |

## 対象外

- どのパーツが体に残るか（接続部側）を決める処理。刻断の側で作る
- 敵の体のプール。刻断の側で作る
- 切断面（`Plane`）を結果に含めること。刻断の側で、切断を呼ぶ `MeleeCutAdapter` が渡す
- 切断のアルゴリズムと Job の変更

## 受け入れの確認

UsefulToolkit のサンドボックスで、次のことを確かめる。

| # | 確認すること |
|---|---|
| 1 | `MeshDataCache` の子に非アクティブで置いたパーツを、アクティブにして F1 で登録すると切れる。同じプレハブのパーツを 10 回登録し直しても、ストアのメッシュ数が伸び続けない |
| 2 | 上限 2 のパーツを切ると、かけらの回数が 1 で、もう一度切れる。そのかけらを切ると、回数 2 のかけらができ、それは切れない |
| 3 | 上限 0 のパーツは、`CanMultiCut` が true なら何回でも切れ、false なら 1 回だけ切れる（今と同じ） |
| 4 | パーツを切り、表のかけらの形を元のパーツに F6 で移して、元のパーツをアクティブにする。かけらをプールへ返し、ほかの物を何度も切ってプールを一周させても、元のパーツの形が消えない。元のパーツはもう一度切れ（回数が上限未満のとき）、当たり判定が切断後の形に合っている |
| 5 | 4 のパーツに F8 を使うと、切断前の形と当たり判定に戻り、回数が 0 になる。F1 で登録し直すと切れる |
| 6 | ストアを再構築（`Rebuild`）したあとも、1・4・5 のパーツが切れる |
| 7 | 右クリックメニュー「切断」と、今の呼び出し元（`await ExecuteCut(...)`）が、変更なしで動く |

## 刻断の側の作業（参考）

UsefulToolkit 側のマージの後で、刻断の `Packages/packages-lock.json` の meshcut の参照を、マージ後のコミットに更新する。この更新は、区間4A のコミット 3（敵のプレハブ・プール・生成）の最初に行う。
