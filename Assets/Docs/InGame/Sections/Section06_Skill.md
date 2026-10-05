# 区間6：スキル基盤・攻撃型スキル

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2026/11/23〜11/29 |
| 前提となる区間 | 3, 5 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

チャージを消費して発動するスキルの仕組みと、攻撃型スキル 2 種を作る。
この区間が終わるとマイルストーンB（1 ステージが最初から最後まで遊べる）になる。

## 関連する仕様

- スキル：https://app.notion.com/p/3e91ea2aa7fa814ebd59e5038006aa39
- ダメージタイプ：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8
- アウトゲーム・通貨：https://app.notion.com/p/3e91ea2aa7fa81e3b271e0fc71189ea6

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間3の成果 | チャージ量の `IChargeState`（`PlayerBoard`、InGame の SceneState）と、加算だけを持つ `ChargeService`（`PlayerInitializer` が生成）。消費の操作はこの区間で `ChargeService` に足す。スキルが切断する場合は、`ExecuteCut` の結果を `FragmentOrbAdapter.ReceiveCutResults` に渡す（渡さないとかけらがオーブにならない） |
| 区間5の成果 | ダメージの窓口、破壊対象、クリア判定 |
| [IVoxelShape](../../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/IVoxelShape.cs) | 球・箱・カプセルの形状 |
| ボクセルの融解 | ビームの演出の候補（[Thermal.md](../../Voxel/Detailed/Thermal.md)、[Melt.md](../../Voxel/Detailed/Melt.md)） |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 6-1 | スキルの定義データ | ExternalLayer | 種類（攻撃型・強化型）、消費量、効果のパラメータ |
| 6-2 | 装備枠の State | BlackBoard / Application | 3 つの装備枠。この区間では装備を固定の仮データにし、区間11でアウトゲームから受け取る |
| 6-3 | 発動 | Application | スキル 1〜3 の入力 → チャージの消費 → 効果の発動 |
| 6-4 | 前方ビーム | Application / EngineAdapter | 前方へ破壊タイプのダメージを出す（カプセル形状で削る） |
| 6-5 | 自分中心の爆発 | Application / EngineAdapter | 自分を中心に破壊タイプのダメージを出す（球形状で削る） |
| 6-6 | 仮の HUD | EngineAdapter | チャージ量と、装備しているスキルの表示 |

## 完了条件

- スキルを発動するとチャージが減り、破壊対象とマップが削れる
- スキルは敵に当たらない
- 雑魚敵を切ってチャージを溜め、スキルで破壊対象を削ってクリアするまでが一通り遊べる（マイルストーンB）

## 詳細仕様で決めること

- 最初に作る攻撃型スキル 2 種の確定と、それぞれの消費量
- クールタイムの有無
- チャージが足りないときの挙動
- ビームを一瞬で出すか、しばらく出し続けるか
- 融解の演出を使うかどうか

## 他プラットフォームへの対応

- 発動の入力はプラットフォームごとに違う（PC は 1 / 2 / 3 キー）。入力の層で吸収する
- VR では、ビームの向きを視線にするか手の向きにするかを、対応するときに決める
