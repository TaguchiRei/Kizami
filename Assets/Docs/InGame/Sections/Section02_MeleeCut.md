# 区間2：近接切断

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2026/10/13〜10/19 |
| 前提となる区間 | 0 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

剣による近接切断（通常攻撃）を作る。切断面の角度はマウスホイールで回す。

## 関連する仕様

- 近接切断（通常攻撃）：https://app.notion.com/p/3e91ea2aa7fa8139b59ffc7be6c0e090
- ダメージタイプ：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8
- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70

## 既存の資産

| 資産 | 内容 |
|---|---|
| UsefulToolkit.MeshCut | `MultiCutBlade` の `BoxCollider` の範囲内にある `CuttableObject` を、1 枚の平面でまとめて切断する。切断対象は `MeshDataCache` の子に置く必要がある。`Can Multi Cut` で何度も切れるかどうかを切り替える |
| セットアップ | メニュー `UsefulToolkit/Mesh Cut/Setup` で、シーンに `MeshDataCache` / `FragmentPool` / `CutBlade` を配置する |
| 旧シーン | `Test/InGame.unity` に旧構成（MeshCutSystem_V2 など）が残っている |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 2-1 | MeshCut の配置 | Level | インゲームシーンに MeshCut のシステムを組む |
| 2-2 | 切断面の角度 | BlackBoard / Application | ホイール入力で切断面の角度を回し、State に持つ |
| 2-3 | 切断の要求 | BlackBoard / Application | 攻撃入力と攻撃間隔から、切断の要求を出す |
| 2-4 | 刃の配置と切断の実行 | EngineAdapter | カメラの位置と向き、切断面の角度から刃を配置し、`MultiCutBlade` で切断する |
| 2-5 | 切断面のプレビュー | EngineAdapter | 切断面の角度を画面上に表示する |
| 2-6 | 切断結果の通知 | BlackBoard | 切断で生まれたかけらを、区間3で受け取れるように通知する |
| 2-7 | 切れるダミー | Level | パーツに分かれた、切断できるダミーの敵 |

## 完了条件

- ダミーの敵を、ホイールで決めた角度で切断できる
- 切断で生まれたかけらが通知される（ログで確認）

## 詳細仕様で決めること

- 切断判定の方式：攻撃した瞬間に範囲内をまとめて切るか、振りの軌跡に沿って判定するか。スロー中も刃は等速で動くことを前提にする
- 攻撃の範囲（距離と幅）と攻撃間隔
- ホイール 1 段あたりの回転角度、角度を段階で持つか連続で持つか
- 剣のアニメーションの有無。付ける場合、Animator の Update Mode は UnscaledTime にする
- 何度も切れる設定（`Can Multi Cut`）を、敵本体とかけらにどう割り当てるか

## 他プラットフォームへの対応

- 切断の要求（位置、向き、角度）は Application と BlackBoard で持ち、プラットフォームに依存させない
- VR では手の動きから切断面を決め、スマホでは既存の Smartphone マップにある方向別の攻撃ボタン（AttackVertical など）を使う想定。どちらも Adapter と入力の層で吸収する
