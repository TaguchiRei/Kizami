# 区間34：プレイヤーの挙動の修正とスキルの範囲

| 項目 | 内容 |
|---|---|
| 状態 | 実装済み（2026-10-21。完了条件の「到達で各スキルが天照塔を倒せる」は、到達をステージ選択から外したので、戻すときまで持ち越し） |
| 目安の時期 | 未定 |
| 前提となる区間 | 32 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

アルファ版の仕上げのうち、手触りの土台になるプレイヤーの挙動を先に直す。腕のアニメーションやヒットストップ（38）、音（36）は挙動の上に乗るので、この区間を最初に置く。

## 確定した仕様（2026-10-21、ユーザー）

| # | 内容 |
|---|---|
| 1 | 小さな段差や急な坂（ディフェンダーのシールド、天照塔の足元など）に体を押し付けたり引っかけたりしても、空へ飛ばない。原因は、コライダーとのめり込みを解消する押し戻しで上向きの速度が生まれ、それを縦の速度としてそのまま引き継いでいること（ユーザー確認済み） |
| 2 | 地上の移動は、目標の速度へ即座に合わせる（慣性なし）。入力と同時に最高速になり、放すとその場で止まる |
| 3 | 空中の方向の制御は、地上の半分程度の効き。ジャンプや壁キックの勢いは残しつつ、空中でも曲がれる |
| 4 | ジャンプは、上がるときも落ちるときも素早く動く。最高点の少し手前から落ち始めた直後までだけ、ふわっとした動きにする |
| 5 | ジャンプボタンの長押しで高さを調節する。長押しは 0.5 秒で最大になり、最大の高さは最小の約 2 倍（押している間は上昇の重力を弱める） |
| 6 | 壁キックは、壁の法線に関係なく、向いている方向（カメラの水平の向き）へ跳ぶ。壁のほうを向いているときは、壁へ向かう成分だけ法線で外へ折り返す。距離は今の約 2.5 倍で、大通りの反対側まで余裕をもって届く。高さは通常のジャンプと同じく長押しで変わる |
| 7 | 体術・衝波、体術・攻走、火術・崩爆の範囲を、大きさは変えずに中心をプレイヤーの正面へずらす |
| 8 | 火術・崩爆は、爆弾を投げる距離を今の約 3 倍にし、爆発の規模（半径）を約 2 倍にする |
| 9 | スキル名に「体術・」などの系統を含める。系統はスキルのデータで enum の選択式にし、表示名は「系統・名前」で組み立てる。アウトゲームの表示にも使う想定 |
| 10 | 最終ステージ（到達）の破壊力の上乗せを、すべてのスキルに効かせ、どのスキルでも天照塔を倒せるようにする。通常攻撃の切断と土術・造山は対象外で、変えない。アウトゲーム区間5 の決定 12（衝波・攻走には上乗せを使わない）を変える |
| 11 | 壁走りにダッシュの要素を入れる。速度はダッシュ時（`_wallRunSprintSpeed`）と非ダッシュ時（`_wallRunWalkSpeed`）で分ける（2026-10-21、ユーザー） |
| 12 | 壁走りの途中で壁が途切れたら、壁走りのままその場にとどまる。持ち時間の残りの間とどまり、ジャンプで最後に触れていた壁の法線を使った壁キックができる。持ち時間が尽きるか、壁から離れる入力が続くか、着地で抜ける（2026-10-21、ユーザー。案 A） |
| 13 | 空中の水平方向の効きは、地上の半分より弱くする。空中の加速度は 25 m/s²（地上の最高速 25 m/s に約 1 秒で届く）。入力がないときは水平速度を保つ（2026-10-21、ユーザー。仕様 3 を変更） |
| 14 | ジャンプでない空中（足場から歩いて落ちたとき）は、最初から下降中の倍率で落ちる。ジャンプの下降は、落ちる速さに応じて最高点の前後の倍率から下降中の倍率へ緩やかに近づける。最高点の前後の停滞は約 0.5 秒（2026-10-21、ユーザー） |

## 既存コードの確認結果（2026-10-21）

