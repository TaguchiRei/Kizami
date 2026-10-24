# 区間39：アタッカーの弾道とディフェンダーのシールドの表示

| 項目 | 内容 |
|---|---|
| 状態 | 実装済み（2026-10-24。弾道、シールドの耐久値の表示、命中時の色収差、バリアを張る条件のタイマー、バリアと装甲が割れる演出、窓ガラスの破片の最適化。39-4・39-5 は、今の問題ではないので扱わない。39-6 は、必要な SE の洗い出しだけで、生成と実装は後ほど） |
| 目安の時期 | 未定 |
| 前提となる区間 | 38（2026-10-21、ユーザーの優先度で並べた順） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

敵の攻撃と防御を見て分かるようにする。アタッカーの射撃がどこから飛んでくるか、ディフェンダーのシールドがあと何回で割れるかを、プレイヤーが読めるようにする。

## 確定した仕様（2026-10-21、ユーザー）

| # | 内容 |
|---|---|
| 1 | アタッカーの射撃に、レーザーのような軌跡の弾道エフェクトを付ける |
| 2 | ディフェンダーのシールドの耐久値を分かりやすく表示する |

### 作業中に足した仕様（2026-10-24、ユーザー）

| # | 内容 |
|---|---|
| 3 | 攻撃が当たった瞬間に、画面へ色収差を掛ける（攻撃したときの感触） |
| 4 | 耐久値の表示は、10 個の四角を並べる案から、バリアより一回り大きい球を置いてシェーダーで描く案に変えた。四角は 100 マス（10 マスの区分けが横に 10 個）で、段ごとに 1 マスずつ方位をずらして下寄りに並べる。耐久が 1 減るたびに、全区分けから 1 マスずつ消える |
| 5 | 頭部に、真上を頂点にした 36° ずつの三角形 10 個を置く。耐久が 1 減るごとに 1 個ずつ消える |
| 6 | 三角形と四角の帯の間を、台形 10 個で覆う。台形は、三角形とは逆の向きに減る |
| 7 | 四角の群れと三角形は時計回りに、台形は反時計回りに回る |
| 8 | バリア本体に、六角形の幾何学模様（格子とフレネル）を出す |
| 9 | バリアを張る条件をゆるくする。プレイヤーが範囲内にいる間にタイマーが進み、ゼロ（積算が 20 秒）になったら張る。距離に応じて、最大 4 倍まで線形に速く進む。範囲を出るとタイマーを戻し、張っていたバリアも解除する。測る距離はプレイヤーとディフェンダーの水平距離。範囲は半径 80 m（最初は 30 m）、最速になる距離は 10 m（最初は 5 m） |
| 10 | バリアと装甲が割れる演出に、爆発は使わない。円形の物をあらかじめ砕いた破片を用意し、それをある程度物理的に動かす。窓ガラスの破片が先例で、最適化の余地があれば最適化する |
| 11 | SE は、この区間では洗い出しだけにする。生成と実装は後ほど AI で生成しながら進める。必要な音は [SoundEffectList.md](../SoundEffectList.md) |
| 12 | 敵が出てくる・吸収とビームの見た目・暗さと姿勢（39-4・39-5）は、今ある問題ではないので扱わない |

## 既存の資産

- `EnemyShooter`（EngineAdapter）と `Assets/Art/Particles/EnemyBullet.vfx`：アタッカーの弾
- `EnemyBarriers`（EngineAdapter）：`_maxDurability` と、グループごとの `BarrierHitCount` を持つ。147〜157 行
- 区間33 の 33-2 のうち、敵に関わるものはこの区間で扱う：バリアが割れる演出（音は 36）、装甲のパネルが壊れる演出（区間10）、補充と出し直しで敵が出てくる演出（区間16）、敵の攻撃（吸収、ビームの予兆とビーム）の見た目（区間13）
- 区間11 から回した敵の見た目の問題：体を貸していない敵が暗く見える、休みの姿勢のまま描かれる（詳細は [Section33](Section33_Polish.md) の「前提の変化（区間11 の追加）」）

## 作業一覧

| # | 作業 | 内容 | 結果 |
|---|---|---|---|
| 39-1 | 弾道の軌跡 | `EnemyBullet.vfx` に軌跡（Trail / Strip）を足すか、レーザー状の弾に作り直す | 完了 |
| 39-2 | シールドの耐久値の表示 | 残りの耐久値に応じて、シールドの見た目を変える | 完了（球のシェーダー、色収差、バリア本体の模様を含む） |
| 39-2b | バリアを張る条件のタイマー | 仕様 9。作業中に足した | 完了 |
| 39-3 | シールドと装甲が割れる演出 | 割れたときのエフェクト。装甲のパネルが壊れる演出も、同じ仕組みで行う | 完了（窓ガラスの破片の最適化を含む） |
| 39-4 | 敵のそのほかの演出 | 敵が出てくる、吸収とビームの見た目 | 扱わない（仕様 12） |
| 39-5 | 敵の見た目のそろえ | まとめて描画の敵の暗さと、立った姿勢での描画 | 扱わない（仕様 12） |
| 39-6 | SE | 足した演出に、36 の口で SE を付ける | 洗い出しだけ（仕様 11） |

