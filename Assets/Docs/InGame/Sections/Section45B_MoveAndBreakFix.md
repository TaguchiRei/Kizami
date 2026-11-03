# 区間45B：移動と破壊の修正

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-30） |
| 目安の時期 | 未定 |
| 前提となる区間 | 40 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section45_Backlog.md](Section45_Backlog.md)（作業者向けの前提は親に書いてある。先に読む） |
| 扱う項目 | 45-13〜45-16 |

## 目的

プレイヤーの移動と破壊の手触りの問題を直す。ガラスが割れない、狭い所や小さいかけらでウォールランに入ってしまう、小さすぎるかけらができる、高い建物に上りにくい、の 4 つ。

## 確定した仕様（2026-10-25〜27、ユーザー）

| # | 内容 |
|---|---|
| 1 | 通常攻撃と砕剣（通常攻撃に破壊タイプが付いたもの）でも、ガラスを割る |
| 2 | 狭い場所や小さいかけらでは、ウォールランに入らない。地面が近いときも、壁に取りつかない |
| 3 | 一定より小さいかけらは、切り離さずに消す |
| 4 | F キーの飛び上がりを、高い建物の屋上にも使えるようにする。目印は、建物の最上階の四隅に建物が自動で置く（2026-10-27、ユーザー。視線の先の壁から屋上を探す案は、手触りがよくなくて取りやめた） |
| 5 | F キーの誘導は、飛び先の位置に出す。屋上だけでなく、塔の帯と倒れた塔の飛び先にも同じ表示を出す（2026-10-27、ユーザー） |
| 6 | F キーの飛び上がりは、滑らかに飛ばず、瞬間移動にする。屋上・塔の帯・倒れた塔のすべての飛び先で（2026-10-27、ユーザー） |
| 7 | チュートリアルの移動の次に、F キーで建物の屋上へ飛び上がる段階を足す。F で飛ぶまで次へ進まない（2026-10-30、ユーザー） |

## 既存コードの確認結果（2026-10-26）

| 場所 | 今の状態 |
|---|---|
| `GlassPane`（`EngineAdapterLayer/Stage/GlassPane.cs`） | 板はコライダーを持たず、空間グリッドに載る。割るのは `ShatterInSphere`・`ShatterInCapsule` の 2 つの static メソッドだけ |
| `GlassPane` を割る呼び出し元 | `VoxelDestructionAdapter`（スキル）、`EnemyFinisherBeams`（ビーム）、`PlayerMovementAdapterBase.UpdateContacts`（体が触れたとき）、`VoxelBuilding`（倒壊）。`MeleeCutAdapter`（通常攻撃・砕剣）は一度も呼ばない。これが割れない原因 |
| `MeleeCutAdapter.CollectTargets`（`EngineAdapterLayer/Player/MeleeCutAdapter.cs` 211 行〜） | 振りの範囲は、カメラの位置から正面へ `_reach` 伸びる、幅 `_width`・厚さ `_thickness` の向きを持った箱（`Physics.OverlapBoxNonAlloc`） |
| ウォールランに入る条件（`PlayerMovementService.CanEnterWallRun`、`Application/Player/PlayerMovementService.cs` 387 行） | 空中で壁に触れ（`PlayerContact.Wall`）、前の入力があり、壁から離れる向きでなく、持ち時間が残っているとき。地面までの距離と壁の大きさは見ていない |
| 壁の判定（`PlayerMovementAdapterBase.TryFindWallNormal`・`IsWall`、351〜394 行） | カプセルを 8 方向へ飛ばし、法線がほぼ水平な面を壁とする。`_wallLayers` のレイヤーか、どんな大きさの `VoxelPiece` でも壁にする |
| `PlayerContact`（`BlackBoardLayer/Player/Runtime/PlayerContactState.cs`） | `Ground`・`Wall` の 2 つの印 |
| かけらの切り離し（`VoxelPiece`、1180〜1205 行と 1238〜1243 行） | 削られて分かれた塊は、内側のサンプル数が `_minPieceSamples`（既定 8）以上なら別のピースにし、少なければ消す。サンプル数の基準なので、ボクセルの大きさ（品質設定で 0.03〜0.4m）によって、消える大きさが 1 万倍以上違う |
| 飛び移り（`FlightAdapter`・`FlightService`・`FlightAnchor`） | `FlightAdapter` が視線の先の `FlightAnchor` を探し、`FlightTarget`（着地点・削るか・余裕の高さ）を `FlightService.SetCandidate` へ渡す。目印は天照塔の帯と、倒れた塔の面にだけある |
| F キー | 普段はスローモード。飛び移れる候補があるときだけ、`FlightService.TryFly` が先に受け取る |
| `VoxelBuilding`（`EngineAdapterLayer/Stage/VoxelBuilding.cs`。2026-10-27 追加） | 建物は、`VoxelBuilding` を付けたプレハブ。階の構成は `_sections`（モデルは ScriptableObject の `VoxelModelAsset`）。階は `Awake` で作り、`Start` の `InitializeFloors` で手付かずの形のメッシュにして、階ごとの範囲（`_floorBounds`）を測る。倒れ始めは `Collapse` |
| `StageHudAdapter`（`EngineAdapterLayer/Stage/StageHudAdapter.cs`。2026-10-27 追加） | F の誘導（`FlightPrompt`）は画面の固定位置に出す。`IFlightPromptState` には出すかどうかだけがあり、飛び先の位置はない |

