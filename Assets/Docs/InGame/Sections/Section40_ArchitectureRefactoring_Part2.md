# 区間40 後半：残った原則違反の整理 作業計画書

区間40（[Section40_ArchitectureRefactoring.md](Section40_ArchitectureRefactoring.md)）のコミット1〜14 のあとに、ユーザーが改めて調べた結果をもとに立てた計画。決めることは推奨案で進める（2026-10-25、ユーザー）。計画書のレビュー（2026-10-25）で見つかった誤りと抜けを直し、見直した決定は #22〜#25 として決めた。

各コミットは、着手する前に「着手時に確かめること」を調べて報告し、ユーザーの確認を受けてから実装する。コミットのあとはコンパイルし、プレイモードの確認はユーザーが行う。コンパイルの前に、プレイモードが動いていないことを確かめる（動いていたら止めずに知らせる）。

状態：コミット15〜24 の実装済み（2026-10-25）。プレイモードの確認待ち。コミットは未作成で、作業ツリーにまとめて残っている

## 結果

各コミットで決めたこと・確かめたことを書く。プレイモードで確かめることは、上の表の右の列のまま。

| コミット | 結果 |
|---|---|
| 15 | 下の「見つけた問題」に書いた |
| 16 | `VoxelBuilding.RegisterOnCollapsedBounds(Action<Bounds>)` を足した。既存の `RegisterOnCollapsed(Action<Rigidbody>)` はそのまま残し、倒れ始めたときに両方へ通知する（`NotifyCollapsed`）。`StageInitializer.OnBuildingCollapsed` は消し、`BrilliantSkillService.OnBuildingCollapsed` を直接購読させた |
| 17 | `Application/CoreSystem/StageSelection`（静的クラス）に、選んだステージ・既定のステージ（`ScriptableObject.CreateInstance` は Application に置けた）・装備の出所の判断を移した。初めのチャージ量は `ChargeService` のコンストラクタがステージから足す（`AddAmount` は private にした） |
| 18 | `IPlayerHealthController`（`Heal`）と `IActionIntervalController`（`CanAct`・`MarkActed`）を、実装する Service と同じファイルに置いた（Application の中だけで使う為。決定 #22）。攻撃間隔は判定と記録の両方が要るので、State ではなく操作面にした |
| 19 | 別の出来事 `PlayerSkillEffectEvents`（`IPlayerSkillEffectEvents`、`PlayerEventBoard`）を足した。ビーム・爆発・薙ぎ払い・攻走の経路は `VoxelDestructionAdapter`、爆弾は `BombThrowAdapter`、腕は `PlayerArmAdapter` が購読する。盛る（開始と終了）は、開始が戻り値（盛り始められたか）を持つ為、対の終了も合わせて直接配線に残した |
| 20 | 飛び移りの削る処理は `IPlayerSkillEffectEvents.OnSphereCarve` にした。盛っている間は、止まる理由（止める操作・チャージ切れ）を知る `VoxelMoundAdapter` が `VoxelMoundState` に書く（`SkillService` は Adapter の中でのチャージ切れを知らない為）。`MeleeCutService` は `IVoxelMoundState` と `ISkyBeamController.IsActive` を読む。Adapter の初期化を `MeleeCutService` の生成より前に移した |
| 21 | `StageEventBoard` と `BrilliantSkillEvents`（`IBrilliantSkillEvents`）を足した。言葉の着弾は `VoxelDestructionAdapter`（削る）と `EnemySpawnAdapter`（倒す）、成立した言葉は `BrilliantSkillTextAdapter` が購読する。着弾は削ってから倒す順に呼ぶ為、削る側を先に購読させる。出す言葉の選び方（`GetWord`）も Initializer の無名関数から Service へ移した。`AddOnCut` は `StageInitializer.ConnectWordCutting` へ移した。`StageEventBoard` は Compositor に手で足したので、作り直しが要る |
| 22 | 操作面 `IStageGuideController` を BlackBoardLayer に置いた（台本の Adapter が使う為。決定 #22）。条件の種類 `GuideConditionKind` は BlackBoardLayer へ移し、段階の条件は `GuideStepRule` で Service へ渡す。`StageGuideScript` は破壊対象が壊れたことを観測して `ReportTargetDestroyed` で報告し、毎フレーム `TickScript` を呼ぶ。`BossGuideScript` は話題（`BossGuideTopic`）ごとの文（`GuideLine`）だけを持ち、段階と周から話題を選ぶのは `StageGuideService`。ボス戦の State は Service が登録を待ち受けて拾う。大目標・小目標・会話・操作説明の 4 つの関数も、操作面のメソッドにまとめた |
| 23 | `BossDamageSettings`（`BossInitializer`）と `EnemyDamageSettings`（`EnemyInitializer`）を ExternalLayer に足し、Initializer から各 Adapter の `Initialize` へ量を渡す。保存されていた値（`BossEnemy.prefab`・`Stage_Boss.unity`・`InGame.unity`）はすべて既定値と同じだった為、設定の既定値で写した。`SkyBeamAdapter._healthDamagePerSecond` は、すでに `SkyBeamEffect`（ExternalLayer）の値を受け取っていた為、変えていない。ダメージの間隔は量ではない為、Adapter に残した |
| 24 | このドキュメントと、全体計画書・親の計画書を直した。40-7 の反映案は下の「設計の正本への反映案」 |

