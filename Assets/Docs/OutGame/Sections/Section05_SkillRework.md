# 区間5：スキルの作り直し

| 項目 | 内容 |
|---|---|
| 状態 | 完了（2026-10-20） |
| 目安の時期 | 未定 |
| 前提となる区間 | ―（インゲーム区間 8・18・27 の成果の上に作る） |
| 全体計画 | [OutGameOverallPlan.md](../OutGameOverallPlan.md) |

## 目的

スキルをアウトゲームの 7 種に揃える。刃術 3 種を回数制で重ねがけできる形にし、火術・崩爆を作る。インゲームの区間27（頑拳・突撃）をこの区間に統合する。

## 関連する仕様

- 全体計画書の 7 章「スキル」
- Notion「スキル」：https://app.notion.com/p/3e91ea2aa7fa814ebd59e5038006aa39
- Notion「ダメージタイプ」：https://app.notion.com/p/3e91ea2aa7fa81c49ddbf615308a48f8

## 既存の資産

| 資産 | 内容 |
|---|---|
| `SkillData`、`SkillEffect` | ビーム・爆発・回復・強化型 5 種（`EnhanceDestroy` / `EnhanceArmorBreak` / `EnhanceRange` / `EnhanceSpeed` / `ExplosiveFragment`） |
| `PlayerEnhanceService` | 強化型の効果を実時間か回数で切る |
| インゲーム区間27の計画 | 頑拳（＝衝波）・突撃（＝攻走）、ボロノイ状の破砕、重心の偏り、発動の制約 |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 5-1 | 刃術 3 種 | Application / External | 砕剣＝破壊タイプ、壊剣＝粉砕タイプ、覇剣＝範囲と速度。どれも 10 回振るまで。重ねがけで両立し、同じ刃術の再発動で回数を 10 に戻す |
| 5-2 | 火術・崩爆 | Application / EngineAdapter | 前後左右に爆弾を 1 つずつ投げ、落ちた所で爆発させる（破壊タイプ） |
| 5-3 | 体術・衝波、体術・攻走 | External / Application / EngineAdapter | インゲーム区間27のうち、削る形・突進・発動の共有を、この区間で行う。ボロノイ破砕と破壊の重心は区間17に残す（決定 1） |
| 5-4 | 既存のスキルの整理 | External | 7 種に入らないスキルをアウトゲームの一覧から外す |

## 完了条件

- 7 種のうち羽天轟号以外の 6 種を装備して発動でき、仕様どおりに効く
- 刃術を 2 つ重ねると、両方の効果が同時にかかり、それぞれ 10 回で切れる

## 詳細仕様で決めること（2026-10-18、ユーザーが案のとおりに決定）

| # | 項目 | 決定 |
|---|---|---|
| 1 | ボロノイ破砕と破壊の重心 | この区間では作らない。インゲーム区間27に残す。衝波・攻走は削る形だけを出す |
| 2 | 覇剣の倍率 | 範囲 ×1.5、攻撃間隔 ×0.5（今の範囲アップ・速さアップの値） |
| 3 | 崩爆の値 | 飛距離 5m、爆発の半径 7.5m（仮。2026-10-19 に 2.5m から 3 倍にした） |
| 4 | 衝波・攻走の範囲、攻走の距離・時間・無敵 | 仮の値で作り、ローカルで調整する |
| 5 | 発動の共有 | 間隔は通常攻撃の攻撃間隔（`PlayerParameterData.MeleeAttackInterval`）と同じ。後隙のキャンセルは作らない |
| 6 | 外すスキル | 一覧（`GameSceneInitializer._skills`）から外すだけ。アセットと効果のコードはデバッグ用に残す |
| 7 | 初期装備 | 衝波と攻走（区間17の「頑拳 1 つ」を置き換える） |
| 8 | 攻走の向き | Notion のとおり入力方向（移動入力がなければ正面）。区間17の「正面へ」は区間の終わりに直す |
| 9 | 短距離ワープ | オミットする。ワープの突進の仕組みを攻走に使い回す（2026-10-18、ユーザー） |
| 10 | 衝波・攻走と装甲 | 装甲に当てる（Notion「攻撃型スキルは装甲には当たる」に合わせる。2026-10-18、ユーザー） |
| 11 | 攻撃範囲 | 通常攻撃（届く距離 9m・幅 6m）、衝波（横幅 12m・奥行き 6m）、攻走（半径 3m）、崩爆（半径 7.5m）を、それまでの 3 倍にした（2026-10-19、ユーザー） |
| 12 | 衝波・攻走の削れ方 | 1m あたりに減る強さ（`SkillData._falloff`）を 0.3（1 ÷ 半径 3m より小さい）にする。コンクリート（普通）は半径の 3m まで、柔い素材は約 4.7m まで削れ、鉄筋（硬い）は削れずに残る。最終ステージの破壊力の上乗せ（`SetMaxPower`）も使わない。鉄筋を削れるのは崩爆と羽天轟号だけにする（2026-10-19、ユーザー） |