| 対象 | 今の状態 | 対応 |
|---|---|---|
| `PlayerMovementAdapterBase.FixedUpdate`（185〜207 行） | 水平速度を `Vector3.MoveTowards` で目標へ近づける。レートは地上・壁走りで加速 40 / 減速 60、空中で 10 / 10（m/s²、コードの既定値）。縦の速度は `_rigidbody.linearVelocity.y` をそのまま引き継ぐ | 仕様 1〜3 |
| `PlayerMovementAdapterBase`（162〜182 行） | 重力は Rigidbody の `useGravity`（壁走りとワープの間は切る） | 仕様 4・5：重力を自前で加える |
| `PlayerMovementService.CalculateJumpSpeed`（397 行） | 初速は `√(2 × 重力 × JumpHeight)`。重力 9.81、`JumpHeight` はアセットで 4m なので、初速 約 8.9 m/s・最高点まで約 0.9 秒。押した瞬間に 1 回だけ初速を与え、長押しは見ていない | 仕様 4・5 |
| `PlayerMovementService.CalculateWallJumpVelocity`（359 行〜） | 水平の向きは、壁へ押し込む成分を除いた入力と壁の法線を `WallJumpInputInfluence` の割合で混ぜる。水平の速さは `WallJumpHorizontalSpeed`（アセットで 10） | 仕様 6 |
| `SkillService`（140〜155 行） | スキルの種類ごとに `skill.Radius` / `Length` / `Distance` を Adapter へ渡す | 仕様 7・8 |
| `VoxelDestructionAdapter.CarveSweep`（175 行） | 衝波。カメラの正面に水平のカプセルで削る | 仕様 7 |
| `SkillData`（ExternalLayer） | 表示名は文字列の `_displayName` だけで、系統の enum はない | 仕様 9 |
| `VoxelDestructionAdapter.SetMaxPower` / `ReachRadius`（76〜87 行） | 到達では `StageInitializer` が `SetMaxPower(true)` を呼び、`_powerBonus` を届く距離に足す。狙い・ビーム・爆発（`ReachRadius` を通るもの）にだけ効く | 仕様 10 |
| `VoxelDestructionAdapter.CarveCapsuleWithArmor`（208 行） | 衝波・攻走。届く距離を `NORMAL_MELTING_POINT / falloff` で計算し、`_powerBonus` を足していない | 仕様 10：`ReachRadius` を通す |

## 作業一覧

| # | 作業 | 内容 |
|---|---|---|
| 34-1 | 空へ飛ぶ不具合 | めり込みの押し戻しで生まれた上向きの速度を、ジャンプ以外では引き継がない |
| 34-2 | 移動の応答 | 地上は目標速度へ即座に合わせ、空中は地上の半分程度の効きにする |
| 34-3 | ジャンプの軌道 | 重力を自前で加え、上昇・頂点付近・下降で倍率を変える。長押しの間は上昇の重力を弱める |
| 34-4 | 壁キック | 向いている方向へ跳ぶ。距離を約 2.5 倍にし、大通りで届くかを確かめる |
| 34-5 | スキルの範囲と崩爆 | 衝波・攻走・崩爆の中心を正面へずらす。崩爆の投げる距離を約 3 倍、半径を約 2 倍にする |
| 34-6 | スキルの系統 | `SkillData` に系統の enum を足し、表示名を「系統・名前」で組み立てる |
| 34-7 | 最終ステージの上乗せ | 衝波・攻走にも上乗せを効かせる。到達で、各スキルだけで天照塔を倒せるかを試す |

## 完了条件

- シールドと天照塔の足元に体を押し付けても、空へ飛ばない
- 地上では入力と同時に動き出し、放すとその場で止まる。空中でも曲がれる
- ジャンプが素早く上がって素早く落ち、最高点の前後だけふわっとする。長押しの長さ（0.5 秒まで）で、高さが最大で約 2 倍変わる
- 壁キックで向いている方向へ跳び、大通りの反対側まで余裕をもって届く
- 衝波・攻走・崩爆を正面に撃ったとき、正面の物が今より多く巻き込まれる。崩爆は今より遠くへ大きく爆発する
- HUD とスキル装備の画面に、スキル名が「体術・衝波」の形で出る
- 到達で、通常攻撃と造山以外のどのスキルを装備しても天照塔を倒せる

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 中心を正面へずらす量 | スキルごとに `SkillData` に値を持たせ、プレイしながら決める |
| 2 | 天照塔を倒すのにかかる時間の目安 | 制限時間に対して、どのスキルでも余裕をもって倒せる程度。34-7 で試して決める |

