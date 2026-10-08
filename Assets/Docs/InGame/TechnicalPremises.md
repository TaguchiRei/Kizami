# インゲームの技術前提

計画・リスク評価・設計の提案では、次の前提をもとにする。仕様書に書かれていないものを含む。

## プラットフォーム

PC（マウスとキーボード）を先に作り、そのあとスマホと VR にも順次対応させる。

- 各区間の完了条件は PC で判定する
- プラットフォームごとの違いは EngineAdapterLayer の Adapter と入力の層に閉じ込め、Application と BlackBoard はプラットフォームに依存させない
- コードにはビルドモード（PC / Mobile / VR）と、操作系ごとの Adapter（Standard / Vr）がある。VR とスマホの既存コードは壊さずに保つ

## 敵・ボクセル・時間制御

- 敵のメッシュ切断は、マルチスレッド / Burst / Job で並列化と非同期化が済んでいる（UsefulToolkit.MeshCut）
- 敵とボスは SkinnedMeshRenderer を使わず、パーツごとに分かれた軽量なメッシュを、パーツ単位で FK / IK で動かす。そのため、ボクセルのスキニング（`Assets/Docs/Voxel/Skinning/SkinningPlan.md`）はボスには要らない
- 敵の仮モデルは四足歩行の攻撃する敵（`Assets/Art/Models/EnemyModels/MachineEnemy_Attacker.fbx`、体のプレハブは `Assets/Level/Prefabs/Enemy/AttackerEnemy.prefab`。三角形は合計 4310）。同じフォルダに、シールドを持つ敵（`MachineEnemy_Defender`）、吸収型の敵（`MachineEnemy_Finisher`。フィニッシャー持ちとも呼ぶ）、ボス（`MachineEnemy_Boss`）のモデルがある。敵のモデルは部位ごとの回転を 0 にして書き出し（脚の IK が休みの姿勢の回転を 0 とする為）、取り込み設定で Read/Write を有効にする。綴りが Attakker のプレハブは旧構成のもの。敵の体は最初にすべてプールに用意し、実行中は Instantiate しない
- シールドを持つ敵と吸収型の敵は、ユーザーが用意したモデルに差し替えて作る。シールドを持つ敵（ディフェンダー）は、グループの中心で円形のバリアを張る。バリアは物理の当たり判定を持ち、プレイヤーが中へ入れないようにする
- ボクセルでできた物は、仕様上メッシュ切断できない。切断攻撃に破壊属性を付けたときは、切断方向と同じ向きに、厚みゼロの平面でボクセルを分ける処理を走らせる
- 大きな建物は、1 階分をベイクして縦に積み重ね、近づくまではメッシュで置く。ボクセルは点ごとに素材（マテリアルと融点）を持ち、融点が強度を表す。攻撃は中心・強さ・減衰率を持ち、届く強さが融点以上の所を壊す。敵は建物に上れない（区間15。方式は `Assets/Docs/Voxel/LargeObjectVoxel.md`）
- スローモードは未実装。設計案は Notion のシステムリスト「時間制御（スローモード）」（https://app.notion.com/p/3ea1ea2aa7fa8183bbfac10bfafd4d70）を正とする。世界全体が遅くなり、プレイヤーのアニメーション・視点操作・UI だけ等速。倍率の正本は TimeScale State で、`Time.timeScale` と `Time.fixedDeltaTime` への反映は EngineAdapterLayer の 1 か所だけ

## 敵の群衆

議論の全体は `Assets/Docs/InGame/EnemyCrowdDiscussion.md` にある。区間 4 は 4A（敵の体と切断）、4B（群衆 AI の試作と計測）、4C（群衆 AI の本実装）、4D（群衆アルゴリズムの改変）に分かれる。4B は完了し、方式と計測の結果は `Assets/Docs/InGame/Sections/Section04B_CrowdPrototype.md` にある。4D の仕様は `Assets/Docs/InGame/EnemyCrowdRedesign.md`、計画は `Assets/Docs/InGame/Sections/Section04D_CrowdRedesign.md` にある。

- 目標は同時に約 1000 体、広いマップ。比較の基準は 1 年次作品 DataCenterOutbreak（GitHub の TaguchiRei/HDRPHookShot。敵は総数で最大 330 体、マップはおおむね 1000m 四方）
- 敵 AI は「目的地（螺旋）／経路／移動（簡易物理）」の 3 層で考える。目的地は過去作 UnlimitedKnight の群衆風アルゴリズムを改良した二重のアルキメデスの螺旋。プレイヤー中心の螺旋がグループの目標位置、グループ中心の螺旋がメンバーの定位置を決める。移動中は N×M の列で道筋に沿い、着いたらグループの螺旋に並ぶ。敵は隊列を離れず、その場でプレイヤーを狙う（4D）
- 経路は、縦の列ごとに立てる層を持つ格子＋プレイヤーからの距離マップ（4B で試作、4C で本実装）。ボクセルが壊れたら、作り直しを待って変わった列だけを物理のクエリで調べ直す。距離マップは区画に分け、プレイヤーの近く（追跡範囲）だけを計算する。持ち場が追跡範囲の外のグループは待ち、範囲に入ると追い、外れると来た道を戻る。2 段の距離マップは作らない（4D）。NavMeshAgent に移動を任せる方式は、足場が壊れたときに破綻するので使わない
- 実装方式は「敵の状態を NativeArray（`EnemyAgent`）＋Burst で持ち、`Graphics.RenderMeshInstanced` でまとめて描画し、近くの敵にだけ GameObject の体を貸す」（4B で確定）。全面 ECS は使わない
- 切断は「スキルまでのつなぎ」で範囲を広げない。大量撃破は、ボクセルを壊した崩落に敵を巻き込む形で作る。巻き込まれた敵は切断もかけらも出さず、エネルギー（チャージ）を出す。演出は VFX Graph が有力
