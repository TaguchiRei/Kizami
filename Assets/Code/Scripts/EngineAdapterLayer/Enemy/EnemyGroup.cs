using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループ 1 つの状態。EnemyGroups が NativeArray に持ち、EnemyGroupJob が更新し、EnemyMoveJob が読む。
    /// グループの先頭（アンカー）は体を持たない仮想の隊長で、距離マップを下って歩き、通った道筋を記録する。メンバーは道筋に沿った隊列の位置を目指す。
    /// プレイヤーに近づいたアンカーは、プレイヤーを囲む螺旋の上の置き場へ向かい、メンバーはアンカーの後ろにまっすぐ並ぶ横隊になる。
    /// 持ち場が追跡範囲の外なら持ち場で待ち、中なら追い、外れたら追跡の間に記録した帰りの道筋を逆にたどって持ち場へ戻る。
    /// </summary>
    public struct EnemyGroup
    {
        /// <summary> 使われているか。メンバーが全員ステージから消えたら false にする </summary>
        public bool IsActive;

        /// <summary> 待機・追跡・帰還の状態 </summary>
        public EnemyGroupState State;

        /// <summary> 持ち場。グループを作ったときのアンカーの位置で、帰還の途中で進めなくなったらその位置に変える </summary>
        public float3 HomePosition;

        /// <summary> 帰りの道筋の最も新しい点の、区画の中の番号 </summary>
        public int ReturnHead;

        /// <summary> 帰りの道筋に残っている点の数 </summary>
        public int ReturnCount;

        /// <summary> 帰還の途中で進めない状態が続いている時間（秒） </summary>
        public float BlockedTime;

        /// <summary> アンカーの位置 </summary>
        public float3 AnchorPosition;

        /// <summary> アンカーの向き（Y 軸まわりの角度、ラジアン）。0 で +Z を向く </summary>
        public float AnchorYaw;

        /// <summary> アンカーの今の速さ（m/s） </summary>
        public float AnchorSpeed;

        /// <summary> アンカーのいるノードの、プレイヤーまでの経路の長さ（m）。前を行くグループかどうかの比較に使う </summary>
        public float AnchorDistance;

        /// <summary> 進んでいる間か。false の間は止まって待つ </summary>
        public bool IsAdvancing;

        /// <summary> プレイヤーを囲む螺旋の上の置き場の番号。置き場を持たず、距離マップを下っている間は -1 </summary>
        public int EncircleSlot;

        /// <summary> アンカーが包囲の置き場に着いているか </summary>
        public bool HasArrived;

        /// <summary> 進む・待つを切り替えるまでの残り時間（秒） </summary>
        public float PhaseTimer;

        /// <summary> メンバーの区画に入っている数。倒れたメンバーの区画も、詰めるまでは数える </summary>
        public int MemberCount;

        /// <summary> 隊列の 1 列に並べる数 </summary>
        public int ColumnCount;

        /// <summary> アンカーの位置の幅から求めた 1 列の数。ColumnCount と違う値が続いたら ColumnCount を変える </summary>
        public int PendingColumnCount;

        /// <summary> PendingColumnCount が続いている時間（秒） </summary>
        public float PendingColumnTime;

        /// <summary> 道筋の最も新しい点の、区画の中の番号 </summary>
        public int PathHead;

        /// <summary> 道筋に記録した点の数 </summary>
        public int PathCount;
    }
}