## 既存コードの確認結果（2026-10-18）

| 対象 | 今の状態 | 対応 |
|---|---|---|
| [PlayerEnhanceService](../../../Code/Scripts/Application/Player/PlayerEnhanceService.cs) | 回数で切れる強化、種類が違えば同時にかかる、同じ種類の再発動で数え直す、を持つ。倍率は 1 つだけ | 刃術の共通ルールはそのまま満たせる。覇剣用に攻撃間隔の倍率を足す |
| [MeleeCutService](../../../Code/Scripts/Application/Player/MeleeCutService.cs) `OnAttack` | 1 回振るたびに強化の `TryUse` を呼ぶ。攻撃間隔は実時間で `_lastSwingTime` と比べる | 刃術の 10 回はこの数え方を使う。発動の共有もここに足す |
| [SkillService](../../../Code/Scripts/Application/Player/SkillService.cs) | 1〜3 キーでチャージを消費し、効果の関数（`Action`）を呼ぶ。発動を止める条件はない | 効果の関数を足す。発動の共有を足す |
| [SkillData](../../../Code/Scripts/ExternalLayer/Player/SkillData.cs) | `SkillEffect` は 8 種。`IsEnhancement` は `>= EnhanceDestroy` | 新しい効果は enum の末尾に足す（アセットの数値を変えない為）。強化型でないものを足すときは `IsEnhancement` を直す |
| [VoxelDestructionAdapter](../../../Code/Scripts/EngineAdapterLayer/Stage/VoxelDestructionAdapter.cs) | `CarveExplosionAt`（179 行）、`CarveCapsuleAt`（220 行）がある | 崩爆・衝波・攻走の削りに使う |
| [PlayerInitializer](../../../Code/Scripts/Initialization/Player/PlayerInitializer.cs) | `SkillService` に効果の関数を渡して生成する（95 行） | 新しい効果の関数をここで配線する |
| `Assets/Level/Data/Player/Skill_*.asset` | 8 種のアセット | 新しいアセットはここに置く |

## 作業計画（コミットの分け方）

各コミットで止めて報告する。クラウドで実装したときは、コンパイルと実機の確認をローカルで行う。

