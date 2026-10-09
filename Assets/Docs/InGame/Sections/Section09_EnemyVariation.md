# 区間9：敵の固有アクション・バリエーション

| 項目 | 内容 |
|---|---|
| 状態 | 実装中（2026-10-09 着手） |
| 目安の時期 | 2027/01/26〜02/08（最速の推定 2026/10/13〜10/16） |
| 前提となる区間 | 4D, 8 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

雑魚敵 3 種（攻撃する敵＝アタッカー、シールドを持つ敵＝ディフェンダー、吸収型の敵＝フィニッシャー）の種類と編成を作り、それぞれの固有のアクションを作る。
固有のアクションは「最後の方に作る」方針（2026-10-05）で区間11のあとに置いたが、2026-10-08 に区間10・11の前へ戻した。区間11（失敗の流れ）がなくても、この区間の完了条件は確かめられる為。敵の攻撃で HP が 0 になっても、区間11までは何も起きない。

## 関連する仕様

- 敵（攻撃する敵・シールドを持つ敵・吸収型の敵）：https://app.notion.com/p/3e91ea2aa7fa81f3a713c0a6e64d438e
- 装甲：https://app.notion.com/p/3e91ea2aa7fa8163a34fdb3a9aa4c4ac
- 仕様検討リストの経緯（雑魚敵の固有アクション）：https://app.notion.com/p/3f41ea2aa7fa81389d37ed677fd2491f
- 群衆の改変（区間9に関わること）：[EnemyCrowdRedesign.md](../EnemyCrowdRedesign.md)

## 前提

- 部位の役割は `[SerializeReference]` ＋ `SubclassSelector` で選ぶ `EnemyPartRole` のサブクラス（今あるのは `CorePartRole`・`MovePartRole`）。攻撃部位の役割は、区間4A では読む処理がなかったので作っていない（区間4A の決定 24）
- 敵の状態は NativeArray（`EnemyAgent`）に持ち、近くの敵にだけ体を貸す。失った部位と壊れた部位は部位ごとのビットで持つので、体の部位は 32 個まで
- 区間4D で交戦はなくなった。敵は隊列を離れず、着いたグループはグループの中心の螺旋（6m 間隔、0 番が中心）に並んでプレイヤーを向く。プレイヤーから 20m より内へは入らず、着いたメンバーはプレイヤーから約 14〜36m にいる。螺旋に並んでいる間は `EnemyGroup.HasArrived` が true（待つ置き場にいるグループは true にならない）
- 装甲は区間8の `ArmorPanel`。耐久値（10）を持ち、攻撃タイプと破壊タイプが 1 回当たるごとに 1 減り、粉砕タイプ（投げたかけら）で一撃で壊れる。スキルのビームは Armor レイヤーで止まる
- Player と Enemy のレイヤーはぶつからず、Player と Armor はぶつかる（uloop で実測）
- 敵の攻撃は `Time.deltaTime` で進めるので、スロー中は一緒に遅くなる。ワープ中のダメージの軽減率は 100%（`PlayerParameterData`）
- 音を鳴らす仕組みはまだない。基盤はユーザーがあとで用意する

### モデルの部位（uloop で実測）

| モデル | 部位 | 寸法など |
|---|---|---|
| `MachineEnemy_Attacker` | 胴（`Body`）、脚 4 本（`〜UpperLeg` / `〜UnderLeg`）、`GunTurret`、その子の `Gun` | 今の `AttackerEnemy.prefab`。役割は脚 8 部位が移動、胴が核、`GunTurret`・`Gun` は役割なし |
| `MachineEnemy_Defender` | 胴、脚 4 本（前脚の脛に盾が付いて太い）、`Shield` | 脚は前後へまっすぐ伸び、今の脚の IK で動く。`Shield`（幅 20.9m の前面の壁）は使わない（決めたことの 7） |
| `MachineEnemy_Finisher` | 胴（高さ 3.0m）、腕 4 本（`〜UpperArm` / `〜Forearm`） | 上腕は肩（高さ 3.55m）から斜め外の下へ伸び（肘が (±1.4, −0.94, ±1.4)）、前腕は真下へ約 2.6m 伸びて地面に着く。休みの姿勢がまっすぐではない |

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間4A〜4D の成果 | 敵の状態、体の貸し借り（`EnemyBodyLender`）、まとめて描画（`EnemyCrowdRenderer`）、脚の IK（`EnemyLegs`）、グループと二重螺旋（`EnemyGroups`・`EnemyGroupJob`・`EnemyMoveJob`）、部位の役割 |
| 区間5の成果 | エネルギーの VFX Graph（`EnemyEnergyAdapter`。GraphicsBuffer で位置を渡し、`EnergyTarget` へ吸い込ませる）、見た目用の部位のディゾルブ（`EnemyDebrisSpawner`） |
| 区間8の成果 | `ArmorPanel`、剣・投げたかけら・スキルのビームの装甲への効き方 |
| Unity | `SpherecastCommand`・`RaycastCommand`（Job でまとめて調べる）、URP の Decal Projector |

