# 区間4F：部隊の意思決定を Application へ移す

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-11） |
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

## 既存コードの確認結果（2026-10-10）

| 対象 | 今の状態 | 4F での扱い |
|---|---|---|
| `EnemyGroupJob.UpdateState` | 持ち場が追跡範囲に入ったら追跡、外れたら帰還にし、帰還のときは置き場を手放して隊列を前後に入れ替える（`FaceFormation`） | 切り替えは Application へ移す。入れ替えは Engine に残し、命令の状態が変わったときに行う |
| `MoveHome` の到着 | Job の中で待機にする | Engine は着いたことを観測で返し、待機にするのは Application |
| `UpdateBlockedTime` | 進めない時間を数え、超えたら持ち場を書き換えて待機にする | Engine は進めないことを観測で返し、時間を数えて持ち場を決めるのは Application |
| `UpdateEncircleSlot`・`FindFreeSlot` | 置き場の割り当て。置き場の位置は格子から求める（`BuildSlotPoints`） | 詳細仕様の 3 |
| `HasAliveMember` | メンバーがいなくなったグループを止める | 残りの人数を観測で返す。全滅後の出し直しは区間4G |
| `MoveAnchor` のバリアの居座り | `HasArrived && IsBarrierRaised` の間は留まる | 詳細仕様の 4 |
| asmdef | BlackBoard・Application は `Unity.Mathematics`・`Unity.Collections` を参照しない | State は `Vector3` と配列で持ち、Adapter が NativeArray へ写す（部隊は数十） |
| 敵の Board | ない | `EnemyBoard` を作る |

## 詳細仕様で決めること

すべて推奨の案に決めた（2026-10-10、ユーザー）。

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | 命令と観測の State の形 | 書く側ごとに 2 つ（Single Writer）。命令 `EnemySquadCommandState`（Application が書く）と観測 `EnemySquadObservationState`（Adapter が書く）。中身は部隊ごとの構造体の配列で、部隊の番号はグループの番号と同じ。長さは有効なスポーン位置の数 |
| 2 | 判断の頻度 | 毎フレーム。部隊は数十で軽く、間隔を空けると切り替えが遅れて挙動が変わる為 |
| 3 | 外側の螺旋の目標位置の割り当て | 置き場の位置（立てる列へずらす計算）は Engine が求めて観測で公開し、どの部隊にどの置き場を渡すかは Application が決める。区間4G の撤退で置き場を手放す処理を Application が持つ為 |
| 4 | 区間9の行動の制御 | 区間4G 以降へ持ち越す。バリアの居座りも Engine の移動に残す |
| 5 | 観測を書くタイミング | `PlayerMovementService.Step` と同じ直接配線。Adapter の `Update` で、前のフレームの観測を書く → `EnemySquadService.Step` → 命令を読んで Job を Schedule、の順 |
| 6 | 範囲の外で止めた敵の落下 | 範囲に入って処理が戻ったときに落ちる形でよい |

観測は `Step` の引数で渡す案もあったが、決めたことの 15 と区間4G 以降の利用（バリア・フィニッシャーの判断）を見込んで State にした。

## 作業計画（2026-10-10）

### 新しく作る型

| 型 | 層 | 今の区間で使うもの |
|---|---|---|
| `EnemyBoard` | BlackBoard | 2 つの State の登録先 |
| `EnemySquadCommandState` | BlackBoard | `EnemySpawnAdapter`（読む）、デバッグ表示 |
| `EnemySquadObservationState` | BlackBoard | `EnemySquadService`（読む） |
| `EnemySquadService` | Application | `EnemySpawnAdapter` が `Step` を呼ぶ |

部隊の状態は `EnemyGroupState` を BlackBoard へ移して使う。

### コミットの分け方

| # | 内容 | 確かめ方 |
|---|---|---|
| 1 | `EnemyBoard` と 2 つの State を作り登録する。Adapter が観測を書く。挙動は変えない | コンパイル、観測の値が動くこと |
| 2 | `EnemySquadService` で待機・追跡・帰還の切り替え、到着、進めないときの新しい持ち場を決める。Job は命令を読み、`UpdateState`・`UpdateBlockedTime` の判断を消す | 切り替え、橋を切ったときの新しい持ち場 |
| 3 | 置き場の割り当てを Service へ移す。Engine は置き場の位置を観測で出す | 外側・内側の螺旋、待つ置き場 |
| 4 | 4F-4：範囲の外の待機中の部隊とメンバーの処理を飛ばし、帰還中の部隊は帰りの道筋をたどる | 範囲の外の帰還、負荷 |
| 5 | 弾・バリア・吸収の確認、計測、実装結果、全体計画書の更新 | 完了条件すべて |

## 見つけた問題（今回は扱わない）

- `EnemyGroupJob.MoveAnchor` のサマリーに、区間4E で消したレーンの待ち（「同じレーンの前で別のグループが待つ番」）が残っている

## 実装結果（2026-10-11）

