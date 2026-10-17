using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの上部タレットの弾を扱う。BossWeapons と同じ GameObject に付ける。
    /// 弾はタレットの武装の位置からプレイヤーへ向けて撃ち、飛んでいる間もプレイヤーの方へ少しずつ曲がる（緩やかな追尾）。
    /// プレイヤーに当たる距離まで近づいたらダメージを与えて消え、寿命が尽きたら消える。
    /// </summary>
    /// <remarks>
    /// 曲がる速さを抑えてあるので、横へ走れば避けられる。弾の見た目はプレハブを最初にプールへ作って使い回し、足りなければ撃たない。
    /// </remarks>
    [RequireComponent(typeof(BossWeapons))]
    public sealed class BossTurret : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("狙う相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("弾の見た目のプレハブ。当たり判定は使わない")]
        private GameObject _bulletPrefab;

        [SerializeField, Min(1)]
        [Tooltip("同時に飛ばせる弾の数")]
        private int _capacity = 16;

        [SerializeField, Min(0f)]
        [Tooltip("撃ってから次に撃てるまでの時間（秒）。壊れたタレットの番でも過ぎる")]
        private float _cooldown = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("弾の速さ（m/s）")]
        private float _speed = 20f;

        [SerializeField, Min(0f)]
        [Tooltip("弾がプレイヤーの方へ曲がる速さ（度/s）")]
        private float _homingSpeed = 20f;

        [SerializeField, Min(0.1f)]
        [Tooltip("弾の寿命（秒）")]
        private float _lifetime = 6f;

        [SerializeField, Min(0.1f)]
        [Tooltip("プレイヤーに当たったとみなす距離（m）")]
        private float _hitRadius = 1f;

        [SerializeField]
        [Tooltip("狙う所の、プレイヤーの位置からの高さ（m）")]
        private float _aimHeight = 1f;

        [SerializeField, Min(0)]
        [Tooltip("当たったときのダメージ量")]
        private int _damage = 5;

        private BossWeapons _weapons;
        private Action<int> _applyDamage;

        /// <summary> 弾ごとの、位置（ワールド座標） </summary>
        private Vector3[] _positions;

        /// <summary> 弾ごとの、進む向き（単位ベクトル） </summary>
        private Vector3[] _directions;

        /// <summary> 弾ごとの、残りの寿命（秒）。0 以下なら使っていない </summary>
        private float[] _remaining;

        private Transform[] _bullets;

        /// <summary> 次に撃てるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        /// <summary>
        /// ダメージの関数を受け取る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        public void Initialize(Action<int> applyDamage)
        {
            _applyDamage = applyDamage;
        }

        /// <summary>
        /// クールタイムが過ぎていれば、次の番のタレットからプレイヤーへ弾を撃つ。
        /// 番のタレットが壊れていれば、撃たずにクールタイムだけを発生させる。
        /// </summary>
        /// <returns>撃つ番を進めたか。クールタイム中なら false</returns>
        public bool TryFire()
        {
            if (_cooldownRemaining > 0f) return false;

            var index = _weapons.TakeNext(BossWeaponKind.Turret);
            if (index < 0) return false;

            _cooldownRemaining = _cooldown;
            if (!_weapons.IsBroken(index) && _target != null) Launch(_weapons.GetPosition(index));
            return true;
        }

        private void Awake()
        {
            _weapons = GetComponent<BossWeapons>();
            _positions = new Vector3[_capacity];
            _directions = new Vector3[_capacity];
            _remaining = new float[_capacity];
            _bullets = new Transform[_capacity];

            if (_bulletPrefab == null)
            {
                UsefulLogger.LogWarning("タレットの弾のプレハブが設定されていない為、弾は見えません。", this);
                return;
            }

            for (var i = 0; i < _capacity; i++)
            {
                _bullets[i] = Instantiate(_bulletPrefab, transform).transform;
                _bullets[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);

            for (var i = 0; i < _capacity; i++)
            {
                if (_remaining[i] <= 0f) continue;

                _remaining[i] -= deltaTime;
                if (_remaining[i] <= 0f || _target == null)
                {
                    Despawn(i);
                    continue;
                }

                var aim = GetAimPosition();
                var toAim = aim - _positions[i];
                if (toAim.sqrMagnitude > 1e-6f)
                {
                    _directions[i] = Vector3.RotateTowards(_directions[i], toAim.normalized,
                        _homingSpeed * Mathf.Deg2Rad * deltaTime, 0f);
                }

                _positions[i] += _directions[i] * (_speed * deltaTime);
                if ((aim - _positions[i]).sqrMagnitude <= _hitRadius * _hitRadius)
                {
                    _applyDamage?.Invoke(_damage);
                    Despawn(i);
                    continue;
                }

                if (_bullets[i] == null) continue;

                _bullets[i].position = _positions[i];
                _bullets[i].rotation = Quaternion.LookRotation(_directions[i]);
            }
        }

        private Vector3 GetAimPosition()
        {
            return _target.position + Vector3.up * _aimHeight;
        }

        /// <summary>
        /// 空いている弾を origin からプレイヤーへ向けて撃つ。空きがなければ撃たない。
        /// </summary>
        private void Launch(Vector3 origin)
        {
            for (var i = 0; i < _capacity; i++)
            {
                if (_remaining[i] > 0f) continue;

                var toAim = GetAimPosition() - origin;
                _positions[i] = origin;
                _directions[i] = toAim.sqrMagnitude > 1e-6f ? toAim.normalized : transform.forward;
                _remaining[i] = _lifetime;
                if (_bullets[i] != null)
                {
                    _bullets[i].position = origin;
                    _bullets[i].gameObject.SetActive(true);
                }

                return;
            }
        }

        private void Despawn(int i)
        {
            _remaining[i] = 0f;
            if (_bullets[i] != null) _bullets[i].gameObject.SetActive(false);
        }
    }
}
