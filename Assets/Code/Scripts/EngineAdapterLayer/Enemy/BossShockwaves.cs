using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスのアウトリガーの足踏みで、地面を広がる衝撃波の輪を扱う。BossWeapons と同じ GameObject に付ける。
    /// 輪は足踏みした脚の足先から水平に広がり、輪の縁がプレイヤーを通り過ぎたときに、高さが輪の高さより低ければ当てる。
    /// 当てたら小さいダメージを与え、ボスの外側（輪の中心から離れる向き）へ少し弾く。1つの輪は1回まで当たる。
    /// </summary>
    /// <remarks>
    /// 輪の見た目は LineRenderer の円で描く。同時に出る輪は総攻撃でも数個なので、プールの数は Inspector で決め、足りなければ撃たない。
    /// </remarks>
    [RequireComponent(typeof(BossWeapons))]
    public sealed class BossShockwaves : MonoBehaviour
    {
        /// <summary> 輪の円を描く頂点の数 </summary>
        private const int RING_SEGMENTS = 64;

        [SerializeField]
        [Tooltip("当てる相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("輪の見た目のマテリアル")]
        private Material _ringMaterial;

        [SerializeField, Min(1)]
        [Tooltip("同時に出せる輪の数")]
        private int _capacity = 4;

        [SerializeField, Min(0f)]
        [Tooltip("足踏みしてから次の足踏みまでの時間（秒）。壊れたアウトリガーの番でも過ぎる")]
        private float _cooldown = 3f;

        [SerializeField, Min(0.1f)]
        [Tooltip("輪が広がる速さ（m/s）")]
        private float _speed = 12f;

        [SerializeField, Min(0.1f)]
        [Tooltip("輪が消える半径（m）")]
        private float _maxRadius = 40f;

        [SerializeField, Min(0.1f)]
        [Tooltip("輪の高さ（m）。プレイヤーの足元がこれより高ければ当たらない（飛び越えられる）")]
        private float _height = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーの Transform の位置から足元までの高さ（m）。足元の高さで輪に当たるかを決める")]
        private float _targetFootOffset = 1f;

        [SerializeField, Min(0)]
        [Tooltip("当たったときのダメージ量")]
        private int _damage = 5;

        [SerializeField, Min(0f)]
        [Tooltip("当たったときに外側へ弾く水平の速さ（m/s）")]
        private float _knockbackSpeed = 8f;

        [SerializeField, Min(0f)]
        [Tooltip("当たったときに弾く上向きの速さ（m/s）")]
        private float _knockbackUpSpeed = 3f;

        private BossWeapons _weapons;
        private Action<int> _applyDamage;
        private Action<Vector3> _requestKnockback;

        /// <summary> 輪ごとの、中心（ワールド座標） </summary>
        private Vector3[] _centers;

        /// <summary> 輪ごとの、今の半径（m）。0 以下なら使っていない </summary>
        private float[] _radii;

        /// <summary> 輪ごとの、もう当てたか </summary>
        private bool[] _hasHit;

        private LineRenderer[] _lines;

        /// <summary> 次に足踏みできるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        /// <summary>
        /// ダメージと弾く関数を受け取る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <param name="requestKnockback">プレイヤーを弾く関数（PlayerMovementService.RequestKnockback）。引数は打ち出し速度</param>
        public void Initialize(Action<int> applyDamage, Action<Vector3> requestKnockback)
        {
            _applyDamage = applyDamage;
            _requestKnockback = requestKnockback;
        }

        /// <summary>
        /// クールタイムが過ぎていれば、次の番のアウトリガーで足踏みし、その足先から輪を出す。
        /// 番のアウトリガーが壊れていれば、輪を出さずにクールタイムだけを発生させる。
        /// </summary>
        /// <returns>足踏みの番を進めたか。クールタイム中なら false</returns>
        public bool TryStomp()
        {
            if (_cooldownRemaining > 0f) return false;

            var index = _weapons.TakeNext(BossWeaponKind.Outrigger);
            if (index < 0) return false;

            _cooldownRemaining = _cooldown;
            if (!_weapons.IsBroken(index)) Spawn(_weapons.GetPosition(index));
            return true;
        }

        private void Awake()
        {
            _weapons = GetComponent<BossWeapons>();
            _centers = new Vector3[_capacity];
            _radii = new float[_capacity];
            _hasHit = new bool[_capacity];
            _lines = new LineRenderer[_capacity];

            if (_ringMaterial == null)
            {
                UsefulLogger.LogWarning("衝撃波の輪のマテリアルが設定されていない為、輪は見えません。", this);
            }

            for (var i = 0; i < _capacity; i++)
            {
                var line = new GameObject($"Shockwave{i}").AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false);
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = RING_SEGMENTS;
                line.widthMultiplier = _height;
                line.sharedMaterial = _ringMaterial;
                line.enabled = false;
                _lines[i] = line;
            }
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);

            for (var i = 0; i < _capacity; i++)
            {
                if (_radii[i] <= 0f) continue;

                var previousRadius = _radii[i];
                _radii[i] += _speed * deltaTime;
                if (_radii[i] >= _maxRadius)
                {
                    _radii[i] = 0f;
                    _lines[i].enabled = false;
                    continue;
                }

                if (!_hasHit[i]) TryHit(i, previousRadius);
                DrawRing(i);
            }
        }

        /// <summary>
        /// 位置 point を中心に、空いている輪を出す。空きがなければ出さない。
        /// </summary>
        private void Spawn(Vector3 point)
        {
            for (var i = 0; i < _capacity; i++)
            {
                if (_radii[i] > 0f) continue;

                _centers[i] = point;
                _radii[i] = 0.01f;
                _hasHit[i] = false;
                _lines[i].enabled = true;
                DrawRing(i);
                return;
            }
        }

        /// <summary>
        /// 輪 i の縁が、このフレームでプレイヤーの水平の位置を通り過ぎ、プレイヤーの足元が輪の高さより低ければ当てる。
        /// </summary>
        private void TryHit(int i, float previousRadius)
        {
            if (_target == null) return;

            var offset = _target.position - _centers[i];
            var height = offset.y - _targetFootOffset;
            offset.y = 0f;
            var distance = offset.magnitude;
            if (distance < previousRadius || distance > _radii[i]) return;
            if (height > _height) return;

            _hasHit[i] = true;
            _applyDamage?.Invoke(_damage);

            var outward = distance > 1e-3f ? offset / distance : transform.forward;
            _requestKnockback?.Invoke(outward * _knockbackSpeed + Vector3.up * _knockbackUpSpeed);
        }

        /// <summary>
        /// 輪 i の円を、中心の高さに、今の半径で描く。
        /// </summary>
        private void DrawRing(int i)
        {
            var line = _lines[i];
            var center = _centers[i] + Vector3.up * (_height * 0.5f);
            for (var s = 0; s < RING_SEGMENTS; s++)
            {
                var angle = s * Mathf.PI * 2f / RING_SEGMENTS;
                line.SetPosition(s, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radii[i]);
            }
        }
    }
}
