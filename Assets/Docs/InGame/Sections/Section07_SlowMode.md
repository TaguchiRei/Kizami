# 区間7：スローモード・つかみ・投擲・ランチャー

| 項目 | 内容 |
|---|---|
| 状態 | 着手（2026-10-08） |
| 目安の時期 | 2026/12/15〜12/28（最速の推定 10/26〜10/29） |
| 前提となる区間 | 3, 5 |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |

## 目的

チャージを消費して時間を遅くするスローモードを作る。スロー中にかけらをつかみ、投げたりランチャーで撃ったりできるようにする。

## 関連する仕様

- スローモード：https://app.notion.com/p/3e91ea2aa7fa81a3914be1ccc47b86aa
- つかみ・投擲・ランチャー：https://app.notion.com/p/3e91ea2aa7fa81c5b7bafeaf029b5f2d
- 時間制御（スローモード）：https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70

着手時の計画は、Notion を読めない環境（サインインが要るブラウザだけで、コネクタがない）で、リポジトリの計画書とコードをもとに立てた。Notion の記述との突き合わせは済んでいない。

## 前提

- スローモードは、区間0の TimeScale State の倍率を `ITimeScaleController` で下げて実現する
- 世界全体（敵、かけら、オーブ、ボクセルの粒子、プレイヤーの物理）が遅くなる。視点操作と UI は等速。プレイヤーの移動も倍率どおりに遅くなる
- 区間3で、かけらは `FragmentOrbAdapter` が管理し、ぶつかるか寿命（3 秒）が来るとオーブになる（`Time.time` で数えるので、スロー中は寿命も遅く進む）

## 前提の変化（2026-10-08、区間6 の完了時）

- ダメージのデータ（種類の組み合わせ・量・形状・発生源）と判定の窓口は、区間5・6では作らず、この区間へ持ち越した（区間6の決定 7）。この区間でも作らず、区間8へ持ち越す（決定 10）
- スキル（区間6）は、チャージを `ChargeService.TryConsume` で消費する。スローモードの消費も同じ操作を使う

## 計画書と今のコードの食い違い（着手時に確認）

2026-10-08 に確認した。

| # | 内容 | 根拠 | 対応 |
|---|---|---|---|
| 1 | つかむ操作の入力がない。あるのは SlowMode（F）、Throw（右クリック）、LoadLauncher（R）、FireLauncher（中クリック） | `InputSystem_Actions.inputactions` の Player マップ | Throw を押してつかみ、離して投げる（決定 6）。入力アクションは足さない |
| 2 | プレイヤーの Animator と手のモデルがない | `Animator` を使うコードがない | 7-3 で確かめるのは視点（Cinemachine）と HUD だけにする |
| 3 | サウンドがない（7-9） | `Audio` を使うコードがない | 区間13（13-3「スローモード中の音」）へ持ち越す |
| 4 | 粉砕ダメージを受ける相手（装甲）は区間8まで出てこない | 区間8の計画書（8-2） | ダメージのデータと窓口は区間8へ持ち越す（決定 10） |
| 5 | `ITimeScaleController` は常駐シーンの DI にある | [TimeScaleInitializer](../../../Code/Scripts/Initialization/CoreSystem/TimeScaleInitializer.cs) | InGame の Initializer も常駐の DI から受け取れる（`StandardPlayerInputRouteInitializer` の `IInputController` と同じ）。`PlayerInitializer` に `IInjectable<ITimeScaleController>` を付け、InGameCompositor を作り直す |
| 6 | インゲームから出るときの倍率のリセットがない | 全体計画書「現状」の TimeScale | スローモードの Service が破棄されるときに、自分がかけたスローを解く。スローを始めるのはこの区間なので、ここで扱う |
| 7 | かけらのプールは固定長のリングバッファで、つかんだかけらや装填したかけらも回収されうる | [FragmentOrbAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/FragmentOrbAdapter.cs) の `ReuseAction` の扱い | つかみ・装填の側でも `ReuseAction` を受けて手放す |
| 8 | 手に持ったかけらを自分の剣で切れてしまう | `MeleeCutAdapter` はカメラ前方の範囲の `CuttableObject` を集める | 持っている間は Kinematic にし、コライダーを無効にする |

## 既存コードの確認結果（2026-10-08）

