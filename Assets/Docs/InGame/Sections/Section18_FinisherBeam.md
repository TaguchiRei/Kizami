# 区間18：フィニッシャーの極太ビームと旧作の姿勢

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-18。18-5 の溶け方は、ユーザーがプレイで確認する） |
| 目安の時期 | 未定（区間14C の街のマップとは独立。どちらを先にしてもよい） |
| 前提となる区間 | 9, 15 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

区間の番号は識別用。ブランチは `feature/alpha/finisher-beam`（ユーザーが作る）。

## 目的

フィニッシャーの攻撃を、「プレイヤーの真上へ飛んで、上から撃つ」強制的に当たる攻撃から、「その場で構えて、地上から極太のビームを撃つ」よけられる攻撃に作り直す。
ビームはボクセルの建物を少しずつ溶かして進むので、建物は盾として時間を稼ぐことにしか使えない。屋上や建物の陰を、安全地帯にしない為。
あわせて、敵の見た目（遠くの敵の姿勢とフィニッシャーの姿勢）を、旧作 HDRPHookShot に合わせる。

## 関連する仕様

- 敵（「吸収型の敵（フィニッシャー）」と「動き」）：https://app.notion.com/p/3e91ea2aa7fa81f3a713c0a6e64d438e

## 確定した仕様（2026-10-17、ユーザー）

| # | 内容 |
|---|---|
| 1 | 流れ：食べる → その場で構える → 照準 → 予兆 → 照射 → 冷却してグループの位置へ戻る。上空への移動と分身は削除する |
| 2 | 時間は旧作に合わせる：食べる 1.0 秒、照準 3 秒、予兆 1 秒、照射 1 秒、冷却 10 秒（最初の 1 回は 10〜15 秒のランダム） |
| 3 | 照準の 3 秒間は、2 秒前のプレイヤーの頭の位置を追い、体ごと傾けて向ける。予兆と照射の間は向きを固定する（旧作どおり） |
| 4 | 予兆は、射線に沿って危険範囲を示す（旧作の `DangerorsArea` の考え方） |
| 5 | 継続ダメージは 0.1 秒ごとに 1（ここだけ旧作から変える。旧作はトリガーに入った瞬間に 40） |
| 6 | ビームはボクセルの建物を、照射を続けた分だけ溶かして奥へ進む。一撃で射線上のすべてを壊さない。メッシュのまま置かれている（ボクセルになる前の）建物では止まる。装甲でも止まる |
| 7 | 指定していないビームの仕様（見た目、太さ、長さなど）は、すべて旧作に合わせる |
| 8 | フィニッシャーは旧作と同じく、ずっと浮いている。脚（kizami では腕）の姿勢と体の高さは、旧作のアニメーションのキーを FK で再現する。IK で歩かせない |
| 9 | 遠くの敵（体を貸していない敵）も、旧作の待機姿勢で描く。攻撃する敵とシールドを持つ敵は足を地面に付けた姿勢、フィニッシャーは浮いた待機姿勢 |
| 10 | （2026-10-18、18-1 のあと）旧作はプレハブが `_maxTimer = 7`、`_beamDelay = 3` を上書きしていて照準は実際には 6 秒だが、#2 の 3 秒・1 秒をそのまま採用する。頭の位置は 2 秒前を追う |
| 11 | （同）構え（BeamStance）は照準と同時に進める旧作の形にする。旧作は敵を倒す 0.75 秒の時点で照準が始まり、構えのアニメーションは並行して流れる |
| 12 | （同）`FinisherClone.prefab`・`BeamTelegraph.prefab`・`FinisherClone.mat` は削除する（決めること #5） |
| 13 | （同）ビームは旧作の大きさ（半径 14.14m、長さ約 1.5km）に合わせ、危険範囲の帯は半径 16m。値は `EnemyFinisherBeams` の SerializeField で変えられる |

## 旧作の資料

旧作のプロジェクトはユーザーのダウンロードフォルダにある（HDRP、Unity のバージョンは旧作の `ProjectSettings/ProjectVersion.txt` を見る）。

`C:\Users\trei0\Downloads\HDRPVerHookShot-20261016T203848Z-1-001\HDRPVerHookShot`

ダウンロードフォルダは消える可能性があるので、18-1 で必要な値を [FinisherReference.md](../FinisherReference.md) に書き写し、以降の作業はそちらを正本にする。旧作のアセットを kizami へコピーしない（HDRP のアセットは URP で使えない為）。

