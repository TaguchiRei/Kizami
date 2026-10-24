using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 装甲の1枚のパネル。攻撃タイプと破壊タイプの攻撃が1回当たるごとに耐久値が1減り、0になるか粉砕タイプが当たると壊れる。
    /// 壊れたパネルは非アクティブにする。残りの耐久値の割合で色を変え、表示用の球があれば、残りの耐久値をそのシェーダーへ渡す。
    /// </summary>
    /// <remarks>
    /// 奥への遮断は、パネルのコライダーが攻撃のレイを止めることで判定する。
    /// </remarks>
    public sealed class ArmorPanel : MonoBehaviour
    {
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _durabilityId = Shader.PropertyToID("_Durability");
        private static readonly int _groupSizeId = Shader.PropertyToID("_GroupSize");

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
        [Tooltip("残りの耐久値を四角の数で見せる球の Renderer。BarrierDurability シェーダーのマテリアルを使う。未設定なら表示しない")]
        private Renderer _durabilityRenderer;

        [SerializeField]
        [Tooltip("壊れたときに破片を出すプールの種類。None なら破片を出さない")]
        private ShardPoolKind _shardKind = ShardPoolKind.None;

        [SerializeField, Min(0f)]
        [Tooltip("破片を、パネルの中心から離れる向きへ飛ばす速さ（m/s）")]
        private float _shardSpeed = 4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("破片ごとの速さのばらつきの割合")]
        private float _shardSpeedJitter = 0.4f;

        [SerializeField, Min(0f)]
        [Tooltip("破片を回す角速度の最大（rad/s）")]
        private float _shardMaxAngularSpeed = 8f;

        [SerializeField, Min(0.1f)]
        [Tooltip("壊れてから破片を消すまでの秒数")]
        private float _shardLifetime = 4f;

        [SerializeField, Min(1f)]
        [Tooltip("壊れた瞬間の色収差が戻るまでの時間の、命中のときに対する倍率")]
        private float _breakAberrationScale = 2.5f;

        private MaterialPropertyBlock _propertyBlock;
        private MaterialPropertyBlock _durabilityBlock;

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
                UpdateDurabilityDisplay();
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
            UpdateDurabilityDisplay();
        }

        private void Awake()
        {
            Durability = _maxDurability;
            _propertyBlock = new MaterialPropertyBlock();

            if (_renderer == null)
            {
                UsefulLogger.LogWarning("Renderer が設定されていない為、耐久値で色が変わりません。", this);
            }

            _durabilityBlock = new MaterialPropertyBlock();
            UpdateColor();
            UpdateDurabilityDisplay();
        }

        /// <summary>
        /// 壊す。破片のプールがあれば、パネルの位置・向き・大きさに破片を出して中心から外へ飛ばし、命中より長く色収差を掛けて、パネルを非アクティブにする。
        /// </summary>
        private void Break()
        {
            if (_shardKind != ShardPoolKind.None)
            {
                var pool = GlassShardPool.Get(_shardKind);
                if (pool != null)
                {
                    pool.Spawn(transform, transform.position, _shardSpeed, _shardSpeedJitter, _shardMaxAngularSpeed,
                        _shardLifetime);
                }
            }

            HitChromaticAberration.Play(_breakAberrationScale);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 残りの耐久値と、1 区分けの数（耐久値の最大）を表示用の球のシェーダーへ渡す。
        /// </summary>
        private void UpdateDurabilityDisplay()
        {
            if (_durabilityRenderer == null) return;

            _durabilityRenderer.GetPropertyBlock(_durabilityBlock);
            _durabilityBlock.SetFloat(_durabilityId, Durability);
            _durabilityBlock.SetFloat(_groupSizeId, _maxDurability);
            _durabilityRenderer.SetPropertyBlock(_durabilityBlock);
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
