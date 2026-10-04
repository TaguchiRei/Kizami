using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// プレイヤーの移動と HP に関わる、遊びのルールのパラメータ。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerParameterData", menuName = "Kizami/Player/PlayerParameterData")]
    public sealed class PlayerParameterData : ScriptableObject
    {
        [Header("歩行・ダッシュ")]
        [SerializeField, Min(0f)]
        [Tooltip("歩行の速度（m/s）")]
        private float _walkSpeed = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("ダッシュの速度（m/s）")]
        private float _sprintSpeed = 9f;

        [Header("ジャンプ")]
        [SerializeField, Min(0f)]
        [Tooltip("ジャンプの高さ（m）。初速は重力の大きさから求める")]
        private float _jumpHeight = 1.5f;

        [Header("壁走り")]
        [SerializeField, Min(0f)]
        [Tooltip("壁走りの速度（m/s）")]
        private float _wallRunSpeed = 9f;

        [SerializeField, Min(0f)]
        [Tooltip("壁走りの持ち時間（秒）。走っている間もとどまっている間も減り、着地で回復する")]
        private float _wallRunDuration = 1.5f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("移動入力の向きと壁から離れる向きのなす角がこの角度（度）以下だと、壁から離れようとしているとみなす")]
        private float _wallDetachAngle = 45f;

        [SerializeField, Min(0f)]
        [Tooltip("壁から離れようとする入力がこの時間（秒）続くと壁走りを抜ける")]
        private float _wallDetachTime = 0.2f;

        [SerializeField, Min(0f)]
        [Tooltip("壁ジャンプの水平方向の速さ（m/s）。上向きの速度はジャンプと同じ")]
        private float _wallJumpHorizontalSpeed = 6f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("壁ジャンプの水平方向に、移動入力の向きを混ぜる割合。0 なら壁から離れる向きのみ、1 なら入力の向きのみ")]
        private float _wallJumpInputInfluence = 0.5f;

        [Header("短距離ワープ")]
        [SerializeField, Min(0f)]
        [Tooltip("ワープで進む距離（m）")]
        private float _warpDistance = 8f;

        [SerializeField, Min(0.001f)]
        [Tooltip("ワープの所要時間（秒）。速度は距離 ÷ 所要時間になる")]
        private float _warpDuration = 0.15f;

        [SerializeField, Min(0f)]
        [Tooltip("ワープが終わってから次のワープができるまでの時間（秒）")]
        private float _warpCooldown = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("ワープの向きに、ワールド空間の移動入力の向きを混ぜる割合。視線の向き ＋ 入力の向き × この値 を正規化した向きへ進む")]
        private float _warpInputInfluence = 0.2f;

        /// <summary> 歩行の速度（m/s） </summary>
        public float WalkSpeed => _walkSpeed;

        /// <summary> ダッシュの速度（m/s） </summary>
        public float SprintSpeed => _sprintSpeed;

        /// <summary> ジャンプの高さ（m） </summary>
        public float JumpHeight => _jumpHeight;

        /// <summary> 壁走りの速度（m/s） </summary>
        public float WallRunSpeed => _wallRunSpeed;

        /// <summary> 壁走りの持ち時間（秒） </summary>
        public float WallRunDuration => _wallRunDuration;

        /// <summary> 壁から離れようとしているとみなす、入力と壁の法線のなす角の上限（度） </summary>
        public float WallDetachAngle => _wallDetachAngle;

        /// <summary> 壁から離れようとする入力が続くと壁走りを抜ける時間（秒） </summary>
        public float WallDetachTime => _wallDetachTime;

        /// <summary> 壁ジャンプの水平方向の速さ（m/s） </summary>
        public float WallJumpHorizontalSpeed => _wallJumpHorizontalSpeed;

        /// <summary> 壁ジャンプの水平方向に移動入力の向きを混ぜる割合（0〜1） </summary>
        public float WallJumpInputInfluence => _wallJumpInputInfluence;

        /// <summary> ワープで進む距離（m） </summary>
        public float WarpDistance => _warpDistance;

        /// <summary> ワープの所要時間（秒） </summary>
        public float WarpDuration => _warpDuration;

        /// <summary> ワープが終わってから次のワープができるまでの時間（秒） </summary>
        public float WarpCooldown => _warpCooldown;

        /// <summary> ワープの向きに移動入力の向きを混ぜる割合 </summary>
        public float WarpInputInfluence => _warpInputInfluence;
    }
}
