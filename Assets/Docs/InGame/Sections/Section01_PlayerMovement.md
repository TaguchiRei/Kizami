# 区間1：プレイヤー移動の完成

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2026/10/06〜10/12 |
| 前提となる区間 | 0 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

仕様「プレイヤー移動」の移動アクションをすべて揃える。敵からの攻撃を受けるための、HP と被ダメージの窓口を用意する。

## 関連する仕様

- プレイヤー移動：https://app.notion.com/p/3e91ea2aa7fa818bbc3fe53a151e424f
- 失敗条件：https://app.notion.com/p/3e91ea2aa7fa813fb405e07c700a75b4

## 既存の資産

| クラス | 内容 |
|---|---|
| [PlayerMovementService](../../../Code/Scripts/Application/Player/PlayerMovementService.cs) | Move 入力から移動方向と目標速度を決め、`PlayerMovementState` に書き込む |
| [PlayerMovementAdapterBase](../../../Code/Scripts/EngineAdapterLayer/Player/PlayerMovementAdapterBase.cs) | FixedUpdate で水平速度を目標へ近づけ、Rigidbody に反映する。Y 方向の速度は上書きしない |
| [PlayerLookService](../../../Code/Scripts/Application/Player/PlayerLookService.cs) | 視点入力に感度をかけて `PlayerLookState` に書き込む |
| 入力アクション | Player マップに Jump と Sprint がある |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 1-1 | ダッシュ | Application / BlackBoard | Sprint 入力で目標速度を切り替える |
| 1-2 | ジャンプ | Application / EngineAdapter | ジャンプ要求の State と、接地の判定、上向きの速度を与える処理 |
| 1-3 | 壁走り | Application / EngineAdapter | 壁との接触の判定、壁走りに入る条件と抜ける条件、壁走り中の重力の扱い |
| 1-4 | 短距離ワープ | Application / EngineAdapter | 画面中央の方向への高速移動、クールタイム、エフェクトの差し込み口 |
| 1-5 | ワープ中の軽減 | Application | 無敵または軽減率（0〜100 で調整できる）の適用 |
| 1-6 | HP と被ダメージの窓口 | BlackBoard / Application | HP の State、ダメージを受け付ける操作（軽減率を適用する）、HP が 0 になったことの通知 |
| 1-7 | 移動パラメータのデータ化 | ExternalLayer | 歩行・ダッシュ・ジャンプ・壁走り・ワープの値を ScriptableObject にまとめる |

## 完了条件

- 歩行、ダッシュ、ジャンプ、壁走り、短距離ワープがすべて操作できる
- 各パラメータを Inspector から調整できる
- デバッグ操作でダメージを与えると HP が減り、ワープ中は軽減率が適用され、HP が 0 になると通知が出る

## 詳細仕様で決めること

- 各移動パラメータ（速度、ジャンプの高さ、ワープの距離と所要時間、クールタイム）
- ワープ中のダメージ軽減率
- 壁走りに入る条件（ダッシュ中に壁に触れる、空中で壁に触れる など）、続く時間、壁からのジャンプの有無
- ワープの方向に上下の成分を含めるか（視線の方向そのものか、水平面だけか）
- 接地や壁との接触のような「エンジン側で分かる情報」を Application へ渡す経路
- プレイヤーの HP の値と、回復の有無

## 他プラットフォームへの対応

- 移動の判断は Application に置き、Rigidbody への反映は `Standard〜` / `Vr〜` の Adapter に置く
- VR では、ダッシュとワープによる酔いへの対策（暗転や視野を狭めるなど）が別に必要になる