## 作業計画

| コミット | 項目 | 内容 | 確かめること |
|---|---|---|---|
| 1 | 45-15 ガラス | `GlassPane` に、向きを持った箱で割る static メソッドを足す（今の `ShatterInCapsule` と同じく、箱が通るセルの板だけを調べる）。`MeleeCutAdapter` の振りで、`CollectTargets` と同じ箱で呼ぶ。通常攻撃と砕剣の両方で呼ぶ | 通常攻撃と砕剣で、刃の範囲の窓が割れ、破片と音が出る。範囲の外の窓は割れない |
| 2 | 45-13 ウォールラン | 下の「ウォールランの条件」のとおり | 地面の近くで壁に寄ってもウォールランに入らない。小さいかけらに触れても入らない。ビルの壁では今までどおり入れる。壁キックも今までどおり |
| 3 | 45-14 小さいかけら | 切り離す下限を、サンプル数から体積（m³）に変える。下限は `VoxelQualitySettings` に `_minPieceVolume` として持つ（品質設定ごとに 1 か所で決められる為）。`VoxelPiece` の 2 か所の比較を `サンプル数 × VoxelCubicVolume >= 下限` にする。`_minPieceSamples` は消す（アセットの値も消える） | 建物を壊したとき、細かいかけらが出ない。今まで出ていた大きさのかけらは今までどおり出る。消える量で、壊した跡の見た目がおかしくならない |
| 4a | 45-16 屋上の目印 | 下の「屋上の目印」のとおり。`VoxelBuilding` が最上階の四隅に目印を置き、`FlightAnchor` に屋上用を足す | 高い建物の屋上の隅を見ると F の誘導が出て、押すと屋上に降りる。低い建物では出ない。倒れ始めた建物の目印は出ない。天照塔の帯と倒れた塔の目印は今までどおり |
| 4b | 45-16 飛び先の表示 | `FlightPromptState` に飛び先の位置を足し、`StageHudAdapter` が飛び先の位置に誘導を出す | 屋上・塔の帯・倒れた塔のどれでも、誘導が飛び先に重なって出る。カメラの後ろでは出ない |
| 4c | 45-16 瞬間移動 | 下の「瞬間移動」のとおり | F で、屋上・塔の帯・倒れた塔のどれでも、一瞬で飛び先に移る。壁走り中に押しても、飛び先で空中に止まらず落ちる。塔の帯で、削る前の壁にめり込まない |
| 5 | チュートリアルの F の出来事 | `PlayerActionKind.Flew` を足し、`FlightService` が飛んだ直後に流す。コンストラクタに `PlayerActionEvents` を足し、`PlayerInitializer` で渡す | コンパイルが通る。F で飛ぶと通知が流れる |
| 6 | チュートリアルの F の段階 | 台本（`Stage_Tutorial` の `StageGuideScript`）の番号 1 に、条件 `PlayerAction`・`Flew`（待ち 1 秒）の段階を、文（`TutorialGuideTexts`）の同じ番号に案内を挿入する。登らせる建物は、開始位置（110, 0.5, 0）の南東にある `Town_Glass`（位置 (122.7, -24.6)、概算 40m）。台本に建物の指定は持たない | プレイして、移動のあとに F の案内が出る。屋上の隅を見て F で飛ぶと、1 秒後に次（ウォールランの説明）へ進む |
| 7 | 滑らかな飛行の削除 | 瞬間移動にしたので使われなくなった、`RequestFlight`（`PlayerMovementService` とインターフェース）、追従の飛行（`_followFlight*` と `Step` の分岐）、`FlightTarget.ApproachClearance`、`FlightAnchor._fallenTowerClearance`、`PlayerParameterData._flightSpeed` を消す | コンパイルが通る。参照が残っていない。F の瞬間移動が今までどおり動く |

