# フィニッシャーの旧作資料（HDRPVerHookShot）

区間23（[Section23_FinisherBeam.md](Sections/Section23_FinisherBeam.md)）で旧作に合わせる値を書き写した資料。以降の作業はこの文書を正本にする。
旧作のプロジェクトは `C:\Users\trei0\Downloads\HDRPVerHookShot-20261016T203848Z-1-001\HDRPVerHookShot`（Unity 6000.3.10f1、HDRP）。下の「旧作のパス」は、その `Assets/` からの相対パス。

書き写した日は 2026-10-18。Unity のオイラー角は Z → X → Y の順に回す値で、旧作と kizami のどちらも同じ。

## 1. 動き（`05_Script/EnemyScript/FinisherEnemyController.cs`）

### 1.1 実際に効く値

コードの初期値ではなく、`06_Prehab/FinisherEnemy.prefab` に保存されている値が効く（シーンとほかのプレハブに上書きはない）。

| 項目 | コードの初期値 | プレハブの値（実際） |
|---|---|---|
| `_maxTimer`（照準と予兆の合計） | 4 | **7** |
| `_beamDelay`（狙う頭の位置の遅れ） | 2 | **3** |
| `_defaultRecastTime`（冷却） | 10 | 10 |
| 最初の冷却 | `Random.Range(10, 15)` | 同じ |
| 食べる範囲 | 半径 20m（`Layer: Enemy`、`EnemyType.attacker`、リーダーでない） | 同じ |
| 照射の長さ（`_moveTimer`） | 1 秒 | 同じ |

### 1.2 流れ（`beamPhase`）

| 段階 | 内容 |
|---|---|
| 開始（`UniqueAction`） | 冷却が終わっていて、半径 20m に食べる敵がいると始まる。`_timer = _maxTimer`。食べる敵を止め、フィニッシャーはその敵の位置へ NavMesh で歩く |
| 0 | 着いたら `beamPhase = 1`、`grab = true`（Absorption のアニメーション） |
| 1 → 2 | Absorption の 0.75 秒の `AttackForEat` イベントで `beamPhase = 2`。食べた敵に `HPChanger(100)`（倒す） |
| 2 | 照準。`_timer > 1` の間、`_mainBody.transform.up = -(遅れた頭の位置 - 体の位置)`。`_timer <= 1` の間は向きを固定。`_timer < 0` で 3 へ。`_timer` は 2 の間だけ減る。**予兆（`DangerorsArea`）は 2 の間ずっと出し続ける** |
| 3 | 予兆を消し、ビーム（`Twist`）を出す。`Beam` アニメーターの `beam = true`。`_moveTimer = 1` |
| 4 | 1 秒たったら、向きを元に戻し（`rotation = identity`）、`_recastTime = 10`、`grab = false`、ビームを消し、リーダーの目的地へ歩く |

`BeamStance` の `AimStart` イベント（1.42 秒）も `beamPhase = 2` にするが、`AttackForEat` で既に 2 なので何も変わらない。
**構えのアニメーションと照準は順番ではなく、同時に進む**。照準の `_timer` は Absorption の 0.75 秒から減り始める。

### 1.3 実際の時間（プレハブの値）

Absorption の 0 秒から数える。

| 時刻（秒） | 出来事 |
|---|---|
| 0 | Absorption 開始 |
| 0.75 | `AttackForEat`。敵を倒す。照準と予兆が始まる |
| 1.0 | Absorption が終わり、BeamStance（1.42 秒）へ |
| 2.42 | BeamStance が終わり、Aiming（0.5 秒、`grab` の間ずっと繰り返す）へ |
| 0.75 + 6 = 6.75 | 照準が終わる（`_timer` が 7 → 1）。ここから向きを固定 |
| 6.75 + 1 = 7.75 | 予兆が終わり、ビームを出す |
| 8.75 | ビームが終わる。冷却 10 秒に入る |

照準中の頭の位置は、毎フレーム `PlayerHead` の位置をキューに入れ、`_beamDelay` 秒たってからは 1 フレームにつき 1 つ取り出す。遅れはおよそ `_beamDelay` 秒。

### 1.4 ダメージ

| 項目 | 値 |
|---|---|
| 判定 | `Twist` の `CapsuleCollider`（トリガー）に、タグ `DMGZone` で触れる |
| 量 | `GaugeChanger(40)`（`PlayerMove.OnTriggerEnter`、`05_Script/PlayerScript/PlayerMove.cs:353`） |
| 間隔 | `_beamDmg` が 5 を超えたときだけ。`_beamDmg` は毎フレーム増え、当たると 0。実質、入った瞬間に 1 回 |