## 背景

規約（スキル state-centrism-architecture の「Application と EngineAdapterLayer の直接配線」と「EngineAdapterLayer による State の所有」）と照らした結果を、3 つに分けた。

| 分類 | 内容 |
|---|---|
| 区間40 で直すつもりで漏れていたもの | 倒れた建物の範囲の計算が `StageInitializer.OnBuildingCollapsed` に残っている（コミット2 の計画では `VoxelBuilding` が中心と半径を通知する予定だった）。初めのチャージ量・装備の出所・既定のステージを決める判断が Initializer にある。Service 同士の関数（`onHeal`・`canAct`・`onActed`）が、コミット12 の操作面の形になっていない |
| 規約に照らして違反といえるもの | 配線相手が複数のクラスになっている Service がある（下の表）。`StageGuideScript` が台本の段階を進める条件を判定し、`BossGuideScript` がボスの段階から案内の文を選んでいる。ボスと敵の攻撃の Adapter がダメージ量を SerializeField に持つ（手本の「ルールの値は ExternalLayer」と食い違う） |
| 規約上は違反ではないもの（今回は扱わない） | Adapter から Service への報告（`applyDamage`・`reportHit`・`tick` など。受け手は 1 クラス。扱いは決定 #24）、案内の文を出す 4 つの関数（`StageGuideScript`・`BossGuideScript` とも、受け手は `StageGuideService` の 1 クラス）、切断の結果を Adapter 同士で配ること、別のシーンにある物の検索、Initializer による生成とエラーログ、巨大クラス、`Camera.main`、`ArmorPanel` の耐久値（区間18 の決定） |

### 配線相手が複数のクラスになっている Service

| Service | Adapter への関数 | Service への関数 |
|---|---|---|
| `SkillService` | 9 個・5 クラス：前方ビーム・爆発・薙ぎ払い・攻走の経路（`VoxelDestructionAdapter`）、爆弾（`BombThrowAdapter`）、体の範囲（`PlayerMovementAdapter`）、盛る開始と終了（`VoxelMoundAdapter`）、腕（`PlayerArmAdapter`） | 3 個：`onHeal`（`PlayerHealthService.Heal`）、`canAct`・`onActed`（`MeleeCutService`） |
| `FlightService` | 2 個・2 クラス：体の中心（`PlayerMovementAdapter.BodyBounds`）、削る（`VoxelDestructionAdapter.CarveExplosionAt`） | ― |
| `MeleeCutService` | 振る（`MeleeCutAdapter.Swing`）、動けないかの判定（`VoxelMoundAdapter.IsMounding` を見る無名関数） | 動けないかの判定の中で `SkyBeamService` を見る |
| `BrilliantSkillService` | 3 クラス：削る（`VoxelDestructionAdapter`）、倒す（`EnemySpawnAdapter`）、文字（`BrilliantSkillTextAdapter`） | ― |

