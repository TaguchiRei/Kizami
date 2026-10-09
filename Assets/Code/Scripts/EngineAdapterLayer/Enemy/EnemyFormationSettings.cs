using System;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// グループの隊列と、アンカー（グループの先頭）の動きの設定。EnemySpawnAdapter の Inspector に持ち、値のまま Job へ渡す。
    /// </summary>
    [Serializable]
    public struct EnemyFormationSettings
    {
        /// <summary> 1 グループの人数の上限。グループのメンバーの配列は、グループごとにこの数の区画を持つ </summary>
        public const int MAX_GROUP_SIZE = 16;

        [SerializeField, Range(1, MAX_GROUP_SIZE)]
        [Tooltip("1 グループの人数。生成した順にこの数ずつグループにする")]
        private int _groupSize;

        [SerializeField, Min(0f)]
        [Tooltip("アンカーが歩く速さの、敵が歩く速さに対する割合。敵が隊列の位置に追いつけるよう、1 より小さくする")]
        private float _anchorSpeedRate;

        [SerializeField, Min(0.01f)]
        [Tooltip("アンカーの加速度（m/s²）")]
        private float _anchorAcceleration;

        [SerializeField, Min(0f)]
        [Tooltip("アンカーが向きを変える速さ（度/秒）")]
        private float _anchorTurnSpeed;

        [SerializeField]
        [Tooltip("グループが進む・待つを交互に繰り返し、同じレーンの前で待っているグループの後ろで待つか。切ると、グループは止まらずに進む")]
        private bool _usesAlternatingAdvance;

        [SerializeField, Min(0.1f)]
        [Tooltip("グループが進み続ける時間（秒）。グループごとに ±30% ずらす")]
        private float _advanceDuration;

        [SerializeField, Min(0f)]
        [Tooltip("グループが止まって待つ時間（秒）。グループごとに ±30% ずらす")]
        private float _holdDuration;

        [SerializeField, Min(0f)]
        [Tooltip("アンカーは、同じレーンの前で待つ番で止まっている別のグループの最後尾までが、この距離（m）より近ければ止まって待つ")]
        private float _groupSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("隊列の列の間隔（道筋に沿った前後の間隔、m）")]
        private float _rowSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("隊列の横の間隔（m）")]
        private float _lateralSpacing;

        [SerializeField, Range(1, MAX_GROUP_SIZE)]
        [Tooltip("1 列に並べる数の上限。広い場所ではこの数まで横に広がる")]
        private int _maxColumns;

        [SerializeField, Min(0f)]
        [Tooltip("1 列に並べる数を変えるのは、新しい数がこの時間（秒）続いたとき。狭い所の出入りで並びが細かく入れ替わらないようにする")]
        private float _columnChangeDelay;

        [SerializeField, Range(0, MAX_GROUP_SIZE)]
        [Tooltip("メンバーがこの数以下に減ったグループは、近くの空きのあるグループへ合流する")]
        private int _mergeSize;

        [SerializeField, Min(0f)]
        [Tooltip("追跡中のアンカーは、プレイヤーまでの経路の長さが「置き場のプレイヤーまでの経路の長さ ＋ この値（m）」より長い間は距離マップを下り、内側で置き場へまっすぐ向かう")]
        private float _encircleApproachMargin;

        [SerializeField, Min(0f)]
        [Tooltip("グループの置き場の螺旋の、内側の半径（m）。着いたグループの螺旋の半径と、プレイヤーとの間をあける距離より大きくする")]
        private float _encircleInnerRadius;

        [SerializeField, Min(0.1f)]
        [Tooltip("グループの置き場の螺旋の、1 周ごとに広がる半径（m）。着いたグループの螺旋の直径より大きくする")]
        private float _encircleLoopSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("グループの置き場の、螺旋に沿った間隔（m）。着いたグループの螺旋の直径より大きくする")]
        private float _encircleSlotSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("置き場に着いたグループのメンバーを並べる螺旋（中心はグループの中心）の、1 周ごとに広がる半径（m）。体の前後の長さより短くし、脚の重なりを少し許す")]
        private float _memberLoopSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("置き場に着いたグループのメンバーの、螺旋に沿った間隔（m）")]
        private float _memberSlotSpacing;

        [SerializeField, Min(0f)]
        [Tooltip("置き場に着いたグループは、置き場がこの距離（m）より離れるまで、螺旋に並んだままついていく。離れたら隊列に戻して移動する")]
        private float _followDistance;

        [SerializeField, Min(0f)]
        [Tooltip("バリアを張っているグループは、プレイヤーとの距離が張ったときの距離よりこの値（m）以上広がるまで、置き場が動いてもその位置に留まる")]
        private float _barrierLeaveMargin;

        /// <summary> 1 グループの人数 </summary>
        public int GroupSize => _groupSize;

        /// <summary> アンカーが歩く速さの、敵が歩く速さに対する割合 </summary>
        public float AnchorSpeedRate => _anchorSpeedRate;

        /// <summary> アンカーの加速度（m/s²） </summary>
        public float AnchorAcceleration => _anchorAcceleration;

        /// <summary> アンカーが向きを変える速さ（度/秒） </summary>
        public float AnchorTurnSpeed => _anchorTurnSpeed;

        /// <summary> グループが進む・待つを交互に繰り返し、同じレーンの前で待っているグループの後ろで待つか </summary>
        public bool UsesAlternatingAdvance => _usesAlternatingAdvance;

        /// <summary> グループが進み続ける時間（秒） </summary>
        public float AdvanceDuration => _advanceDuration;

        /// <summary> グループが止まって待つ時間（秒） </summary>
        public float HoldDuration => _holdDuration;

        /// <summary> 前を行く別のグループの最後尾との間にあける距離（m） </summary>
        public float GroupSpacing => _groupSpacing;

        /// <summary> 隊列の列の間隔（m） </summary>
        public float RowSpacing => _rowSpacing;

        /// <summary> 隊列の横の間隔（m） </summary>
        public float LateralSpacing => _lateralSpacing;

        /// <summary> 1 列に並べる数の上限 </summary>
        public int MaxColumns => _maxColumns;

        /// <summary> 1 列に並べる数を変えるまでに、新しい数が続く必要がある時間（秒） </summary>
        public float ColumnChangeDelay => _columnChangeDelay;

        /// <summary> 近くのグループへ合流する、メンバーの数の上限 </summary>
        public int MergeSize => _mergeSize;

        /// <summary> 追跡中のアンカーが距離マップを下るのをやめ、置き場へまっすぐ向かい始める、置き場のプレイヤーまでの経路の長さからの余裕（m） </summary>
        public float EncircleApproachMargin => _encircleApproachMargin;

        /// <summary> グループの置き場の螺旋の、内側の半径（m） </summary>
        public float EncircleInnerRadius => _encircleInnerRadius;

        /// <summary> グループの置き場の螺旋の、1 周ごとに広がる半径（m） </summary>
        public float EncircleLoopSpacing => _encircleLoopSpacing;

        /// <summary> グループの置き場の、螺旋に沿った間隔（m） </summary>
        public float EncircleSlotSpacing => _encircleSlotSpacing;

        /// <summary> 置き場に着いたグループのメンバーを並べる螺旋の、1 周ごとに広がる半径（m） </summary>
        public float MemberLoopSpacing => _memberLoopSpacing;

        /// <summary> 置き場に着いたグループのメンバーの、螺旋に沿った間隔（m） </summary>
        public float MemberSlotSpacing => _memberSlotSpacing;

        /// <summary> 置き場に着いたグループが、螺旋に並んだままついていく置き場までの距離の上限（m） </summary>
        public float FollowDistance => _followDistance;

        /// <summary> バリアを張っているグループが位置に留まる、張ったときのプレイヤーとの距離からの余裕（m） </summary>
        public float BarrierLeaveMargin => _barrierLeaveMargin;

        /// <summary> 既定の値（仮の値） </summary>
        public static EnemyFormationSettings Default => new()
        {
            _groupSize = 12,
            _anchorSpeedRate = 0.8f,
            _anchorAcceleration = 1.5f,
            _anchorTurnSpeed = 90f,
            _advanceDuration = 4f,
            _holdDuration = 2f,
            _groupSpacing = 4f,
            _rowSpacing = 8f,
            _lateralSpacing = 2.5f,
            _maxColumns = 4,
            _columnChangeDelay = 1f,
            _mergeSize = 4,
            _encircleApproachMargin = 5f,
            _encircleInnerRadius = 25f,
            _encircleLoopSpacing = 25f,
            _encircleSlotSpacing = 25f,
            _memberLoopSpacing = 6f,
            _memberSlotSpacing = 6f,
            _followDistance = 10f,
            _barrierLeaveMargin = 10f
        };

        /// <summary>
        /// 中心から見た、アルキメデスの螺旋（半径 = 内側の半径 ＋ 1 周ごとの広がり × 角度 / 2π）の上の slot 番目の置き場の位置（水平）。
        /// 置き場は螺旋に沿って slotSpacing ずつ並ぶ。0 番目は +Z の向きの内側の端で、番号が大きいほど外側になる。
        /// </summary>
        public static float2 GetSpiralOffset(int slot, float innerRadius, float loopSpacing, float slotSpacing)
        {
            var radius = GetSpiralRadius(slot, innerRadius, loopSpacing, slotSpacing);
            var theta = (radius - innerRadius) * (2f * math.PI) / loopSpacing;
            return new float2(math.sin(theta), math.cos(theta)) * radius;
        }

        /// <summary>
        /// 螺旋の上の slot 番目の置き場の、中心からの距離（m）。番号が大きいほど大きい。
        /// </summary>
        public static float GetSpiralRadius(int slot, float innerRadius, float loopSpacing, float slotSpacing)
        {
            // 螺旋に沿った長さ s は、角度 θ について s ≒ 内側の半径 × θ ＋ 広がり × θ² / 4π なので、
            // θ について解くと、半径は √(内側の半径² ＋ 広がり × s / π) になる
            return math.sqrt(innerRadius * innerRadius + loopSpacing * slot * slotSpacing / math.PI);
        }
    }
}
