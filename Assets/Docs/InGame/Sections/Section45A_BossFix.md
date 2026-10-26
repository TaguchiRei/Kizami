# 区間45A：ボス戦の修正

| 項目 | 内容 |
|---|---|
| 状態 | 実装済み（2026-10-26）。コミット2（45-6 の調査）だけ、ボス戦を遊んで起きるかを確かめるまで持ち越し。確認の結果は「実装結果」に書いた |
| 目安の時期 | 未定 |
| 前提となる区間 | 40（ボスの行動の順番を `BossBattleService` に移したあと） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section45_Backlog.md](Section45_Backlog.md)（作業者向けの前提は親に書いてある。先に読む） |
| 扱う項目 | 45-6〜45-12 |

## 目的

ボス戦で見つかった不具合（2 回目の直接戦闘がすぐ終わる、足が上がったまま、円の柵が重い）を直し、遊びやすさ（ビームのまぶしさ、塔投げの落ちる位置、攻撃の予兆、最後の段階のクリア）を整える。

## 確定した仕様（2026-10-25・26、ユーザー）

| # | 内容 |
|---|---|
| 1 | 円の柵（`BossArena` のビーム）はボクセルを溶かさない。プレイヤーへのダメージだけを残す |
| 2 | まぶしさを抑えるのはボスのビームだけ。通常の敵のフィニッシャーは今のまま。ボス用のマテリアルを別に作り、基本色を黒などの暗い色にする |
| 3 | 塔投げは、プレイヤーの位置ではなく、ボスからプレイヤーの方向へ一定の距離の地点に落とす。距離は水平（XZ）だけで決め、高さは地面から決める |
| 4 | 攻撃の前に「ピピッ」という音を鳴らし、攻撃に使う部位を赤か黄色で点滅させる |
| 5 | 最後の段階（`Stopped`。ボスが止まったあと）は、ボスのどこか（コア以外も含む）を一度でも壊したら、その 5 秒後にクリアにする |

## 既存コードの確認結果（2026-10-26）