## 決めたこと（2026-10-25、ユーザー。推奨案）

| # | 項目 | 決定 |
|---|---|---|
| 17 | スキルの配線 | スキルの発動を `PlayerEventBoard` の出来事にし、各 Adapter が購読する。親の計画書の決めること #5（腕のアニメーションは直接つなぐ）を見直す。戻り値のある関数（体の範囲、盛り始められたか）は直接配線に残す |
| 18 | 台本の進め方 | 段階を進める条件の判定を `StageGuideService` へ移す。`StageGuideScript` は段階の定義（データ）と、段階に入ったときの物の切り替えと、エンジン側の観測だけを持つ |
| 19 | ダメージ量 | 攻撃ごとのダメージ量も ExternalLayer の設定にまとめる。ボスは `BossBattleSettings` の隣、敵は敵の設定に置く。範囲は決定 #23 |
| 21 | デバッグ用の Initializer | キー入力と画面表示を持つのは、デバッグ専用の例外として規約に書く（コードは変えない） |

決定 #20（操作面を BlackBoardLayer へ移す）は、レビューで移せないものが見つかった為、#22 で置き換えた。

## 決めたこと（2026-10-25 のレビューから。ユーザー。すべて案のとおり）

| # | 項目 | 決定 |
|---|---|---|
| 22 | 操作面の置き場所（#20 の置き換え） | 規則を「Adapter からも使う操作面は BlackBoardLayer、Application の中だけで使う操作面は実装する Service と同じ Application」とし、コードは動かさない。<br>理由：`IPlayerEnhanceController`・`ISkyBeamController` は ExternalLayer の型（`EnhanceEffect`・`SkyBeamEffect`）を引数に取り、BlackBoardLayer の asmdef（参照は `UsefulToolkit.Framework.BlackBoard.Runtime` だけ）からは参照できない。`IGameSceneController` も `StageData` と `UniTask` を使う。今 Application にある 7 つ（コミット12・13 の 5 つと `ITimeScaleController`・`IGameSceneController`）は、どれも Adapter から使われていない。`IOperationSettingController` だけが BlackBoardLayer にある理由は、着手時に確かめて規則の例として書く。|
| 23 | 量を決めたダメージの扱い | #19 の範囲に、`BossTowerThrow._bossDamage`（倒れた塔がボスの体力に与える 30）と `SkyBeamAdapter._healthDamagePerSecond`（羽天轟号が体力に与える毎秒の量）を含める。親の計画書のコミット7 の「量を決めたダメージは攻撃側が持ったまま」は、この決定で見直す。|
| 24 | Adapter から Service への報告を直接配線に残すこと | 残す。受け手が 1 クラスなので規約どおり。`PlayerHealthService.ApplyDamage` を約 10 クラスの攻撃の Adapter が握るが、量は #19 で ExternalLayer へ移るので、Adapter に残るのは「当たった」ことの報告だけになる。操作面のインターフェースにしても名前が付くだけで、構造は変わらない。この判断をユーザーの判断として記録する。|
| 25 | `BossGuideScript` の文の選び方 | コミット22 に含める。ボスの段階から案内の文を選ぶ判断を `StageGuideService`（またはボス戦の案内を受け持つ Application のクラス）へ移し、`BossGuideScript` は段階ごとの文の定義（データ）だけを持つ。|

## コミットの分け方

