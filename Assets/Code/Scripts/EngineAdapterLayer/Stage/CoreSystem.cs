using System.Text;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 普通のオブジェクトでできた、倒して壊すコア。子孫の CorePart を部位として集め、分離した部位が必要な割合以上になったら破壊済みとする。
    /// ステージシーンへ置く。ビームや爆発では壊れず、部位に倒した物を当てて分離させる。
    /// </summary>
    public sealed class CoreSystem : MonoBehaviour, IDestructionTarget
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("部位の数のうち、分離している必要がある割合")]
        private float _requiredRatio = 0.5f;

        private CorePart[] _parts;

        /// <summary> 部位の数 </summary>
        public int PartCount => _parts.Length;

        /// <summary> 分離している部位の数 </summary>
        public int DetachedCount
        {
            get
            {
                var count = 0;
                foreach (var part in _parts)
                {
                    if (part.IsDetached) count++;
                }

                return count;
            }
        }

        /// <summary> 分離した部位が必要な割合以上か。部位がなければ false </summary>
        public bool IsDestroyed => _parts.Length > 0 && DetachedCount >= _parts.Length * _requiredRatio;

        private void Awake()
        {
            _parts = GetComponentsInChildren<CorePart>();
        }

        public void AppendStatus(StringBuilder builder)
        {
            builder.Append(name).Append(": ").Append(DetachedCount).Append('/').Append(_parts.Length)
                .Append(" / ").Append(_requiredRatio.ToString("F2"));
        }
    }
}