| 場所 | 今の状態 |
|---|---|
| `BossBattleService.Tick`・`UpdateDamageRules`（`Application/Enemy/BossBattleService.cs` 101〜127・203〜220 行） | 直接戦闘（Arriving・Stomping・CoreExposed）で体力が尽きたら Down へ進む。足踏みを始めて `CollapseGrace`（2 秒）過ぎたら、落ちてくる塊で体力が減るようになる |
| `BossBattleAdapter.GetArrivalDestination`（`EngineAdapterLayer/Enemy/BossBattleAdapter.cs` 382〜387 行） | 1 周目は決まった着地点、2 周目は水晶を置いた場所の中心へ飛来する |
| `BossBattleAdapter.Enter` の Stomping（231〜235 行） | 足踏みの段階に入ると、ボスの位置を中心に円形の空間（`BossArena.Open`）を作る |
| `BossArena.Open`（`EngineAdapterLayer/Enemy/BossArena.cs`） | ユニットごとに `EnemyFinisherBeams.Shot.BeginContinuous(Vector3.down)` を呼ぶ。`melts` の既定は true で、柵のビームが真下のボクセルを溶かし続ける |
| `EnemyFinisherBeams.Shot.UpdateFiring` | `melts == false` のときは「見た目だけ」になり、プレイヤーへのダメージも出さない。ダメージだけを残す形はまだない |
| `HealthTarget.FixedUpdate`（`EngineAdapterLayer/Stage/HealthTarget.cs` 143〜162 行） | ボスのパーツの範囲に入った、落ちている塊の体積で体力を減らす |
| `BossBody`（`EngineAdapterLayer/Enemy/BossBody.cs`） | 脚ごとに足を置いた位置（`_planted`）を持つ。足を運び直すのは、足の位置と基準の位置の水平のずれが閾値を超えたときだけ。足の下の地面がなくなっても、運び直さない |
| `BossArrival.Finish`（`EngineAdapterLayer/Enemy/BossArrival.cs` 129〜135 行） | 着いたら、周りのボクセルを半径 15m・高さ 80m の円柱で削る（`ClearAround`） |
| `BossTowerThrow.Throw`（`EngineAdapterLayer/Enemy/BossTowerThrow.cs` 140〜165 行） | 狙う地点はプレイヤーの位置。地面はプレイヤーの 5m 上から下へ探し（420〜425 行）、見つからなければプレイヤーの位置そのもの（空中）を使う。塔は円の中心を挟んだ反対側の上空から飛んでくる |
| ボスのビームのマテリアル | 降下フィニッシャー（`BossEnemy.prefab`）と円の柵（`Stage_Boss.unity`）の `EnemyFinisherBeams` は、通常の敵のフィニッシャーと同じ `Art/Materials/FinisherBeam.mat`・`FinisherBeamInner.mat`・`FinisherGlow.mat` を使う。下部レーザーは `BossLaserRed.mat` |
| 攻撃の実行（`BossBattleAdapter.TickAction`） | 足踏み（`BossShockwaves.TryStomp`）・下部レーザー（`BossLaser.TryFire`）・降下フィニッシャー（`BossFinisherStrikes.TryFire`）は、行動が来たら（クールタイムが空き次第）すぐ撃つ。予兆があるのは塔投げ（予測線 2.5 秒）と、フィニッシャーのビームそのものの照準・予兆だけ |
| `BossWeapons.SetHighlight` | ダウン中に、壊せる武装を `MaterialPropertyBlock` で赤く塗る。点滅の先例になる |
| `SoundPlayer` / `SoundEffectKind`（`EngineAdapterLayer/CoreSystem/`） | 効果音は 4 種（GlassBreak・ArmorCut・VoxelHit・VoxelCollapse）。種類ごとにクリップの配列を Inspector で持つ |
| 最後の段階のクリア | `Stopped` では、クリアの破壊対象（コア）を 0.7 削ったらクリア（区間32）。案内の文は「ボスのコアを壊せ」（`BossGuideScript.cs` 30 行） |
| 止まったあとの当たり | 最終で体力を減らす `Drain` は `HealthTarget` を通らない為、`HealthTarget._isDepleted` は false のまま。止まったあとに当てても `TargetHealthService.ReportHit` が無視し（表示が Broken）、誰にも伝わらない |

## 原因の見立て（仮定。未確認）

| # | 不具合 | 見立て | 確かめ方 |
|---|---|---|---|
| 45-6 | 2 回目の直接戦闘が一瞬で終わる | 2 周目は水晶のあった所（建物の間）に降りる。足踏みの段階で柵が開き、柵のビームが周りの建物を溶かして崩す。猶予の 2 秒が過ぎたあとに落ちてきた塊が、ボスの範囲に入って体力を大きく減らす | 2 周目で `TargetHealthService.ReportHit` に一時的なログを入れるか、uloop の pause-point で止め、減った当たりの種類（Collapse か）と量を見る。ログは確認のあとで消す |
| 45-7 | 2 回目の直接戦闘で足が上がったまま | 着地のあとの `ClearAround`、または柵の溶かしで、足を置いていた建物が消える。ボスは歩かないので足を運び直す条件を満たさず、足が消えた建物の高さに残る | 2 周目の着地のあと、上がった脚の `_planted` の高さと、その真下の地面の高さを比べる |

45-6 は、コミット1（柵が溶かさない）で原因の大半がなくなる見込み。コミット1 のあとに確かめて、まだ起きるなら原因を調べ直して報告する。

## 作業計画

コミットごとにコンパイルし、ボスステージで確かめてから報告して止まる。

