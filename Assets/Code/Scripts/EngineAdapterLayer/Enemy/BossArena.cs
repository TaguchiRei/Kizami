using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスとの直接戦闘で、プレイヤーを閉じ込める円形の空間を作る。ボスとは別の GameObject に置き、拡大率は 1 にする。
    /// 円周に等間隔でユニットを上空に浮かべ、各ユニットから真下の地面へフィニッシャーのビームを撃ち続けて、円周に光の柵を作る。
    /// 柵のビームはボクセルを溶かさず、最初に当たった物までの範囲のダメージだけをフィニッシャーのビーム（同じ GameObject の EnemyFinisherBeams）が与え、円の外へ出たときのダメージはこのコンポーネントが与える。
    /// </summary>
    /// <remarks>
    /// ユニットはフィニッシャーのモデルを見た目だけ置いたもので、吸収型の敵そのものではない。
    /// 柵に隙間を残さないよう、ユニットの数は「円周 ÷ ビームの直径」以上にする。隣のビームと重なる所では、ダメージがそれぞれ加わる。
    /// 円の外の判定は、プレイヤーの水平の位置が円の中心から半径以上離れているかで決め、高さは見ない。
    /// </remarks>
    public sealed class BossArena : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("閉じ込める相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("ユニットの見た目のプレハブ。なければユニットは見えない")]
        private GameObject _unitPrefab;

        [SerializeField]
        [Tooltip("ビームの止まる地面を探すレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(3)]
        [Tooltip("円周に並べるユニットの数。円周 ÷ ビームの直径 以上にする")]
        private int _unitCount = 16;

        [SerializeField, Min(1f)]
        [Tooltip("円の半径（m）")]
        private float _radius = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("ユニットが浮く、地面からの高さ（m）")]
        private float _height = 25f;

        [SerializeField, Min(0.1f)]
        [Tooltip("地面を探すレイを、円周の位置からこの高さ（m）上から下へ撃つ")]
        private float _groundRayHeight = 50f;

        [SerializeField, Min(0.01f)]
        [Tooltip("円の外にいる間にダメージを与える間隔（秒）")]
        private float _damageInterval = 0.5f;

        private Action<int> _applyDamage;

        /// <summary> 円の外にいるときのダメージ量 </summary>
        private int _damage;

        private EnemyFinisherBeams.Shot[] _shots;
        private Transform[] _units;

        /// <summary> 各ユニットのビームを撃つ所（ワールド座標） </summary>
        private Vector3[] _emitters;

        /// <summary> 円の中心（ワールド座標） </summary>
        private Vector3 _center;

        /// <summary> 次に円の外のダメージを与えられるまでの時間（秒） </summary>
        private float _damageCooldownRemaining;

        /// <summary> 円の空間を作っているか </summary>
        public bool IsOpen { get; private set; }

        /// <summary> 円の中心（ワールド座標）。Open で作った最後の中心 </summary>
        public Vector3 Center => _center;

        /// <summary> 円の半径（m） </summary>
        public float Radius => _radius;

        /// <summary>
        /// 閉じ込める相手を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。Initialize より前に呼ぶ。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// ダメージの関数を受け取り、ユニットの数だけビームを作る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <param name="damage">円の外にいるときのダメージ量</param>
        /// <param name="beamDamage">柵のビームに当たっているときに、間隔ごとに与えるダメージ量</param>
        public void Initialize(Action<int> applyDamage, int damage, int beamDamage)
        {
            _applyDamage = applyDamage;
            _damage = damage;

            var beams = GetComponent<EnemyFinisherBeams>();
            if (beams == null || !beams.Initialize(_target, applyDamage, beamDamage))
            {
                UsefulLogger.LogWarning("円形の空間に EnemyFinisherBeams がないか使えない為、柵のビームは出ません。", this);
                return;
            }

            _shots = new EnemyFinisherBeams.Shot[_unitCount];
            for (var i = 0; i < _unitCount; i++)
            {
                _shots[i] = beams.CreateShot();
            }
        }

        /// <summary>
        /// center を中心に、円形の空間を作る。すでに作っていれば作り直す。
        /// </summary>
        /// <param name="center">円の中心（ワールド座標）。高さは地面を探すレイの基準になる</param>
        public void Open(Vector3 center)
        {
            _center = center;
            _damageCooldownRemaining = 0f;

            for (var i = 0; i < _unitCount; i++)
            {
                var angle = Mathf.PI * 2f * i / _unitCount;
                var rim = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius;
                _emitters[i] = FindGround(rim) + Vector3.up * _height;

                if (_units[i] != null)
                {
                    _units[i].position = _emitters[i];
                    _units[i].gameObject.SetActive(true);
                }

                _shots?[i].BeginContinuous(Vector3.down, EnemyFinisherBeams.BeamMode.DamageOnly);
            }

            IsOpen = true;
        }

        /// <summary>
        /// 円形の空間を閉じる。ユニットとビームを消す。
        /// </summary>
        public void Close()
        {
            IsOpen = false;
            if (_units == null) return;

            for (var i = 0; i < _unitCount; i++)
            {
                _shots?[i].Stop();
                if (_units[i] != null) _units[i].gameObject.SetActive(false);
            }
        }

        private void Awake()
        {
            _units = new Transform[_unitCount];
            _emitters = new Vector3[_unitCount];
            if (_unitPrefab == null) return;

            for (var i = 0; i < _unitCount; i++)
            {
                _units[i] = Instantiate(_unitPrefab, transform).transform;
                _units[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (_shots != null)
            {
                for (var i = 0; i < _unitCount; i++)
                {
                    _shots[i].Tick(_emitters[i], Time.deltaTime, out _);
                }
            }

            _damageCooldownRemaining = Mathf.Max(0f, _damageCooldownRemaining - Time.deltaTime);
            if (_target == null || _damageCooldownRemaining > 0f) return;

            var offset = _target.position - _center;
            offset.y = 0f;
            if (offset.sqrMagnitude < _radius * _radius) return;

            _damageCooldownRemaining = _damageInterval;
            _applyDamage?.Invoke(_damage);
        }

        /// <summary>
        /// 位置 position の真下の地面。地面がなければ、位置の高さのまま返す。
        /// </summary>
        private Vector3 FindGround(Vector3 position)
        {
            var origin = position + Vector3.up * _groundRayHeight;
            return Physics.Raycast(origin, Vector3.down, out var hit, _groundRayHeight * 2f, _groundLayers,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : position;
        }
    }
}