## 既存コードの確認結果（2026-10-09）

| 対象 | 状態 | 対応 |
|---|---|---|
| `EnemySpawnAdapter._bodyPrefab`（[EnemySpawnAdapter.cs:66](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)） | 体のプレハブは 1 つ。`EnemyCrowdRenderer`（352 行）と `EnemyBodyLender`（371 行）を 1 つずつ作る | 種類ごとに作る（決めたことの 1） |
| 移動部位の上限（[EnemySpawnAdapter.cs:492・497・541](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnAdapter.cs)） | `EnemyMoveJob`、`EnemyGroups.MaintainNext`、`ReturnStrandedAgents` の 3 か所が `_bodyPrefab.BrokenMovePartLimit` を読む | 敵ごとの値にする（決めたことの 1） |
| `EnemySpawnInfo`（[EnemySpawnSystem.cs:51](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemySpawnSystem.cs)） | 出す敵の種類を持たない（TODO） | 編成を持たせる（決めたことの 2） |
| `EnemyLegs.Awake`・`GetMeshLength`・`Solve`（[EnemyLegs.cs:73・161・168・263](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyLegs.cs)） | 腿と脛のどちらにも膝の向き（`_axes`）を休みの向きとして使う。脛の長さは範囲の z だけで測る。足を置く基準の位置は、脚の向きの z と決まった左右のずらしで決める | フィニッシャーの腕が動かない。脛の休みの向きを持たせる（決めたことの 3） |
| `EnemyGroups.MaintainNext`（[EnemyGroups.cs:227〜313](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyGroups.cs)） | 穴詰め（`Compact`）、合流（`TryMerge`。ほかのグループの最後に付け足す）、移動中の並べ替え（`Reorder`。螺旋に並んでいる間はしない） | ディフェンダーを 0 番に保つ（決めたことの 4） |
| `MeleeCutAdapter.CollectTargets`（[MeleeCutAdapter.cs:126](../../../Code/Scripts/EngineAdapterLayer/Player/MeleeCutAdapter.cs)） | 装甲と切る対象を同じ箱で別々に集める。装甲の奥の敵も切れる（区間8から持ち越し） | 装甲の奥の対象を切らない（決めたことの 8） |
| `ArmorPanel.Awake`・`Break`（[ArmorPanel.cs:75・88](../../../Code/Scripts/EngineAdapterLayer/Stage/ArmorPanel.cs)） | 耐久値は Awake で最大にし、壊れたら非アクティブにする。外から耐久値を戻せない | プールで使い回すバリアと分身のために、耐久値を戻す口と、壊れたことを知らせる口を足す |
| `PlayerMovementService.Step`（[PlayerMovementService.cs:112](../../../Code/Scripts/Application/Player/PlayerMovementService.cs)） | ジャンプの打ち出し速度を戻り値で返し、Adapter が Rigidbody に与える。Compositor の注入の対象ではない | 打ち上げの要求を足し、次の `Step` で返す。`PlayerInitializer` で登録する（決めたことの 9）。登録は Compositor の収集の段階（`Awake`）に限られるので、`PlayerHealthService` と同じく生成と `Initialize` を分けた（コミット 6） |
| `InGameCompositor` | 生成物。`PlayerHealthService` を `PlayerDebugInitializer` へ注入している | `EnemyInitializer` に `IInjectable` を足したら、UsefulToolkit のメニューで作り直す。手では直さない |
| `PC_Renderer`・`Mobile_Renderer` | Renderer Feature は SSAO だけ | Decal Renderer Feature を足す（ユーザーの了承済み） |
| `EnemyEnergyAdapter`（[EnemyEnergyAdapter.cs:83](../../../Code/Scripts/EngineAdapterLayer/Player/EnemyEnergyAdapter.cs)） | 吸い込む先（`EnergyTarget`）は毎フレーム MainCamera | 同じ VFX Graph を別の VisualEffect で使い、吸い込む先をフィニッシャーにする（決めたことの 11） |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 9-3 | 敵の種類と編成 | EngineAdapter | 種類ごとの体のプールとまとめて描画、生成情報の編成、ディフェンダーを 0 番に保つ。ほかの作業の土台なので最初に行う |
| 9-4 | 敵のプレハブ | Level / EngineAdapter | 届いたモデルからディフェンダーとフィニッシャーの体のプレハブを作る。フィニッシャーの腕のために脚の IK を広げる |
| 9-0 | アタッカーの攻撃 | EngineAdapter / Application | タレットから弾を撃つ。ダメージの配線 |
| 9-1 | ディフェンダーのバリア | EngineAdapter / Application | 球のバリア、剣の遮断、プレイヤーの打ち上げ |
| 9-2 | フィニッシャーの吸収とビーム | EngineAdapter | 吸収、分身、デカールの予兆、特大のビームと追いかけるビーム、冷却 |
| 9-5 | 特殊部位 | ― | この区間では作らずに持ち越す（決めたことの 13） |