| 対象 | 状態 | 対応 |
|---|---|---|
| [TimeScaleService](../../../Code/Scripts/Application/CoreSystem/TimeScaleService.cs) / [TimeScaleAdapter](../../../Code/Scripts/EngineAdapterLayer/CoreSystem/TimeScaleAdapter.cs) | 倍率を `Time.timeScale` と `Time.fixedDeltaTime` に反映する。操作面は `ITimeScaleController`（常駐の DI） | 変更なし。スローモードの Service が使う |
| [ChargeService](../../../Code/Scripts/Application/Player/ChargeService.cs) | 加算と `TryConsume` | 変更なし |
| [MeleeCutService](../../../Code/Scripts/Application/Player/MeleeCutService.cs) | 攻撃間隔を実時間で数えて `onSwing` を呼ぶ | 振る前に、スロー中の切断回数を使う関数を呼ぶ |
| [FragmentOrbAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/FragmentOrbAdapter.cs) | 切断で生まれたかけらを管理し、オーブにする | 管理から外す `TryTake` を足す |
| [StandardPlayerCameraAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/StandardPlayerCameraAdapter.cs) | 視点入力を届いたその場で Cinemachine の軸へ足す（経過時間を使わない） | 実機で確かめる。CinemachineBrain は Smart Update で、追従が遅れて見えたら `IgnoreTimeScale` を有効にする |
| [PlayerParameterData](../../../Code/Scripts/ExternalLayer/Player/PlayerParameterData.cs) | 移動・HP・近接切断・チャージの値 | スローモードと投擲の値を足す |
| [PlayerInitializer](../../../Code/Scripts/Initialization/Player/PlayerInitializer.cs) | プレイヤーまわりの配線 | スローモードと投擲の配線を足す |

## 作業一覧

| # | 作業 | 層 | 内容 |
|---|---|---|---|
| 7-1 | スローモードの制御 | Application / BlackBoard | 発動時の消費、継続中の消費、ゲージが 0 になったときの終了。倍率は `ITimeScaleController` で変える |
| 7-2 | 切断回数の上限 | Application | スロー中に切断できる回数の上限 |
| 7-3 | 等速で動くものの確認 | EngineAdapter | 視点操作と UI が等速のまま動くこと（食い違い #2） |
| 7-4 | つかむ | Application / EngineAdapter | スロー中に、敵のかけらだけをつかむ |
| 7-5 | 投げる | Application / EngineAdapter | つかんだかけらを投げる |
| 7-6 | ランチャー | Application / EngineAdapter | つかんだかけらを 1 発だけ装填し、撃つ。装填はスロー中だけ |
| 7-7 | 粉砕ダメージ | ― | 区間8へ持ち越す（決定 10） |
| 7-8 | チャージからの除外 | EngineAdapter | つかんだかけらを `FragmentOrbAdapter` の管理から外す（決定 11） |
| 7-9 | サウンドのスロー表現 | ― | 区間13へ持ち越す（食い違い #3） |

## 完了条件

- スローモード中は世界全体が遅くなり、カクつかない。視点操作と UI は等速
- スロー中に何度も切断でき、上限の回数で止まる
- かけらをつかんで投げる、ランチャーに装填して撃つ、ができる
- 投げたかけらと撃ったかけらはチャージにならない
- ゲージが 0 になるとスローモードが終わる

## 詳細仕様

### 決定（2026-10-08）

値はすべて仮で、コミット 5 で調整する。

| # | 項目 | 決定 |
|---|---|---|
| 1 | スローの倍率 | 0.25 |
| 2 | 消費量 | 発動時に 20、継続中は実時間で毎秒 5（0.2 秒ごとに 1）。チャージが 20 未満なら発動しない |
| 3 | 終える操作 | F キーで切り替える。チャージが 0 になっても終わる |
| 4 | スロー中の切断回数の上限 | 1 回のスローにつき、振った回数で 5 回。上限に達したら、スローが終わるまで振っても切らない。区間4A の部位の系統ごとの上限（2 回）はそのまま両方を数える。攻撃間隔（実時間 0.3 秒）も変えない |
| 5 | つかむ対象の選び方 | カメラから視線の向きへ、半径 0.5m・長さ 6m の SphereCast で当たったかけらのうち、視線の中心からの角度が最も小さいものをつかむ。対象は `FragmentOrbAdapter` が管理しているかけら（オーブになる前のもの）だけ。今の切断対象は敵だけなので、これで敵のかけらだけになる |
| 6 | 操作 | 右クリック（Throw）を押してつかみ、離して投げる。持っている間に R（LoadLauncher）で装填し、中クリック（FireLauncher）で撃つ |
| 7 | 投げる軌道 | 重力ありの放物線、初速 25 m/s |
| 8 | ランチャー | 重力なしのまっすぐ、60 m/s。装填はスロー中だけ、撃つのはスローの外でもできる。装填は 1 発 |
| 9 | スローが終わったときに持っているかけら | 持ったまま。投げられるが、装填はできない |
| 10 | ダメージのデータと判定の窓口 | 作らず、区間8へ持ち越す。区間7では粉砕ダメージの受け手が 0 で、型だけ先に作ると基準1に反する。命中した所に `// TODO:` を残す |
| 11 | 投げた・撃ったかけらの後始末 | つかんだ時点で `FragmentOrbAdapter` の管理から外し、オーブにしない。投げた後は、何かに当たるか寿命が来たらプールへ返す |
| 12 | 等速の確かめ方 | 視点の操作は入力ごとに軸へ足すので、倍率によらない。CinemachineBrain の追従は実機で確かめ、遅れて見えたら `IgnoreTimeScale` を有効にする |