| コミット | 項目 | 内容 | 確かめること |
|---|---|---|---|
| 1 | 45-8 柵が溶かさない | `EnemyFinisherBeams.Shot` に「溶かさず、最初に当たった物までのビームでダメージを与える」形を足す。今の `bool melts` を、溶かす・見た目だけ・ダメージだけ の 3 つを表す引数にする（enum か bool 2 つかは作業者が決め、理由を報告する）。ダメージだけの形では、ビームの長さを `CastBeam`（ボクセルを含む最初の物）で決め、当たり判定（`IsPlayerHit`）もその長さまでにする。`BossArena.Open` はダメージだけの形で撃つ。`BossArena.Update` の `BeginStrikeBatch` は削らないので外す | 柵の下の建物が削れない。柵に触れるとダメージを受ける。円の外に出たときのダメージも今のまま。柵を開いた直後のフレーム時間が下がる（Profiler の `Voxel.BeamMelt` が出ない） |
| 2 | 45-6 調査 | 上の見立てを確かめる。コミット1 のあとでも起きるかを見て、原因と直し方を報告して止まる。直し方はユーザーが決める | 2 周目の直接戦闘が、足踏み → コア露出 と進む |
| 3 | 45-7 足の置き直し | `BossBody` で、足を置いた位置の真下の地面を一定の間隔で調べ直し、足が地面より決めた高さ以上浮いていたら、その組の足を運び直す。飛来を終えたときにも全部の足を置き直す（`BossArrival` が終わったことは `BossBattleAdapter` が知っているので、そこから `BossBody` の置き直しを呼ぶ） | 2 周目の着地のあと、すべての足が地面に着く。1 周目と最終の足踏みの見た目が変わらない |
| 4 | 45-10 塔投げの落ちる位置 | `BossTowerThrow.Throw` で、狙う地点を「円の中心（なければボスの位置）から、プレイヤーへの水平の向きに `_aimDistance` 離れた所」にする。`_aimDistance` は新しい SerializeField（既定は塔の長さより短くし、倒れた塔がボスに届く値。案は 18m）。地面は狙う地点の真上の高い所（`_spawnHeight` 程度）から下へ探し、見つからなければ円の中心の地面の高さを使う | プレイヤーが空中にいても、遠くにいても、塔が地面に刺さる。刺さった塔の根元を削ると、倒れてボスに当たる |
| 5 | 45-9 ボスのビームの色 | `FinisherBeam.mat`・`FinisherBeamInner.mat` を複製して、ボス用のマテリアル（名前の案：`BossBeam.mat`・`BossBeamInner.mat`）を作り、基本色を暗くする。`BossEnemy.prefab` と `Stage_Boss.unity` の `EnemyFinisherBeams` に割り当てる。発射口と先端の光は、`_glowColor` と `_glowScale` を下げる。先にシェーダーの合成方法を確かめる。加算合成だと黒は見えなくなるので、その場合はアルファ合成のシェーダーかマテリアルの設定を使い、方法を報告してから進める | ボスの柵と降下フィニッシャーが暗い色で見え、まぶしくない。通常の敵のフィニッシャーは今のまま |
| 6 | 45-11 攻撃の予兆 | 下の「攻撃の予兆の作り」のとおり | 足踏み・下部レーザー・降下フィニッシャー・最終の一斉攻撃の前に、使う部位が点滅し、音が鳴る。壊れた武装の番では予兆を出さない。ダウン中の赤い塗りと混ざらない |
| 7 | 45-12 止まったあとのクリア | 下の「止まったあとのクリアの作り」のとおり | 止まったあと、コア以外のどこかを壊すと、5 秒後にクリアになる。コアを先に壊しきったら、今までどおりすぐクリアになる。止まる前の当たりでは数え始めない |

### 攻撃の予兆の作り

- 予兆は「攻撃 1 つを実行する中身」なので、行動の順番を決める `BossBattleService` ではなく、`BossBattleAdapter` で行う（区間40 で、武装ごとのクールタイムを Adapter に残したのと同じ考え方）
- `BossWeapons` に次の 2 つを足す
  - 種類ごとに、次に使う武装の番号を、順番を進めずに返すメソッド（`TakeNext` の読むだけの版）
  - 武装 1 つを点滅させる・止めるメソッド。点滅は `Update` で `_warningColor` と元の色を `_warningBlinkInterval` ごとに切り替える。色は `SetHighlight` と同じ `MaterialPropertyBlock` の書き方にする
