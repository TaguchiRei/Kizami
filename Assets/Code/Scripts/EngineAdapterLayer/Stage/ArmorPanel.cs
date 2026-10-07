using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 装甲の 1 枚のパネル。攻撃タイプと破壊タイプのダメージで耐久値が減り、0 になるか粉砕タイプが当たると壊れる。
    /// 壊れたパネルは非アクティブにする。残りの耐久値の割合で色を変える。
    /// </summary>
    /// <remarks>
    /// 奥への遮断は、パネルのコライダーが攻撃のレイを止めることで判定する。破壊対象とのひも付けは持たない。
    /// </remarks>
    public sealed class ArmorPanel : MonoBehaviour
    {
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Min(1)]
        [Tooltip("耐久値の最大")]
        private int _maxDurability = 6;

        [SerializeField]
        [Tooltip("耐久値が最大のときの色")]
        private Color _fullColor = new(0.45f, 0.55f, 0.65f);

        [SerializeField]
        [Tooltip("耐久値が 0 に近いときの色")]
        private Color _damagedColor = new(0.9f, 0.35f, 0.15f);

        [SerializeField]
        [Tooltip("色を変える Renderer")]
        private Renderer _renderer;

        private MaterialPropertyBlock _propertyBlock;

        /// <summary> 残りの耐久値 </summary>
        public int Durability { get; private set; }

        /// <summary> 耐久値の最大 </summary>
        public int MaxDurability => _maxDurability;

        /// <summary> 壊れたか </summary>
        public bool IsBroken => Durability <= 0;

        /// <summary>
        /// 耐久値を減らし、0 になったら壊す。壊れたパネルでは何もしない。
        /// </summary>
        /// <param name="amount">減らす量</param>
        public void ApplyDamage(int amount)
        {
            if (IsBroken || amount <= 0) return;

            Durability = Mathf.Max(0, Durability - amount);

            if (IsBroken)
            {
                Break();
            }
            else
            {
                UpdateColor();
            }
        }

        /// <summary>
        /// 耐久値によらず一撃で壊す。壊れたパネルでは何もしない。
        /// </summary>
        public void Shatter()
        {
            if (IsBroken) return;

            Durability = 0;
            Break();
        }

        private void Awake()
        {
            Durability = _maxDurability;
            _propertyBlock = new MaterialPropertyBlock();

            if (_renderer == null)
            {
                UsefulLogger.LogWarning("Renderer が設定されていない為、耐久値で色が変わりません。", this);
            }

            UpdateColor();
        }

        private void Break()
        {
            gameObject.SetActive(false);
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