## 2. アニメーション

### 2.1 ステートの遷移（`04_Animations/EnemyAnimation/Finisher.controller`）

| 元 | 先 | 条件 |
|---|---|---|
| FinisherStay | FinisherMove | `Walking = true`（0.25 秒でつなぐ） |
| FinisherMove | FinisherStay | `Walking = false`（0.25 秒でつなぐ） |
| Stay / Move | FinisherAbsorption | `grab = true`（Stay は 0.12 秒、Move は 0.08 秒でつなぐ） |
| FinisherAbsorption | FinisherBeamStance | 終わりまで再生したら |
| FinisherBeamStance | FinisherAiming | 終わりまで再生したら |
| FinisherAiming | FinisherStay | `grab = false` |
| Stay / Move / Aiming | FinisherDelete | `Delete = true` |

### 2.2 姿勢のキー（`FinisherStay`・`Move`・`Absorption`・`Aiming`・`BeamStance`）

キーは旧作の骨ごとに、`FinisherEnemy/Body` の下を指す。値は `Body` の局所座標（旧作の `Body` は `FinisherEnemy`（内側）の子）。
動かしているのは、体の高さ（位置 Y）と、`UpperLeg`・`UnderLeg` のオイラー角だけ。骨の位置・スケールは動かさない。

体の高さ（`Body` の局所 Y、m）

| アニメーション | 長さ | キー（時刻 秒 : 高さ） |
|---|---|---|
| Stay | 0.5 | 3.021 で一定（ループ） |
| Move | 1.0 | 0 : 3.021 → 0.5 : 3.23 → 1.0 : 3.021（ループ） |
| Absorption | 1.0 | 0 : 3.021 → 0.333 : 4.025 → 0.583 : 4.141 → 0.75 : 0.58 → 1.0 : 4.141 |
| Aiming | 0.5 | 4.141 で一定（ループ） |
| BeamStance | 1.42 | 4.141 で一定 |

脚（kizami では腕）のオイラー角（X, Y, Z）。Stay・Move・Aiming・BeamStance はすべて同じ値で一定（Move は体の高さだけが動く）。

| 旧作の骨 | UpperLeg | UnderLeg |
|---|---|---|
| BuckLeft | (-25, 0, -25) | (-45, 0, -45) |
| BuckRight | (-25, 0, 25) | (-40, 0, 40) |
| FrontLeft | (25, 0, -25) | (45, 0, -45) |
| FrontRight | (25, 0, 25) | (45, 0, 45) |

Absorption の脚（時刻 : X, Y, Z）。Y は常に 0。

| 旧作の骨 | 0 | 0.333 | 0.583 | 0.75 | 1.0 |
|---|---|---|---|---|---|
| BuckLeft UpperLeg | -25, -25 | 19.8, 19.8 | 25, 25 | -19.8, -19.8 | -25, -25 |
| BuckRight UpperLeg | -25, 25 | 19.8, -19.8 | 25, -25 | -19.8, 19.8 | -25, 25 |
| FrontLeft UpperLeg | 25, -25 | -19.8, 19.8 | -25, 25 | 19.8, -19.8 | 25, -25 |
| FrontRight UpperLeg | 25, 25 | -19.8, -19.8 | -25, -25 | 19.8, 19.8 | 25, 25 |
| BuckLeft UnderLeg | -45, -45 | 15.39, 15.301 | 22.4, 22.3 | -37.99, -38.001 | -45, -45 |
| BuckRight UnderLeg | -40, 40 | 15.91, -16.806 | 22.4, -23.4 | -33.51, 33.406 | -40, 40 |
| FrontLeft UnderLeg | 45, -45 | -15.39, 16.107 | -22.4, 23.2 | 37.99, -37.907 | 45, -45 |
| FrontRight UnderLeg | 45, 45 | -15.39, -16.107 | -22.4, -23.2 | 37.99, 37.907 | 45, 45 |

表の各セルは「X, Z」。補間は Unity の既定（キーごとの接線）。値は `04_Animations/EnemyAnimation/FinisherAbsorption.anim`。

イベント

| アニメーション | 時刻（秒） | 関数 |
|---|---|---|
| Absorption | 0.75 | `AttackForEat` |
| BeamStance | 1.4167 | `AimStart` |

そのほかの注意