## 完了条件

- アタッカーが射程の中のプレイヤーへ弾を撃ち、当たると HP が減る。タレットを切ると撃たない
- ディフェンダーが、着いたグループをバリアで覆う。バリアを壊すまで中の敵を切れず、プレイヤーは中へ入れない。壊すと中の敵を切れる
- フィニッシャーが仲間を吸収し、分身してビームを撃ってくる。ビームは屋根の下に隠れるか、横への移動とワープでよけられる

## 詳細仕様で決めること

2026-10-09 にユーザーと決めた（確定）。値に「仮」とあるものは実機で調整する。

### 種類と編成

| # | 項目 | 決めたこと |
|---|---|---|
| 1 | 敵の種類の持ち方 | 列挙 `EnemyKind`（Attacker / Defender / Finisher）を作り、`EnemyAgent` に種類と移動部位の上限を持たせる（出すときにプレハブの値を書き、Job とグループはこれを読む）。`EnemySpawnAdapter` は種類ごとに「プレハブ・体の数」の設定を持ち、`EnemyBodyLender` と `EnemyCrowdRenderer` を種類ごとに作る。`EnemyAgent.BodyIndex` は、その種類の `EnemyBodyLender` の中の番号 |
| 2 | 編成 | `EnemySpawnInfo` と `EnemyInitialSpawnArea` に、グループの編成（種類の並び。例：Defender, Attacker×11）を持たせ、その順でグループを埋める。空なら全部 Attacker にして、今のシーンの挙動を変えない |
| 3 | フィニッシャーの腕と移動 | `EnemyLeg` に「脛の休みの向き」を足す。既定値（ゼロ）は腿と同じ向きなので、アタッカーとディフェンダーは変わらない。フィニッシャーは真下を指定する。脛の長さはその向きに沿って測り、足を置く基準の位置は、脚の向きの水平の成分（x と z）から決める。フィニッシャーは歩かずに宙に浮いて移動する（ユーザーの指摘で、コミット 8 のあとに変えた。Notion にも反映した）。浮くのは見た目で、プレハブのモデルの部位を 2m（仮）上げ、体の根は地面に置いたまま隊列・経路の処理をほかの敵と共有する。`EnemyLegs` に「歩かない」設定を足し、歩かない体は足を運ばず、腕の先を畳んだ位置（体の根の軸から 0.9m、高さ 4.5m、仮）に置く。遠くのまとめての描画もプレハブの形を使うので、畳んだ姿勢をプレハブ（本体と分身）に焼き込んだ。腕は移動部位のまま。浮いている敵（`EnemyAgent.IsFloating`、体のプレハブの値）は、足場が壊れると秒速 2m（仮）を上限にゆっくり落ち、落ちて着地しても倒れない。落ちてくる塊には潰される |
| 4 | ディフェンダーの位置 | 隊列の 0 番（着いたら内側の螺旋の中心）に置き、常に 0 番に保つ。移動中の並べ替えでは先頭に固定する。穴詰め（`EnemyGroups.Compact`）のたびに、最初のディフェンダーを 0 番へ移す。合流や戻れない敵の移し替えで後ろに入っても、ここで中心に戻るので、合流は止めない（コミット 3 で、「合流の元にも先にもしない」から変えた。合流を止めると、残った少人数のグループがディフェンダーのグループに入れない為） |
| 5 | 攻撃部位の役割 | `AttackPartRole` を足して `GunTurret`・`Gun` に付ける。働きは目印だけで、壊れた部位のビットに入っていれば撃たない |