| 資料 | 場所（旧作の `Assets/` から） | 中身（2026-10-17 に読んだ範囲） |
|---|---|---|
| 動きのコード | `05_Script/EnemyScript/FinisherEnemyController.cs` | 半径 20m 以内の、リーダーでない攻撃する敵を食べる。`_maxTimer = 4`、照準は `_timer > 1` の間、`_timer < 0` で発射。照射は `_moveTimer = 1` 秒。冷却 `_defaultRecastTime = 10`、最初は `Random.Range(10, 15)`。照準は `PlayerHead` の位置を `_beamDelay = 2` 秒遅らせたキューから取り、`_mainBody.transform.up = -(狙う位置 - 体の位置)` で体を傾ける |
| アニメーター | `04_Animations/EnemyAnimation/Finisher.controller` | パラメータは `Walking`、`Delete`、`grab` |
| アニメーション | `04_Animations/EnemyAnimation/Finisher*.anim` | 下の表 |
| ビームのアニメーション | `04_Animations/ObjectAnimation/BeamBeam.anim`（1.67 秒）、`04_Animations/BeamStop.anim`（0.5 秒） | マテリアルの `_NoiseLevel`、`_Tilling`、表示の有無を動かす |
| ビームのシェーダー | `10_Shader/Beam.shadergraph`（HDRP） | Gradient Noise → Step で 3 色（`_Color1`〜`_Color3`）の帯。`_Tilling` と `_Offset` で流す。`_Emission`、`_Metalic`、`_Smoothness` |
| ビームのマテリアル | `02_Img/Materials/BeamMaterialInside.mat`、`BeamMaterialOutside.mat` | 内側と外側の 2 枚を重ねる。外側は `_Emission: 10`、`_NoiseLevel: 0.295`、`_Tilling: (1, -6.9)`、色は赤・緑・水色 |
| 予兆 | `03_Models/DangerorsArea.fbx`、`02_Img/Materials/dangerous.mat` | プレハブでのスケールは (4, 8, 4) |
| プレハブ | `06_Prehab/FinisherEnemy.prefab`、`AttackerEnemy.prefab`、`DefenderEnemy.prefab` | ビームの物体（`_beamObject`）の太さと長さは、このプレハブの中に見つからなかった。18-1 で、旧作を Unity で開くか、シーンを探して確かめる |
| 待機のアニメーション | `04_Animations/EnemyAnimation/AttackerIdle.anim`、`DefenderIdle.anim`、`FinisherStay.anim` | 遠くの敵の姿勢の元 |

フィニッシャーのアニメーションの体（`FinisherEnemy/Body`）の高さ

| アニメーション | 長さ | 体の高さ（m） | イベント |
|---|---|---|---|
| FinisherStay | 0.5 秒 | 3.02 で一定 | なし |
| FinisherMove | 1.0 秒 | 3.02 → 3.23 → 3.02 | なし |
| FinisherAbsorption | 1.0 秒 | 3.02 → 4.02（0.33 秒）→ 4.14（0.58 秒）→ 0.58（0.75 秒）→ 4.14 | 0.75 秒で `AttackForEat`（つかむ） |
| FinisherAiming | 0.5 秒 | 4.14 で一定 | なし |
| FinisherBeamStance | 1.42 秒 | 4.14 で一定 | 1.42 秒で `AimStart`（照準を始める） |

旧作の骨の名前は `{Front|Buck}{Left|Right}{UpperLeg|UnderLeg}`。kizami のフィニッシャーは `{Front|Back}{Left|Right}{UpperArm|Forearm}`（`Assets/Level/Prefabs/Enemy/FinisherClone.prefab` で確認）。名前は対応するが、休みの姿勢の向きが同じかは確かめていない（18-1）。
旧作のアニメーションには、食べた敵の死骸（`AttakkerEnemyCadaver`）を抱える部位がある。kizami はディゾルブと粒で吸収するので、死骸は再現しない（決めること #2）。

## 既存の資産

