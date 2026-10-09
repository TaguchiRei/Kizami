# 区間4F：部隊の意思決定を Application へ移す

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（着手前にこの計画を見直す） |
| 目安の時期 | 2027/02/16〜03/01（最速の推定 2026/10/13〜10/16） |
| 前提となる区間 | 4E |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

区間の番号は識別用。区間4F は 2026-10-10 に追加した区間で、区間4E のあと、区間4G の前に行う。

## 目的

部隊（グループ）の意思決定を、EngineAdapter の Job から Application へ移す。

- Application が部隊ごとに状態（待機・追跡・帰還）と目的地を決め、State で公開する
- EngineAdapter は State を読み、経路探索・アンカーの移動・隊列の制御・個々の敵の移動を、今の Burst の Job で行う
- EngineAdapter は部隊の観測（アンカーの位置、残りの人数、着いたか、進めないか）を返す

挙動は区間4D・9 のままにする。区間4G の撤退・補充は、ここで作った Application の部隊の意思決定に足す。

## 関連する仕様

- 仕様の決定の一覧：[EnemySquadRedesign.md](../EnemySquadRedesign.md)（「決めたこと」13〜16）
- 群衆の動き：[EnemyCrowdRedesign.md](../EnemyCrowdRedesign.md)
- スキル `state-centrism-architecture`（Single Writer、Getter インターフェース、層の参照ルール）

## 既存の資産

| 資産 | 内容 |
|---|---|
| `EnemyGroupJob` | 状態の切り替え（`UpdateState`）、帰還と新しい持ち場（`UpdateBlockedTime`）、外側の螺旋の目標位置の割り当て、アンカーの移動、組み替えの先読み |
| `EnemyGroup`・`EnemyGroups` | グループの状態・持ち場・帰りの道筋・目標位置の番号。NativeArray で持つ |
| `EnemyBarriers`・`EnemyShooter`・`EnemyFinisherAttack` | 区間9の行動。バリアは `EnemyGroup.HasArrived` の間だけ張る |
| EngineAdapter が書く State の手本 | `PlayerContactState`（物理の判定の結果を Adapter が書く） |
| Application と EngineAdapter の直接配線の手本 | `PlayerInitializer` が `PlayerMovementService.Step` を Adapter に渡す |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 4F-1 | 部隊の命令と観測の State | BlackBoard | 命令（Application が書く：状態、目的地、陣形）と観測（EngineAdapter が書く：アンカーの位置、残りの人数、着いたか、進めないか、持ち場が追跡範囲にあるか） |
| 4F-2 | 部隊の意思決定の Service | Application | 待機・追跡・帰還の切り替え、新しい持ち場、外側の螺旋の目標位置の割り当てを、`EnemyGroupJob` から移す |
| 4F-3 | Engine 側の読み替え | EngineAdapter | `EnemyGroupJob` が命令を読んで動く形にし、観測を書き戻す。判断の処理を消す |
| 4F-4 | 追跡範囲の外の部隊 | EngineAdapter | 範囲の外では、帰還中の部隊が帰りの道筋を逆にたどる移動だけを行う。待機中の部隊とそのメンバーは、`EnemyGroupJob`・`EnemyMoveJob` の処理を飛ばし、待機の姿勢で描くだけにする（[EnemySquadRedesign.md](../EnemySquadRedesign.md) の決めたことの 19・20） |

## 完了条件

- 部隊の状態と目的地を決める処理が Application にあり、`EnemyGroupJob` には判断の処理が残っていない
- 区間4D・9 の完了条件の挙動を壊さない（待機・追跡・帰還、新しい持ち場、外側と内側の螺旋、弾・バリア・吸収）
- 追跡範囲の外では、待機中の部隊は処理されず、帰還中の部隊は帰りの道筋をたどって持ち場まで歩く
- 敵の処理が合計 4ms 以下（区間4B の決定 6。1000 体、32 体に体を貸す、エディタ、安全チェックなし）

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 命令と観測の State の形 | 部隊の数の上限を固定にし、部隊の番号を Engine 側のグループの番号と同じにする。State の中身を配列で持つか、部隊ごとの構造体の配列か |
| 2 | 判断の頻度 | 毎フレームか、0.2〜0.5 秒ごとか。状態の切り替えのきっかけ（追跡範囲の切り替え、帰還の到着）は観測で受け取る |
| 3 | 外側の螺旋の目標位置の割り当て | 置き場の位置の計算（立てる列へずらす）は格子を読むので Engine に残し、どの部隊にどの置き場を渡すかを Application で決めるか |
| 4 | 区間9の行動の制御 | 攻撃するか、バリアを張るかを命令に入れるか、区間4G 以降へ持ち越すか |
| 5 | 観測を書くタイミング | Job の完了後に EngineAdapter がまとめて書く。Application の判断は次のフレームの観測を使う |
| 6 | 範囲の外で止めた敵の落下 | 処理を飛ばしている間に足場が壊れたときは、範囲に入って処理が戻ったときに落ちる形でよいか |

## 他プラットフォームへの対応

- プラットフォームによる違いはない

## 次の区間へ持ち越すこと（計画の時点）

- 区間4G：撤退・補充・全滅後の出し直しを、部隊の意思決定に足す
- 範囲の外の巡回と VAT の見た目（[EnemySquadRedesign.md](../EnemySquadRedesign.md) の「検討中の提案」）
- 陣形の切り替え、固定ルートの巡回など、部隊の行動の種類を増やすこと（使う区間が決まったら）