| コミット | 作業 | 内容 | 着手時に確かめること | 確かめること（プレイモード） |
|---|---|---|---|---|
| 15 | 調査 | Adapter のうち、ルールが入っていそうなもの（`EnemySpawnAdapter` の出し直しの間隔と撃破、`TowerTopple`、`StageHudAdapter`、`EnemyFinisherAttack` など）を読み、見つけたものを報告する。計画には入れず、この計画書の「見つけた問題」に書く | 読む範囲（ファイルの一覧）をユーザーに見せる | ― |
| 16 | 漏れ | `VoxelBuilding` に、倒れ始めた階の範囲を通知する購読口を足し、`StageInitializer.OnBuildingCollapsed` を消す。Brilliant Skill の判定は範囲を直接受け取る | 今の `RegisterOnCollapsed(Action<Rigidbody>)` の利用者は `EnemySpawnAdapter`・`TowerTopple`・`StageInitializer` の 3 つ。前の 2 つを変えずに足せるか | Brilliant Skill のスイッチを一時的に入れ、倒壊の巻き込みが成立する |
| 17 | 漏れ | 初めのチャージ量を足す判断を `ChargeService`（または流れの Service）へ移す。装備の出所（`GetEquippedSkills`）と既定のステージ（`GetSelectedStage`）の判断を `GameProgressService` 側へ移す | 既定のステージを `ScriptableObject.CreateInstance` で作る処理を Application に置けるか。置けなければ別案を出す | ステージ開始時のチャージ量、アウトゲームを通さず始めたときの装備とステージが前と同じ |
| 18 | 漏れ | Service 同士の関数を、コミット12 の形（操作面）にする：`onHeal`（`PlayerHealthService`）、`canAct`・`onActed`（`MeleeCutService`）。置き場所は決定 #22 による | `MeleeCutService` の攻撃間隔を共有する形（`CanAct`・`MarkActed`）を、操作面にするか State にするか | 回復のスキル、通常攻撃とスキルで共有する攻撃間隔が前と同じ |
| 19 | 配線相手 | スキルの発動を `PlayerEventBoard` の出来事にし、前方ビーム・爆発・爆弾・薙ぎ払い・攻走の経路・腕のアニメーションを、それぞれの Adapter が購読する形にする | 出来事の形の案を出す。既存の `PlayerActionKind.SkillUsed`（スキル名だけを持ち、案内のログが使う）と `ChargeSpent` に中身を足すのか、効果を実行させる別の出来事を足すのか。推奨は別の出来事（`PlayerActionNotice` はログ用の通知で、効果ごとの値を持たせると用途が混ざる為）。盛る（開始と終了）を出来事にできるか | すべてのスキルが前と同じに発動し、腕が振られる |
| 20 | 配線相手 | `FlightService` と `MeleeCutService` の配線相手を 1 つにする。`FlightService` の削る処理はコミット19 と同じ出来事の形にし、体の中心は直接配線に残す。`MeleeCutService` の動けないかの判定は、盛っている間を誰が持つかを決めて、State か操作面から読む | 盛っている間（`IsMounding`）を、盛るのを始める `SkillService` が State に持てるか | 飛び移りで上昇 → 下降し、経路が削られる。盛っている間と羽天轟号の間に通常攻撃が出ない |
| 21 | 配線相手 | `BrilliantSkillService` の Adapter への関数（削る・倒す・文字を出す）を、コミット19 と同じ考え方で整理する。`BrilliantSkillTextAdapter` が自分で `AddOnCut` を呼んでいる（`BrilliantSkillTextAdapter.cs:194`）のを、Initializer の配線に移す（`TutorialDummy` と同じ） | Brilliant Skill がスイッチで無効なままでよいか。確認の仕方 | スイッチを一時的に入れ、言葉の着弾・文字を切る・成立が前と同じ |
| 22 | 台本 | 台本の段階を進める条件の判定を `StageGuideService` へ移す。`StageGuideScript` は段階の定義と、段階に入ったときの物の切り替え（`ShowObjects`・スポーン位置の保留を解く）と、エンジン側の観測の報告だけにする。決定 #25 により、`BossGuideScript` の文の選び方も移す（分量が大きければコミットを分ける） | 条件ごとの扱い：`DefeatCount`・`DestroyedTargetCount` は `StageFlowState`、`PlayerMoved` は `PlayerMovementState`、`PlayerAction` は `IPlayerActionEvents` から Service が読める。`TargetDestroyed` は `IDestructionTarget`（エンジン側の物）を見る為そのままでは移せず、`StageGuideScript` が壊れたことを観測して Service へ報告する形にする。`IPlayerActionEvents` は今 `StageGuideService`（L44）と `StageGuideScript`（L69）の両方が購読していて、移したあとは Service だけにする | チュートリアルと各ステージの台本、ボス戦の案内が、前と同じ順に進む |
| 23 | ダメージ量 | ダメージ量を ExternalLayer の設定へ移し、Initializer から渡す。対象：ボスの攻撃（衝撃波・下部レーザー・降下フィニッシャー・上部タレット・頭部のレーザー・塔投げ・円形の空間）、敵の攻撃（弾・ビーム）。決定 #23 により、`BossTowerThrow._bossDamage` と `SkyBeamAdapter._healthDamagePerSecond` も含める。プレハブとシーンに保存された値を写す | 保存されている値の一覧（プレハブ・シーン・アセット）。敵の設定の置き場所（既存の設定に足せるか） | ボス戦と敵の攻撃で受けるダメージ、塔と羽天轟号がボスに与えるダメージが前と同じ |
| 24 | ドキュメント | 全体計画書の手本と現状、区間40 の計画書の状態を直す。40-7（Notion の設計ページとスキルへの反映案）を出す。反映案には、#17（発動は EventBoard）、#21（デバッグ用の Initializer の例外）、#22（操作面の置き場所）、#24（報告の直接配線を残す判断）、EventBoard を使う条件、Service が決めた行動を Adapter が実行する形、ルールの値は ExternalLayer、を入れる | 書き戻しはユーザーが確かめてから | ― |