## 作業計画（2026-10-21）

実装は別のセッション（Sonnet）が行い、レビューはこの計画を立てたセッションが行う。実装するセッションは、着手前に CLAUDE.md、`.claude/rules/csharp-conventions.md`、`.claude/rules/unity-compile.md` を読む。コミットの区切りごとにコンパイルし、プレイモードで確認してから止まって報告する。

### 方針

- 縦の速度は Rigidbody の値を正本にせず、Adapter が自分で持つ `_verticalVelocity` を正本にする。Rigidbody の重力（`useGravity`）は使わず、Adapter が毎ステップ重力を足す。Rigidbody の縦の速度は、天井にぶつかったとき（自分の値より小さいとき）だけ取り込む。押し戻しで生まれた上向きの速度は取り込まないので、仕様 1 がこれで直る
- ジャンプの軌道（上昇・頂点付近・下降の重力の倍率）は見た目と手触りの値なので、Adapter の SerializeField に置く。長押し中かどうかはゲームのルールなので、`PlayerMovementService` が判定して `PlayerMovementState` に書き、Adapter が読む
- 新しい型は作らない。既存の `PlayerMovementAdapterBase`、`PlayerMovementService`、`PlayerMovementState`、`PlayerParameterData`、`SkillData`、`SkillService`、`VoxelDestructionAdapter`、`BombThrowAdapter`、`PlayerInitializer` を直す。系統の enum（`SkillSchool`）だけ `SkillData.cs` に足す

### コミット 1：縦の速度を自分で持つ（仕様 1）

| ファイル | 変更 |
|---|---|
| `PlayerMovementAdapterBase` | `_verticalVelocity` を足す。`useGravity` は常に false。通常時は `_verticalVelocity += Physics.gravity.y × dt`。打ち出し速度があればその y で上書き。接地していて `_verticalVelocity < 0` なら 0 にする。壁走り中とワープ終了のステップは 0。`_rigidbody.linearVelocity.y < _verticalVelocity` のとき（天井などで止められた）だけ Rigidbody の値を取り込む。ワープ中・Aiming 中・`PlaceAt` では 0 に戻す |

確認：シールドの側面と天照塔の足元に体を押し付けながら歩いても、空へ飛ばない。普通のジャンプの高さが変わらない（重力の倍率はまだ 1）。坂を上り下りできる。

### コミット 2：地上は即応、空中は半分の効き（仕様 2・3）

| ファイル | 変更 |
|---|---|
| `PlayerMovementAdapterBase` | 接地中と壁走り中は `_horizontalVelocity = target`。`_acceleration` / `_deceleration` は消す。空中は `_airAcceleration` で `MoveTowards`。空中で入力がない（target が 0）ときは減速せず、今の水平速度を保つ（決めること #3）。`_airDeceleration` は消す |

確認：地上で入力と同時に最高速になり、放すとその場で止まる。空中で入力を変えると曲がれる。`_airAcceleration` の値はプレイしながら決める（地上の最高速に約 0.2 秒で届く程度から始める）。

### コミット 3：ジャンプの軌道と長押し（仕様 4・5）

| ファイル | 変更 |
|---|---|
| `PlayerParameterData` | `_jumpHeight`（最大の高さ）に加え、`_minJumpHeightRatio`（0.5）と `_jumpHoldTime`（0.5 秒）を足す |
| `PlayerMovementService` | `OnJump` で Canceled も受け、押している間を記録する。ジャンプ（壁キックを含む）の時から押し続けている間、かつ `_jumpHoldTime` 以内なら「長押し中」として `PlayerMovementState` に書く。着地・壁走り・ワープで解く |
| `PlayerMovementState` | 長押し中かどうかのプロパティと Setter を足す（Getter インターフェースにも足す） |
| `PlayerMovementAdapterBase` | 重力の倍率を SerializeField で持つ：上昇中 `_riseGravityScale`、長押し中の上昇 `_holdRiseGravityScale`、頂点付近 `_apexGravityScale`（`|縦の速さ| < _apexSpeedThreshold` のとき）、下降中 `_fallGravityScale`。毎ステップ、該当する倍率を重力に掛ける |