- 足踏み・下部レーザー・降下フィニッシャーの各 Adapter（`BossShockwaves`・`BossLaser`・`BossFinisherStrikes`）に、クールタイムが空いたかを返すプロパティを足す。予兆は、クールタイムが空いてから始める（予兆のあとにクールタイムで待たないように）
- `BossBattleAdapter.TickAction` の Stomp・Laser・Strike・FinalVolley では、クールタイムが空いたら、次に使う武装を点滅させて音を鳴らし、`_warningDuration`（新しい SerializeField。案は 0.8 秒）待ってから今の撃つ処理を呼ぶ。次の武装が壊れていれば、予兆を出さずに今までどおり進める
- 音は `SoundEffectKind` に `BossWarning` を足し、`SoundPlayer` にクリップの配列を足して、常駐シーンの `SoundPlayer` に割り当てる。音は audio-studio（`generate_audio`、audio_type は sfx）で作る。プロンプトの案：sound_source「electronic warning beeper」、sound_action「two short high-pitched beeps」、sound_material「clean synthetic tone」、sound_space「close, dry」。長さ 1 秒、variations 3 でユーザーに選んでもらう。保存先は既存の効果音と同じフォルダにする
- 水晶の段階（`Barrage`：上部タレット・降下フィニッシャー・頭部のレーザー）は、決めること #3 の答えで扱う

### 止まったあとのクリアの作り

- `HealthTarget` の「体力が尽きたあとの当たり」の通知を、プレイヤーの攻撃の当たり（通常攻撃・スキル・量を決めたダメージ。落ちる塊は除く）をすべて知らせる通知に変える。知らせたあと、今までどおり体力の報告へ進む
- `BossBattleService.ReportHitWhileDown` を、当たりを受け取るメソッド（名前の案：`ReportPlayerHit`）に変える
  - Down で攻撃を待っているとき：今までどおり、武装を 1 つ壊させる
  - Stopped で、まだ数えていないとき：クリアまでの時間（`BossBattleSettings` に `_stoppedClearDelay` を足す。既定 5 秒）を数え始める
  - ほかの段階では何もしない。直接戦闘で体力を尽きさせた一撃は、段階がまだ Down でないので数えない
- `Tick` で、数え始めた時間が過ぎたら `BossBattleState` の「クリアしたか」（新しいプロパティ。名前の案：`IsDefeated`）を true にする。段階は Stopped のままにする（`IsStopped` を読む通常攻撃・案内・目標の選び方を変えない為）
- `StageFlowService.Tick` で、`IBossBattleState` があり（なければ毎フレーム取りに行く。`BossBattleService` が体力の State を取るのと同じ形）、`IsDefeated` なら `Clear` を呼ぶ
- 案内の文（`BossGuideScript` の Stopped）を、コアに限らない文に変える。文はユーザーに確かめる（決めること #4）

### 新しく作る型と、今の区間で使う利用者

| 型 | 層 | 利用者 |
|---|---|---|
| ビームの撃ち方の enum（コミット1 で enum にする場合） | EngineAdapterLayer | `BossArena`（ダメージだけ）、`EnemyFinisherAttack` など今の呼び出し元（溶かす・見た目だけ） |
| `BossBeam.mat`・`BossBeamInner.mat` | アセット | ボスの降下フィニッシャーと円の柵 |
| `SoundEffectKind.BossWarning` と、その音のクリップ | EngineAdapterLayer・アセット | `BossBattleAdapter` |

## 完了条件