## 作業計画

### 新しく作る型と、区間7 での利用者

| 型 | 層・置き場所 | 区間7 での利用者 |
|---|---|---|
| `SlowModeService`（F の入力、発動・継続の消費、倍率の変更、切断回数） | Application | `PlayerInitializer`。継続の消費は UniTask で実時間を待って行う |
| `SlowModeState` / `ISlowModeState`（スロー中か、残りの切断回数） | BlackBoard（`PlayerBoard`、SceneState） | 書くのは `SlowModeService`、読むのは `FragmentThrowService`（つかめるか・装填できるか）と `PlayerDebugInitializer`（DebugGUI） |
| `FragmentThrowService`（つかむ・投げる・装填・撃つの入力と条件） | Application | `PlayerInitializer`。VR で手でつかむ操作にするときも、要求は Application に置く |
| `FragmentThrowAdapter`（持つ・投げる・装填・撃つの物理） | EngineAdapter、InGame | `PlayerInitializer` |

既存の型の拡張：`MeleeCutService`（振る前に切断回数を使う関数を呼ぶ。`onSwing` と同じく直接配線）、`FragmentOrbAdapter`（`TryTake`）、`PlayerParameterData`（倍率・消費量・切断回数の上限・投げる速さ・撃つ速さ）、`PlayerInitializer`（配線）、`PlayerDebugInitializer`（スローの状態の表示）。

- 基準1：粉砕ダメージの窓口（決定 10）、スローの HUD 表示（画面全体が遅くなるので見て分かる。区間11の本番の HUD で要れば足す）、装填の State（持っている・装填したは `FragmentThrowAdapter` が持つ。プールの回収で食い違わないように 1 か所で持つ）は作らない
- 基準2：設計の見直しは提案しない
- 基準3：食い違い #6（倍率のリセット）は、この区間でスローを始める為に今回扱う
- 基準4：リスクは挙げていない。Notion を読めていないことは確認事項としてユーザーに伝えた

### コミットの分け方

| # | 内容 | 確かめ方（ローカル、TestStage、敵 10 体） |
|---|---|---|
| 0 | 区間計画書の更新 | ― |
| 1 | スローモード（`SlowModeService`、State、DI、消費、破棄されるときにスローを解く） | F で発動するとチャージが 20 減り、そのあと毎秒 5 減る。0 になるか、もう一度 F を押すと終わる。20 未満では発動しない。スロー中、敵・かけら・オーブは遅くカクつかず、視点と HUD は等速 |
| 2 | スロー中の切断回数の上限 | スロー中は 5 回振ると、それ以上は切れない。スローを解くと切れる。部位の上限 2 回も効いている |
| 3 | つかむ・投げる | スロー中、視線の中心に近いかけらを右クリックでつかみ、離すと放物線で飛ぶ。投げたかけらはオーブにならず、チャージは増えない。スローの外ではつかめない |
| 4 | ランチャー | 持っている間に R で装填し、中クリックでまっすぐ撃てる。スローの外でも撃てる。装填は 1 発だけ |
| 5 | 通しの確認、値の調整、実装結果と全体計画書の更新 | 完了条件をすべて確かめる |

## 実装中に確かめたこと

- （コミット 1）ローカルでコンパイルし、エラーと警告は 0 件。`PlayerInitializer` が `ITimeScaleController` を受け取るように、InGameCompositor を作り直した。新しい値は `PlayerParameterData` のフィールドの初期値（0.25、20、毎秒 5）のまま使われ、アセットは保存し直していない
- （コミット 1）常駐シーンから再生し、TestStage で確かめた。チャージ 15 では F で発動しなかった。チャージ 100 で F を押すと 80 になり、倍率 0.25、`fixedDeltaTime` 0.005 になった。もう一度 F で倍率 1、0.02 に戻った。チャージ 40 で発動すると、0 になったところで終わった。継続の消費は、10.5 秒で 52（毎秒 4.94）、4.8 秒で 24（毎秒 4.99）
- （コミット 1）継続の消費を、はじめは実時間 0.2 秒ごとに 1 を消費する形で書いたが、待ち終わるフレームの分だけ遅れが積み重なり、毎秒 4.2 しか減らなかった。毎フレーム実時間の経過を溜めて、1 以上溜まった分を消費する形にした
- （コミット 1）スロー中にアウトゲームへ移ると、倍率 1、`fixedDeltaTime` 0.02 に戻った（食い違い #6）。エラーと警告は 0 件
- （コミット 1）視点のカメラ（`CameraPivot`）は `PlayerRoot` の子で、追従とダンピングを持たない。視点の回転は入力をその場で PanTilt の軸へ足すので、倍率によらない。CinemachineBrain の `IgnoreTimeScale` はダンピングとブレンドにしか効かないので、変えていない（決定 12）。スロー中の見た目のなめらかさは、コミット 5 の通しの確認でユーザーが確かめる