| 資産 | 内容 |
|---|---|
| `EnemyFinisherAttack`（`EngineAdapterLayer/Enemy/EnemyFinisherAttack.cs`） | 攻撃の開始の判定（マップ全体で 1 体、グループが並んでいる、射程の中、冷却済み）、対象選び、吸収（腕を開いて閉じる、ディゾルブと粒）、戻る動き。段階は `Phase`（Idle / Approaching / Opening / Grabbing / Absorbing / Barrage / Returning）。吸収のあとは `EnemyFinisherBeams` を呼ぶ |
| `EnemyFinisherBeams`（同 `EnemyFinisherBeams.cs`） | 分身、上空への移動、デカールの予兆、特大のビームと追いかけるビーム。追いかけるビームは「当たっている間、間隔ごとにダメージ」（`_trackingDamage`、`_trackingInterval`）を持っており、継続ダメージの手本になる。ダメージは `Action<int> _applyDamage`（`PlayerHealthService.ApplyDamage`）で与える |
| `EnemyBeam`（同 `EnemyBeam.cs`） | ビーム 1 本の円柱とデカールの見た目。当たり判定は持たない |
| `EnemyLegs`（同 `EnemyLegs.cs`） | 体を貸した敵の脚を IK で動かす。歩かない体（フィニッシャー）は足先を畳んだ位置に置く（`GetFoldedFootPosition`） |
| `EnemyCrowdRenderer`（同 `EnemyCrowdRenderer.cs`） | 体を貸していない敵を、部位ごとに `Graphics.RenderMeshInstanced` で描く。部位の行列は、初期化のときに体のプレハブの姿勢から作る（`_partLocalMatrices`、47〜80 行） |
| `VoxelPiece.ApplyStrike`（`EngineAdapterLayer/Voxel/Piece/VoxelPiece.cs:379`） | 強さと減衰率で削る。形状は「融点0の素材まで届く範囲」（球やカプセル）。届く強さが融点以上の点が壊れる |
| `VoxelDestructionAdapter.Carve`（`EngineAdapterLayer/Stage/VoxelDestructionAdapter.cs:199`） | コライダーから `VoxelPiece` を集めて `ApplyStrike` を当てる手本。メッシュのまま置いた `VoxelModelLoader` を当たったときにボクセルにする処理もある（この区間のビームでは、そこを使わずに止める） |
| `VoxelBuilding`（`EngineAdapterLayer/Stage/VoxelBuilding.cs`） | 大きな建物。階は近づくか攻撃が当たるまでメッシュで置く（区間15） |

## 既存コードの確認結果（2026-10-17）

| 対象 | 状態 | 対応 |
|---|---|---|
| `EnemyFinisherAttack` の吸収まで | 対象の上へ飛び、腕を開いて閉じ、ディゾルブで消す | 吸収の流れは残す。腕の姿勢と体の高さを旧作のキーに置き換える（18-3） |
| `EnemyFinisherAttack` の Barrage | `EnemyFinisherBeams` に任せる | 新しいビームの段階に置き換える（18-4） |
| `EnemyFinisherBeams` | 分身・上空・デカール・2 種類のビーム | 分身と上空への移動を削除し、その場で撃つ極太ビームに作り直す。継続ダメージの書き方は追いかけるビームから流用する（18-4） |
| `FinisherClone.prefab`、`BeamTelegraph.prefab` | 分身と、デカールの予兆 | 使わなくなる。削除するかどうかは決めること #5 |
| `EnemyLegs` の歩かない体 | 腕を畳んだ位置に置く | フィニッシャーは IK を使わず、FK の姿勢を当てる（18-3） |
| `EnemyCrowdRenderer` | プレハブの姿勢のまま描く。プレハブは休みの姿勢の回転を 0 にして書き出しているので、足が地面に付いていない | 種類ごとの待機姿勢を当てた行列で描く（18-2） |
| 継続ダメージ | `PlayerHealthService.ApplyDamage(int)` だけ。時間あたりのダメージはない | 照射側で 0.1 秒ごとに `ApplyDamage(1)` を呼ぶ（18-4） |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 18-1 | 旧作の値を書き写す | ドキュメント | 旧作の Finisher*.anim、AttackerIdle、DefenderIdle、BeamBeam、BeamStop のキー（部位ごとの位置と回転、時間）、ビームの太さと長さ、マテリアルの値を、[FinisherReference.md](../FinisherReference.md) に表で書き写す。旧作の骨と kizami の部位の対応表を作り、休みの姿勢の向きの違いを確かめる。食い違いがあれば、ここで報告して止まる |
| 18-2 | 遠くの敵の待機姿勢 | EngineAdapter | 種類ごとの待機姿勢（部位ごとの、体の根から見た位置と回転）をデータとして持ち、`EnemyCrowdRenderer` の `_partLocalMatrices` をそれで作る。データの持ち方（ScriptableObject か、プレハブに付けるか）は決めること #1 |
| 18-3 | フィニッシャーの姿勢を FK で再現 | EngineAdapter | Stay・Move・Absorption・Aiming・BeamStance のキーを再現する。移動中は Move（上下に揺れる）、待機中は Stay。フィニッシャーの体では `EnemyLegs` の IK と畳んだ位置を使わない。再現の方法は決めること #3 |
| 18-4 | 極太ビームの流れと継続ダメージ | EngineAdapter | 吸収のあと、その場で BeamStance → 照準 3 秒（2 秒遅れの頭の位置を追う）→ 予兆 1 秒 → 照射 1 秒 → 冷却。分身と上空への移動を削除する。照射中は射線上のプレイヤーに 0.1 秒ごとに 1 のダメージ。装甲と、メッシュのまま置かれた建物で止まる。フィニッシャーが倒れたら止める |
| 18-5 | ボクセルを溶かして進む | EngineAdapter | 照射中、ビームの先端が当たったボクセルに、毎フレーム（または一定間隔）小さなカプセルの `ApplyStrike` を当てる。強さは融点以上にし、先端は壊れた分だけ奥へ進む。溶ける速さの値は決めること #4 |
| 18-6 | ビームの見た目 | アート（シェーダー） | 旧作の `Beam.shadergraph` を URP の Shader Graph で作り直し、内側と外側の 2 枚、`BeamBeam` の動きで再現する。そのうえで強化する（先端の溶ける所の発光、発射口の光など。範囲は決めること #6） |

