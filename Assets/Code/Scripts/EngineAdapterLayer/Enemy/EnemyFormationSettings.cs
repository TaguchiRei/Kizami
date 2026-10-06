using System;
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

        /// <summary> 既定の値。区間4C の決定 3〜5 の仮の値 </summary>
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
            _columnChangeDelay = 1f
        };
    }
}
