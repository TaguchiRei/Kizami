# 区間6：スキル基盤・攻撃型スキル

| 項目 | 内容 |
|---|---|
| 状態 | 着手（2026-10-07） |
| 目安の時期 | 2026/12/08〜12/14（最速の推定 10/24〜10/25） |
| 前提となる区間 | 3, 5 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

チャージを消費して発動するスキルの仕組みと、攻撃型スキル 2 種を作る。
この区間が終わるとマイルストーンB（1 ステージが最初から最後まで遊べる）になる。

## 関連する仕様

- スキル：https://app.notion.com/p/3e91ea2aa7fa814ebd59e5038006aa39
- ダメージタイプ：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8
- アウトゲーム・通貨：https://app.notion.com/p/3e91ea2aa7fa81e3b271e0fc71189ea6

着手時の計画は、Notion を読めない環境（クラウドセッション）で、リポジトリの計画書とコードをもとに立てた。Notion の記述との突き合わせは済んでいない。

## 計画書と今のコードの食い違い（着手時に確認）

2026-10-07 に確認した。

| # | 内容 | 根拠 | 対応 |
|---|---|---|---|
| 1 | EngineAdapter はスキルの定義データ（External）を読めない | `Kizami.EngineAdapter.Runtime.asmdef` の参照に `Kizami.External.Runtime` がない | HUD が表示する枠の情報（名前・消費量）は、BlackBoard の State で渡す（`SkillSlotState`） |
| 2 | 融解の `VoxelMeltSystem` は `InGame` にも `TestStage` にも置かれていない | 置かれているのは `Development/Voxel` の 2 シーンだけ | 融解の演出は使わない（決定 5） |
| 3 | 6-1 の「種類（攻撃型・強化型）」は、強化型が区間10なので、この区間では使う所がない | 区間10の計画書 | 攻撃型と強化型を分けるデータは持たない。効果の種類（ビーム・爆発）だけを持つ |
| 4 | `VoxelDestructionAdapter` は、カメラの中心で狙った所を球で削ることしかできない | `Carve` は private で、形は `SphereShape` に固定 | 削る処理を形を問わない作りにし、ビームと爆発のメソッドを足す |
| 5 | スキル 1〜3 の入力（1 / 2 / 3 キー）はあるが、購読している所はない | `PlayerActions.Skill1`〜`Skill3`、`InputSystem_Actions.inputactions` | 計画どおり。`SkillService` が購読する |

## 既存の資産

| 資産 | 内容 |
|---|---|
| 区間3の成果 | チャージ量の `IChargeState`（`PlayerBoard`、InGame の SceneState）と、加算だけを持つ `ChargeService`（`PlayerInitializer` が生成）。消費の操作はこの区間で `ChargeService` に足す |
| 区間5の成果 | 破壊対象（`DestructionTarget`）、クリア判定（`StageClearAdapter`、仮の表示）、狙った所を球で削る `VoxelDestructionAdapter`（InGame の `VoxelDestruction`）、崩落による撃破とエネルギー（`ChargeService.AddCollapsedEnemies`） |
| [IVoxelShape](../../../Code/Scripts/EngineAdapterLayer/Voxel/Shapes/IVoxelShape.cs) | 球・箱・カプセルの形状 |
| 入力 | `Skill1`〜`Skill3`（PC は 1 / 2 / 3 キー。区間0） |

## 前提の変化（2026-10-07、区間5 の完了時）

- ダメージのデータ（種類の組み合わせ・量・形状・発生源）と判定の窓口は、区間5では作らずにこの区間へ持ち越した（区間5の決定 1）。剣はもともとボクセルを切らない（`MeleeCutAdapter` が `CuttableObject` だけを集める為）
- 破壊ダメージの量と削る形状の大きさの対応（区間5の決定 6）と、デバッグの破壊攻撃で敵が通れる穴を開けやすくするか（区間5の決めること #13）も、ここで決める
- スキルで壊したマップが崩れると、巻き込まれた敵は崩落で倒れ、チャージが増える（区間5）。完了条件の「スキルは敵に当たらない」は、スキルの攻撃そのものが敵に当たらないという意味で、崩落で倒れるのは仕様どおり

