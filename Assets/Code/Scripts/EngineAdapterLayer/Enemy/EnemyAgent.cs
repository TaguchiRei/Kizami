using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵 1 体の状態。EnemySpawnAdapter が NativeArray に持ち、Burst の Job で読み書きする。
    /// 後から ECS のコンポーネントへ移せるよう、値型のフィールドだけで持つ。
    /// 部位の状態は体を返しても持ち続け、次に体を貸すときに体へ反映する。
    /// </summary>
    public struct EnemyAgent
    {
        /// <summary> ステージに出ているか </summary>
        public bool IsAlive;

        /// <summary> 敵の種類 </summary>
        public EnemyKind Kind;

        /// <summary> 体の根の位置 </summary>
        public float3 Position;

        /// <summary> 向き（Y 軸まわりの角度、ラジアン）。0 で +Z を向く </summary>
        public float Yaw;

        /// <summary> 上向きの速さ（m/s）。立っている間は 0 </summary>
        public float VerticalSpeed;

        /// <summary> 立てる層の上に立っているか。false の間は落ちている </summary>
        public bool IsGrounded;

        /// <summary> 落ち始めた高さ。着地したときに、落ちた高さを測る基準にする </summary>
        public float FallStartHeight;

        /// <summary> 崩落（足場ごとの落下、落ちてくる塊）で倒されたか。EnemySpawnAdapter が数えたら false に戻す </summary>
        public bool IsDefeatedByCollapse;

        /// <summary> 貸している体の、その種類の EnemyBodyLender の体の一覧での番号。貸していなければ -1 </summary>
        public int BodyIndex;

        /// <summary> 所属するグループの、EnemyGroups での番号。グループを持たなければ -1 で、距離マップを下って歩く </summary>
        public int GroupIndex;

        /// <summary> グループの隊列の中の順番。先頭の列から、列の中は左から数える </summary>
        public int SlotIndex;

        /// <summary> 立っている層からプレイヤーへたどり着けない状態が続いている時間（秒） </summary>
        public float StrandedTime;

        /// <summary> 体から外れた部位。ビット i が体の部位 i を表す </summary>
        public uint LostParts;

        /// <summary> 壊れたことを役割へ伝えた部位。ビット i が体の部位 i を表す </summary>
        public uint BrokenParts;

        /// <summary> 壊れた移動部位の数 </summary>
        public int BrokenMovePartCount;

        /// <summary> 壊れた移動部位がこの数に達すると、移動しなくなる。出すときに体のプレハブの値を書く </summary>
        public int BrokenMovePartLimit;

        /// <summary> 次に攻撃できるまでの時間（秒）。0 以下なら攻撃できる。アタッカー（弾）とフィニッシャー（吸収とビーム）が使う </summary>
        public float AttackCooldown;

        /// <summary> 動き方。出したときは Walking </summary>
        public EnemyMoveMode MoveMode;

        /// <summary> 飛んでいる間に向かう位置。MoveMode が Flying の間だけ使う </summary>
        public float3 FlyTarget;
    }
}
