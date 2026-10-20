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

        /// <summary> 分離していない部位の位置の平均。すべて分離していれば全部の部位の平均、部位がなければ自身の位置。CoreSystem の原点は部位から離れて置かれることがある為、部位から求める </summary>
        public Vector3 Center
        {
            get
            {
                var sum = Vector3.zero;
                var count = 0;
                foreach (var part in _parts)
                {
                    if (part == null || part.IsDetached) continue;

                    sum += part.transform.position;
                    count++;
                }

                if (count > 0) return sum / count;

                foreach (var part in _parts)
                {
                    if (part == null) continue;

                    sum += part.transform.position;
                    count++;
                }

                return count > 0 ? sum / count : transform.position;
            }
        }

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
