# 区間2：近接切断

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2026/10/13〜10/19 |
| 前提となる区間 | 0 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

剣による近接切断（通常攻撃）を作る。切断面の角度はマウスホイールで回す。
切断で生まれたかけらを、区間3が受け取れるように通知する。

## 関連する仕様

- 近接切断（通常攻撃）：https://app.notion.com/p/3e91ea2aa7fa8139b59ffc7be6c0e090
- ダメージタイプ：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8
- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70

## 既存の資産

UsefulToolkit.MeshCut（`Library/PackageCache/com.rei.usefultoolkit.meshcut@*/README.md`）

| 資産 | 内容 |
|---|---|
| `MultiCutBlade` | 自分の Transform を刃にする（`position` が平面上の点、`up` が法線）。`ExecuteCut(CuttableObject[] targets)` で、渡した対象を 1 枚の平面でまとめて切り、プールのかけらに結果を反映する。切る対象は呼び出す側が集める（`BoxCollider` の範囲で集めるのは Inspector の右クリックメニュー「切断」のテスト用だけ）。**生まれたかけらを返さず、通知もしない** |
| `MultiMeshCut` | 切断の計算だけを行う。結果のメッシュ（`CutMesh`、`i*2` が表・`i*2+1` が裏）とコライダー用の点を返し、かけらへの反映は呼び出す側が書く |
| `CuttableObject` | 切断対象とかけらの両方に使う。`IsCuttable`、`Can Multi Cut`（何度も切れるか。かけらへ引き継がれる）、`ReuseAction`（プールに回収されたときに呼ばれる） |
| `MeshDataCache` | `Start` の時点で自分の子孫にある `CuttableObject` のメッシュを登録する。切断対象は必ずこの子孫に置く |
| `MeshCutObjectPool` | かけらを事前に生成して使い回す。固定長のリングバッファで、空きがないと最も古いかけらを回収して使う |
| 切断後の元の対象 | `ExecuteCut` は元の対象を `DisableCutting()` して非アクティブにする。元の対象の代わりに、表と裏の 2 つのかけらが表示される |
| セットアップ | メニュー `UsefulToolkit/Mesh Cut/Setup` で、シーンに `MeshCut System`（`MeshDataCache` / `FragmentPool` / `CutBlade`）を置き、選んだオブジェクトを切断対象にする。かけらのプレハブ（`CuttableObject` / `MeshFilter` / `Renderer`、物理を使うなら `Rigidbody`）は自分で用意する |
| 制限 | 切断の結果を受け取れるのは、切断を始めた次のフレーム以降（`ExecuteCut` は非同期） |
| 旧シーン | `Test/InGame.unity` に旧構成（MeshCutSystem_V2 など）が残っている。参考程度にし、使わない |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 2-1 | MeshCut の配置 | Level | インゲームの場面シーンに MeshCut のシステムを組む。かけらのプレハブを用意し、Rigidbody の補間を有効にする |
| 2-2 | 切断面の角度 | BlackBoard / Application | ホイール入力で切断面の角度を回し、State に持つ |
| 2-3 | 切断の要求 | BlackBoard / Application | 攻撃入力と攻撃間隔から、切断の要求を出す |
| 2-4 | 刃の配置と切断の実行 | EngineAdapter | カメラの位置と向き、切断面の角度から刃を配置し、範囲内の切れる `CuttableObject` を集めて切断する |
| 2-5 | 切断面のプレビュー | EngineAdapter | 切断面の角度を画面上に表示する |
| 2-6 | かけらの通知 | BlackBoard / EngineAdapter | 切断で生まれたかけらと、元の対象を通知する。区間3（チャージ）と区間4（敵の討伐）が受け取る。「決めること」の 1 の方法で実現する |
| 2-7 | 切れるダミー | Level | パーツに分かれた、切断できるダミーの敵 |

## 完了条件

- ダミーの敵を、ホイールで決めた角度で切断できる
- 切断で生まれたかけらと元の対象が通知される（ログで確認）

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 生まれたかけらを受け取る方法 | UsefulToolkit の `MultiCutBlade.ExecuteCut` を、生まれたかけら（元の対象との対応つき）を返すように拡張する。UsefulToolkit はユーザーのリポジトリ（https://github.com/TaguchiRei/UsefulToolkit）なので、変更はユーザーに確認してから行う。拡張しない場合は、`MultiMeshCut` を直接使い、`MultiCutBlade.ApplyResult` を参考にかけらへの反映を自前で書く |
| 2 | 切断判定の方式 | 攻撃した瞬間に範囲内をまとめて切るか、振りの軌跡に沿って判定するか。スロー中も刃は等速で動くことを前提にする |
| 3 | 攻撃の範囲と間隔 | 距離と幅、攻撃間隔 |
| 4 | 切断面の回転 | ホイール 1 段あたりの回転角度、角度を段階で持つか連続で持つか |
| 5 | 剣のアニメーション | 付けるかどうか。付ける場合、Animator の Update Mode は UnscaledTime にする |
| 6 | 何度も切れる設定 | `Can Multi Cut` を敵のパーツとかけらにどう割り当てるか。かけらを切り直せると、かけらの数が増えてプールの上限に早く届く |

## 他プラットフォームへの対応

- 切断の要求（位置、向き、角度）は Application と BlackBoard で持ち、プラットフォームに依存させない
- VR では手の動きから切断面を決め、スマホでは既存の Smartphone マップにある方向別の攻撃ボタン（AttackVertical など）を使う想定。どちらも Adapter と入力の層で吸収する
