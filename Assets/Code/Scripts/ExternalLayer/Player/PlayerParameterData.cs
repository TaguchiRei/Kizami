using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// プレイヤーの移動・HP・近接切断・チャージ・スローモード・投擲に関わる、遊びのルールのパラメータ。
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
        [Tooltip("初速を決める高さ（m）。重力の倍率が 1 のときにこの高さまで上がる初速で跳ぶ。実際の高さは Adapter の重力の倍率で決まる")]
        private float _jumpHeight = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("ジャンプボタンの長押しで、上昇の重力を弱めていられる時間（秒）")]
        private float _jumpHoldTime = 0.5f;

        [Header("壁走り")]
        [SerializeField, Min(0f)]
        [Tooltip("ダッシュしていないときの壁走りの速度（m/s）")]
        private float _wallRunWalkSpeed = 9f;

        [SerializeField, Min(0f)]
        [Tooltip("ダッシュ中の壁走りの速度（m/s）")]
        private float _wallRunSprintSpeed = 12f;

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

        [Header("飛び移り")]
        [SerializeField, Min(1f)]
        [Tooltip("目印へ飛ぶ速さ（m/s）。所要時間は距離 ÷ この値")]
        private float _flightSpeed = 120f;

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

        [Header("HP")]
        [SerializeField, Min(1)]
        [Tooltip("最大 HP")]
        private int _maxHealth = 100;

        [SerializeField, Range(0f, 100f)]
        [Tooltip("ワープ中に受けるダメージの軽減率（%）。100 で無効、0 で軽減なし")]
        private float _warpDamageReduction = 100f;

        [Header("近接切断")]
        [SerializeField, Min(0f)]
        [Tooltip("近接切断の攻撃間隔（秒）。スローモード中も等速で数える")]
        private float _meleeAttackInterval = 0.3f;

        [Header("ヒットストップ")]
        [SerializeField, Min(0f)]
        [Tooltip("近接切断が当たったときに時間を止める実時間（秒）。0 で止めない")]
        private float _hitStopDuration = 0.06f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("ヒットストップ中に基本倍率へ掛ける倍率。0 で完全に止まる")]
        private float _hitStopTimeScale;

        [Header("チャージ")]
        [SerializeField, Min(0)]
        [Tooltip("かけら 1 個を吸収したときに増えるチャージ量")]
        private int _chargePerFragment = 1;

        [SerializeField, Min(0)]
        [Tooltip("崩落で敵を 1 体倒したときに増えるチャージ量")]
        private int _chargePerCollapsedEnemy = 1;

        [SerializeField, Min(1)]
        [Tooltip("チャージ量の上限")]
        private int _maxCharge = 100;

        [Header("スローモード")]
        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("スローモード中の時間の倍率")]
        private float _slowModeTimeScale = 0.25f;

        [SerializeField, Min(0)]
        [Tooltip("スローモードの発動時に消費するチャージ量")]
        private int _slowModeActivationCost = 20;

        [SerializeField, Min(0f)]
        [Tooltip("スローモードを続けている間に、実時間 1 秒あたりに消費するチャージ量。0 で消費しない")]
        private float _slowModeDrainPerSecond = 5f;

        [SerializeField, Min(0)]
        [Tooltip("1 回のスローモードの間に近接切断を振れる回数")]
        private int _slowModeCutLimit = 5;

        [Header("つかむ・投げる・ランチャー")]
        [SerializeField, Min(0f)]
        [Tooltip("かけらをつかめる、カメラからの距離（m）")]
        private float _grabRange = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("かけらを探す球の半径（m）")]
        private float _grabRadius = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("投げたかけらの初速（m/s）")]
        private float _throwSpeed = 25f;

        [SerializeField, Min(0f)]
        [Tooltip("ランチャーで撃ったかけらの速さ（m/s）")]
        private float _launcherSpeed = 60f;

        /// <summary> 歩行の速度（m/s） </summary>
        public float WalkSpeed => _walkSpeed;

        /// <summary> ダッシュの速度（m/s） </summary>
        public float SprintSpeed => _sprintSpeed;

        /// <summary> 初速を決める高さ（m）。重力の倍率が 1 のときにこの高さまで上がる </summary>
        public float JumpHeight => _jumpHeight;

        /// <summary> ジャンプボタンの長押しで、上昇の重力を弱めていられる時間（秒） </summary>
        public float JumpHoldTime => _jumpHoldTime;

        /// <summary> ダッシュしていないときの壁走りの速度（m/s） </summary>
        public float WallRunWalkSpeed => _wallRunWalkSpeed;

        /// <summary> ダッシュ中の壁走りの速度（m/s） </summary>
        public float WallRunSprintSpeed => _wallRunSprintSpeed;

        /// <summary> 壁走りの持ち時間（秒） </summary>
        public float WallRunDuration => _wallRunDuration;

        /// <summary> 壁から離れようとしているとみなす、入力と壁の法線のなす角の上限（度） </summary>
        public float WallDetachAngle => _wallDetachAngle;

        /// <summary> 壁から離れようとする入力が続くと壁走りを抜ける時間（秒） </summary>
        public float WallDetachTime => _wallDetachTime;

        /// <summary> 壁ジャンプの水平方向の速さ（m/s） </summary>
        public float WallJumpHorizontalSpeed => _wallJumpHorizontalSpeed;

        /// <summary> 目印へ飛ぶ速さ（m/s） </summary>
        public float FlightSpeed => _flightSpeed;

        /// <summary> ワープで進む距離（m） </summary>
        public float WarpDistance => _warpDistance;

        /// <summary> ワープの所要時間（秒） </summary>
        public float WarpDuration => _warpDuration;

        /// <summary> ワープが終わってから次のワープができるまでの時間（秒） </summary>
        public float WarpCooldown => _warpCooldown;

        /// <summary> ワープの向きに移動入力の向きを混ぜる割合 </summary>
        public float WarpInputInfluence => _warpInputInfluence;

        /// <summary> 最大 HP </summary>
        public int MaxHealth => _maxHealth;

        /// <summary> ワープ中に受けるダメージの軽減率（%、0〜100） </summary>
        public float WarpDamageReduction => _warpDamageReduction;

        /// <summary> 近接切断の攻撃間隔（秒）。スローモード中も等速で数える </summary>
        public float MeleeAttackInterval => _meleeAttackInterval;

        /// <summary> かけら1個を吸収したときに増えるチャージ量 </summary>
        public int ChargePerFragment => _chargePerFragment;

        /// <summary> 崩落で敵を1体倒したときに増えるチャージ量 </summary>
        public int ChargePerCollapsedEnemy => _chargePerCollapsedEnemy;

        /// <summary> チャージ量の上限 </summary>
        public int MaxCharge => _maxCharge;

        /// <summary> 近接切断が当たったときに時間を止める実時間（秒） </summary>
        public float HitStopDuration => _hitStopDuration;

        /// <summary> ヒットストップ中に基本倍率へ掛ける倍率 </summary>
        public float HitStopTimeScale => _hitStopTimeScale;

        /// <summary> スローモード中の時間の倍率 </summary>
        public float SlowModeTimeScale => _slowModeTimeScale;

        /// <summary> スローモードの発動時に消費するチャージ量 </summary>
        public int SlowModeActivationCost => _slowModeActivationCost;

        /// <summary> スローモードを続けている間に、実時間1秒あたりに消費するチャージ量 </summary>
        public float SlowModeDrainPerSecond => _slowModeDrainPerSecond;

        /// <summary> 1回のスローモードの間に近接切断を振れる回数 </summary>
        public int SlowModeCutLimit => _slowModeCutLimit;

        /// <summary> かけらをつかめる、カメラからの距離（m） </summary>
        public float GrabRange => _grabRange;

        /// <summary> かけらを探す球の半径（m） </summary>
        public float GrabRadius => _grabRadius;

        /// <summary> 投げたかけらの初速（m/s） </summary>
        public float ThrowSpeed => _throwSpeed;

        /// <summary> ランチャーで撃ったかけらの速さ（m/s） </summary>
        public float LauncherSpeed => _launcherSpeed;
    }
}