### アタッカーの攻撃

| # | 項目 | 決めたこと |
|---|---|---|
| 6 | 弾 | タレットの先から、弾速のある球を撃つ。位置・速度・寿命を NativeArray に持って Burst の Job で進め、前のフレームの位置から今の位置までを `SpherecastCommand` で調べて、壁と装甲で止める。プレイヤーへの当たりは、プレイヤーのカプセルとの距離で Job の中で計算する。描画は `EnemyEnergyAdapter` と同じく、GraphicsBuffer で位置を VFX Graph へ渡す。撃つ条件は、射程 50m 以内・タレットからプレイヤーまで壁がない（`RaycastCommand`）・攻撃部位が残っている・同時に撃つ敵の数の上限（仮 4）。仮の値は秒速 20m、ダメージ 5、間隔 3 秒 ±30%、半径 0.3m。体を貸していない遠くの敵も、`EnemyAgent` の値だけで撃つ。同時に撃つ敵の数の上限は、同時に飛んでいる弾の数の上限として持つ（1 体が 1 発ずつ撃つ為）。銃口は、攻撃部位をまとめた範囲の前の端。壁にさえぎられた敵は 0.5 秒後に調べ直す。弾の VFX Graph はユーザーが [Section09_BulletVfxGraph.md](Section09_BulletVfxGraph.md) の手順で組む（グラフをスクリプトから組む公開の方法がない為） |

### ディフェンダーのバリア

| # | 項目 | 決めたこと |
|---|---|---|
| 7 | バリアの形と見た目 | モデルの `Shield` はプレハブで非アクティブにし、部位に入れない。グループが着いて並ぶと、前脚 2 本の足を体の前で合わせる姿勢をとり（体を貸しているときだけ）、グループ全体を覆う球のバリアを出す。半径は 17m（仮。最も外のメンバーの位置の約 11.2m に、体の半分の約 5.3m を足した値）。見た目は半透明の球（仮のマテリアル）。張るのは `HasArrived` の間だけ |
| 8 | 当たり判定と効き方 | 張っているバリアの数だけ、球のバリアの物（SphereCollider を Armor レイヤーに置き、Kinematic の Rigidbody と `ArmorPanel` を付けたもの）をプールから貸す。耐久値（10）は `EnemyGroup` に持ち、貸し借りしても続く。壊れたバリアは張り直さない（仮）。効き方は装甲と同じ（剣は 1 回で 1 減り、投げたかけらで一撃、スキルのビームは止まる）。剣は、切る対象の中心とカメラの間に Armor レイヤーへレイを撃ち、当たったら切らない（`VoxelDestructionAdapter.IsBehindArmor` と同じ判定）。崩落は防がない。プレイヤーはドームの上に乗れ、敵は乗れない（ユーザーが確定。プレイヤーの接地の判定は Armor レイヤーを含み、敵の経路の格子が床として読むのは Default と Wall のレイヤーだけなので、今の作りのままでそうなる） |
| 8a | 割れたとき | 割れたことを知らせる口を作り、音は `// TODO:` で残す（基盤はユーザーがあとで用意する）。割れる演出の作り込みは区間13 |
| 8b | 敵の弾とバリア | 弾の `SpherecastCommand` は、調べ始めに重なっている当たり判定には当たらないので、内から外へは抜け、外から内へは止まる見込み。コミット 5 で確かめる |
| 9 | プレイヤーの打ち上げ | バリアを張るときにプレイヤーが球の中にいたら、球の面の高さに余裕を足した高さまで、プレイヤーのカプセルを上へ CapsuleCast で調べる。さえぎる物がなければ打ち上げ、プレイヤーが球の外へ出たら張る。さえぎる物があれば何もせず、プレイヤーが外へ出るまで張らない。打ち上げは `PlayerMovementService` に要求として渡し、次の `Step` でジャンプと同じく打ち出し速度として返す。プレイヤーが要る速度の 9 割以上で上がっている間（打ち上げたあと）は要求し直さない。打ち上げは壁走りとジャンプより優先し、ワープ中は捨てる。壁に触れたまま打ち上げられても壁走りに戻らないよう、壁ジャンプ後と同じく再突入を止める（コミット 6） |