- 2 周目の直接戦闘が、1 周目と同じ流れで進む（足踏み → コア露出 → 体力が尽きたらダウン）
- 2 周目の着地のあと、ボスの足が地面に着いている
- 円の柵がボクセルを溶かさず、触れるとダメージを受ける
- ボスのビームが暗い色で、まぶしくない
- 塔投げが、プレイヤーが空中にいても遠くにいても地面に刺さり、倒すとボスに当たる
- 足踏み・下部レーザー・降下フィニッシャーの前に、部位の点滅と音が出る
- 止まったあと、ボスのどこかを壊すと 5 秒後にクリアになる

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 45-6 の直し方 | コミット2 の調査の結果で決める。落ちる塊がまだ原因なら、2 周目も猶予を長くするか、ボス戦では落ちる塊のダメージをやめる |
| 2 | 点滅の色 | 黄色。ダウン中の「壊せる武装」の赤と見分ける為 |
| 3 | 水晶の段階の攻撃にも予兆を付けるか | まず直接戦闘と最終だけに付け、実機で見てから決める |
| 4 | 止まったあとの案内の文と、残り時間の表示 | 文は「ボスを壊せ」。5 秒の残りは出さない（出すなら案内の文に秒数を足す） |
| 5 | 下部レーザーと頭部のレーザーも暗い色にするか | 柵と降下フィニッシャーを変えたあと、実機で見てユーザーが決める |
| 6 | `_aimDistance` と `_warningDuration` の値 | 18m と 0.8 秒から始め、実機で調整する |

## 実装結果（2026-10-26）

### 決めたこと
- 柵のビームの撃ち方は `EnemyFinisherBeams.BeamMode`（`Melt`・`VisualOnly`・`DamageOnly`）の enum にした。bool 2 つだと「溶かす」と「ダメージを与える」の組み合わせが 4 通りになり、使わない組み合わせが混ざる為。`DamageOnly` はボクセルを含む最初に当たった物までをビームの長さと当たり判定の範囲にする（`CastBlock` にボクセルを含めるかの引数を足した）。`SkyBeamAdapter`（プレイヤーのビーム）は `VisualOnly`
- 足の置き直しは、`BossBody` の `Replant()`（飛来を終えたとき `BossBattleAdapter` が呼ぶ）と、置いた足が真下の地面より `_floatThreshold`（1m）以上浮いたら運び直す定期チェック（`_groundCheckInterval` 0.5 秒）の 2 つ
- 塔投げの狙う地点は、円の中心からプレイヤーの方向へ水平に `_aimDistance`（18m）離れた所。地面は、狙う地点の真上（中心の地面から `_spawnHeight`）から下へ探し、見つからなければ中心の地面の高さ
- ビームの色：シェーダー（`FinisherBeam.shader`）は加算合成ではなく、プリマルチプライドアルファ合成（`Blend One OneMinusSrcAlpha`）。色は `_BaseColor` ではなく `_Color1〜3` で決まり、中心は `_CoreAmount` で白に寄る。暗くしすぎると「ビームに見えない」ので、ユーザーの見た目で明るさを戻した。ボス用マテリアル `BossBeam`・`BossBeamInner` の最終値は `_Color1` (1, 0.04, 0.08)、`_Color2` (0.45, 0, 0.8)、`_Color3` (0.1, 0.1, 0.5)、`_Emission` 6、`_CoreAmount` 0.25、`_ScrollSpeed` 1.2（元は 0.3）。発射口と先端の光は `_glowColor` (0.25, 0.05, 0.08)、`_glowScale` 0.3
- 攻撃の予兆は、点滅の色を黄色（`BossWeapons._warningColor`）、間隔 0.1 秒、予兆の長さ `_warningDuration` を 0.8 秒にした。音は `BossWarning_v2`・`v3` を、ランダムに 1 つ選ぶのではなく重ねて鳴らす（`SoundPlayer.PlayLayered`）。予兆の対象は、足踏み・下部レーザー・降下フィニッシャー・最終の一斉攻撃（下部レーザーの前）。水晶の段階（`Barrage`）には付けていない
- 止まったあとのクリアは、`HealthTarget` の通知を落ちる塊以外の当たり全部に広げ（`SetOnPlayerHit`）、`BossBattleService.ReportPlayerHit` が止まったあとの最初の当たりから `StoppedClearDelay`（5 秒）を数えて `IsDefeated` にし、`StageFlowService.Tick` が `Clear` を呼ぶ。案内の文は「ボスを壊せ」、5 秒の残りは出さない