## 新しく作る型と、今の区間で使う利用者

| 型 | 層 | 利用者 |
|---|---|---|
| `StageSelection`（コミット17） | Application | `PlayerInitializer`・`StageInitializer`・`EnemyInitializer` |
| `IPlayerHealthController`・`IActionIntervalController`（コミット18） | Application | 実装：`PlayerHealthService`・`MeleeCutService`。使う：`SkillService` |
| `PlayerSkillEffectEvents`（コミット19・20） | BlackBoardLayer | 流す：`SkillService`・`FlightService`。購読：`VoxelDestructionAdapter`・`BombThrowAdapter`・`PlayerArmAdapter` |
| `VoxelMoundState`（コミット20） | BlackBoardLayer | 書く：`VoxelMoundAdapter`。読む：`MeleeCutService` |
| `StageEventBoard`・`BrilliantSkillEvents`（コミット21） | BlackBoardLayer | 流す：`BrilliantSkillService`。購読：`VoxelDestructionAdapter`・`EnemySpawnAdapter`・`BrilliantSkillTextAdapter` |
| `IStageGuideController`・`GuideStepRule`・`GuideConditionKind`・`BossGuideTopic`・`GuideLine`（コミット22） | BlackBoardLayer | 実装：`StageGuideService`。使う：`StageGuideScript`・`BossGuideScript` |
| `BossDamageSettings`（コミット23） | ExternalLayer | 持つ：`BossInitializer`。受け取る：ボスの攻撃の各 Adapter と `BossTowerThrow` |
| `EnemyDamageSettings`（コミット23） | ExternalLayer | 持つ：`EnemyInitializer`。受け取る：`EnemySpawnAdapter` から `EnemyShooter`・`EnemyFinisherAttack`（`EnemyFinisherBeams`） |

## 残る直接配線（このあとも残すもの）