### フィニッシャーの吸収とビーム

| # | 項目 | 決めたこと |
|---|---|---|
| 10 | 始める条件 | グループが螺旋に並んでいて（`HasArrived`）、プレイヤーが 50m 以内、冷却中でなく、同じグループにほかの生きているメンバーがいる。攻撃中のフィニッシャーはマップ全体で 1 体まで |
| 11 | 吸収 | 同じグループの最も近いメンバーを選ぶ。選ばれた敵はその場で止まる。フィニッシャーは飛んでいる状態になり（地面に立つ処理と重力を止める）、対象の上（体の根どうしで 1m、仮）へ移る。畳んでいた腕を開き（0.4 秒、仮）、閉じて対象をつかみ（0.3 秒、仮）、1 秒（仮）で吸収する。吸収したあとは 0.3 秒（仮）で腕を畳む。腕の動きは `EnemyLegs` の足の行き先の上書きで作る。吸収された敵は、切っていない部位も切った部位もディゾルブで消え（オーブもチャージも出さない）、エネルギーの VFX Graph を別の VisualEffect で使って、小さな粒をフィニッシャーへ吸い込ませる。止まる・飛ぶは `EnemyAgent.MoveMode`（`EnemyMoveMode` の Walking・Held・Flying）で持ち、移動の Job が読む。ディゾルブは、体の全レンダラーのマテリアルを、区間5の見た目用の部位と同じディゾルブのマテリアルに差し替えて行い、体を返すときに戻す。消えている途中の体は切れなくする（かけらにディゾルブのマテリアルが写る為）。吸収を始めたあとにフィニッシャーが倒れたら、対象もそのまま消す（消えかけた体を戻して歩かせない）。止められたアタッカーは撃たない（コミット 7） |
| 12 | 行動中の敵の体 | 攻撃中のフィニッシャーと選ばれた敵には、距離によらず体を貸す（優先して貸す）。腕でつかむ動きとディゾルブを、遠くでも見せる為。空きがなければ、ほかの敵から体を取り上げて貸す。行動中の敵の体は、離れても返させず、取り上げの相手にもしない。行動中の敵は、戻れない敵を持ち場へ戻す処理の対象にもしない（コミット 7） |
| 13 | 特殊部位（9-5） | 作らずに持ち越す。仕様が「検討中」で、届いたモデルに当てはまる部位がなく、完了条件にも入っていない為 |
| 14 | 分身 | 吸収したあと、本体と 4 体の分身がプレイヤーの上 25m（仮）へ移る。本体が中心で、分身は周りを回る。分身はフィニッシャーの見た目を色違いのマテリアルにしたプレハブで、プールから出す。当たり判定（Armor レイヤー）と `ArmorPanel`（耐久値 3、仮）を持ち、装甲と同じく壊せる。グループにも移動にも敵の数にも入らない。壊れた分身のビームは止まる。分身は本体の周りを半径 8m・秒速 60 度（仮）で回り、出してから 1 秒で広がる。本体はプレイヤーの上を狙い直しながら飛び、3m まで近づいたら位置を決める（動くプレイヤーを追い続けると、飛ぶ先に着かない為）（コミット 8） |
| 15 | 予兆と特大のビーム | 地面に URP の Decal Projector で攻撃の範囲を出し、2 秒（仮）後に、本体が上から特大のビーム（半径 4m、ダメージ 40、仮）を撃つ。見た目は円柱。範囲は本体の真下でビームが止まる所に出す（赤い円のテクスチャは仮）。ビームは 0.5 秒（仮）出し、その間に 1 回だけダメージを与える。範囲が広いので、屋根の下かは、撃つ所の高さからプレイヤーの頭まで真下へ調べて決める。撃つ所を包む物は調べ始めに重なるので数えない（TestStage の見えない天井 `Ceiling` が、本体の浮く高さと重なる為）（コミット 8） |
| 16 | 追いかけるビーム | 分身 4 体が、細いビーム（半径 1m、0.5 秒ごとにダメージ 10、4 秒、仮）を撃つ。ビームの当たる所は、秒速 4m（仮）でプレイヤーを追う。当たる所にもデカールを出す。特大のビームと同時に撃ち始め、当たる所は各分身の真下から始める。ビームは分身から当たる所までの斜めの円柱で、間に物があればそこで止まる。当たりは円柱とプレイヤーのカプセルの距離で決める（コミット 8） |
| 17 | ビームを止める物 | ビームは上から下へ撃ち、最初に当たった物（Default・Wall・Armor のレイヤー）で止まる。円柱の長さもそこまでにする。屋根の下に隠れるか、横への移動とワープでよける |
| 18 | 冷却と中断 | 撃ち終えたら 15 秒（仮）は攻撃しない。分身とデカールは消え、本体は飛び立った位置へ飛んで降り、そこから歩いて自分の螺旋の位置へ戻る（コミット 7）。吸収する前に選ばれた敵が倒れたら、冷却に入って戻る。本体が倒れたら、分身・ビーム・デカールを消す |