コミットは作業ごとに 1 つを基本にする。18-1 はドキュメントだけなので、終わったら報告して止まる。

## 完了条件

- 遠くの敵が、3 種類とも旧作の待機姿勢で描かれる（攻撃する敵とシールドを持つ敵は足が地面に付く）
- フィニッシャーが浮いたまま移動し、待機・移動・吸収・照準・構えの姿勢が旧作と見比べて同じに見える
- 吸収のあと、その場で構え、3 秒プレイヤーを追って向きを変え、1 秒予兆を出し、1 秒ビームを撃つ。分身は出ず、上空へも移らない
- ビームに当たっている間、0.1 秒ごとに HP が 1 減る。射線から外れると減らない
- ボクセルの建物（TestStage の `VoxelBuildingTest`）を盾にすると、ビームが少しずつ溶かして進み、照射の 1 秒の間に壁の奥まで届くかどうかが、溶ける速さの値で変わる
- メッシュのまま置かれた建物の階と、装甲では、ビームが止まる
- 冷却は 10 秒、最初の 1 回は 10〜15 秒

確かめるときは uloop のスキルでコンパイルし、プレイモードで動かす。旧作との見比べは、ユーザーに確認してもらう。

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 待機姿勢のデータの持ち方 | 種類ごとの `EnemyKindSettings` に、部位ごとの姿勢の配列を持たせる。値はエディタ上で旧作のキーから作る |
| 2 | 食べた敵の死骸を抱える表現 | 再現しない。kizami のディゾルブと粒の吸収を残す |
| 3 | FK の再現方法 | kizami の部位に合わせた AnimationClip を作り、Animator で再生する案と、キーの値をコードで補間する案がある。体を貸すときと返すときの切り替え、プールの扱いを見て決める。新しい型を増やす前に planning-criteria の基準1を当てはめる |
| 4 | 溶ける速さ | 先端が毎秒 何 m 進むか。普通の強度の壁を基準に決める。融点の 3 段階で差を付けるか |
| 5 | 使わなくなるプレハブ | `FinisherClone.prefab`、`BeamTelegraph.prefab` を削除するか |
| 6 | 見た目の強化の範囲 | 先端の溶ける所の発光、発射口の光、照射中のカメラの揺れなど |
| 7 | 旧作にない値 | 予兆の範囲の見せ方（射線の帯かデカールか）。ビームの太さと長さが旧作で確かめられないときの値 |

## 仮定（未確認）

- 旧作の骨と kizami の部位は、名前の対応（Leg → Arm、Buck → Back、UnderLeg → Forearm）で 1 対 1 に結べる
- 旧作の攻撃する敵とシールドを持つ敵のモデルの骨も、kizami の `MachineEnemy_Attacker`、`MachineEnemy_Defender` と対応する

## 実装結果（2026-10-18）

### 決めたこと