## 完了条件

- アタッカーの弾が、飛んでいる経路を軌跡として残す
- ディフェンダーのシールドを攻撃すると、残りの耐久値が見た目で分かり、0 で割れる演出が出る
- 装甲のパネルが壊れる演出が付く（敵が出てくる、吸収とビームの演出は、扱わないと決めた）
- （扱わないと決めた）体を貸した敵と貸していない敵が並んでも、明るさと姿勢がそろって見える

## 詳細仕様で決めること

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | 耐久値の見せ方 | 仕様 4〜8。四角の帯、頭頂部の三角形、台形、本体の六角形の模様 |
| 2 | 軌跡の長さと、当たらなかった弾の扱い | 見た目だけで、当たり判定は変えない。粒の数と寿命で長さを出した |

## 見つけた問題（今回は扱わない）

| # | 内容 | 場所 |
|---|---|---|
| 1 | 体を貸していない敵が暗く見える疑い。`RenderParams` に `lightProbeUsage` を渡していない（原因かは未確認）。ユーザーは今の問題として感じていない（2026-10-24） | `EnemyCrowdRenderer.cs` の `RenderParams` を作る箇所 |
| 2 | 体を貸していない敵の休みの姿勢の描画。ユーザーは今の問題として感じていない（2026-10-24） | `EnemyCrowdRenderer.cs` の `_partLocalMatrices` |
| 3 | 敵が出てくる演出、吸収・ビームの見た目の作り込み。ユーザーは今の問題として感じていない（2026-10-24） | `EnemyFinisherAttack`、`EnemyFinisherBeams`、`EnemySpawnPoint` |

## 実装結果（2026-10-24）

### 決めたこと
- 弾道の軌跡は、Strip を足さず、`EnemyBullet.vfx` にもともとあった軌跡の粒を増やして作った（粒の出る数 90 → 240 個/秒、寿命 0.3 → 0.4 秒、上限 256 → 1024、弾の頭の筋の長さ 4 → 10）。VFX の編集は、UsefulToolkit の `uloop` の VFX 編集ツール（`export-vfx-graph`・`edit-vfx-graph`）で行った
- 耐久値の表示は、バリアより一回り大きい球（直径 1.04 倍）のシェーダー `BarrierDurability` で描く。四角 1 つずつのオブジェクトは作らない。`ArmorPanel` が、残りの耐久値と区分けの数（耐久値の最大）を `MaterialPropertyBlock` で渡す
- 頭頂部の三角形と台形は、真上を頂点にした扇（耐久値の最大で円を割る）の中を、真上からの距離で分けて描く。三角形と台形が逆向きに回っても重ならないよう、台形の内側の辺は、三角形の頂点が届く円の外に置く
- 命中時の色収差は、URP の Volume の `ChromaticAberration` を使い、専用の全体 Volume の weight を `HitChromaticAberration` が動かす（最大から 0.15 秒で戻る。実時間）。壊れたときは、戻るまでの時間を 2.5 倍にする。呼び出しは、`SoundPlayer.Play` と同じ静的な形
- バリアを張る条件は、`EnemyGroup.BarrierTimer` に積算するタイマー。壊れたバリアは張り直さない（今までどおり）。ディフェンダーが螺旋の置き場に着いていること（`HasArrived`）の条件は外した
- 割れる演出は、事前に砕いた破片のプレハブを窓ガラスの破片のプール（`GlassShardPool`）と同じ形で動かす。プールを種類（`ShardPoolKind`：Glass・Barrier・Armor）ごとに置けるようにした。破片は物理を使わず、`Transform` だけで動かす
- 窓ガラスの破片は、測ってから、Job にまとめて動かす形に変えた。バリアと装甲の破片も、同じ仕組みを使う
- SE は、洗い出しだけにした。43 個の一覧を [SoundEffectList.md](../SoundEffectList.md) にまとめた

