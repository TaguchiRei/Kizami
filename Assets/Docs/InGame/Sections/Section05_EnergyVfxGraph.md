# 区間5：エネルギーの VFX Graph の組み方

崩落で倒した敵のエネルギーを、プレイヤー（MainCamera）へ吸い込ませる VFX Graph（`Assets/Art/Particles/EnemyEnergy.vfx`）の組み方。
C# の側は [EnemyEnergyAdapter](../../../Code/Scripts/EngineAdapterLayer/Player/EnemyEnergyAdapter.cs) で、InGame の `EnemyEnergy` に `VisualEffect` と一緒に付いている。
アセットは、VFX Graph の雛形（01_Minimal_System）を写したもの。中身はこの手順で組み替える。

## C# から渡すもの

| 名前 | 型 | 内容 |
|---|---|---|
| `EnergyPositions` | Graphics Buffer（float3 の並び） | このフレームに崩落で倒した敵の体の中心の位置（ワールド座標）。最大 256 個 |
| `EnergyPositionCount` | int | `EnergyPositions` に入っている位置の数 |
| `EnergyTarget` | Vector3 | 吸い込む先（MainCamera の位置、ワールド座標）。毎フレーム渡す |
| イベント `OnEnergy` | Event | 倒した敵がいたフレームに 1 回送る。イベントの属性 `spawnCount` に「位置の数 × 1 体あたりの粒の数（既定 8）」が入っている |

名前は C# の定数と一致させる。変えるときは、C# の側も合わせる。

## 組み方

### 1. Blackboard にプロパティを足す

Blackboard の「+」から次を足し、すべて Exposed にする。

| 名前 | 型 | 既定値 |
|---|---|---|
| `EnergyPositions` | Graphics Buffer | ― |
| `EnergyPositionCount` | int | 1 |
| `EnergyTarget` | Vector3 | (0, 0, 0) |

### 2. イベントと System の設定

- 雛形にある Spawn コンテキスト（Constant Spawn Rate）を消す
- Event コンテキストを置き、Event Name を `OnEnergy` にして、Initialize Particle の入力へ直接つなぐ。Event を Initialize へ直接つなぐと、イベントの属性 `spawnCount` の数だけ粒が出る
- System の Space（コンテキストの右上）を World にする
- Initialize Particle の Capacity を 4096 にする。Bounds Setup Mode を Manual にし、Bounds を Center (0, 0, 0)・Size (300, 100, 300) にする（ステージ全体を覆う為）

### 3. Initialize Particle

| ブロック | 設定 |
|---|---|
| Set Position | Position に、下の「倒した位置」の出力をつなぐ |
| Set Position (Shape: Sphere) | Composition を Add、Radius 0.8。倒した位置のまわりに散らす |
| Set Velocity Random | A (-3, 2, -3)、B (3, 6, 3)。最初に周りへはじける |
| Set Lifetime | 3 |
| Set Size | 0.15 |
| Set Color | 好みの色（例：HDR の水色） |

「倒した位置」は、次の Operator でつなぐ。

1. Get Attribute: spawnIndex（イベントの中で何番目の粒か）
2. Modulo：A に spawnIndex、B に `EnergyPositionCount`
3. Sample Graphics Buffer：Type を Vector3、Buffer に `EnergyPositions`、Index に Modulo の出力

### 4. Update Particle

| ブロック | 設定 |
|---|---|
| Conform to Sphere | Sphere の Center に `EnergyTarget`、Radius 0.2。Attraction Speed 25、Attraction Force 20、Stick Distance 0.1、Stick Force 50。吸い込む先へ引き寄せる |
| Kill (Sphere) | Center に `EnergyTarget`、Radius 0.8。吸い込む先に届いた粒を消す |

### 5. Output

- 雛形の Output Particle（URP の Unlit）のまま使い、Blend Mode を Additive にする
- 粒の見た目は自由に変えてよい

## 確かめ方

- TestStage で、張り出しの梁か橋を G で壊して敵を巻き込む。倒れた敵の位置から粒がはじけ、カメラへ吸い込まれて消える
- DebugGUI の「Charge」が、崩落で倒した数だけ増える（粒が届く前に増える）

## うまく出ないとき

| 症状 | 確かめること |
|---|---|
| 粒が 1 個しか出ない | Event を Initialize へ直接つないでいるか。Spawn コンテキストを挟むと `spawnCount` が使われない |
| 粒が原点に出る | `EnergyPositions` が Exposed になっているか。Sample Graphics Buffer の Type が Vector3 か |
| 粒が見えない | System の Space が World か。Bounds がステージを覆っているか |
| 粒がカメラの手前で止まる | Kill (Sphere) の Radius が、Conform to Sphere の Radius より大きいか |