- 時間は確定仕様どおり、照準 3 秒（2 秒前の頭の位置を追う）→ 予兆 1 秒 → 照射 1 秒。冷却は 10 秒、最初の 1 回は 10〜15 秒（出したときに `EnemyFinisherAttack.RollFirstCooldown` で決める）
- 旧作の実際は、プレハブが `_maxTimer = 7`、`_beamDelay = 3` を上書きしていて照準が 6 秒だった。仕様の 3 秒を優先した。値は [FinisherReference.md](../FinisherReference.md) の「5. 食い違い」にある
- 構えは照準と同時に進める旧作の形にする。体の傾けは照準の開始から始まる。構えの姿勢のアニメーション自体は 18-3
- ビームは旧作の大きさ（半径 14.14m、長さ約 1.5km）、危険範囲の帯は半径 16m。値は `EnemyFinisherBeams` の SerializeField
- `FinisherClone.prefab`、`BeamTelegraph.prefab`、`FinisherClone.mat` は削除した（決めること #5）

### 18-5 の決めたこと

- 溶ける速さ（決めること #4）：毎秒 4m。穴の半径 3m、減衰率 0.5、溶かす間隔 0.05 秒。すべて `EnemyFinisherBeams` の SerializeField（`_meltSpeed`・`_meltRadius`・`_meltFalloff`・`_meltInterval`）で、プレイしながら調整する
- 融点の 3 段階の差は `ApplyStrike` の強さに任せ、別の値は持たない。硬い素材は穴が小さくなり、強さが足りなければ溶けずに止まる
- 止まる物の判定は、当たったコライダーの親に `VoxelPiece` があるかだけ。`VoxelPiece` でない物（メッシュのままの建物・装甲・地形）は溶かさずに止まる
- 溶かした体積は `VoxelDestructionAdapter` を通さないので、削った体積の通知（`_onCarved`）には入らない

### 18-2・18-3 の決めたこと

- FK の再現方法（決めること #3）：Unity の Animator とクリップ。`Assets/Code/Editor/Enemy/FinisherAnimationBuilder.cs`（メニュー Kizami/Finisher/Build Animations）が、`Assets/Art/sample/EnemyAnimation/` の旧作のクリップを kizami の体用に変換して `Assets/Art/Animation/Finisher/` に書き出し、旧作の遷移と同じコントローラー（`Walking`・`grab`）を作り、フィニッシャーのプレハブに Animator を付ける
- 骨の対応は位置で結ぶ（旧作の Left/Right は kizami と逆）。旧作の `Body` の高さの動きは、kizami の `Body` の高さ（5.021m）に足し、腕の付け根の高さにも足す。体の高さは旧作の 5.744m に揃えず、動き幅だけを旧作どおりにした
- 変換したクリップと旧作のモデルにかけた姿勢の突き合わせは、5 クリップ × 21 時刻で、ずれが 0.00000m（`FinisherAnimationBuilder.Verify`）
- フィニッシャーの体では `EnemyLegs` の IK を使わない（Animator を設定すると IK を切り、`Walking` を体の速さから書く）。吸収は旧作の Absorption（`grab`）に置き換え、腕を開いて閉じる IK は削除した。ディゾルブと粒はそのまま
- 吸収の長さは旧作の「敵を食べるイベント」（Absorption の 0.75 秒）に合わせて 0.75 秒にした。構えは照準と同時に流れる（吸収のあと BeamStance → Aiming）
- 待機姿勢のデータの持ち方（決めること #1）：`EnemyPart` に待機姿勢（体の根から見た位置と回転）を持たせ、`EnemyCrowdRenderer` がそれで描く。持っていない部位は、これまでどおりプレハブの姿勢
- 待機姿勢の焼き込み：`EnemyIdlePoseBuilder.cs`（メニュー Kizami/Enemy/Bake Idle Poses）。フィニッシャーは変換した FinisherStay の姿勢。アタッカーとシールドを持つ敵は、旧作の待機姿勢で脛のメッシュが最も低い点（足先）へ、kizami の脚を IK（`EnemyLegs.PoseFeet`）で向けた姿勢。足先の最低点は 0.02m 台で地面に付く
- 食べた敵の死骸（決めること #2）：再現することにした。旧作は BeamStance で死骸が表示され、4 本の脚が外へ広がって 2.1〜3.4 倍になり、ビームを抱えるように見える。体の根の下に非表示の `AttakkerEnemyCadaver` を置き、表示・位置・拡大・色のキーも同じクリップに写した。旧作のアタッカーのモデル（`AttakkerEnemy.fbx`）を `Assets/Art/Models/EnemyModels/` へ移して使う。当たり判定は付けない
- 死骸の色と現れ方は `Kizami/FinisherCadaver`（旧作の `Dissolve.shadergraph` と同じ計算。`_NoiseLevel` で現れ、縁が `_DissolveEmissionColor` で光る）。マテリアルは `FinisherCadaverUpper`・`FinisherCadaverLower`