### ウォールランの条件

- 地面が近い：`PlayerContact` に「地面が近い」の印（名前の案：`NearGround`）を足す。`PlayerMovementAdapterBase.UpdateContacts` で、足元から下へ `_wallMinGroundClearance`（新しい SerializeField。案は 1.5m）以内に地面があれば付ける。`PlayerMovementService.CanEnterWallRun` は、この印があれば入らない。壁走り中の判定には使わない（壁走りから抜ける条件は今のまま）
- 小さいかけら：`IsWall` で、`VoxelPiece` の範囲の高さが `_minWallPieceHeight`（案は 2m）より小さければ壁にしない
- 狭い場所：まず実機で再現し、どの面に触れて入っているかを報告する。上の 2 つで直らなければ、直し方を提案して止まる（例：反対向きの壁に同時に触れているときは入らない）
- 値はコミットのあとに実機で調整する

### 屋上の目印

- `VoxelBuilding` が `Start` で、最上階の上面の四隅（階の範囲を水平方向に内側へ `_roofAnchorInset`（案 3m）寄せた点）に、屋上用の `FlightAnchor` を最上階の子として置く。真上から最上階のコライダーへ真下にレイを飛ばし、上向きの面に当たった角にだけ置く
- `FlightAnchor.ConfigureAsRoof` で、飛び先を「面の点 ＋ `_roofAnchorLift`（案 2m）」にする。ボクセルは削らない
- 目印は、飛び先がカメラより `_roofAnchorMinHeightAbove`（案 10m）以上高いときだけ選ぶ（`FlightAnchor.MinHeightAbove`。塔の目印は制限なし。`FlightAdapter` が見る）
- 屋上の目印は、カメラからの水平な距離（XZ 平面）が `_roofAnchorMaxHorizontalDistance`（35m。2026-10-27 に 20m で入れ、2026-10-28 に 35m へ伸ばした。ユーザー）以内のときだけ選ぶ（`FlightAnchor.MaxHorizontalDistance`。塔の目印は無制限）
- 屋上の目印は、視線の先とみなす角度を、`FlightAdapter._aimAngle`（8 度）の `_roofAnchorAimAngleScale`（2 倍 ＝ 16 度。2026-10-30、ユーザー）にする（`FlightAnchor.AimAngleScale`。塔の目印は 1 倍）。壁を見たままでも選ばれやすくする
- 壁越しに触れられない判定（カメラから飛び先までの遮る物）は行わない。一度入れたが、安定しなかったので水平距離だけにした（2026-10-27、ユーザー）
- 建物が倒れ始めたら（`Collapse`）、屋上の目印を無効にする
- 屋上の目印も、塔の目印と同じく、視線との角度が最も小さいものを選ぶ（`FlightAdapter`）
- 新しい型は作らない

