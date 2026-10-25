using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 羽天轟号の使用中のプレイヤーの位置とビームを扱う Adapter。発動した地点の真上の指定高度まで上昇し、止めるまでそこにとどまってビームを撃ち、止められたら発動した地点へ戻る。
    /// 位置は PlaceAt で直接書くので、上昇と戻りの途中の地形や建物を無視する。
    /// ビームは EnemyFinisherBeams の見た目を溶かさない形で使い、上昇し終えたら予兆（チャージ）から始め、毎フレーム、カメラの正面へ向ける。
    /// </summary>
    /// <remarks>
    /// 照射中は、撃つ所からビームの半径の球を射線に沿って飛ばし、最初に当たった敵でない物を基準にする。
    /// 判定の間隔ごとに、続けてよいかを尋ねてから基準の位置を中心に壊す（照射の開始時に 1 回目）。基準の物が体力制の対象なら毎フレーム体力を減らし、射線上の敵は ENEMY_INTERVAL ごとに倒す。
    /// </remarks>
    public sealed class SkyBeamAdapter : MonoBehaviour
    {
        /// <summary> 射線上の敵を倒す間隔（秒） </summary>
        private const float ENEMY_INTERVAL = 0.1f;

        /// <summary> 射線を調べるときに受け取る当たりの数 </summary>
        private const int MAX_HIT_COUNT = 64;

        private readonly RaycastHit[] _castBuffer = new RaycastHit[MAX_HIT_COUNT];

        [SerializeField]
        [Tooltip("ビームの見た目。太さとチャージ（予兆）の時間はこのコンポーネントの設定を使う。未設定ならビームを出さない")]
        private EnemyFinisherBeams _beams;

        [SerializeField, Min(0f)]
        [Tooltip("ビームを撃つ所を、カメラから下へずらす距離（m）")]
        private float _pivotDown = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("ビームを撃つ所を、カメラから前へずらす距離（m）")]
        private float _pivotForward = 1f;

        private PlayerMovementAdapterBase _movement;
        private Action _onFinished;
        private Func<bool> _tryContinue;
        private Action<Vector3, float> _carve;
        private Action<Vector3, float> _defeatInRadius;
        private EnemyFinisherBeams.Shot _shot;
        private Phase _phase;

        /// <summary> 発動した地点 </summary>
        private Vector3 _origin;

        /// <summary> 今の移動の始点 </summary>
        private Vector3 _from;

        /// <summary> 今の移動の終点 </summary>
        private Vector3 _to;

        /// <summary> 上昇と戻りの所要時間（秒） </summary>
        private float _duration;

        /// <summary> 今の移動を始めてからの時間（秒） </summary>
        private float _elapsed;

        /// <summary> 判定の間隔（秒） </summary>
        private float _judgeInterval;

        /// <summary> 判定で壊す球の半径（m） </summary>
        private float _carveRadius;

        /// <summary> 体力制の対象の体力を 1 秒あたりに減らす量 </summary>
        private float _healthDamagePerSecond;

        /// <summary> 上昇し終えてからの時間（秒）。チャージの時間を過ぎたら照射中 </summary>
        private float _hoverTime;

        /// <summary> 次の判定までの時間（秒） </summary>
        private float _judgeTimer;

        /// <summary> 次に射線上の敵を倒すまでの時間（秒） </summary>
        private float _enemyTimer;

        /// <param name="movement">位置を書き込む先のプレイヤーの移動の Adapter</param>
        /// <param name="onFinished">発動した地点へ戻り終えたときに呼ぶ関数</param>
        /// <param name="tryContinue">判定の前に呼び、続けてよいかを返す関数（SkyBeamService.TryContinue）。false なら止まる</param>
        /// <param name="carve">判定で壊す関数（VoxelDestructionAdapter.CarveSkyBeamAt）。引数は中心と半径</param>
        /// <param name="defeatInRadius">球の中の敵を倒す関数（EnemySpawnAdapter.DefeatInRadius）。引数は中心と半径</param>
        public void Initialize(PlayerMovementAdapterBase movement, Action onFinished, Func<bool> tryContinue,
            Action<Vector3, float> carve, Action<Vector3, float> defeatInRadius)
        {
            _movement = movement;
            _onFinished = onFinished;
            _tryContinue = tryContinue;
            _carve = carve;
            _defeatInRadius = defeatInRadius;

            if (_beams == null || !_beams.Initialize(movement.transform, null, 0))
            {
                UsefulLogger.LogWarning("EnemyFinisherBeams がないか使えない為、羽天轟号はビームを出しません。", this);
                return;
            }

            _shot = _beams.CreateShot();
        }

        /// <summary>
        /// 今の位置を発動した地点として、その真上の高度まで上がり始める。今の高さが高度より高ければ上がらない。
        /// </summary>
        /// <param name="altitude">上昇先のワールド高度（Y）</param>
        /// <param name="duration">上昇と戻りの所要時間（秒）</param>
        /// <param name="judgeInterval">照射中の判定の間隔（秒）</param>
        /// <param name="carveRadius">判定で壊す球の半径（m）</param>
        /// <param name="healthDamagePerSecond">体力制の対象の体力を 1 秒あたりに減らす量</param>
        public void Begin(float altitude, float duration, float judgeInterval, float carveRadius,
            float healthDamagePerSecond)
        {
            if (_movement == null) return;

            _origin = _movement.Position;
            _duration = duration;
            _judgeInterval = judgeInterval;
            _carveRadius = carveRadius;
            _healthDamagePerSecond = healthDamagePerSecond;
            StartMove(_origin, new Vector3(_origin.x, Mathf.Max(_origin.y, altitude), _origin.z), Phase.Rising);
        }

        /// <summary>
        /// ビームを止め、今の位置から発動した地点へ戻り始める。上昇や戻りの途中でも呼べる。
        /// </summary>
        public void Stop()
        {
            if (_phase == Phase.Idle || _phase == Phase.Returning) return;

            _shot?.Stop();
            StartMove(_movement.Position, _origin, Phase.Returning);
        }

        private static bool IsEnemy(Collider target)
        {
            return target.GetComponentInParent<EnemyBody>() != null;
        }

        private void StartMove(Vector3 from, Vector3 to, Phase phase)
        {
            _from = from;
            _to = to;
            _elapsed = 0f;
            _phase = phase;
        }

        /// <summary>
        /// ビームを撃つ所と向きをカメラから求める。カメラがなければ false。
        /// </summary>
        private bool TryGetAim(out Vector3 pivot, out Vector3 direction)
        {
            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                pivot = direction = Vector3.zero;
                return false;
            }

            var cameraTransform = cameraMain.transform;
            direction = cameraTransform.forward;
            pivot = cameraTransform.position + Vector3.down * _pivotDown + direction * _pivotForward;
            return true;
        }

        /// <summary>
        /// pivot から direction へビームの半径の球を飛ばし、最初に当たった敵でない物を返す。当たらなければ false。
        /// </summary>
        private bool TryFindBase(Vector3 pivot, Vector3 direction, out RaycastHit baseHit)
        {
            var count = Physics.SphereCastNonAlloc(pivot, _beams.BeamRadius, direction, _castBuffer,
                EnemyFinisherBeams.MaxBeamLength, _beams.BlockLayers, QueryTriggerInteraction.Ignore);
            baseHit = default;
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = _castBuffer[i];

                // 始めから重なっている物は当たりの位置が取れないので数えない
                if (hit.distance <= 0f || IsEnemy(hit.collider)) continue;
                if (found && hit.distance >= baseHit.distance) continue;

                baseHit = hit;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 照射中の 1 フレーム。基準の物を探し、体力制の対象の体力を減らし、間隔ごとに敵を倒して判定を行う。
        /// </summary>
        private void UpdateFiring(Vector3 pivot, Vector3 direction, float deltaTime)
        {
            var hasBase = TryFindBase(pivot, direction, out var baseHit);
            var stop = hasBase ? baseHit.point : pivot + direction * EnemyFinisherBeams.MaxBeamLength;

            if (hasBase)
            {
                var healthTarget = baseHit.collider.GetComponentInParent<HealthTarget>();
                if (healthTarget != null) healthTarget.ReportDamage(_healthDamagePerSecond * deltaTime);
            }

            _enemyTimer -= deltaTime;
            if (_enemyTimer <= 0f)
            {
                _enemyTimer += ENEMY_INTERVAL;
                DefeatAlong(pivot, stop);
            }

            _judgeTimer -= deltaTime;
            if (_judgeTimer > 0f) return;

            if (_tryContinue != null && !_tryContinue()) return;

            _judgeTimer += _judgeInterval;
            if (hasBase) _carve?.Invoke(baseHit.point, _carveRadius);
        }

        /// <summary>
        /// from から to までの射線に、ビームの半径の球を並べて、中の敵を倒す。
        /// </summary>
        private void DefeatAlong(Vector3 from, Vector3 to)
        {
            if (_defeatInRadius == null) return;

            var radius = _beams.BeamRadius;
            var count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / radius));
            for (var i = 0; i <= count; i++)
            {
                _defeatInRadius(Vector3.Lerp(from, to, (float)i / count), radius);
            }
        }

        private void Update()
        {
            if (_phase != Phase.Hovering || _shot == null || !TryGetAim(out var pivot, out var direction)) return;

            var deltaTime = Time.deltaTime;
            _shot.SetDirection(direction);
            _shot.Tick(pivot, deltaTime, out _);

            _hoverTime += deltaTime;
            if (_hoverTime >= _beams.TelegraphDuration) UpdateFiring(pivot, direction, deltaTime);
        }

        private void FixedUpdate()
        {
            if (_phase == Phase.Idle || _phase == Phase.Hovering) return;

            _elapsed += Time.fixedDeltaTime;
            var ratio = Mathf.Clamp01(_elapsed / _duration);
            _movement.PlaceAt(Vector3.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, ratio)));
            if (ratio < 1f) return;

            if (_phase == Phase.Rising)
            {
                _phase = Phase.Hovering;
                _hoverTime = 0f;
                _judgeTimer = 0f;
                _enemyTimer = 0f;
                if (_shot != null && TryGetAim(out _, out var direction)) _shot.BeginContinuous(direction, false);
                return;
            }

            _phase = Phase.Idle;
            _onFinished?.Invoke();
        }

        /// <summary>
        /// 位置を動かす段階
        /// </summary>
        private enum Phase
        {
            /// <summary> 使っていない </summary>
            Idle,

            /// <summary> 指定高度へ上がっている </summary>
            Rising,

            /// <summary> 指定高度にとどまり、ビームをチャージして撃っている </summary>
            Hovering,

            /// <summary> 発動した地点へ戻っている </summary>
            Returning
        }
    }
}