## 既存コードの確認結果（2026-10-07）

| 対象 | 状態 | 対応 |
|---|---|---|
| [ChargeService](../../../Code/Scripts/Application/Player/ChargeService.cs) | 加える操作だけ | 消費する操作 `TryConsume` を足す |
| [VoxelDestructionAdapter](../../../Code/Scripts/EngineAdapterLayer/Stage/VoxelDestructionAdapter.cs) | 狙った所を球で削るだけ。InGame の `_targetLayers` は Ignore Raycast・Player・Shard・Enemy・Blade を除く | 削る処理を形を問わない作りにし、ビーム（カプセル）と爆発（球）を足す。敵のレイヤーは除かれているので、スキルは敵に当たらない |
| [PlayerInitializer](../../../Code/Scripts/Initialization/Player/PlayerInitializer.cs) | チャージ・切断の配線を持つ。`VoxelDestructionAdapter` は持たない | `SkillService` の生成と、効果の関数・HUD の配線を足す |
| [PlayerDebugInitializer](../../../Code/Scripts/Initialization/Player/PlayerDebugInitializer.cs) | G キーで `CarveAtAim` を呼ぶ | 変更なし（決定 9） |
| `InGame.unity` | HUD はない。クリアの表示は `StageClearAdapter` の `OnGUI` | 仮の HUD を置く（ローカルで行う。決定 12） |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 6-1 | スキルの定義データ | ExternalLayer | `SkillData`。表示名、効果の種類（ビーム・爆発）、消費量、半径、長さ（食い違い #3） |
| 6-2 | 装備枠の State | BlackBoard / Application | 3 つの装備枠の名前と消費量を持つ `SkillSlotState`（`SkillService` が書き、HUD が読む）。装備は `PlayerInitializer` の Inspector に置いた仮のデータで、区間11でアウトゲームから受け取る（決定 11） |
| 6-3 | 発動 | Application | `SkillService` がスキル 1〜3 の入力を購読し、チャージを消費できたら効果の関数を呼ぶ |
| 6-4 | 前方ビーム | EngineAdapter | カメラの位置から視線の向きへ、カプセルで一度だけ削る（決定 4） |
| 6-5 | 自分中心の爆発 | EngineAdapter | カメラの位置を中心に、球で削る（決定 6） |
| 6-6 | 仮の HUD | EngineAdapter | `OnGUI` で、チャージ量と装備している 3 枠を表示する（決定 10） |

## 完了条件

- スキルを発動するとチャージが減り、破壊対象とマップが削れる
- スキルは敵に当たらない
- 雑魚敵を切ってチャージを溜め、スキルで破壊対象を削ってクリアするまでが一通り遊べる（マイルストーンB）

## 詳細仕様

### 決定（2026-10-07）

