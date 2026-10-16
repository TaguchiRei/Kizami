using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// あらかじめ壊れた状態のコアの部位1つ。CoreSystem の子へ置く。普通のメッシュ・コライダーで、始めはキネマティックで止まっている。
    /// ボクセルの塊（VoxelPiece）が一定の速さ以上で当たったら、止めるのをやめて分離する。ビームや爆発では動かない。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CorePart : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("分離するのに必要な、当たったときの相対速度（m/s）")]
        private float _minImpactSpeed = 2f;

        private Rigidbody _rigidbody;

        /// <summary> 分離して落ちているか </summary>
        public bool IsDetached { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsDetached || collision.relativeVelocity.magnitude < _minImpactSpeed) return;
            if (collision.collider.GetComponentInParent<Voxel.VoxelPiece>() == null) return;

            IsDetached = true;
            _rigidbody.isKinematic = false;
        }
    }
}