- （コミット 2）`SlowModeService.TryUseCut` を `MeleeCutService` へ直接渡し、攻撃間隔の判定のあと、振る前に呼ぶ。残りの回数は `SlowModeState.RemainingCuts` に持ち、DebugGUI の SlowMode に出す。スロー外では回数を数えない。ローカルでコンパイルし、エラーと警告は 0 件
- （コミット 2）プレイモードで、スロー外に 1 回振れた。スロー中に左クリックを 7 回押すと、5 回目まで振って残りが 4 → 0 になり、6・7 回目は振らなかった（最後に振った時刻が変わらない）。スローを解くと振れた。スローをかけ直すと残りは 5 に戻った。エラーと警告は 0 件
- （コミット 2）部位の系統ごとの上限（2 回）は、`MeleeCutAdapter` が `CuttableObject.IsCuttable` で対象を除く既存の処理のままで、この区間では変えていない。実機での確認は、コミット 5 の通しの確認で行う

- （コミット 3）InGame に `FragmentThrow`（`FragmentThrowAdapter`）を置き、`PlayerInitializer` につないだ。かけらのプールは `FragmentOrb` と同じもの、つかむかけらを探すレイヤーは Shard、投げる先を決めるレイは Ignore Raycast・Player・Shard・Blade を除く。Compositor の作り直しは要らない。ローカルでコンパイルし、エラーと警告は 0 件
- （コミット 3）持っている間は `Rigidbody` を Kinematic にして `detectCollisions` を切り、`CuttableObject.DisableCutting` で剣の対象から外す。プールへ返すと `OnRecycle` が切断できる状態に戻し、`ReuseAction` で物理の設定を戻す。投げる向きは、持っているかけらの中心から、視線の先でレイが当たった点（なければ 100m 先）へ
- （コミット 3）プレイモードで、スロー中に敵を切ってかけらを出し、確かめた。右クリックの長押しで、押したときにつかみ、離したときに初速 25 m/s・重力ありで飛んだ（スロー中なので実時間では約 6 m/s）。Adapter を直接呼んでつかむと、管理中のかけらが 16 → 15 になり、持っているかけらは切断できず、Kinematic で当たり判定を切り、カメラのローカル座標 (0.40, -0.30, 1.20) にあった。寿命（スロー中は実時間で約 12 秒）を過ぎても、17 秒持ったままだった（ほかのかけらはすべてオーブになった）。視点を 90 度回してもついてきた
- （コミット 3）投げたかけらは、地面に当たったところでプールへ返り、オーブは出なかった。返ったかけらは物理の設定（Kinematic なし、当たり判定あり、重力あり、補間あり）と切断できる状態に戻っていた
- （コミット 3）スローモードを使わずにデバッグの操作で倍率だけ 0.25 にし、かけらが 4 つある状態で右クリックしても、つかまなかった。直後に Adapter を直接呼ぶとつかめたので、スローモード中だけという条件で止まっている。持ったままスローの外で右クリックすると手から離れた（決定 9）。エラーと警告は 0 件
- （コミット 3）持っているかけらは `LateUpdate` でカメラに合わせる。CinemachineBrain との実行順によっては 1 フレーム遅れて見える可能性があるので、コミット 5 の通しの確認で見る

## 次の区間へ持ち越すこと（計画の時点）

- 区間8：ダメージのデータと判定の窓口、投げた・撃ったかけらの粉砕ダメージ（決定 10）
- 区間11：スローの状態の HUD 表示（要るなら）。ポーズを `timeScale = 0` で作る場合、スローの継続の消費は実時間で数えるのでポーズ中も減ること、スローモードとポーズがどちらも `ITimeScaleController` で倍率を書くこと（ポーズを解いたときにスローの倍率へ戻す必要がある）
- 区間13：スローモード中の音（食い違い #3）
- 仕様書：決定 1〜12 を Notion のスローモードと、つかみ・投擲・ランチャーの仕様に反映するか（この区間の計画は Notion を読まずに立てた）

## 他プラットフォームへの対応

- VR では、実際に手でつかんで投げる操作にできる。つかむ・投げるの要求を Application（`FragmentThrowService`）で持ち、判定の違いは Adapter に閉じ込める