| # | 項目 | 決定 |
|---|---|---|
| 1 | 攻撃型スキル 2 種と消費量 | 前方ビーム（消費 30）と自分中心の爆発（消費 50）。チャージの上限は 100（`PlayerParameterData.MaxCharge`）のまま。値は仮 |
| 2 | クールタイム | なし。チャージの消費が使える回数を抑える。連打が目立てば区間13で足す |
| 3 | チャージが足りないとき | 発動せず、消費もしない。HUD で、足りない枠の色を変える |
| 4 | ビームを一瞬で出すか、出し続けるか | 一瞬で出す。カメラの位置から視線の向きへ、長さ 30m・半径 1m（仮）のカプセルで一度だけ削る。壁を貫通する。出し続ける方式は、毎フレームの消費と発動中の状態が要るので作らない |
| 5 | 融解の演出 | 使わない（食い違い #2）。区間13（または区間10）へ持ち越す。ビームと爆発の見た目の演出も作らず、削れること自体を手応えにする |
| 6 | 爆発の中心と、足場が削れること | 中心はカメラの位置、半径 4m（仮）。ボクセルの足場の上で使うと足場も削れて落ちるのは、仕様として認める |
| 7 | ダメージのデータと判定の窓口を置く層（区間5から持ち越し） | 作らず、区間7へ持ち越す。区間6でスキルの攻撃を受けるのはボクセルだけで、受け手が 1 種類しかない。種類で結果が変わる相手（粉砕のダメージ、装甲）が出るのは区間7・8 |
| 8 | 破壊ダメージの量と削る形状の大きさの対応（区間5から持ち越し） | ダメージ量は持たず、`SkillData` に形の大きさ（半径・長さ）を直接書く。量が要るのは、装甲の耐久値が入る区間8 |
| 9 | デバッグの破壊攻撃で敵が通れる穴を開けやすくするか（区間5の決めること #13） | デバッグの攻撃は今のまま。穴を開けるのは壁を貫通するビームが受け持つ。足りなければ、コミット 1 の確認のあとで Inspector の半径を上げる（区間5の案A） |
| 10 | 仮の HUD の作り方 | `OnGUI` で描く Adapter（`StageClearAdapter` の仮の表示と同じ作り方）。区間11の本番の HUD で置き換える |
| 11 | 仮の装備 | `PlayerInitializer` の Inspector に `SkillData` を 3 枠ぶん並べる（1：ビーム、2：爆発、3：空き） |
| 12 | シーンの配線と `SkillData` のアセット作成 | ローカル（Unity エディタ）で行う。クラウドではコードと手順までを書く（`.unity` と `.asset` の GUID を確かめる手段がない為） |

## 作業計画

### 新しく作る型と、区間6 での利用者

| 型 | 層・置き場所 | 区間6 での利用者 |
|---|---|---|
| `SkillData`（ScriptableObject）と、効果の種類の enum `SkillEffect` | External | `SkillService` |
| `SkillService`（スキル 1〜3 の入力 → チャージの消費 → 効果の関数を呼ぶ） | Application | `PlayerInitializer` |
| `SkillSlotState` と `ISkillSlotState`（3 枠の名前と消費量） | BlackBoard（`PlayerBoard`、SceneState） | 書くのは `SkillService`、読むのは `SkillHudAdapter` |
| `SkillHudAdapter`（仮の HUD） | EngineAdapter、InGame | `PlayerInitializer` |

既存の型の拡張：`ChargeService`（`TryConsume`）、`VoxelDestructionAdapter`（ビームと爆発）、`PlayerInitializer`（配線）。

- 基準1：攻撃型と強化型の種類分け（食い違い #3）、ダメージの窓口（決定 7）、ダメージ量（決定 8）、クールタイム（決定 2）、見た目の演出（決定 5）は、区間6で使う所がないので作らない。新しい Initializer も作らず、`PlayerInitializer` を広げる（`InGameCompositor` の作り直しは要らない）。`SkillSlotState` は今の利用者が HUD 1 つだが、HUD（EngineAdapter）は asmdef の参照ルールで `SkillData` と `SkillService` を読めないので、層の分離の例外として作る（食い違い #1）
- 基準2：EngineAdapter が External を読めないことで表示用の State が 1 つ増えるが、区間11でアウトゲームから装備を受け取るときの置き場にもなるので、設計の見直しは提案しない
- 基準4：リスクは挙げていない。Notion の仕様を読めていないことは確認事項としてユーザーに伝えた

### コミットの分け方