### 作った主なもの
- 弾道：`Assets/Art/Particles/EnemyBullet.vfx`（変更）
- 耐久値の表示：`Shader/Stage/BarrierDurability.shader`（四角の帯・頭頂部の三角形・台形・回転）、`Shader/Stage/BarrierBody.shader`（バリア本体の六角形の格子とフレネル）、`Art/Materials/BarrierDurability.mat`、`BarrierBody.mat`。`EnemyBarrier.prefab` の子に `Durability` 球を足し、本体のマテリアルを差し替えた。`ArmorPanel` に `_durabilityRenderer` を足した
- 色収差：`HitChromaticAberration`（EngineAdapter/Stage）と、`Settings/HitVolumeProfile.asset`。`InGame.unity` に Volume と一緒に置いた
- タイマー：`EnemyBarriers`（`_timerRange` 80 m、`_timerFastDistance` 10 m、`_timerDuration` 20 秒、`_timerMaxRate` 4 倍。コードの初期値と `InGame.unity` の保存値を同じにした）、`EnemyGroup.BarrierTimer`、`EnemySpawnAdapter` から `deltaTime` を渡す
- 割れる演出：`Stage/Editor/BreakShardsBuilder.cs`（メニュー Kizami/Stage/Build Break Shards。破片のメッシュとプレハブを作る）、`Art/Models/Shards/`（`BarrierShards.asset` 41 個、`ArmorShards.asset` 16 個）、`BarrierShards.prefab`、`ArmorShards.prefab`、`BreakShardPools.prefab`（バリア用のプールの容量 8、装甲用の容量 48。`InGame.unity` に置いた）、`Shader/Stage/BarrierShard.shader`、`BarrierShard.mat`、`ArmorShard.mat`、`ShardPoolKind`。`ArmorPanel` に、壊れたときの破片の設定（`_shardKind`、速さ、角速度、寿命）を足した。`EnemyBarrier.prefab` はバリア（速さ 14 m/s、寿命 5 秒）、`ArmorPanel.prefab` は装甲（速さ 3 m/s、寿命 4 秒）
- 窓ガラスの最適化：`GlassShardBuffers`（全破片の動きの状態を 1 本の配列にまとめる）、`GlassShardGroup`、`GlassShardPool`（`TransformAccessArray` と Burst の Job で動かす）

### 完了条件の確認結果
- 弾の軌跡：ユーザーがプレイして確認した（290bab4 でコミット）。数値の調整は、ユーザーの見た目で行った
- シールドの耐久値の表示：空の上に置いたバリアを、耐久 10・6・5・3 で撮って、四角の帯・三角形・台形が減る向きと、回る向きを画像で確認した。ステージでの見え方は、ユーザーが確認した（053dd2d でコミット）
- 色収差：命中の瞬間に画面の縁へ色のずれが出るのを、プレイ中に確認した。割れたときの 2.5 倍の長さは、画像では確認していない
- バリアを張るタイマー：プレイ中に、範囲を一時的に 100 m に広げて確認した。距離が近いグループほど速く進み、20 秒で張り、範囲を戻すとタイマーが 0 になってバリアが解けた。速さの式は、40・30・17.5・5・0 m で 1・1・2.5・4・4 になった。最大 80 m・最小 10 m にしたあとの、実際のステージでの解除の距離の感触は、確認していない
- 割れる演出：空の上に置いたバリアを割ると、殻が破片になって外へ飛び、縮んで消えるのを画像で確認した。装甲のパネルも、砕けた板になって飛ぶのを確認した。ステージでプレイヤーが攻撃して割る流れは、確認していない
- 窓ガラスの最適化：400 グループ（8,800 破片）が同時に動く最悪の場合で、破片の更新が 2.10 ms から 0.55 ms になった（エディターで、Job の完了を待つ時間を含む）。破片が重力で落ちて、寿命で戻ることは数値で確認した。見た目が前と同じかは、画像では比べていない
- 敵が出てくる・吸収とビーム・暗さと姿勢：扱わないと決めた

### 次の区間へ持ち越すこと
- SE の生成と実装：[SoundEffectList.md](../SoundEffectList.md) の 43 個。SE の口（`SoundEffectKind` とクリップの配列）を、一覧のものだけ足す。決まっていない点は、一覧の末尾の表
- 近接攻撃が当たったときのエフェクト（区間38 の 38-4 の持ち越し）
- 窓ガラスの破片の描画の最適化：8,800 個の `MeshRenderer` の描画の負荷は、測っていない。重いと分かったら、`Graphics.RenderMeshInstanced` でまとめて描くことを検討する（区間44 の通しの負荷の調整で測る）
- ステージで、プレイヤーが攻撃してバリアと装甲を割る流れと、変更後のタイマーの解除の距離の確認
- 区間40 の確認：`EnemyGroup` に `BarrierTimer` を足した。書き手は `EnemyBarriers`（毎フレーム）と `EnemySpawnAdapter`（グループの作り直し）の 2 か所

### 使い方
- バリアのタイマーを調整する：`InGame` の `Enemy` の `EnemyBarriers` の `_timerRange`・`_timerFastDistance`・`_timerDuration`・`_timerMaxRate`
- 耐久値の表示の見た目を調整する：`BarrierDurability.mat`（`_SlotsPerRing`、`_RingCount`、`_ElevationMin`・`_ElevationMax`、`_CapRadius`、`_RotationSpeed`、`_Color`）、`BarrierBody.mat`（`_HexCount`、`_Gain`）
- 色収差の強さと長さを調整する：`HitChromaticAberration` の `_peakWeight`・`_duration`、`ArmorPanel` の `_breakAberrationScale`
- 破片を作り直す：メニュー Kizami/Stage/Build Break Shards（破片の数は `BreakShardsBuilder` の引数）
- 新しく砕けるものに破片を付ける：破片のプレハブを作って、`GlassShardPool` を新しい `ShardPoolKind` で置き、`ArmorPanel` の `_shardKind` に設定する