| 配線 | 残す理由 |
|---|---|
| Adapter から Service への報告（`applyDamage`・`reportHit`・`reportActionFinished`・`tick` など） | エンジンのイベントを Application の 1 クラスへ届ける、規約の直接配線そのもの（決定 #24） |
| 案内の文を出す 4 つの関数（`StageGuideScript`・`BossGuideScript` → `StageGuideService`） | 受け手が 1 クラス。表示する文を渡すだけ |
| `SkillService` の体の範囲・盛り始められたか（と対の盛るのを止める）、`FlightService` の体の中心 | 戻り値がある為、出来事にできない |
| 切断の結果を `EnemySpawnAdapter`・`FragmentOrbAdapter` などへ配ること | Adapter 同士の受け渡しで、MeshCut の型を BlackBoardLayer に出せない為 |

## 見つけた問題（今回は扱わない）

コミット15 で読んだ範囲：`EnemySpawnAdapter`・`TowerTopple`・`StageHudAdapter`・`EnemyFinisherAttack`。

- `EnemyFinisherAttack.TryStart`：フィニッシャーが攻撃を始める条件（冷却・同時に攻撃する数の上限・グループが追跡中か）を Adapter が決めている。プレイヤーが見えるかと吸収の対象を探す部分はエンジン側の観測だが、始めるかどうかは敵の AI の判断にあたる。群衆の Job と同じデータ（`EnemyAgent`）の上で動く為、移すなら `EnemySquadCommandState` のような命令の State を足す大きな作業になる
- `StageHudAdapter.UpdateSkills`：スキルが使えるか（チャージがコスト以上か）を表示の為に Adapter が比べている。`SkillService` の判定と同じ式で、変わると表示と食い違う。`ISkillSlotState` に使えるかを足せば済む
- `EnemySpawnAdapter`：出し直しと補充は `EnemySquadCommandState` の命令に従っていて、判断は Application にある。落下で倒したことにする高さ（`_fallDefeatHeight`）など、物理の観測に使う値が SerializeField にある。ルールの値ともいえるが、観測の閾値として扱う
- `TowerTopple`：物理と削る処理だけで、ルールは見つからなかった

## 設計の正本への反映案（40-7）

Notion の設計ページと state-centrism-architecture スキルに書き足す案。書き戻しはユーザーが確かめてから行う。

- EventBoard を使う条件：状態を持たない出来事を受け取る相手が 2 つ以上なら EventBoard に流す。Service が決めた効果をエンジン側で実行させる場合も同じで、効果の実行を受け持つ Adapter が購読する（`PlayerSkillEffectEvents`・`BrilliantSkillEvents`）。案内のログのような通知の出来事と、効果の値を持つ出来事は分ける（決定 #17）
- 戻り値のある関数は直接配線に残す。対になる関数（盛るの開始と終了）も合わせて残す
- Service が決めた行動を Adapter が実行する：次に何をするかは Service が決めて State に書き、Adapter は実行して報告する（`BossBattleService`）
- Adapter が観測を State に書く：止まる理由を知るのが Adapter なら、その状態は Adapter が持つ State に書く（`VoxelMoundState`・`BossWeaponState`）
- 台本の分け方：台本の Adapter は段階と文の定義（データ）とエンジン側の観測の報告だけを持ち、段階を進める判断と文を選ぶ判断は Application が持つ（`IStageGuideController`）
- ルールの値は ExternalLayer：回数・間隔・ダメージ量などは ExternalLayer の Serializable にまとめ、Initializer から渡す。EngineAdapterLayer は ExternalLayer を参照しない為、Adapter へは値で渡す（`BossDamageSettings`・`EnemyDamageSettings`）
- 操作面の置き場所：Adapter からも使う操作面は BlackBoardLayer（`IOperationSettingController`・`IStageGuideController`）、Application の中だけで使う操作面は実装する Service と同じ Application のファイル（決定 #22）
- Adapter から Service への報告は直接配線に残す：受け手が 1 クラスなら規約どおり。量は ExternalLayer に移し、Adapter に残るのは当たったことの報告だけ（決定 #24）
- デバッグ用の Initializer がキー入力と画面表示を持つのは、デバッグ専用の例外とする（決定 #21）