| # | 内容 | 確かめ方（ローカル） |
|---|---|---|
| 0 | 区間計画書の更新 | ― |
| 1 | スキルの基盤と前方ビーム（`SkillData`、`SkillService`、`ChargeService.TryConsume`、`VoxelDestructionAdapter` のビーム、`PlayerInitializer` の配線） | チャージが 30 以上のときに 1 キーで、チャージが 30 減り、視線の先の壁と門が貫通して削れる。30 未満では何も起きない。敵には当たらない |
| 1.5 | テストプレイ用の少人数の敵の設定（TestStage の `EnemySpawnSystem_Few`）。計画の後で追加した（1000 体だと敵がプレイヤーを囲み、確認がしにくい為） | 敵が 10 体・2 つの群れで出る。倒すと補充される |
| 2 | 自分中心の爆発 | 2 キーで、周りのボクセルが球の形に削れ、チャージが 50 減る |
| 3 | 装備枠の State と仮の HUD（`SkillSlotState`、`SkillHudAdapter`） | チャージ量と 3 枠が出る。チャージが足りない枠は色が変わる |
| 4 | 通しの確認（マイルストーンB）、値の調整、実装結果と全体計画書の更新 | 敵を切って溜め、スキルで門の核を削り、「STAGE CLEAR」まで遊べる |

クラウドで書いたコミットは、コンパイルと動作を確かめていない。ローカルに引き取ってから、Application / BlackBoard / External / EngineAdapter / Initialization の asmdef をコンパイルし、常駐シーンから再生して TestStage で確かめる。

## 実装中に確かめたこと

- （コミット 1）ローカルでコンパイルし、エラーと警告は 0 件。ビームの `SkillData` を `Assets/Level/Data/Player/Skill_Beam.asset`（消費 30、半径 1、長さ 30）に作り、InGame の `PlayerInitializer` に `VoxelDestructionAdapter`（`VoxelDestruction`）と装備の 0 番（ビーム）を割り当てた
- （コミット 1）プレイヤーを門 B の右の柱の正面（-17, 0, -5）に置き、1 キーを押して確かめた。チャージ 0 と 10 では何も起きなかった。チャージ 40 ではチャージが 10 になり、柱の体積が 70.4% になった。ビームの軸上（高さ 0.9m）は柱の手前から奥まで空になり、軸から外れた高さ 3m は残っていた（貫通している）。敵は 1000 体のまま、崩落による撃破は 0 体で、エラーは 0 件。ビームは Enemy のレイヤーを除いて削る相手を探すので、敵には当たらない
- （コミット 1.5）TestStage の `EnemySpawnSystem` を `EnemySpawnSystem_Crowd`（1000 体、無効）と `EnemySpawnSystem_Few`（有効）に分けた。`EnemySpawnAdapter` は `FindAnyObjectByType` で有効な方だけを見つける。`_Few` は、同時に存在する上限 10、初期配置は北と南の 2 か所 × 5 体（群れは 1 つ 12 人までなので 2 つになる）、生成位置と実行中の追加（3 秒ごとに 1 体）は `_Crowd` と同じ。群衆の挙動や負荷を見るときは、有効にする方を入れ替える
- （コミット 1.5）プレイモードで、敵は 10 体（南 5、北 5）・群れ 2 つで出た。2 体を格子より下に落として倒すと、8 秒ほどで 10 体に戻った。敵の処理は 0.16ms で、エラーと警告は 0 件

## 次の区間へ持ち越すこと（計画の時点）

- 区間7：ダメージのデータと判定の窓口（決定 7）
- 区間8：ダメージ量と、装甲の耐久値との対応（決定 8）
- 区間10：攻撃型と強化型の種類分け（食い違い #3）
- 区間11：装備をアウトゲームから受け取る（決定 11）、仮の HUD の置き換え（決定 10）
- 区間13：ビームと爆発の見た目の演出、融解を使うか（決定 5）、クールタイムが要るか（決定 2）
- 仕様書：決定 1〜6 を Notion のスキルの仕様に反映するか（この区間の計画は Notion を読まずに立てた）

## 他プラットフォームへの対応

- 発動の入力はプラットフォームごとに違う（PC は 1 / 2 / 3 キー）。入力の層で吸収する
- VR では、ビームの向きを視線にするか手の向きにするかを、対応するときに決める
- 仮の HUD は `OnGUI` なので、VR では見えない。区間11の本番の HUD で扱う

## 見つけた問題（今回は扱わない）

- なし
