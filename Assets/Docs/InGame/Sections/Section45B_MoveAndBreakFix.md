# 区間45B：移動と破壊の修正

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（計画済み 2026-10-26。着手時に「既存コードの確認結果」を見直す） |
| 目安の時期 | 未定 |
| 前提となる区間 | 40 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section45_Backlog.md](Section45_Backlog.md)（作業者向けの前提は親に書いてある。先に読む） |
| 扱う項目 | 45-13〜45-16 |

## 目的

プレイヤーの移動と破壊の手触りの問題を直す。ガラスが割れない、狭い所や小さいかけらでウォールランに入ってしまう、小さすぎるかけらができる、高い建物に上りにくい、の 4 つ。

## 確定した仕様（2026-10-25・26、ユーザー）

| # | 内容 |
|---|---|
| 1 | 通常攻撃と砕剣（通常攻撃に破壊タイプが付いたもの）でも、ガラスを割る |
| 2 | 狭い場所や小さいかけらでは、ウォールランに入らない。地面が近いときも、壁に取りつかない |
| 3 | 一定より小さいかけらは、切り離さずに消す |
| 4 | F キーの飛び上がりを、高い建物の屋上にも使えるようにする。目印は置かず、見ている建物の屋上を自動で見つける |

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

## 作業計画

| コミット | 項目 | 内容 | 確かめること |
|---|---|---|---|
| 1 | 45-15 ガラス | `GlassPane` に、向きを持った箱で割る static メソッドを足す（今の `ShatterInCapsule` と同じく、箱が通るセルの板だけを調べる）。`MeleeCutAdapter` の振りで、`CollectTargets` と同じ箱で呼ぶ。通常攻撃と砕剣の両方で呼ぶ | 通常攻撃と砕剣で、刃の範囲の窓が割れ、破片と音が出る。範囲の外の窓は割れない |
| 2 | 45-13 ウォールラン | 下の「ウォールランの条件」のとおり | 地面の近くで壁に寄ってもウォールランに入らない。小さいかけらに触れても入らない。ビルの壁では今までどおり入れる。壁キックも今までどおり |
| 3 | 45-14 小さいかけら | 切り離す下限を、サンプル数から体積（m³）に変える。下限は `VoxelQualitySettings` に `_minPieceVolume` として持つ（品質設定ごとに 1 か所で決められる為）。`VoxelPiece` の 2 か所の比較を `サンプル数 × VoxelCubicVolume >= 下限` にする。`_minPieceSamples` は消す（アセットの値も消える） | 建物を壊したとき、細かいかけらが出ない。今まで出ていた大きさのかけらは今までどおり出る。消える量で、壊した跡の見た目がおかしくならない |
| 4 | 45-16 屋上への飛び上がり | 下の「屋上の見つけ方」のとおり | 高い建物の壁を見ると F の誘導が出て、押すと屋上に降りる。低い建物、遠い建物、屋上がない物（塔の帯の目印がある所など）では出ない。天照塔の帯と倒れた塔の目印は今までどおり |

### ウォールランの条件

- 地面が近い：`PlayerContact` に「地面が近い」の印（名前の案：`NearGround`）を足す。`PlayerMovementAdapterBase.UpdateContacts` で、足元から下へ `_wallMinGroundClearance`（新しい SerializeField。案は 1.5m）以内に地面があれば付ける。`PlayerMovementService.CanEnterWallRun` は、この印があれば入らない。壁走り中の判定には使わない（壁走りから抜ける条件は今のまま）
- 小さいかけら：`IsWall` で、`VoxelPiece` の範囲の高さが `_minWallPieceHeight`（案は 2m）より小さければ壁にしない
- 狭い場所：まず実機で再現し、どの面に触れて入っているかを報告する。上の 2 つで直らなければ、直し方を提案して止まる（例：反対向きの壁に同時に触れているときは入らない）
- 値はコミットのあとに実機で調整する

### 屋上の見つけ方

- `FlightAdapter` で、目印が見つからなかったときだけ屋上を探す。目印は今までどおり優先する
- 視線を `_roofSearchDistance`（案 30m）まで飛ばし、法線がほぼ水平な面（壁）に当たったら、当たった点から壁の奥へ `_roofInset`（案 2m）入った点の、高い所から真下へ飛ばす。最初に当たった面の法線が上向きで、その高さがプレイヤーの足元より `_minRoofHeight`（案 10m）以上高ければ、そこを屋上とする
- 見つけた屋上は `FlightTarget`（削らない、余裕の高さあり）にして報告する。`FlightService` と `FlightTarget` は変えない
- 調べるレイヤーは、建物（ボクセルとメッシュのまま）の層にする。敵・プレイヤー・ガラスは除く

## 完了条件

- 通常攻撃と砕剣でガラスが割れる
- 地面の近くと小さいかけらでは、ウォールランに入らない
- 細かすぎるかけらが出ない
- 高い建物の屋上へ F で上がれる

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | F キーの取り合い | 屋上の候補が出ている間は、F がスローモードではなく飛び上がりになる。高い建物の壁を見ている間はスローモードに入れなくなるので、実機で困らないかユーザーが確かめる。困るなら、屋上は壁の近く（例：10m 以内）でだけ候補にする |
| 2 | かけらの下限の体積 | 品質設定ごとに、今のかけらの大きさを見て決める。作業者が品質設定ごとのボクセルの大きさと、今の 8 サンプルの体積を表にして提案する |
| 3 | 地面までの距離と壁の高さの値 | 1.5m と 2m から始め、実機で調整する |

## 見つけた問題（今回は扱わない）

- なし
