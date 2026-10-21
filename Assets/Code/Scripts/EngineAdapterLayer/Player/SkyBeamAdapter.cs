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
    public sealed class SkyBeamAdapter : MonoBehaviour
    {
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

        /// <param name="movement">位置を書き込む先のプレイヤーの移動の Adapter</param>
        /// <param name="onFinished">発動した地点へ戻り終えたときに呼ぶ関数</param>
        public void Initialize(PlayerMovementAdapterBase movement, Action onFinished)
        {
            _movement = movement;
            _onFinished = onFinished;

            if (_beams == null || !_beams.Initialize(movement.transform, null))
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
        public void Begin(float altitude, float duration)
        {
            if (_movement == null) return;

            _origin = _movement.Position;
            _duration = duration;
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

        private void Update()
        {
            if (_phase != Phase.Hovering || _shot == null || !TryGetAim(out var pivot, out var direction)) return;

            _shot.SetDirection(direction);
            _shot.Tick(pivot, Time.deltaTime, out _);
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