### 配線

| # | 項目 | 決めたこと |
|---|---|---|
| 19 | ダメージと打ち上げの配線 | `EnemyInitializer` が `IInjectable<PlayerHealthService>` と `IInjectable<PlayerMovementService>` で受け取り、`ApplyDamage` と打ち上げの要求を関数として `EnemySpawnAdapter` へ渡す。`PlayerMovementService` は `PlayerInitializer` で `TryRegisterContent` に登録する。敵のルールは区間4A 以降すべて EngineAdapter にあり、読む側もいないので、Application に敵の Service は作らない |

## 作業計画

### 新しく作る型と、区間9での利用者

| 型 | 層 | 区間9での利用者 |
|---|---|---|
| `EnemyKind`（列挙） | EngineAdapter | 編成、`EnemyAgent`、`EnemySpawnAdapter` |
| `EnemyKindSettings`（仮。Serializable） | EngineAdapter | `EnemySpawnAdapter` の Inspector（3 種で 3 件） |
| `AttackPartRole` | EngineAdapter | アタッカーの `GunTurret`・`Gun`。撃つ判定が壊れた部位のビットと照らし合わせる |
| `EnemyShooter` | EngineAdapter | `EnemySpawnAdapter`。撃つ敵を選び、弾の NativeArray・Job・`SpherecastCommand`・VFX へ渡す GraphicsBuffer を持つ。設定と VisualEffect の参照を自分で持つよう、`EnemySpawnAdapter` と同じ GameObject のコンポーネントにした（コミット 4） |
| `EnemyBarriers` | EngineAdapter | `EnemySpawnAdapter`。バリアのプール、当たった数の受け渡し、前脚を合わせる姿勢、打ち上げの判定（コミット 6）。`EnemyShooter` と同じく、`EnemySpawnAdapter` と同じ GameObject のコンポーネントにした（コミット 5） |
| `EnemyMoveMode`（列挙） | EngineAdapter | `EnemyAgent`、`EnemyMoveJob`、`EnemyBodyLender`、`EnemyFinisherAttack`。歩く・止まる・飛ぶ（コミット 7） |
| `EnemyFinisherBeams` | EngineAdapter | `EnemyFinisherAttack`。分身のプール、予兆のデカール、特大のビームと追いかけるビーム。`EnemyFinisherAttack` が大きくなりすぎない為に分け、同じ GameObject のコンポーネントにした（コミット 8） |
| `EnemyBeam` | EngineAdapter | `EnemyFinisherBeams`。ビーム 1 本の見た目（円柱とデカール）（コミット 8） |
| `CapsuleGeometry` | EngineAdapter | `EnemyShooter`・`EnemyBarriers`・`EnemyFinisherBeams`。プレイヤーのカプセルの軸と、線分どうしの距離。3 か所目の利用者ができたので、弾とバリアにあった同じ計算をまとめた（コミット 8） |
| `EnemyFinisherAttack` | EngineAdapter | `EnemySpawnAdapter`。吸収、冷却。`EnemyShooter` と同じく、`EnemySpawnAdapter` と同じ GameObject のコンポーネントにした。攻撃の段階と時間は、`EnemyAgent` ではなくここに持つ（攻撃するのはマップ全体で 1 体だけの為）（コミット 7） |

