# 区間9：アタッカーの弾の VFX Graph の組み方

アタッカーの弾を描く VFX Graph（`Assets/Art/Particles/EnemyBullet.vfx`）の組み方。
C# の側は [EnemyShooter](../../../Code/Scripts/EngineAdapterLayer/Enemy/EnemyShooter.cs) で、InGame の `EnemySpawnAdapter` の子の `EnemyBullets` に `VisualEffect` が付いている。
アセットは、区間5で URP 向けに直したエネルギーのグラフ（`EnemyEnergy.vfx`）を写したもの。中身はこの手順で組み替える。

弾の位置は C# が毎フレーム GraphicsBuffer に書く。グラフは、弾の数だけの粒を最初に 1 回だけ出し、毎フレーム自分の番号の弾の位置へ動かす。飛んでいない弾は大きさを 0 にして隠す。粒を出し直さないので、弾を撃つたびのイベントは要らない。

## C# から渡すもの

| 名前 | 型 | 内容 |
|---|---|---|
| `BulletStates` | Graphics Buffer（float4 の並び） | 弾ごとの状態。xyz が位置（ワールド座標）、w が飛んでいれば 1、飛んでいなければ 0。並びは弾の番号 |
| `BulletCapacity` | int | `BulletStates` の数（`EnemyShooter` の「同時に飛んでいる弾の数の上限」。既定 4） |

名前は C# の定数と一致させる。変えるときは、C# の側も合わせる。グラフにこの 2 つがないと、`EnemyShooter` が「弾は見えないまま飛びます」と警告を出す。

## 組み方

### 1. Blackboard

- エネルギー用のプロパティ（`EnergyPositions`・`EnergyPositionCount`・`EnergyTarget`）を消す
- 「+」から次を足し、すべて Exposed にする

| 名前 | 型 | 既定値 |
|---|---|---|
| `BulletStates` | Graphics Buffer | ― |
| `BulletCapacity` | int | 4 |

### 2. Spawn と System の設定

- Event コンテキスト（`OnEnergy`）を消す
- Spawn コンテキストを置き、Initialize Particle の入力へつなぐ。中の Constant Spawn Rate を消し、Single Burst を置いて Count に `BulletCapacity` をつなぐ（Delay は 0）。再生を始めたとき（`OnPlay`）に、弾の数だけの粒が 1 回だけ出る
- System の Space は World のまま
- Initialize Particle の Capacity を 16 にする（`BulletCapacity` より大きければよい）。Bounds Setup Mode は Manual、Bounds は Center (0, 0, 0)・Size (300, 100, 300) のまま
- Update Particle を選び、Inspector の Age Particles と Reap Particles を切る。粒が寿命で消えないようにする為（消えると出し直されない）

### 3. Initialize Particle

- エネルギー用のブロック（Set Position、Add Position Shape Sphere、Set Velocity Random Per Component、Set Lifetime）を消す
- Set Size（0）と Set Color（好みの色。例：HDR のオレンジ）を置く

### 4. Update Particle

- エネルギー用のブロック（Attractor Shape Sphere、Kill Shape Sphere）を消す
- 次のブロックを置く

| ブロック | 設定 |
|---|---|
| Set Position | Position に、下の「弾の状態」の xyz をつなぐ |
| Set Size | Size に、下の「弾の状態」の w に 0.6（弾の直径）を掛けた値をつなぐ |

「弾の状態」は、次の Operator でつなぐ。

1. Get Attribute: particleId（何番目の粒か。Single Burst で出した粒は 0 から順に番号が付く）
2. Sample Graphics Buffer：Type を Vector4、Buffer に `BulletStates`、Index に particleId
3. 出力を Split（または Swizzle）で xyz と w に分け、w は Multiply で 0.6 を掛ける

### 5. Output

- エネルギーのグラフの Output Particle（URP の Unlit、Additive）のまま使う
- 粒の見た目は自由に変えてよい（例：Output を Output Particle Mesh にして球を出す）

## 確かめ方

- TestStage で、アタッカーのグループが見通しのよい位置に着くと、オレンジの粒がプレイヤーへ向かって飛ぶ。当たると DebugGUI の「Player HP」が 5 減り、粒が消える
- Scene ビューでは、`EnemyShooter` が飛んでいる弾を黄色い球のギズモで描く。粒の位置がギズモと重なっていれば、位置の受け渡しは合っている

## うまく出ないとき

| 症状 | 確かめること |
|---|---|
| 「弾は見えないまま飛びます」の警告が出る | `BulletStates` と `BulletCapacity` の名前と型が合っているか、Exposed になっているか |
| 粒がまったく出ない | Spawn コンテキストに Single Burst があり、Count に `BulletCapacity` がつながっているか。Update Particle の Reap Particles を切ったか |
| 粒が原点に出たまま動かない | Set Position が Update Particle にあるか（Initialize にあると最初の 1 回しか動かない）。Sample Graphics Buffer の Type が Vector4 か |
| 弾を撃っていないのに粒が見える | Set Size に w を掛けているか |
| 少し撃つと粒が出なくなる | Age Particles を切ったか。寿命で消えた粒は出し直されない |
