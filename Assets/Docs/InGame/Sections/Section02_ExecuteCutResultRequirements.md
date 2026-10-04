# 要件定義：MultiCutBlade.ExecuteCut が切断の結果を返す

区間2（[Section02_MeleeCut.md](Section02_MeleeCut.md)）のコミット0。作業するのは UsefulToolkit のリポジトリ（https://github.com/TaguchiRei/UsefulToolkit）。

| 項目 | 内容 |
|---|---|
| 対象のパッケージ | `com.rei.usefultoolkit.meshcut`（`Packages/com.rei.usefultoolkit.meshcut/`） |
| 対象のクラス | `UsefulToolkit.MeshCut.MultiCutBlade` |
| ブランチ | `feature/meshcut/execute-cut` |
| 作成日 | 2026-10-04 |

## 背景

刻断（kizami）の近接切断では、切断で生まれたかけらと元の対象を知る必要がある。

- 区間2：切断の結果をログで確認する
- 区間3：かけらをオーブに変え、チャージにする
- 区間4：どの敵のどのパーツが切られたかを知る

今の `MultiCutBlade.ExecuteCut(CuttableObject[])` は `UniTask` を返すだけで、生まれたかけらを呼び出し元に返さず、通知もしない。中では、対象ごとに表と裏のかけらを `fragmentStubs[i * 2]` / `fragmentStubs[i * 2 + 1]` として持っているが、外からは取り出せない。

自前で `MultiMeshCut` を使う方法では、`ApplyResult` と `ComputeColliderSpheres` など、かけらへの反映の処理（約150行）を利用者の側で重複して持つことになる。そのため、`ExecuteCut` が結果を返すようにする。

## 要件

### 機能

| # | 要件 |
|---|---|
| F1 | `ExecuteCut` は、切断した対象ごとに「元の対象」「表のかけら」「裏のかけら」の組を返す。表は刃の法線（`transform.up`）の側、裏はその反対の側（`MultiMeshCut.CutMesh` の `i * 2` と `i * 2 + 1` と同じ） |
| F2 | 返す組の並び順は、実際に切った対象の順（`FilterCuttable` で除外された後の順）にする。除外された対象（null、`IsCuttable` が false）の組は含めない |
| F3 | 結果を返すのは、すべてのかけらへの反映（Transform、メッシュ、マテリアル、アクティブ化、球コライダー、切断の設定の引き継ぎ、物理の初速）が終わった後にする。反映がフレームをまたいで分割されたときも、最後の反映が終わってから返す |
| F4 | 何も切らずに終わるとき（`targets` が null か空、すべて除外された、かけらが足りない）は、空の結果を返す。null は返さない。かけらが足りないときのエラーログは今のまま出す |
| F5 | 元の対象の扱いは今と変えない（`DisableCutting()` して非アクティブにする。元の対象がプールのかけらなら `TryReleaseObject` で返す） |

### 型の形（案）

次の形を想定しているが、名前と形は作業者が決めてよい。F1〜F5 を満たせばよい。

```csharp
/// <summary> 1つの対象を切断した結果 </summary>
public readonly struct MultiCutResult
{
    /// <summary> 切断した元の対象。切断後は非アクティブで、もう切れない </summary>
    public CuttableObject Original { get; }

    /// <summary> 刃の法線の側のかけら </summary>
    public CuttableObject Front { get; }

    /// <summary> 刃の法線と反対の側のかけら </summary>
    public CuttableObject Back { get; }
}

public async UniTask<MultiCutResult[]> ExecuteCut(CuttableObject[] targets)
```

### 互換性

| # | 要件 |
|---|---|
| C1 | 今の呼び出し方（`await _blade.ExecuteCut(targets);`）は、書き換えなしでコンパイルでき、同じように動く |
| C2 | Inspector の右クリックメニュー「切断」（`Test`）は、今までどおり動く |
| C3 | 処理時間の計測（`EnableProfileLog` / `CollectProfile` / `LastProfile`）の結果と出力は変えない |
| C4 | `MultiMeshCut`、`MeshCutObjectPool`、`CuttableObject`、`MeshDataCache` の公開 API は変えない |

### 性能

| # | 要件 |
|---|---|
| P1 | 結果のために増やしてよい割り当ては、1回の `ExecuteCut` につき結果の配列 1 つまで（切断は毎フレーム呼ぶものではない為） |
| P2 | 反映のループの中で、新しく割り当てを増やさない |

### 文書

| # | 要件 |
|---|---|
| D1 | README の「使い方」の例と「API」の `MultiCutBlade` の表に、戻り値と結果の型を書く |
| D2 | README に、結果の参照の扱いについての注意を書く。プールは固定長のリングバッファなので、返したかけらは後の切断で回収され、別のかけらとして使い回されることがある（そのとき `ReuseAction` が呼ばれる）。元の対象がプールのかけらだった場合も、プールへ返された後に使い回されることがある |
| D3 | CHANGELOG の `[Unreleased]` に `### Added` として追記する |
| D4 | リポジトリの `CLAUDE.md` の「Mesh cut package」の節で `MultiCutBlade` を説明している箇所に、結果を返すことを書き足す |

## 対象外

- かけらが生まれたことを通知する仕組み（イベントやコールバック）。刻断の側で区間3に作る
- 実行中に敵（切断対象）を `MeshDataCache` へ登録し直す API。刻断の区間4で検討する
- 切断のアルゴリズムと Job の変更

## 受け入れの確認

UsefulToolkit のサンドボックスで、次のことを確かめる。

| # | 確認すること |
|---|---|
| 1 | 2 つ以上の対象を一度に切ると、対象の数だけ組が返り、並び順が渡した順と同じで、表のかけらが刃の法線の側にある |
| 2 | 切れない対象（切断済みのもの）を混ぜると、その対象の組は返らず、ほかの対象の組だけが返る |
| 3 | 返ったときには、表と裏のかけらがアクティブで、球コライダーが反映されている |
| 4 | `Can Multi Cut` が true のかけらをもう一度切ると、元の対象としてそのかけらが返り、新しい表と裏が返る |
| 5 | プールの生成数より多くのかけらが必要なときは、空の結果が返り、今と同じエラーログが出る |
| 6 | `await ExecuteCut(...)` と書いている今の呼び出し元と、右クリックメニュー「切断」が、変更なしで動く |

## 刻断の側の作業（参考）

UsefulToolkit 側のマージの後で、刻断の `Packages/packages-lock.json` の meshcut の参照を、マージ後のコミットに更新する。この更新は、区間2のコミット4の最初に行う。