- 食べた敵の死骸（`AttakkerEnemyCadaver`）のキーがあるが、`m_IsActive` を 0 にしている。再現しない（決めること #2）
- 骨のキーのほかに、体と脚のマテリアルの `_BaseColor`・`_NoiseLevel` のキーがある。ディゾルブのための値で、再現しない
- `FinisherEnemy` の `Sphere`（位置 (0, 5.37, 0)、スケール 1.55、`SphereCollider` の半径 1.4・中心 (0, 5.27, 0)）は動かさない

### 2.3 体の階層と高さ（旧作の `FinisherEnemy.prefab`）

```
FinisherEnemy（根。NavMeshAgent 半径 0.82、高さ 3.74、速度 3.5、BaseOffset 0）
├ Sphere（位置 Y 5.37、スケール 1.55）
└ FinisherEnemy（内側。位置 Y 2.723。照準で傾ける `_mainBody`）
   ├ Body（位置 Y 3.021）… 根から見た Y は 5.744
   │  ├ BuckLeftUpperLeg … 位置 (0.464, 0.529, -0.449)
   │  │  └ BuckLeftUnderLeg … (1.375, -0.938, -1.402)
   │  ├ BuckRightUpperLeg … (-0.488, 0.529, -0.475) / UnderLeg (-1.379, -0.938, -1.396)
   │  ├ FrontLeftUpperLeg … (0.459, 0.529, 0.473) / UnderLeg (1.397, -0.938, 1.380)
   │  ├ FrontRightUpperLeg … (-0.449, 0.529, 0.464) / UnderLeg (-1.400, -0.938, 1.372)
   │  └ AttakkerEnemyCadaver（非表示）
   ├ DangerorsArea（非表示。回転 Z 180°、スケール (4, 8, 4)）
   └ Twist（非表示。ビーム。回転 Z 180°、スケール (3, 8, 3)）
```

照準で傾けるのは内側の `FinisherEnemy`。回転の中心は根から 2.723m 上で、`Body` と `Twist`・`DangerorsArea` が一緒に傾く。ビームの出る点も、この 2.723m の位置。

## 3. ビーム

### 3.1 形と大きさ

| 項目 | 値 |
|---|---|
| 物体 | `Twist`（`03_Models/Twist.fbx`）。タグ `DMGZone` |
| メッシュ | 半径 4.714m の円柱。長さ 186.2m（原点から +Y へ伸びる） |
| スケール | (3, 8, 3) |
| 回転 | Z 軸まわりに 180°（-Y へ伸びる。照準で内側の `FinisherEnemy` の -Y が頭の位置を向く） |
| 実際の大きさ | **半径 14.14m、長さ約 1489.6m**（原点から射線に沿って） |
| 当たり判定 | `CapsuleCollider`（トリガー）半径 4.714、高さ 186.2、向き Y、中心 (0, 93.1, 0) をスケールした大きさ。メッシュと同じ |
| 影 | 落とさない |
| 止まる物 | なし。射線上のすべてを貫く |

### 3.2 予兆（`DangerorsArea`）

| 項目 | 値 |
|---|---|
| メッシュ | `03_Models/DangerorsArea.fbx`。半径 4m の円柱、長さ 186.1m |
| スケール・回転 | (4, 8, 4)、Z 軸まわりに 180°。実際は半径 16m、長さ約 1488.6m |
| 出る間 | 照準の開始から予兆の終わりまで（1.2 の段階 2 の間ずっと） |
| マテリアル | `02_Img/Materials/dangerous.mat`（HDRP/Lit、透明、加算ブレンド `_BlendMode: 1`、ZWrite オフ） |
| マテリアルの値 | `_BaseColorMap` と `_MainTex` は `02_Img/dangerous.png`（タイリング (1, 2)）。`_EmissiveColor` (30, 30, 30, 30)、`_Metallic` 1、`_Smoothness` 1、`_UseEmissiveIntensity` 1 |
| アニメーション | `DengerorsArea.anim`（1 秒、ループ）はキーを持たない |

### 3.3 ビームのマテリアル

`Twist` のマテリアルは 2 枚。スロット 0 が `BeamMaterialInside`、スロット 1 が `BeamMaterialOutside`。シェーダーは `10_Shader/Beam.shadergraph`（HDRP の Shader Graph）。どちらも透明（`_SurfaceType: 1`）、描画順 3000。

