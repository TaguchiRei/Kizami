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

        [SerializeField, Min(0f)]
        [Tooltip("アンカーは、プレイヤーまでの経路の長さがこの値（m）以下になったら止まる")]
        private float _anchorStopDistance;

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
        [Tooltip("アンカーは、プレイヤーまでの経路の長さがこの値（m）以下になったら、プレイヤーを囲む螺旋の上の置き場を受け取り、そこへ向かう")]
        private float _encircleDistance;

        [SerializeField, Min(0f)]
        [Tooltip("置き場までの直線の距離がこの値（m）より離れたら（プレイヤーが遠ざかったら）、置き場を手放して距離マップを下る")]
        private float _encircleLeaveDistance;

        [SerializeField, Min(0f)]
        [Tooltip("グループの置き場の螺旋の、内側の半径（m）。交戦する敵の螺旋より外にする")]
        private float _encircleInnerRadius;

        [SerializeField, Min(0.1f)]
        [Tooltip("グループの置き場の螺旋の、1 周ごとに広がる半径（m）。囲んだ隊列の奥行きより大きくする")]
        private float _encircleLoopSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("グループの置き場の、螺旋に沿った間隔（m）。囲んだ隊列の幅より大きくする")]
        private float _encircleSlotSpacing;

        [SerializeField, Range(1, MAX_GROUP_SIZE)]
        [Tooltip("置き場へ向かう間と着いたあとの、1 列に並べる数（横隊）")]
        private int _encircleColumns;

        [SerializeField, Min(0f)]
        [Tooltip("敵は、プレイヤーまでの経路の長さがこの値（m）以下になったら隊列から外れて交戦する")]
        private float _engageEnterDistance;

        [SerializeField, Min(0f)]
        [Tooltip("交戦中の敵は、プレイヤーまでの経路の長さがこの値（m）を超えたら隊列に戻る。入る距離より大きくする")]
        private float _engageExitDistance;

        [SerializeField, Min(0f)]
        [Tooltip("交戦する敵の置き場の螺旋の、内側の半径（m）")]
        private float _spiralInnerRadius;

        [SerializeField, Min(0.1f)]
        [Tooltip("交戦する敵の置き場の螺旋の、1 周ごとに広がる半径（m）")]
        private float _spiralLoopSpacing;

        [SerializeField, Min(0.1f)]
        [Tooltip("交戦する敵の置き場の、螺旋に沿った間隔（m）")]
        private float _spiralSlotSpacing;

        /// <summary> 1 グループの人数 </summary>
        public int GroupSize => _groupSize;

        /// <summary> アンカーが歩く速さの、敵が歩く速さに対する割合 </summary>
        public float AnchorSpeedRate => _anchorSpeedRate;

        /// <summary> アンカーの加速度（m/s²） </summary>
        public float AnchorAcceleration => _anchorAcceleration;

        /// <summary> アンカーが向きを変える速さ（度/秒） </summary>
        public float AnchorTurnSpeed => _anchorTurnSpeed;

        /// <summary> アンカーが止まる、プレイヤーまでの経路の長さ（m） </summary>
        public float AnchorStopDistance => _anchorStopDistance;

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

        /// <summary> アンカーが包囲の置き場を受け取る、プレイヤーまでの経路の長さ（m） </summary>
        public float EncircleDistance => _encircleDistance;

        /// <summary> アンカーが包囲の置き場を手放す、置き場までの直線の距離（m） </summary>
        public float EncircleLeaveDistance => _encircleLeaveDistance;

        /// <summary> グループの置き場の螺旋の、内側の半径（m） </summary>
        public float EncircleInnerRadius => _encircleInnerRadius;

        /// <summary> グループの置き場の螺旋の、1 周ごとに広がる半径（m） </summary>
        public float EncircleLoopSpacing => _encircleLoopSpacing;

        /// <summary> グループの置き場の、螺旋に沿った間隔（m） </summary>
        public float EncircleSlotSpacing => _encircleSlotSpacing;

        /// <summary> 包囲の間の、1 列に並べる数 </summary>
        public int EncircleColumns => _encircleColumns;

        /// <summary> 敵が交戦に入る、プレイヤーまでの経路の長さ（m） </summary>
        public float EngageEnterDistance => _engageEnterDistance;

        /// <summary> 交戦中の敵が隊列に戻る、プレイヤーまでの経路の長さ（m） </summary>
        public float EngageExitDistance => _engageExitDistance;

        /// <summary> 交戦する敵の置き場の螺旋の、内側の半径（m） </summary>
        public float SpiralInnerRadius => _spiralInnerRadius;

        /// <summary> 交戦する敵の置き場の螺旋の、1 周ごとに広がる半径（m） </summary>
        public float SpiralLoopSpacing => _spiralLoopSpacing;

        /// <summary> 交戦する敵の置き場の、螺旋に沿った間隔（m） </summary>
        public float SpiralSlotSpacing => _spiralSlotSpacing;

        /// <summary> 既定の値（仮の値） </summary>
        public static EnemyFormationSettings Default => new()
        {
            _groupSize = 12,
            _anchorSpeedRate = 0.8f,
            _anchorAcceleration = 1.5f,
            _anchorTurnSpeed = 90f,
            _anchorStopDistance = 12f,
            _advanceDuration = 4f,
            _holdDuration = 2f,
            _groupSpacing = 4f,
            _rowSpacing = 8f,
            _lateralSpacing = 2.5f,
            _maxColumns = 4,
            _columnChangeDelay = 1f,
            _mergeSize = 4,
            _encircleDistance = 40f,
            _encircleLeaveDistance = 30f,
            _encircleInnerRadius = 15f,
            _encircleLoopSpacing = 12f,
            _encircleSlotSpacing = 18f,
            _encircleColumns = 6,
            _engageEnterDistance = 12f,
            _engageExitDistance = 18f,
            _spiralInnerRadius = 7f,
            _spiralLoopSpacing = 6f,
            _spiralSlotSpacing = 2.5f
        };

        /// <summary>
        /// 中心から見た、アルキメデスの螺旋（半径 = 内側の半径 ＋ 1 周ごとの広がり × 角度 / 2π）の上の slot 番目の置き場の位置（水平）。
        /// 置き場は螺旋に沿って slotSpacing ずつ並ぶ。0 番目は +Z の向きの内側の端で、番号が大きいほど外側になる。
        /// </summary>
        /// <remarks>
        /// 螺旋に沿った長さ s は、角度 θ について s ≒ 内側の半径 × θ ＋ 広がり × θ² / 4π なので、これを θ について解く。
        /// このとき、半径は √(内側の半径² ＋ 広がり × s / π) になる。
        /// </remarks>
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
            return math.sqrt(innerRadius * innerRadius + loopSpacing * slot * slotSpacing / math.PI);
        }
    }
}