### 飛び先の表示

- `FlightPromptState` に `TargetPosition` を足し、`FlightService.SetCandidate` が書く
- `StageHudAdapter` が、飛び先のビューポート座標（`Camera.WorldToViewportPoint`）をアンカーにして、`[F] JUMP` を飛び先の位置に出す。カメラの後ろにあるときは出さない。画面の外にあるときは端に寄せる
- 塔の帯と倒れた塔の飛び先も、同じ表示になる
- VR の表示は扱わない

### 瞬間移動

- `FlightService.Fly` が、飛び先と体の中心の差を `PlayerMovementAdapterBase.PlaceAt`（Transform と Rigidbody を両方書き、速度を止める。補間が前の位置から滑らない）へ渡して移動する。`PlayerInitializer` が、`PlaceAt(Position + 移動量)` を `FlightService` へ渡す
- 移動のあと `IPlayerMovementController.OnTeleported` を呼ぶ。`PlayerMovementService` が壁走りを抜けて通常の移動に戻し、壁から離れるまで再突入を止める。壁走りのまま空中に止まるのを防ぐ
- 塔の帯は、今までどおり移動の前に周囲のボクセルを削る
- 新しい型は作らない。使われなくなった滑らかな飛行の仕組みは、コミット 7 で消した

## 完了条件

- 通常攻撃と砕剣でガラスが割れる
- 地面の近くと小さいかけらでは、ウォールランに入らない
- 細かすぎるかけらが出ない
- 高い建物の屋上へ F で上がれる
- チュートリアルで、移動のあと、F で屋上へ飛ぶまで次へ進まない

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | F キーの取り合い | 屋上の隅が視線の先にある間は、F がスローモードではなく飛び上がりになる。実機で困らないかユーザーが確かめる。困るなら、屋上の目印だけ、選ぶ角度か距離を小さくする |
| 2 | かけらの下限の体積 | 品質設定ごとに、今のかけらの大きさを見て決める。作業者が品質設定ごとのボクセルの大きさと、今の 8 サンプルの体積を表にして提案する |
| 3 | 地面までの距離と壁の高さの値 | 1.5m と 2m から始め、実機で調整する |
| 4 | 屋上の目印の値 | 内側へ寄せる 3m、持ち上げ 2m、カメラより 10m 以上高いとき、水平距離 35m 以内（20m から伸ばした）、視線の許容角 16 度（8 度の 2 倍）、から始め、実機で調整する |

## 実装結果（2026-10-30）

### 決めたこと

| # | 内容 |
|---|---|
| 1 | ウォールランは、足元から 1.5m 以内に地面があれば入らない。ボクセルのピースは、高さが 2m 未満なら壁にしない。狭い場所は、この 2 つで問題ないとユーザーが確かめた |
| 2 | かけらを切り離す下限は、品質設定ごとの体積（m³）。Building は 0.768（0.512 の 1.5 倍）、Terrain・Boss・Mobile・PC は 0.064 |
| 3 | 屋上は、建物が最上階の四隅に目印を置く。選ぶのは、水平距離 35m 以内、カメラより 10m 以上高い、視線との角度が 16 度以内（8 度の 2 倍）のとき。壁越しの遮蔽判定は入れない |
| 4 | F の誘導は飛び先の位置に出し、飛び上がりは全部の飛び先（屋上・塔の帯・倒れた塔）で瞬間移動にする |
| 5 | チュートリアルに、移動の次に、F で屋上へ飛ぶ段階を足す（登らせる建物は `Town_Glass`） |

### 作った主なもの