### 作った主なもの
- 柵：`EnemyFinisherBeams`（`BeamMode`）、`BossArena`（`DamageOnly` で撃ち、削りジョブのまとめ待ちを外した）
- 足：`BossBody`（`Replant`・`FindFloatingPair`）、`BossBattleAdapter`
- 塔投げ：`BossTowerThrow`（`_aimDistance`）
- ビーム：`Art/Materials/BossBeam.mat`・`BossBeamInner.mat`。`BossEnemy.prefab` と `Stage_Boss.unity` の `EnemyFinisherBeams` に割り当てた
- 予兆：`BossWeapons`（`PeekNext`・`SetWarning`・`SetPaint`）、`BossShockwaves`・`BossLaser`・`BossFinisherStrikes` の `IsReady`、`BossBattleAdapter`（`IsWarningDone`・`CancelWarning`）、`SoundEffectKind.BossWarning`、`SoundPlayer`（`_bossWarningClips`・`PlayLayered`）、`LocalAssets/Sound/BossWarning_v2.wav`・`v3.wav`。常駐シーンの `SoundPlayer` に割り当てた
- クリア：`BossBattleState`（`IsDefeated`）、`BossBattleSettings`（`_stoppedClearDelay`）、`BossBattleService`、`HealthTarget`、`StageFlowService`、`BossGuideScript`

### 完了条件の確認結果
- 円の柵がボクセルを溶かさず、触れるとダメージを受ける：ユーザーがプレイして確認した（60700c6）。フレーム時間（Profiler の `Voxel.BeamMelt`）は測っていない
- 2 周目の着地のあと足が地面に着く：ユーザーがプレイして確認した（5b5118a）
- ボスのビームが暗い色でまぶしくない：ユーザーが見た目を確認し、暗すぎて見えない点を明るさと動く速さの調整で直した（34b5f8f・96d3f0e）。通常の敵のフィニッシャーは変えていない
- 塔投げが、プレイヤーが空中・遠くにいても地面に刺さり、倒すとボスに当たる：コンパイルは確認したが、動かして確かめた結果は記録していない。レビューのときに確認する
- 足踏み・下部レーザー・降下フィニッシャーの前に、部位の点滅と音が出る：コンパイルは確認したが、動かして確かめた結果は記録していない。壊れた武装の番で出ないこと、ダウン中の赤い塗りと混ざらないことも、レビューのときに確認する
- 止まったあと、ボスのどこかを壊すと 5 秒後にクリアになる：コンパイルと、アウトゲームの起動でエラーが出ないことだけ確認した。止まったあとの当たりが `HealthTarget` まで届くか、コアを先に壊しきったら今までどおりすぐクリアになるか、止まる前の当たりで数え始めないかは、動かして確かめていない。レビューのときに確認する
- 2 周目の直接戦闘が、1 周目と同じ流れで進む（45-6）：確かめていない。ユーザーの判断で、コミット2（調査）は後回しにした

### 次の区間へ持ち越すこと
- 45-6：コミット1（柵が溶かさない）で、建物が倒れてボスにダメージが入る流れが直った可能性がある。次にボス戦を遊んで 2 周目がすぐ終わるかを確かめ、まだ起きるなら、コミット2 の調査（`TargetHealthService.ReportHit` の一時ログで当たりの種類と量を見る）を行って、原因と直し方を報告する
- 決めること #3（水晶の段階の攻撃にも予兆を付けるか）と #5（下部レーザーと頭部のレーザーも暗い色にするか）は、実機で見てから決める

## 見つけた問題（今回は扱わない）

- プレハブとシーンを保存し直した為、`BossEnemy.prefab` と `Stage_Boss.unity` の差分に、コードにもうない古い値の削除（`_damage` など）と、新しい値の追加が混ざっている

- 飛来の前に周りの建物を先にボクセルにする処理（`BossBattleAdapter.PreloadArrivalArea`）は、柵が溶かしたときの負荷を散らす為のもの。柵が溶かさなくなると要らなくなるかもしれない。消すかはコミット1 のあとに計測して決める