### 作った主なもの

- `EnemyFinisherBeams`：分身と上空への移動を削除し、照準・予兆・照射の 3 段階に作り直した。照射中は、当たった面から球を射線に沿って動かし、ボクセルを溶かして進む（`Melt`）。射線は pivot（体の根から 2m）から最初に当たった物まで。継続ダメージは 0.1 秒ごとに 1。プレイヤーの頭の位置の履歴を持つ
- `EnemyFinisherAttack`：`Barrage` を `Beam` にし、吸収した位置に浮いたまま、pivot を中心に体を傾けて狙う（`TiltBody`）
- `EnemyBeam`：デカールを外し、複数マテリアルの円柱と `MaterialPropertyBlock`（長さ・半径・ノイズのしきい値・模様の細かさ）に変えた
- シェーダー（`Assets/Code/Shader/FinisherBeam/`）：`FinisherBeam`（旧作 `Beam.shadergraph` と同じ 2 枚のノイズ、白い芯、縁と交差部のフェード）、`FinisherTelegraph`（旧作の警告の標識 `FinisherDangerous.png` を流し、脈動）、`FinisherGlow`（発射口・先端の光とリング、火花）
- マテリアル：`FinisherBeam`（外側）、`FinisherBeamInner`（内側）、`FinisherTelegraph`、`FinisherGlow`
- 旧作の `BeamBeam` の動きは、`AnimationCurve` 2 本（ノイズのしきい値、模様の細かさ）に写した
- 発射口の光（予兆の間に育つ）、先端の光と火花、照射中の画面の揺れ（`CinemachineImpulseSource` と、カメラの `CinemachineImpulseListener`）
- `EnemyLegs`：Animator を設定した体は IK を使わず、`Walking` を書く。`PoseFeet`（待機姿勢の焼き込み用）を追加
- `EnemyFinisherAttack`：吸収は Animator の `grab` を立てる段階（`Approaching` → `Absorbing`）に置き換えた
- `EnemyPart`：待機姿勢の位置と回転。`EnemyCrowdRenderer`：待機姿勢で描く
- エディタのツール：`Assets/Code/Editor/Enemy/`（asmdef は `Kizami.Enemy.Editor`）
- 資料：[FinisherReference.md](../FinisherReference.md)

### 完了条件の確認

| 条件 | 結果 |
|---|---|
| 吸収のあと、その場で 3 秒追い、1 秒予兆、1 秒照射。分身も上空への移動もない | ユーザーがプレイモードで確認 |
| 当たっている間、0.1 秒ごとに 1 減る | ユーザーがプレイモードで確認（「悪くない」） |
| 冷却は 10 秒、最初の 1 回は 10〜15 秒 | コードで確認（プレイでの計測はしていない） |
| ビームと警告の見た目 | ユーザーが確認 |
| ボクセルの建物を溶かして進む。メッシュの建物と装甲で止まる | 実装済み（18-5）。コンパイルとコンソールのエラーなしまでは確認。`VoxelBuildingTest` での溶け方はユーザーが確認する |
| 遠くの敵が 3 種類とも旧作の待機姿勢で描かれる（攻撃する敵とシールドを持つ敵は足が地面に付く） | 足先の最低点で確認（0.02m 台）。見た目はユーザーが確認 |
| フィニッシャーが浮いたまま移動し、待機・移動・吸収・照準・構えの姿勢が旧作と同じに見える | 変換した姿勢と旧作の姿勢の突き合わせでずれ 0m。見た目はユーザーが確認（死骸の脚も含む） |

### 次へ持ち越すこと

- ビームの円柱は、溶けた穴より太い（半径 14.14m に対して穴 3m）ので、穴の中へ入り込まず、面の手前で止まって見える。穴に沿った見た目は必要になったら扱う
- 体を貸していない遠くのフィニッシャーは、待機の姿勢だけで描く（移動の上下の揺れは描かない）
- 旧作の資料（`Assets/Art/sample/`）は、旧作との見比べや変換のやり直しに使う。`AttakkerEnemy.fbx` だけは、死骸のメッシュとして `Assets/Art/Models/EnemyModels/` へ移した
- `EnemyFinisherAttack` の `_pivotHeight`（2m）は、旧作の内側の `FinisherEnemy`（根から 2.723m）の位置を kizami の体の高さに換算した値

## 見つけた問題（今回は扱わない）

- なし