| プロパティ | Inside | Outside |
|---|---|---|
| `_Color1` | (1, 0, 0) 赤 | (1, 0, 0) |
| `_Color2` | (0, 1, 0.042) 緑 | (0, 1, 0.042) |
| `_Color3` | (0, 1, 0.991) 水色 | (0, 1, 0.991) |
| `_Emission` | 10 | 10 |
| `_NoiseLevel` | 0.295 | 0.295 |
| `_Tilling`（x, y） | (1, -6.9) | (1, -6.9) |
| `_Offset`（x, y） | (0.1, 0.1) | (0, 0) |
| `_Metalic` | 0 | 0 |
| `_Smoothness` | 0.5 | 0.5 |
| `_ZWrite` | 0 | 1 |

シェーダーグラフの中身は、Gradient Noise を Step で 3 段に区切り、3 色の帯にして、`_Tilling` と `_Offset` で流す（`_NoiseLevel` が閾値）。グラフの詳細は 18-6 でシェーダーを作るときに読む。

### 3.4 ビームのアニメーション（`Beam.controller`）

| ステート | アニメーション | 長さ | 内容 |
|---|---|---|---|
| Beam | `ObjectAnimation/BeamBeam.anim` | 1.667 秒 | `_NoiseLevel`：0 → 1（0.167 秒）→ 0.5（0.667 秒）→ 0（1.667 秒）。`_Tilling.y`：1 → -4（0.167 秒）→ 1（1.167 秒）→ 1（1.667 秒）。`_Tilling.x` は 1、`z`・`w` は 0 で一定 |
| BeamStop | `04_Animations/BeamStop.anim` | 0.5 秒 | `_NoiseLevel` 0、`_Tilling` (1, 1, 0, 0) で一定 |

`beam = true` で BeamStop → Beam。Beam が終わると BeamStop に戻る。ビームの照射は 1 秒なので、BeamBeam は 1 秒の時点（`_NoiseLevel` が 0.5 から 0 へ下がる途中）で消える。

## 4. 旧作の骨と kizami の部位

### 4.1 フィニッシャー

旧作のモデル（`03_Models/FinisherEnemy.fbx`）と kizami のモデル（`Assets/Art/Models/EnemyModels/MachineEnemy_Finisher.fbx`）は、同じメッシュ（頂点数・大きさが同じ）。

名前は **左右が逆**。位置で対応させる。Back/Buck は同じ向き（-Z 側）。

| 旧作 | kizami | 付け根の位置（体から、X, Y, Z） |
|---|---|---|
| BuckLeftUpperLeg / UnderLeg | BackRightUpperArm / Forearm | (0.464, 0.529, -0.449) |
| BuckRightUpperLeg / UnderLeg | BackLeftUpperArm / Forearm | (-0.488, 0.529, -0.475) |
| FrontLeftUpperLeg / UnderLeg | FrontRightUpperArm / Forearm | (0.459, 0.529, 0.473) |
| FrontRightUpperLeg / UnderLeg | FrontLeftUpperArm / Forearm | (-0.449, 0.529, 0.464) |

先端の骨の位置（`UnderLeg` / `Forearm` の局所位置）も、この対応で同じ値になる（例：旧作 BuckLeftUnderLeg (1.375, -0.938, -1.402) = kizami BackRightForearm）。
対応の根拠は、`Assets/Level/Prefabs/Enemy/FinisherEnemy.prefab` の各部位の位置と、`MachineEnemy_Finisher.fbx` のメッシュの位置。

### 4.2 構造の違い（フィニッシャー）

| 項目 | 旧作 | kizami |
|---|---|---|
| 階層 | 根 → 内側の `FinisherEnemy`（Y 2.723）→ `Body`（Y 3.021）→ 脚 | 根 → `Body`・各腕が並ぶ（脚は `Body` の子ではない） |
| 体の高さ（根から） | 5.744 | **5.021** |
| 脚の付け根の高さ（根から） | 6.273（5.744 + 0.529） | 5.55（5.021 + 0.529） |
| 休みの姿勢（腕のオイラー角、上腕） | Stay：(±25, 0, ±25)（X は Front が +、Back が -。Z は Left が -、Right が +） | 下の表。Stay とは別の値 |
| 傾ける中心 | 内側の `FinisherEnemy`（根から 2.723m） | 傾ける物がまだない |

kizami の休みの姿勢（`FinisherEnemy.prefab`、上腕のオイラー角 / 前腕のオイラー角）

| kizami の部位 | 上腕 | 前腕 |
|---|---|---|
| BackLeft | (31.5, 350.2, 322.6) | (315.8, 313.3, 92.0) |
| BackRight | (32.5, 10.2, 38.7) | (316.0, 46.7, 268.3) |
| FrontLeft | (328.0, 13.2, 320.6) | (45.9, 45.8, 91.8) |
| FrontRight | (328.4, 347.5, 38.5) | (45.8, 314.0, 268.1) |