### 決めたこと

- 「詳細仕様で決めること」の 1〜6（推奨の案）
- 範囲の外で待機する部隊を止めるのは、待機を始めてから「隊列の長さ ÷ 歩く速さ ＋ 1 秒」が経ってから。飛んでいる・落ちているメンバーがいる間は止めず、待った時間を 0 に戻す。止めた部隊でも、飛んでいる・落ちている途中の敵は動かす（フィニッシャーが飛んでいる途中で止まり、空中や隊列から 179m 離れた所に残った為）
- 置き場の位置は、距離マップの更新のあと、観測を書く前に `EnemyEncircleSlotJob` で求める。前のフレームの位置で割り当てると、ワープなどでプレイヤーが大きく動いたときに外側の置き場を受け取り、帰還まで持ち続けた為

### 作った主なもの

| 種類 | 内容 |
|---|---|
| BlackBoard | `EnemyBoard`、命令 `EnemySquadCommandState`（状態・持ち場・置き場の番号）、観測 `EnemySquadObservationState`（使われているか・アンカーの位置・人数・持ち場が追跡範囲にあるか・置き場に着いたか・持ち場に着いたか・進めないか、置き場の位置・周・角度と螺旋の中心）。`EnemyGroupState` を BlackBoard へ移した |
| Application | `EnemySquadService`：待機・追跡・帰還の切り替え、帰還の到着、進めないとき（5 秒）の新しい持ち場、置き場の割り当て |
| EngineAdapter | `EnemyGroupJob` は命令の状態へ切り替え（`ApplyCommand`）、観測を書き、範囲の外の待機を止める（`UpdateDormant`）。`EnemyEncircleSlotJob` が置き場の位置を求める。`EnemySpawnAdapter` の毎フレームの順番は「置き場の位置 → 観測 → `EnemySquadService.Step` → 命令をグループに写す → グループと敵の Job」 |
| Initialization | `EnemyInitializer` が Service を作り、Adapter より先に初期化して `Step` を渡す。`UsefulToolkitPersistentCompositor` を生成し直して `EnemyBoard` を登録 |

### 完了条件の確認結果

| 完了条件 | 結果 |
|---|---|
| 状態と目的地を決める処理が Application にあり、`EnemyGroupJob` に判断が残っていない | 確認済み。切り替え・持ち場・置き場の割り当ては `EnemySquadService` にある。Job に残るのは命令への切り替えと移動、観測の書き込み |
| 区間4D・9 の挙動を壊さない | 確認済み（uloop で状態を読んで確認）。待機・追跡・帰還の切り替え、帰還の到着、進めないときの新しい持ち場（アンカーを格子の外へ移して作った）、内側の螺旋の割り当てと到着、弾（最大 4 発）・バリア（最大 4 枚）・フィニッシャーの吸収・弾幕・帰還。待つ置き場と、使えなくなった置き場を手放す流れは、TestStage では使える置き場（64〜80）が追跡する部隊（8〜9）より多く作れず、確かめていない |
| 範囲の外では、待機中の部隊は処理されず、帰還中の部隊は帰りの道筋をたどって持ち場まで歩く | 確認済み。待機中の 72 部隊が止まり、帰還した部隊は持ち場に着いてから止まった。止めた部隊のメンバーは、アンカーから約 21m（隊列の長さ）以内。プレイヤーが戻ると、すぐに追跡を始めた |
| 敵の処理が合計 4ms 以下 | 確認済み。`_Crowd`・960 体で 1.1〜1.2ms、8 部隊が帰還中で 1.2〜1.8ms（エディタ、Jobs Debugger と Burst の安全チェックを切って測り、測ったあと戻した）。区間4E と同じく、体を貸したのは 1〜3 体で、32 体に貸した状態は作れなかった |

### 次の区間へ持ち越すこと

- 区間4G：撤退・補充・全滅後の出し直しを `EnemySquadService` に足す。範囲の外で止めた部隊（`IsDormant`）への補充の出し方を決める
- 区間4G 以降：攻撃するか、バリアを張るかを命令に入れること（詳細仕様の 4）。バリアの居座りは Engine の移動に残っている
- 帰還で持ち場に着いてから待機になるまでの 1 フレームの遅れは、見た目に出ないので残した
- レビュー：建物や穴の近くでの、待つ置き場と使えなくなった置き場の挙動
- 範囲の外の巡回と VAT の見た目、部隊の行動の種類を増やすこと（計画の時点から変わらない）

## 他プラットフォームへの対応

- プラットフォームによる違いはない

## 次の区間へ持ち越すこと（計画の時点）

- 区間4G：撤退・補充・全滅後の出し直しを、部隊の意思決定に足す
- 範囲の外の巡回と VAT の見た目（[EnemySquadRedesign.md](../EnemySquadRedesign.md) の「検討中の提案」）
- 陣形の切り替え、固定ルートの巡回など、部隊の行動の種類を増やすこと（使う区間が決まったら）