- 弾・バリア・フィニッシャーの 3 つは、どれも利用者が `EnemySpawnAdapter` だけ。区間8R で `EnemyBodyLender`・`EnemyDebrisSpawner` を切り出したのと同じ理由で、`EnemySpawnAdapter` に書かずに分ける
- 作らないもの：敵の State・Event・Service、シールドの役割のクラス、弾 1 発ごとの MonoBehaviour、分身を描くための新しい描画の仕組み

拡張する型：`EnemyAgent`（種類、移動部位の上限、動き方と飛ぶ先）、`EnemySpawnSystem`（`EnemySpawnInfo` の編成）、`EnemyInitialSpawnArea`（編成）、`EnemySpawnAdapter`、`EnemyBodyLender`（種類で絞る、行動中の敵に優先して貸す）、`EnemyCrowdRenderer`（種類で絞る）、`EnemyBody`（吸収で消す）、`EnemyLeg`・`EnemyLegs`（脛の休みの向き、足の行き先の上書き、休みの姿勢の足先）、`EnemyShooter`（止められた敵は撃たない）、`EnemyGroup`（バリアの耐久値）、`EnemyGroups`（ディフェンダーを 0 番に保つ）、`EnemyMoveJob`（敵ごとの移動部位の上限、飛んでいる敵と止まった敵）、`ArmorPanel`（耐久値を戻す口。壊れたことは `IsBroken` を毎フレーム読んで知るので、知らせる口は作らなかった）、`MeleeCutAdapter`（装甲の奥を切らない）、`PlayerMovementService`（打ち上げの要求、生成と初期化を分ける）、`PlayerInitializer`（登録）、`EnemyInitializer`（注入）

作るアセット：`DefenderEnemy.prefab`、`FinisherEnemy.prefab`、分身のプレハブ（`FinisherClone.prefab`）、バリアのプレハブ、弾の VFX Graph、予兆のデカールのプレハブ（`BeamTelegraph.prefab`）と円のテクスチャ、バリア・分身・デカール・ビームの仮のマテリアル。`Kizami.EngineAdapter.Runtime` の asmdef に `Unity.RenderPipelines.Universal.Runtime` の参照を足した（Decal Projector を使う為）

### コミットの分け方

