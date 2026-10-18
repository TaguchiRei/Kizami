using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスとの直接戦闘で、プレイヤーを閉じ込める円形の空間を作る。ボスとは別の GameObject に置き、拡大率は 1 にする。
    /// 円周に等間隔でユニットを上空に浮かべ、各ユニットから真下の地面へフィニッシャーと同じ見た目のビームを撃って、円周に光の柵を作る。
    /// プレイヤーが柵に触れるか外へ出ると、決めた間隔ごとにダメージを与える。
    /// </summary>
    /// <remarks>
    /// ユニットはフィニッシャーのモデルを見た目だけ置いたもので、吸収型の敵そのものではない。
    /// ビームは EnemyFinisherBeams と同じく内側と外側のマテリアルを重ねて描き、模様は照射中の一定の値で止めておく。
    /// 柵の当たりは、プレイヤーの水平の位置が円の中心から「半径 − レーザーの半径」以上離れているかで決め、レーザーの高さは見ない。
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
        [Tooltip("ビームの外側のマテリアル。フィニッシャーのビームと同じものにする")]
        private Material _beamMaterial;

        [SerializeField]
        [Tooltip("ビームの内側のマテリアル。未設定なら外側だけで描く")]
        private Material _beamInnerMaterial;

        [SerializeField]
        [Tooltip("ビームのノイズのしきい値。大きいほど濃く映る")]
        private float _noiseLevel = 0.8f;

        [SerializeField]
        [Tooltip("ビームの射線に沿った模様の細かさ。符号で流れる向きが変わる")]
        private float _tillingY = -4f;

        [SerializeField]
        [Tooltip("レーザーの止まる地面を探すレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(3)]
        [Tooltip("円周に並べるユニットの数")]
        private int _unitCount = 12;

        [SerializeField, Min(1f)]
        [Tooltip("円の半径（m）")]
        private float _radius = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("ユニットが浮く、地面からの高さ（m）")]
        private float _height = 25f;

        [SerializeField, Min(0.1f)]
        [Tooltip("レーザーの半径（m）")]
        private float _beamRadius = 0.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("地面を探すレイを、円周の位置からこの高さ（m）上から下へ撃つ")]
        private float _groundRayHeight = 50f;

        [SerializeField, Min(0)]
        [Tooltip("柵に触れたときのダメージ量。遠距離の追跡ビームと同じ値にする")]
        private int _damage = 10;

        [SerializeField, Min(0.01f)]
        [Tooltip("柵に触れ続けている間にダメージを与える間隔（秒）")]
        private float _damageInterval = 0.5f;

        private Action<int> _applyDamage;
        private EnemyBeam[] _beams;
        private Transform[] _units;

        /// <summary> 円の中心（ワールド座標） </summary>
        private Vector3 _center;

        /// <summary> 次にダメージを与えられるまでの時間（秒） </summary>
        private float _damageCooldownRemaining;

        /// <summary> 円の空間を作っているか </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 閉じ込める相手を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// ダメージの関数を受け取る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        public void Initialize(Action<int> applyDamage)
        {
            _applyDamage = applyDamage;
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
                var ground = FindGround(rim);
                var emitter = ground + Vector3.up * _height;

                if (_units[i] != null)
                {
                    _units[i].position = emitter;
                    _units[i].gameObject.SetActive(true);
                }

                _beams[i].SetPattern(_noiseLevel, _tillingY);
                _beams[i].ShowBeam(emitter, ground, _beamRadius);
            }

            IsOpen = true;
        }

        /// <summary>
        /// 円形の空間を閉じる。ユニットとレーザーを消す。
        /// </summary>
        public void Close()
        {
            IsOpen = false;
            if (_beams == null) return;

            for (var i = 0; i < _unitCount; i++)
            {
                _beams[i].Hide();
                if (_units[i] != null) _units[i].gameObject.SetActive(false);
            }
        }

        private void Awake()
        {
            _beams = new EnemyBeam[_unitCount];
            _units = new Transform[_unitCount];

            if (_beamMaterial == null)
            {
                UsefulLogger.LogWarning("円形の空間のレーザーのマテリアルが設定されていない為、レーザーは見えません。", this);
            }

            for (var i = 0; i < _unitCount; i++)
            {
                _beams[i] = _beamInnerMaterial != null
                    ? new EnemyBeam(transform, _beamInnerMaterial, _beamMaterial)
                    : new EnemyBeam(transform, _beamMaterial);

                if (_unitPrefab == null) continue;

                _units[i] = Instantiate(_unitPrefab, transform).transform;
                _units[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            _damageCooldownRemaining = Mathf.Max(0f, _damageCooldownRemaining - Time.deltaTime);
            if (!IsOpen || _target == null || _damageCooldownRemaining > 0f) return;

            var offset = _target.position - _center;
            offset.y = 0f;
            if (offset.sqrMagnitude < (_radius - _beamRadius) * (_radius - _beamRadius)) return;

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