旧作の Stay の値（2.2）を、4.1 の対応で kizami の部位に当てる。kizami の今の休みの姿勢は旧作の Stay と一致しない。

### 4.3 攻撃する敵とシールドを持つ敵

旧作と kizami では、モデルが別物。旧作の待機姿勢の値（回転 0）を kizami にそのまま当てることはできない。

| 項目 | 旧作（`AttackerIdle.anim`・`DefenderIdle.anim`） | kizami（`AttackerEnemy.prefab`・`DefenderEnemy.prefab`） |
|---|---|---|
| 待機姿勢 | 全部の脚の回転が (0, 0, 0)、`Body` の位置 Y 0.488（= プレハブの値。待機はプレハブの姿勢そのもの） | プレハブの姿勢は、脚の回転が 0 |
| 脚の付け根 | `Body` の子。局所位置 (±0.46〜0.49, 1.17, ±0.45〜0.48) | `Body` の兄弟。局所位置 (±0.46〜0.51, 1.51〜1.70, ±0.46〜0.52) |
| 脚の下の骨 | 局所位置 (±1.375〜1.400, 0.938, ±1.372〜1.402) | 局所位置 (0.003, -0.029, ±2.12) |
| 脚のメッシュの向き | 上の脚は上へ、下の脚は下へ伸び、足先が地面（Y 0）に付く | 脚のメッシュの長辺が Z 方向。回転 0 の姿勢では足が地面に付かない |
| 銃（Attacker） | `Body/GunTurret`（位置 (0, 2.660, -0.079)）、`Gun`（(0, 0.207, 0.079)） | `GunTurret`（(0, 2.874, 0.018)）、`Gun`（(0, 0.437, -0.018)） |
| シールド（Defender） | `ShieldRoot`、`Shield` は別物（`Shield.controller` で動く） | `Shield`（位置 (0, 5.026, 2.920)、メッシュ 20.9 × 10.4 × 2.5） |

足が地面に付く姿勢の値は、旧作のキーをそのまま使えない。kizami の骨の向きと長さから、足先が地面に付く回転を求める（18-2）。

Defender の旧作の骨には前脚の下の骨（FrontLeftUnderLeg・FrontRightUnderLeg）に位置のキー（(-1.397, 0.938, 1.380)・(1.400, 0.938, 1.372)）があるが、プレハブの値と同じ。

## 5. 18-1 で見つけた、計画書との食い違い

区間23の計画書の「確定した仕様」と、旧作の実際の動きで、次の違いがある。ユーザーの判断が要る。

| # | 計画書 | 旧作の実際 |
|---|---|---|
| 1 | 照準 3 秒、予兆 1 秒（コードの初期値 `_maxTimer = 4`、`_beamDelay = 2` から） | プレハブが `_maxTimer = 7`、`_beamDelay = 3` を上書きしている。実際は **照準 6 秒、予兆 1 秒、頭の位置は約 3 秒前** |
| 2 | 食べる 1.0 秒 → 構え → 照準 | 構え（BeamStance 1.42 秒）は照準と**同時**。照準は食べる敵を倒す 0.75 秒から始まり、体は 0.75 秒から傾き始める |
| 3 | 予兆は 1 秒（射線に沿って危険範囲を示す） | 予兆の物体は照準の開始から予兆の終わりまで出ている（実際は 6 + 1 = 7 秒）。体の向きの追従は予兆の前に止まる |
| 4 | ビームの太さと長さは旧作に合わせる | 半径 14.14m、長さ約 1.5km の円柱。街のマップの建物（高さ・幅）より大きい。予兆は半径 16m |
| 5 | フィニッシャーは旧作と同じく、ずっと浮いている | 旧作のフィニッシャーは NavMesh の上を歩く（BaseOffset 0）。体の高さ（5.744m）が地面からの高さで、足は地面に付かない。「浮いている」のは足が地面から離れているという意味 |
| 6 | 脚の姿勢と体の高さを FK で再現 | 体の高さが旧作 5.744m、kizami 5.021m で 0.723m 違う。Absorption の体の高さのキー（3.021 → 4.141 → 0.58）は旧作の `Body` の局所値なので、根からの高さに直すには 2.723m を足す必要がある |
| 7 | 継続ダメージは 0.1 秒ごとに 1 | 旧作は入った瞬間に 40。ここは確定どおり変える |
