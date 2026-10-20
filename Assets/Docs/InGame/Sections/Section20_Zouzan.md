# 区間20：土遁・造山

| 項目 | 内容 |
|---|---|
| 状態 | 計画済み（詳細仕様の確定待ち。2026-10-20） |
| 目安の時期 | 未定 |
| 前提となる区間 | 10, OutGame 区間5 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

刃術・覇剣をなくし、その枠に「土遁・造山」を入れる。長押しの間、視線の先にボクセルを盛り、1 つのボクセルを大きくしていく。盛ったボクセルは、強化なしの通常攻撃で切れる（ゼロ厚の切り分け）。ビルを倒さなくても、能動的に壊せる物を置けるようにする。

## 関連する仕様

- スキル：https://app.notion.com/p/3e91ea2aa7fa814ebd59e5038006aa39

## 確定した仕様（2026-10-19、ユーザー）

| # | 内容 |
|---|---|
| 1 | 覇剣（範囲と速度の強化）を消し、土遁・造山に置き換える。覇剣の効果は消す |
| 2 | 長押しで発動する。押している間、視線の向きの地形にボクセルを盛る |
| 3 | ボクセルを盛る仕組み（Union）で、1 つのボクセルを続けて大きくしていく |
| 4 | 造山で作ったボクセルは、強化なしの通常攻撃でも、ゼロ厚の平面で切り分けられる |

## 既存コードの確認結果（2026-10-20）

| 対象 | 今の状態 | 対応 |
|---|---|---|
| `SkillEffect`（`SkillData.cs` 151 行） | `EnhanceRangeSpeed` = 8 が覇剣。後ろに `Bomb` = 9、`Sweep` = 10、`Rush` = 11 が続き、アセットは番号で保存している | 値を消すと後ろの番号がずれるので、8 を `Mound`（造山）に名前を変えて使う（決めること #6） |
| `SkillData.IsEnhancement` | `>= EnhanceDestroy && <= EnhanceRangeSpeed` | `<= ExplosiveFragment` に直す |
| `SkillData._enhanceIntervalScale` | 覇剣だけが使う | 消す |
| `MeleeCutService`（85・122 行） | 覇剣の範囲と攻撃間隔の倍率を掛けている | 消す |
| `PlayerEnhanceService.IsActive(effect, out scale, out intervalScale)` | 覇剣のために足した重ね | 使う所がなくなれば消す |
| `Skill_Haken.asset` | 覇剣。参照は常駐シーン `UsefulToolkitPersistent.unity`（`GameSceneInitializer._skills`）だけ。`_id` は空なので、保存の ID はアセット名 | `.meta` ごと `Skill_Zouzan.asset` に名前を変える。GUID が残るので、シーンの参照はそのまま。保存の ID は変わる（決めること #8） |
| `SkillService.OnSkill` | `InputPhase.Performed` だけを受け、押した瞬間に 1 回消費する。Update はない | 造山だけ、押したら始め、`Canceled`（離した）で止める。入力は Button 型なので、押すと Performed、離すと Canceled が来る |
| `VoxelPiece.CreateVolume` / `ApplyEdit(…, Union)` | 空のボリュームを作り、形を足せる。手本は `VoxelDebugPrimitive`。品質（`_quality`）が設定されたピースが要る | 造山のピースのプレハブに品質を設定しておく |
| `MeleeCutAdapter.CollectTargets`（200 行〜） | 破壊タイプのときだけピースを集める。手付かずのモデルは、集めるときにボクセルにする（`loader.Load()`） | 破壊タイプでないときは、印の付いたピースだけを集め、モデルのボクセル化はしない（毎回の振りで建物をボクセルにしない為） |
| `EnemySpawnAdapter` | 敵の格子と崩落の判定が見張るのは `VoxelModelLoader` と、倒れた建物のピース（`OnBuildingCollapsed` → `WatchFalling`）だけ | 単独のピースを見張る口（`WatchPiece`）を足す。足さないと、盛った物を切って落としても敵が潰れず、格子も更新されない |
| `VoxelPiece.RegisterOnSplit` / `RegisterOnShapeChanged` | 単独のピースにも、分離と形の変化の通知がある | `WatchPiece` で使う |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 20-1 | 覇剣の削除 | External / Application | 上の表の「覇剣」の行をすべて直す |
| 20-2 | 長押しの発動 | Application | `SkillService` で、造山の枠を押したら始め、離したら止める。押している間のチャージの消費は Adapter から呼ぶ |
| 20-3 | ボクセルを盛る | EngineAdapter | 押し始めに視線の先へ箱を確保し、押している間は視線の当たる点に球を足す |
| 20-4 | 通常攻撃で切れる | EngineAdapter | `VoxelPiece` に「通常攻撃でも切り分けられる」印を持たせ、`MeleeCutAdapter` が破壊タイプなしでも、印の付いたピースを切り分ける。区間22の崩壊の対象も同じ印を使う |
| 20-5 | 敵が見張る | EngineAdapter | `EnemySpawnAdapter.WatchPiece` を足す。形が変わったら格子を調べ直し、分かれて落ちる塊を崩落の判定に入れる |
| 20-6 | アセットと一覧 | External / Level | `Skill_Zouzan.asset` の値と説明を書く |

## 完了条件