初速は「長押しを最後まで続けたときに `_jumpHeight` に届き、押してすぐ放したときに `_jumpHeight × _minJumpHeightRatio` になる」ように、倍率から逆算して決める。逆算が複雑になるなら、初速を固定して倍率を調整する形でよい（どちらにしたかを報告する）。

確認：押してすぐ放すと低く、0.5 秒押すと約 2 倍の高さになる。上昇と下降が今より速く、頂点の前後だけ緩やかになる。値の調整はレビュー時にユーザーと行う。

### コミット 4：壁キック（仕様 6）

| ファイル | 変更 |
|---|---|
| `PlayerMovementService.CalculateWallJumpVelocity` | 水平の向きを `_viewForward` にする。`Dot(_viewForward, wallNormal) < 0` なら `Vector3.Reflect(_viewForward, wallNormal)` にする。入力との混ぜ合わせはやめる |
| `PlayerParameterData` | `_wallJumpInputInfluence` を消す。`PlayerParameterData.asset` の `_wallJumpHorizontalSpeed` を 10 → 25 にする |

確認：壁走り中に向いている方向へ跳ぶ。壁のほうを向いて跳ぶと外へ折り返す。大通りの壁から跳んで、反対側の壁まで届く（届かなければ値を上げて報告）。長押しで高さが変わる。

### コミット 5：スキルの範囲と崩爆（仕様 7・8）

| ファイル | 変更 |
|---|---|
| `SkillData` | `_forwardOffset`（m、既定 0）を足す |
| `SkillService` | `_onBomb`・`_onSweep`・`_onRush` に `skill.ForwardOffset` を渡す |
| `VoxelDestructionAdapter.CarveSweep` | カプセルの中心を `forward × forwardOffset` だけ前へずらす |
| `PlayerInitializer.Rush` | 削るカプセルの両端を `direction × forwardOffset` だけ前へずらす（突進の移動距離は変えない） |
| `BombThrowAdapter.Throw` | 4 つの爆弾を投げる起点を `forward × forwardOffset` だけ前へずらす |
| `Skill_Bomb.asset` | `_distance` 5 → 15、`_radius` 7.5 → 15 |

`_forwardOffset` の値は、衝波は奥行きの半分、攻走は半径、崩爆は投げる距離の半分から始め、プレイして決める（決めること #1）。

確認：3 つのスキルで、正面の物が今より多く削れる。崩爆が遠くに大きく爆発する。

### コミット 6：スキルの系統（仕様 9）

| ファイル | 変更 |
|---|---|
| `SkillData.cs` | `SkillSchool` enum（体術・刃術・土術・火術・火術奥義）と `_school` を足す。`DisplayName` は「系統・名前」を返す。系統の表示文字列は enum から引く。`_displayName` の中身から「体術・」などの部分を消す（アセット側） |
| 各 `Skill_*.asset` | `_school` を設定する。系統を持たないスキル（テスト用など）のために `None` を用意し、そのときは名前だけを返す |

確認：HUD・スキル装備の画面・解放の通知で、名前が「体術・衝波」の形で出る。

### コミット 7：最終ステージの上乗せ（仕様 10）

| ファイル | 変更 |
|---|---|
| `VoxelDestructionAdapter.CarveCapsuleWithArmor` | 届く距離を `ReachRadius(radius, falloff)` で求める。サマリーの「上乗せは使わない」を消す |

確認：到達で、衝波だけ・攻走だけ・崩爆だけ（ほかのスキルも同様）を装備して天照塔を倒せる。時間を計って報告する（決めること #2）。

### 決めること（作業計画で足したもの）

| # | 項目 | 案 |
|---|---|---|
| 3 | 空中で入力がないとき | 確定（2026-10-21、ユーザー）：水平速度を保つ。減速させると壁キックの勢いが空中で消え、大通りを越えられない為 |

## 実装結果（2026-10-21）

### 決めたこと