| # | 内容 | 変更 | 確かめ方 |
|---|---|---|---|
| 1 | 刃術 3 種 | `SkillEffect` の末尾に `EnhanceRangeSpeed`（覇剣）を足す。`SkillData` に攻撃間隔の倍率 `_enhanceIntervalScale` を足す。`PlayerEnhanceService` の `Enhancement` に攻撃間隔の倍率を持たせ、`IsActive(effect, out scale, out intervalScale)` を足す。`MeleeCutService.OnAttack` で、覇剣の攻撃間隔の倍率を攻撃間隔に、範囲の倍率を `rangeScale` に掛ける。アセット 3 つ：砕剣（`EnhanceDestroy`、消費 30）、壊剣（`EnhanceArmorBreak`、消費 25）、覇剣（`EnhanceRangeSpeed`、消費 40、範囲 1.5・間隔 0.5）。どれも回数で切れて 10 回 | 砕剣と壊剣を重ねて、両方の効果がかかり、それぞれ 10 回振ると切れる。効果中に同じ刃術を使うと 10 回に戻る。覇剣で刃が広がり、振る間隔が短くなる |
| 2 | 火術・崩爆 | `SkillEffect` に `Bomb` を足す。`SkillData` に飛距離を足す（半径は `_radius`）。EngineAdapter に爆弾を投げる Adapter を 1 つ作る（前後左右へ Rigidbody の爆弾を投げ、何かに当たったら `CarveExplosionAt` を呼んで消える）。`SkillService` に爆弾の関数を足し、`PlayerInitializer` で配線する | 4 方向に飛び、落ちた所が半径 2.5m で削れる。敵は削られない |
| 3 | 体術・衝波 | `SkillEffect` に `Sweep` を足す。`SkillData` に横幅と奥行きを足す。カメラの前方に、横向きのカプセル（`CarveCapsuleAt`）で削る | 正面の横長の範囲が削れる。消費 10 で連打できる |
| 4 | 体術・攻走 | `SkillEffect` に `Rush` を足す。移動入力の向き（なければ正面）へ一定時間突進し、通った経路を `CarveCapsuleAt` で削る。突進は `PlayerMovementService` のダッシュを読んでから、流用できるか決める | 入力方向へ進み、通った所が削れる |
| 5 | 発動の共有 | 最後に通常攻撃かスキルを出した時刻を、`MeleeCutService` と `SkillService` で共有する。どちらも、攻撃間隔が経つまで出せない。共有の持ち方（`MeleeCutService` に時刻を持たせて `SkillService` に渡す、など）は既存の配線に合わせる | スキルの直後に、通常攻撃もスキルも出ない |
| 6 | 整理と初期装備 | `GameSceneInitializer._skills` を 6 種にする（羽天轟号は区間6で足す）。初期装備を衝波と攻走にする（`GameProgressService.Initialize` の初期の装備の決め方を読んでから直す） | アウトゲームの一覧が 6 種になり、衝波と攻走を装備して始まる |

新しい型は、`SkillEffect` の値（`EnhanceRangeSpeed`・`Bomb`・`Sweep`・`Rush`）と、崩爆の爆弾の Adapter 1 つ（利用者は `SkillService`）だけにする。

## 実装結果

- 作業計画の 1〜6 をそのとおりに実装した。新しい型は `SkillEffect` の 4 値と `BombThrowAdapter` だけ
- 衝波・攻走は `VoxelDestructionAdapter.CarveCapsuleWithArmor` で削る。カプセルに入った装甲の耐久値を 1 減らし、攻撃を出した位置から見て装甲の奥は削らない（崩爆の爆発と同じ当て方）
- 攻走は、カプセルの軸を足元から半径＋0.1m 上に置いて足場に穴を開けない。突進中は攻走を出さず、チャージも減らない
- InGame シーンの `PlayerInitializer` に `BombThrowAdapter` を配線した。インゲームから直接再生したときの装備を崩爆・衝波・攻走にした

### ローカルで行う確認

1. 崩爆：4 方向に爆弾が飛び、落ちた所が削れて、チャージが 50 減るか
2. 攻走：入力方向へ進み、床に穴が開かずに、途中の壁が削れるか。突進中にもう一度押しても、チャージが減らないか
3. 衝波・攻走を装甲に当てたとき：装甲の耐久値が 1 減り、装甲の奥が削れないか
4. 弱い素材の床では、攻走の削る範囲（強さで半径より広く届く）が床に届かないか

## 後の変更（2026-10-19）

- 覇剣は、インゲーム区間29（[Section29](../../InGame/Sections/Section29_Zouzan.md)）で土遁・造山に置き換える
- 決定 12（鉄筋は崩爆と羽天轟号だけが削れる）は、インゲーム区間28（[Section28](../../InGame/Sections/Section28_InitialSkillPower.md)）で、衝波・攻走でも効率は低く削れる形に変える

## 見つけた問題（今回は扱わない）

- なし（区間17の計画書の攻走の向きと初期装備は、この区間で直した）

