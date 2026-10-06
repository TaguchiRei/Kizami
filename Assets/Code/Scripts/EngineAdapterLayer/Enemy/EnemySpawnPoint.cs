using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 実行中に敵が出てくる位置。EnemySpawnSystem の子に置く。
    /// 出す位置は、出すときの Transform の位置から求めるので、実行中に動かせる。
    /// </summary>
    public sealed class EnemySpawnPoint : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("この半径（m）の円の中に出す")]
        private float _radius = 2f;

        [SerializeField]
        [Tooltip("敵を出すか")]
        private bool _isEnabled = true;

        /// <summary> 敵を出せる状態か。GameObject が非アクティブのときも出さない </summary>
        public bool IsEnabled => _isEnabled && isActiveAndEnabled;

        /// <summary> 敵を出せる状態にする </summary>
        public void Enable()
        {
            _isEnabled = true;
        }

        /// <summary> 敵を出さない状態にする </summary>
        public void Disable()
        {
            _isEnabled = false;
        }

        /// <summary> 半径の円の中から、出す位置を 1 つ選ぶ </summary>
        public Vector3 GetSpawnPosition()
        {
            var offset = Random.insideUnitCircle * _radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isEnabled ? Color.red : Color.gray;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