| 場所 | 内容 |
|---|---|
| `GlassPane`、`MeleeCutAdapter` | `ShatterInBox`（向きを持った箱で割る）。`MeleeCutAdapter` の `GetCutBox` が、切断の範囲と同じ箱を渡す |
| `PlayerContact`、`PlayerMovementAdapterBase`、`PlayerMovementService` | `NearGround`、`IsGroundWithin`、`_wallMinGroundClearance`、`_minWallPieceHeight`、`CanEnterWallRun` の `!IsNearGround`、`OnTeleported` |
| `VoxelQualitySettings`、`VoxelPiece` | `_minPieceVolume`。`_minPieceSamples` は消した |
| `VoxelBuilding`、`FlightAnchor`、`FlightAdapter` | `CreateRoofAnchors`（屋上の四隅の目印）、`ConfigureAsRoof`、`MinHeightAbove`、`MaxHorizontalDistance`、`AimAngleScale` |
| `FlightPromptState`、`FlightService`、`StageHudAdapter` | `TargetPosition`、`PlaceAt` での瞬間移動、`Flew` の通知、`UpdateFlightPrompt`（飛び先の位置への表示） |
| `PlayerActionKind`、`Stage_Tutorial`、`TutorialGuideTexts` | `Flew`。台本と文の番号 1 に F の段階 |
| 消したもの | `RequestFlight`、追従の飛行、`FlightTarget.ApproachClearance`、`FlightAnchor._fallenTowerClearance`、`PlayerParameterData._flightSpeed` |

### 完了条件の確認結果

- ガラス、ウォールラン、かけらは、各コミットのあとに、ユーザーが実機で確認した
- 屋上の目印、飛び先の表示、瞬間移動、チュートリアルの F の段階は、距離（20m → 35m）と許容角（8 度 → 16 度）を調整したうえで、2026-10-30 にユーザーが確認した
- 個別に確かめた記録がないもの：塔の帯への瞬間移動のめり込み、壁走り中に F を押したときの動き（下の「見つけた問題」）

### 次の区間へ持ち越すこと

- 下の「見つけた問題」の 4 件

### 使い方

- 高い建物は、最上階の四隅に屋上の目印を自動で持つ。目印が選ばれる条件は、建物プレハブの `VoxelBuilding` の「屋上の目印」の項目で変えられる（内側へ寄せる距離、持ち上げ、カメラより高い距離、水平距離、許容角の倍率）
- 視線の基準の角度と距離は、`FlightAdapter` の `_aimAngle`（8 度）と `_range`（400m）

## 見つけた問題（今回は扱わない）

- 塔の帯への瞬間移動は、削る処理（`VoxelDestructionAdapter.CarveExplosionAt`）と同じフレームに体を動かす。削ったあとのコライダーは `VoxelPiece` の再メッシュ（1 フレームに `RemeshChunksPerFrame` チャンクずつ）で作り直される為、すぐは反映されないおそれがある。個別には確かめていない。めり込んで押し出されたら、削ってから数フレーム待って移動する
- 最上階だけが削られて屋上が無くなっても、屋上の目印は残る。無効にするのは建物が倒れ始めたときだけ（`VoxelBuilding.DisableRoofAnchors`）。直すなら、飛び先の候補にするときに、真下に面があるかを確かめる
- 壁走り中に F を押したときの動き（`OnTeleported` で壁走りを抜けて落ちる想定）は、個別に確かめた記録がない
- 消したフィールドの値が、シーンとプレハブに残っている（Unity は無視し、開いて保存し直すと消える）：`_minPieceSamples`（`VoxelMoundPiece.prefab`・`VoxelTest.unity`）、`_fallenTowerClearance`（`Stage_Tower_Tensho_Upper_01.prefab` の 4 か所）
- Console に `AudioVolumeAdapter.Apply`（`AudioVolumeAdapter.cs:56`）の `NullReferenceException` が 1 件出た。45B の変更とは関係がないと見ているが、切り分けていない