| # | 内容 | 確かめること |
|---|---|---|
| 0 | 区間計画書の書き直し（この内容）。Notion の「敵」と仕様検討リストへの反映は済んでいる | ― |
| 1 | 敵の種類と、種類ごとの体のプールとまとめて描画（中身は Attacker だけ）。移動部位の上限を敵ごとの値にする（決めたことの 1） | 10 体と 1000 体で今と同じに動き、切れ、倒れる。敵の処理の時間が区間4D（1.28〜1.65ms）から大きく変わらない |
| 2 | 脚の IK で腕を扱えるようにする（3） | アタッカーの歩き方が変わらない（脚の基準の位置と長さの値が同じ） |
| 3 | 編成、ディフェンダーとフィニッシャーのプレハブ、ディフェンダーを 0 番に保つ（2・4・7 の `Shield`）。TestStage の初期生成の範囲に編成を入れる | 3 種が出て歩き、切ると核で倒れる。フィニッシャーが腕で歩く。着いたグループの中心にディフェンダーがいて、移動中も合流しても 0 番のまま |
| 4 | アタッカーの弾とダメージの配線（5・6・19 のダメージ）。`AttackPartRole` を作ってアタッカーのプレハブに付ける。Compositor を作り直す | 射程の中で撃ち、当たると HP が減る。壁の陰からは撃たない。タレットを切ると撃たない。スロー中は弾が遅い。ワープ中は無傷 |
| 5 | バリア（7・8・8a・8b）。`ArmorPanel` の口、剣の遮断、前脚を合わせる姿勢 | 着くとバリアが出る。剣 10 回か投げたかけら 1 個で割れる。割れるまで中の敵を切れず、プレイヤーが入れない。スキルのビームが止まる。中の弾が外へ抜ける。崩落で中の敵が倒れる |
| 6 | プレイヤーの打ち上げ（9・19 の打ち上げ） | 球の中にいるときに打ち上げられ、外へ出てからバリアが出る。天井の下では打ち上げられず、外へ出るまでバリアが出ない |
| 7 | フィニッシャーの前半（10・11・12） | 仲間を選んで上へ移り、腕でつかんで吸収する。吸収された敵がディゾルブで消え、粒が吸い込まれる。チャージは増えない。遠くでも体を借りて見える |
| 8 | フィニッシャーの後半（14〜18）。Decal Renderer Feature を足す | 分身してプレイヤーの上へ移り、デカールのあと特大のビーム、分身が追いかけるビームを撃つ。屋根の下とワープでよけられる。分身を投げたかけらで壊すと、そのビームが止まる。冷却のあと戻る |
| 9 | 通しの確認、値の調整、実装結果と全体計画書の更新 | 完了条件 3 つ。敵の処理の時間 |

### 基準の当てはめで見直した点（2026-10-09）

- 基準1：種類ごとの「貸す距離」は持たない。バリアの当たり判定を体に付けない形になったので、要るのは「行動中の敵に優先して貸す」だけになった（決めたことの 12）
- 基準1：分身は見た目だけの `EnemyAgent` にせず、当たり判定と `ArmorPanel` を持つプレハブにした。壊せる仕様で、どのみち GameObject が要る為
- 基準1：`EnemyLegs` の足の行き先の上書き（`HoldFoot`・`ReleaseFoot`。行き先はワールド座標）は、ディフェンダーの前脚とフィニッシャーの腕の 2 つが使う。`ArmorPanel` の耐久値を戻す口（`SetDurability`）は、バリアと分身の 2 つが使う
- 基準1：バリアの当たった数は、耐久値ではなく当たった数（`EnemyGroup.BarrierHitCount`）で持つ。グループを作るときに耐久値の最大を知らなくても 0 から始められる為
- 基準2：打ち上げは、敵の側からプレイヤーの Rigidbody を書き換えず、ジャンプと同じ経路（`PlayerMovementService.Step` の戻り値）で渡す
- 基準3：フィニッシャーの腕の IK（決めたことの 3）とディフェンダーの位置（4）は、9-4 と 9-1 の実装に直接かかわるので計画に入れた
- 基準4：弾がバリアを内から外へ抜けること（8b）は確かめていないので、コミット 5 の確かめることに入れた

## 他プラットフォームへの対応

- 敵の攻撃にプラットフォームによる違いはない。Decal Renderer Feature は `PC_Renderer` と `Mobile_Renderer` の両方に足す

## 次の区間へ持ち越すこと（計画の時点）

- 区間13：バリアが割れる演出の作り込み。割れた音（音の基盤ができてから）
- 特殊部位（9-5）：仕様が決まったら、役割のクラスを足して作る

## 見つけた問題（今回は扱わない）

- 【見た目】体を貸していない敵は休みの姿勢で描かれるので、ディフェンダーの前脚を合わせる姿勢は、体を貸している近くの敵にしか出ない（区間4C の「見つけた問題」と同じ）
