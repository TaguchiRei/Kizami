# 区間36：SE と BGM

| 項目 | 内容 |
|---|---|
| 状態 | 未着手（詳細仕様の確認事項が残っている） |
| 目安の時期 | 未定 |
| 前提となる区間 | 35（羽天轟号）。演出より先に音の土台を作る（2026-10-21、ユーザーの優先度） |
| 全体計画 | [InGameOverallPlan.md](../InGameOverallPlan.md) |
| 親の計画書 | [Section33_Polish.md](Section33_Polish.md) |

## 目的

音の土台を作り、今ある素材で BGM と 4 つの SE を鳴らす。

## 確定した仕様

| # | 内容 | 決めた日 |
|---|---|---|
| 1 | SE と BGM を入れる | 2026-10-21 |
| 2 | 今ある音は、ガラスが割れる音・装甲を切る音・ボクセルを攻撃したときの音・ボクセルが崩れたときの音・BGM。それ以外の音は一度無視して進める | 2026-10-21 |
| 3 | 音の再生は、Adapter から SE 再生の Adapter へ直接つなぐ（State / Event を挟まない） | 2026-10-21（方針案どおり） |

## 既存の資産

- 音の素材：`Assets/LocalAssets/Sound/`（10 個。`.gitignore` で除外されている）。二次配布禁止のものを含むので、リポジトリ外へ出さない。クローンやクラウドのセッションには無いので、クリップが無くても動く作りにする。AI で生成した音に置き換える場合もある
- 音量の反映：[AudioVolumeAdapter.cs](../../../Code/Scripts/EngineAdapterLayer/CoreSystem/AudioVolumeAdapter.cs) が、設定の音量を AudioMixer の `MasterVolume` / `BgmVolume` / `SeVolume`（dB）へ反映する。[OperationSettingInitializer.cs](../../../Code/Scripts/Initialization/ApplicationManagement/OperationSettingInitializer.cs) が、Mixer が設定されているときだけつなぐ
- AudioMixer アセット：プロジェクトにあるか未確認（確認事項 2）
- 音を鳴らすコード：まだない

### 音を鳴らす場所

| 音 | 場所 |
|---|---|
| ガラスが割れる | `GlassPane.Shatter`（[GlassPane.cs:104](../../../Code/Scripts/EngineAdapterLayer/Stage/GlassPane.cs)） |
| 装甲を切る | `ArmorPanel.ApplyDamage`（[ArmorPanel.cs:51](../../../Code/Scripts/EngineAdapterLayer/Stage/ArmorPanel.cs)） |
| ボクセルを攻撃 | `VoxelDestructionAdapter.SetOnCarved` / `Carve*` |
| ボクセルが崩れる | `VoxelBuilding.RegisterOnCollapsed` / `RegisterOnCollapsing` |

## 作業一覧

| # | 作業 | 内容 |
|---|---|---|
| 36-1 | 音の土台 | AudioMixer（Master / BGM / SE）を用意し、既存の `AudioVolumeAdapter` につなぐ。SE を鳴らす口を作る。再生はエンジン標準の `AudioSource` と `AudioMixerGroup` を使う |
| 36-2 | BGM | 1 曲を常駐シーンでループ再生する |
| 36-3 | SE 4 つ | 上の表の 4 か所に再生をつなぐ。ガラスは 4 バリエーションからランダムに選ぶ。同じ SE が重なる場面は、同時再生数の上限で間引く |

## 新しく作る型

| 型 | 層 | 今の区間の利用者 |
|---|---|---|
| SE 再生の Adapter（BGM の再生を含む） | EngineAdapterLayer | 上の 4 か所と BGM |
| クリップの登録設定（`ScriptableObject`） | ExternalLayer | SE 再生の Adapter |

State と Event は足さない。クリップが空なら鳴らさず、エラーも出さない。

## 完了条件

- BGM が流れる
- 4 つの SE が、対応する出来事で鳴る
- コンフィグの音量が、実際の音量に反映される
- クリップが無い環境でも、エラーなく動く

## 詳細仕様で決めること

| # | 項目 | 案 |
|---|---|---|
| 1 | 素材の対応（未確認） | ガラス＝`Firefly_audio_…` の 4 個、装甲＝`鋼の剣.mp3`、ボクセル攻撃＝`打撃4.mp3`、崩壊＝`建物が少し崩れる2.mp3` / `建物が大きく崩れる2.mp3`（使い分けるか片方だけか）、BGM＝`Electronic_Circuit.mp3`。ファイル名からの推測 |
| 2 | AudioMixer アセット | あるかを確認する。なければ 36-1 で作る |
| 3 | ボクセル攻撃の鳴らし方 | 1 回の攻撃につき 1 回にする（広範囲の攻撃で重ならないようにする）案 |
| 4 | 同時再生数の上限 | 値は実装時に聞きながら決める |

## 実装結果（2026-10-21、実機での聴き取りは未確認）

- 作ったもの：`SoundPlayer`・`SoundEffectKind`（EngineAdapterLayer/CoreSystem）。クリップの登録は ScriptableObject を作らず、`SoundPlayer` の SerializeField に直接置いた（利用者が `SoundPlayer` だけなので）
- 常駐シーン（`UsefulToolkitPersistent`）に `SoundPlayer` を置き、`MainMixer` の SE / BGM グループと、素材を割り当てた
- 呼び出し：`GlassPane.Shatter`、`ArmorPanel.ApplyDamage`（攻撃・破壊・粉砕が当たったとき）、`VoxelDestructionAdapter.Carve`（体積を削れた攻撃ごとに 1 回。倒壊の跡を削るなど `notifiesCarved` が false のものは鳴らさない）、`VoxelBuilding` の倒壊 2 か所
- 確認：コンパイルは成功。プレイモードで BGM と SE 2 つが再生状態になり、エラーログは出なかった。音の聴こえ方・素材の対応・音量バランスはレビュー時に確認する

## 次の区間へ持ち越すこと

| 内容 | 持ち越す条件 |
|---|---|
| プレイヤーの行動・命中・爆発・敵の攻撃・羽天轟号・UI の SE | 素材を用意したとき |
| スローモード中の音（ピッチ・Low-pass） | 素材と同じく、必要になったとき |
| シーン・ステージ・ボス戦ごとの BGM の切り替え | 曲が 2 曲以上になったとき |
| State / Event 経由の再生 | Application 起点の SE が増えたとき |
| 37・38・39 で足す演出の SE | それぞれの区間で付ける |

## 見つけた問題（今回は扱わない）

| 内容 | 場所 |
|---|---|
| 区間36の旧計画書に「音量の State は保存・読み込みのみで、音への反映はまだ」とあったが、`AudioVolumeAdapter` で反映は実装済み | 本書で修正済み |
