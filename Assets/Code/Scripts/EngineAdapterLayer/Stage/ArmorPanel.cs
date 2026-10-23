using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 装甲の1枚のパネル。攻撃タイプと破壊タイプの攻撃が1回当たるごとに耐久値が1減り、0になるか粉砕タイプが当たると壊れる。
    /// 壊れたパネルは非アクティブにする。残りの耐久値の割合で色を変え、目印のプレハブがあれば、残りの耐久値の数だけ目印を出す。
    /// </summary>
    /// <remarks>
    /// 奥への遮断は、パネルのコライダーが攻撃のレイを止めることで判定する。
    /// </remarks>
    public sealed class ArmorPanel : MonoBehaviour
    {
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<GameObject> _markers = new();

        [SerializeField, Min(1)]
        [Tooltip("耐久値の最大。壊れるまでに耐える、攻撃タイプと破壊タイプの攻撃が当たった回数")]
        private int _maxDurability = 10;

        [SerializeField]
        [Tooltip("耐久値が最大のときの色")]
        private Color _fullColor = new(0.45f, 0.55f, 0.65f);

        [SerializeField]
        [Tooltip("耐久値が 0 に近いときの色")]
        private Color _damagedColor = new(0.9f, 0.35f, 0.15f);

        [SerializeField]
        [Tooltip("色を変える Renderer")]
        private Renderer _renderer;

        [SerializeField]
        [Tooltip("耐久値 1 つにつき 1 枚並べる目印のプレハブ。球のパネルのローカル空間（直径 1）で、外を向いた面を表にして置く。未設定なら目印は出さない")]
        private GameObject _markerPrefab;

        [SerializeField, Range(-90f, 90f)]
        [Tooltip("目印を並べる緯度（度）。赤道が 0、真上が 90")]
        private float _markerLatitude = 45f;

        [SerializeField, Min(0f)]
        [Tooltip("目印を置く球の半径（パネルのローカル空間）")]
        private float _markerRadius = 0.5f;

        private MaterialPropertyBlock _propertyBlock;

        /// <summary> 残りの耐久値 </summary>
        public int Durability { get; private set; }

        /// <summary> 耐久値の最大 </summary>
        public int MaxDurability => _maxDurability;

        /// <summary> 壊れたか </summary>
        public bool IsBroken => Durability <= 0;

        /// <summary>
        /// 攻撃を1回当てる。粉砕タイプを含むなら耐久値によらず一撃で壊す。
        /// 含まなければ、攻撃タイプか破壊タイプを含むときに耐久値を1減らし、0になったら壊す。
        /// 減らす量は攻撃の種類の数や大きさによらず1で、何度も当たる攻撃は当たった回数だけ呼ぶ。
        /// </summary>
        /// <param name="damageType">当てた攻撃が持つダメージの種類</param>
        public void ApplyDamage(DamageType damageType)
        {
            if (IsBroken) return;

            if ((damageType & (DamageType.Shatter | DamageType.Attack | DamageType.Destroy)) != 0)
            {
                SoundPlayer.Play(SoundEffectKind.ArmorCut, transform.position);
                HitChromaticAberration.Play();
            }

            if ((damageType & DamageType.Shatter) != 0)
            {
                Durability = 0;
                Break();
                return;
            }

            if ((damageType & (DamageType.Attack | DamageType.Destroy)) == 0) return;

            Durability--;

            if (IsBroken)
            {
                Break();
            }
            else
            {
                UpdateColor();
                UpdateMarkers();
            }
        }

        /// <summary>
        /// 耐久値を設定し直して色を合わせる。プールから使い直すパネルに、持ち主の残りの耐久値を戻すときに呼ぶ。アクティブにしてから呼ぶ。
        /// </summary>
        /// <param name="durability">残りの耐久値。1から最大までに収める</param>
        public void SetDurability(int durability)
        {
            Durability = Mathf.Clamp(durability, 1, _maxDurability);
            UpdateColor();
            UpdateMarkers();
        }

        private void Awake()
        {
            Durability = _maxDurability;
            _propertyBlock = new MaterialPropertyBlock();

            if (_renderer == null)
            {
                UsefulLogger.LogWarning("Renderer が設定されていない為、耐久値で色が変わりません。", this);
            }

            CreateMarkers();
            UpdateColor();
            UpdateMarkers();
        }

        private void Break()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 耐久値の最大と同じ数の目印を、緯度 _markerLatitude の輪に等間隔で並べる。外向きの面を表にし、角度の小さい順に並ぶ。
        /// </summary>
        private void CreateMarkers()
        {
            if (_markerPrefab == null) return;

            var latitude = _markerLatitude * Mathf.Deg2Rad;
            for (var i = 0; i < _maxDurability; i++)
            {
                var angle = Mathf.PI * 2f * i / _maxDurability;
                var outward = new Vector3(Mathf.Cos(latitude) * Mathf.Cos(angle), Mathf.Sin(latitude),
                    Mathf.Cos(latitude) * Mathf.Sin(angle));
                var marker = Instantiate(_markerPrefab, transform);
                // Unity の Quad は -Z 側が表なので、外向きの逆へ向ける
                marker.transform.SetLocalPositionAndRotation(outward * _markerRadius, Quaternion.LookRotation(-outward));
                _markers.Add(marker);
            }
        }

        /// <summary>
        /// 残りの耐久値の数だけ目印を出し、残りは隠す。減るときは番号の大きい目印から消える。
        /// </summary>
        private void UpdateMarkers()
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                _markers[i].SetActive(i < Durability);
            }
        }

        private void UpdateColor()
        {
            if (_renderer == null) return;

            var color = Color.Lerp(_damagedColor, _fullColor, (float)Durability / _maxDurability);
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(_baseColorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