- 造山を長押しすると、視線の先にボクセルが盛られて大きくなる。離すか、チャージが尽きると止まる
- 盛ったボクセルを、強化なしの通常攻撃で切れる。切り離された側は落ちる
- 盛ったボクセルを切って落とすと、下の敵が崩落で倒れる
- 盛ったボクセルの上を、敵が通り抜けない（格子に障害物として入る）
- 覇剣がアウトゲームの一覧から消え、造山が入っている

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 箱の大きさとボクセルの大きさ | 箱は 8m 四方、ボクセルは 0.4m（建物と同じ品質）。切り分けの重さはボリュームの点の数に比例するので、大きくしすぎない |
| 2 | 盛る速さとチャージの消費 | 押した瞬間に消費 10。押している間は 0.25 秒ごとに消費 2 と、半径 1.2m の球を 1 つ足す（仮） |
| 3 | 盛り始めの位置 | カメラから視線の向きへ 30m までのレイが当たった点。何にも当たらなければ発動せず、消費もしない。箱は当たった点を底の中心にする |
| 4 | 視線が箱の外へ出たとき | 箱の外には盛らない。消費もしない。押したまま視線を戻せば、また盛れる |
| 5 | 同時に置ける数 | 4 つ。超えたら最も古いものを消す。ピースはプールから出し、実行中に Instantiate しない |
| 6 | `SkillEffect.EnhanceRangeSpeed` の扱い | 値 8 の名前を `Mound` に変えて使う。番号を変えないので、ほかのアセットが崩れない |
| 7 | 盛ったボクセルの素材 | 普通の強度の 1 種類。マテリアルは `VoxelDefault.mat` |
| 8 | 保存データの ID | アセット名が変わるので、覇剣を解放済みの保存データでは造山が未解放になる。開発中なので、保存データの移し替えは作らない |
| 9 | 盛っている間の攻撃間隔 | 押し始めだけを、通常攻撃と共有する攻撃間隔に数える。盛っている間に通常攻撃は振れる |

2026-10-20 に、ユーザーが決めること #8（保存データの移し替えは作らない）を案のとおりに決めた。ほかの項目は着手時に確かめる。

## 作業計画（2026-10-20）

### 新しく作る型と、区間20 での利用者

| 型 | 層・置き場所 | 区間20 での利用者 |
|---|---|---|
| `VoxelMoundAdapter`（ピースのプール、盛る・止める、箱の確保） | EngineAdapter、InGame の `VoxelDestruction` と同じ GameObject かその隣 | `PlayerInitializer`（`SkillService` に関数を渡す） |

既存の型の拡張：`SkillData` / `SkillEffect`、`SkillService`、`MeleeCutService`、`PlayerEnhanceService`、`MeleeCutAdapter`、`VoxelPiece`（印）、`EnemySpawnAdapter`・`EnemyDistanceField`・`EnemyCollapseDetector`（単独のピースを見張る）、`PlayerInitializer`。

- 基準1：長押しの状態を State にしない。盛っているかを読むのは `VoxelMoundAdapter` だけで、`SkillService` から直接呼ぶ。印は `VoxelPiece` の bool 1 つで、専用のコンポーネントにしない
- 基準2：`SkillService` のコンストラクタは引数がすでに 15 個ある。さらに 2 つ（始める・止める）増える。引数をまとめる型を作る見直しは提案に留め、今回は足すだけにする
- 基準4：重さのリスクは、切り分けがボリュームの点の数に比例する（0.4m で 1 回 27ms、区間15の計測）ことだけを根拠にし、箱の大きさ（#1）で抑える

### 配線

- `SkillService` に `Func<float, float, bool> onMoundBegin`（引数は球の半径、盛る間隔。始められたら true）と `Action onMoundEnd` を足す。押している間の消費は、`VoxelMoundAdapter` が間隔ごとに `Func<int, bool> tryConsume`（`ChargeService.TryConsume`）を呼び、false なら止める
- `PlayerInitializer` が `VoxelMoundAdapter` を受け取り（Inspector）、`EnemySpawnAdapter` を渡し（盛ったピースを `WatchPiece` する為）、関数を `SkillService` に渡す

### コミットの分け方

| # | 内容 | 確かめ方（ローカル） |
|---|---|---|
| 1 | 20-1：覇剣の削除、`SkillEffect` の 8 を `Mound` に、`IsEnhancement` の範囲、アセットの名前の変更 | コンパイル。刃術の砕剣・壊剣は今までどおり動く。アウトゲームの一覧に「造山」（効果はまだない）が出る |
| 2 | 20-4：`VoxelPiece` の印と `MeleeCutAdapter` | デバッグ用に印を付けたピース（`VoxelDebugPrimitive`）が、強化なしの通常攻撃で切れる。印のない建物は切れない |
| 3 | 20-2・20-3：`SkillService` の長押しと `VoxelMoundAdapter`、`PlayerInitializer` の配線 | 長押しで盛られて大きくなる。離すか、チャージ切れで止まる。箱の外には盛らない。5 つ目で最も古いものが消える |
| 4 | 20-5：`EnemySpawnAdapter.WatchPiece` | 盛った物の上を敵が通らない。切って落とすと、下の敵が崩落で倒れる |
| 5 | 20-6：値の調整、計画書の実装結果、全体計画書の更新 | 完了条件すべて |

クラウドで書いたコミットは、コンパイルと動作を確かめていない。ローカルで `Kizami.External.Runtime`・`Kizami.Application.Runtime`・`Kizami.EngineAdapter.Runtime`・`Kizami.Initialization.Runtime` をコンパイルし、InGame の `PlayerInitializer` に `VoxelMoundAdapter` を割り当ててから確かめる。

## 見つけた問題（今回は扱わない）

- なし