- ジャンプの初速は固定（`JumpHeight` を、重力の倍率 1 で届く高さの意味に変えた。アセットは 4 のまま、初速 約 8.86 m/s）。高さの変化は Adapter の重力の倍率で作る。倍率から逆算する方式は、Application と Adapter が互いの値を知る必要があるので避けた
- 重力の倍率の既定値：上昇 2.0、長押し中の上昇 0.8、最高点の前後 0.6（縦の速さが 1.5 m/s 未満）、下降 2.0、下降の倍率に届くまでの落下の速さの幅 12 m/s。長押しは `JumpHoldTime`（0.5 秒）まで
- 壁キックは水平の速さ 25（`_wallJumpHorizontalSpeed`）。`_wallJumpInputInfluence` は消した
- 壁走りの速度を `_wallRunWalkSpeed`（30）と `_wallRunSprintSpeed`（37.5）に分けた。壁が途切れても壁走りのままその場にとどまり、最後に触れていた壁の法線（`_wallNormal`）で壁キックできる。抜けるのは、持ち時間が尽きる、離れる入力が続く、着地のいずれか
- 中心を正面へずらす量（`SkillData._forwardOffset`）：衝波 5、攻走 5、崩爆 7.5。崩爆は距離 15・半径 15
- スキルの系統は `SkillSchool`（体術・刃術・土術・火術・火術奥義）。表示名は「系統・名前」。アセットの `_displayName` には元から系統の文字がなかったので、削る作業は不要だった。系統は、衝波・攻走＝体術、砕剣・壊剣＝刃術、造山＝土術、崩爆＝火術。試作や強化型は `None`
- 衝波・攻走の届く距離を `ReachRadius` に通した（到達の上乗せが効く）

### 作った主なもの

- 縦の速度を Adapter が持つ（`PlayerMovementAdapterBase._verticalVelocity`）。押し戻しで生まれた上向きの速度は取り込まない
- `PlayerMovementState.IsJumpHolding`、`PlayerParameterData.JumpHoldTime`
- `SkillSchool`、`SkillData.ForwardOffset`

### 完了条件の確認結果

| 条件 | 結果 |
|---|---|
| 押し付けても空へ飛ばない | ユーザーが確認 |
| 地上は即応、空中は曲がれる | ユーザーが確認（空中は弱める方向に調整） |
| ジャンプの軌道と長押し | ユーザーが確認（「今の感覚でよい」） |
| 壁キックで向いている方向へ | ユーザーが確認 |
| スキルの範囲が正面へ、崩爆が遠くへ大きく | ユーザーが確認 |
| スキル名が「体術・衝波」の形で出る | ユーザーが確認 |
| 到達で、どのスキルでも天照塔を倒せる | 未確認（到達をステージ選択から外した。コードは入っている） |

### 計画の外で入ったもの

- **F キーの飛び移り**：天照塔の足場として、板を置く案から変えた。視線の先に飛び移れる目印（`FlightAnchor`）があると HUD に「[F] JUMP」が出て、F で飛ぶ。F はスローモードと同じ入力なので、目印があるときだけ先に飛び移りが受ける。帯の目印は、少し上かつ奥へ着地し、飛ぶ瞬間にその周囲を先に壊す。倒れた塔の目印は、塔の側面（`TowerFace`）の子にあらかじめ置いておき、倒れて止まったとき真上を向いた面の目印だけを `TowerTopple` が解放する。置き方はエディタの `Kizami/Stage/Build Tower Flight Landings`。新しい型は `FlightAnchor`、`FlightAdapter`、`FlightService`、`FlightPromptState`、`FlightTarget`
- **到達ステージをステージ選択から外した**：挙動が安定しなかったため（2026-10-21、ユーザー）。ステージのアセットとシーンは残してある。戻すときは、常駐シーンの `GameSceneInitializer._stages` に `ArrivalStageData` を足す

### 次へ持ち越すこと

- 到達を戻すとき：飛び移りの動作確認（帯への着地、倒れたあとの橋への着地）、および完了条件の「どのスキルでも天照塔を倒せる」の確認と、倒すまでの時間（決めること #2）
- 実機でのジャンプ・壁キック・スキルの範囲の値の最終調整（レビューのとき）
