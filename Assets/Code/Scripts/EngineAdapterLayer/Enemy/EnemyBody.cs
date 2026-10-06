using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵 1 体の体。敵のプレハブの根に付け、EnemySpawnAdapter のプールから使い回す。
    /// 出すたびに全部位を切断前の形に戻し、MeshDataCache へ登録し直す。
    /// プールで非アクティブのまま待つ部位は、MeshDataCache の初期登録に含まれない為。
    /// </summary>
    public sealed class EnemyBody : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("体を作る部位")]
        private EnemyPart[] _parts;

        [SerializeField, Min(0f)]
        [Tooltip("仮の移動で、プレイヤーへ近づく速さ（m/s）")]
        private float _moveSpeed = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("仮の移動で、プレイヤーとの水平距離がこの値（m）以下になったら止まる")]
        private float _stopDistance = 6f;

        /// <summary> ステージに出ているか </summary>
        public bool IsSpawned => gameObject.activeSelf;

        /// <summary>
        /// 指定した位置と向きで体を出し、全部位を切断できる状態にする。
        /// </summary>
        /// <param name="cache">部位を登録し直す先</param>
        public void Spawn(Vector3 position, Quaternion rotation, MeshDataCache cache)
        {
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);

            foreach (var part in _parts)
            {
                var cuttable = part.Cuttable;
                if (cuttable == null) continue;

                cuttable.gameObject.SetActive(true);
                cuttable.RestoreInitialShape();
                cache.Register(cuttable);
            }
        }

        /// <summary>
        /// 目標へ水平にまっすぐ近づき、進む向きを向く。止まる距離の内側では動かない。
        /// </summary>
        // TODO: 区間4B・4C の群衆 AI に置き換える
        public void MoveToward(Vector3 target, float deltaTime)
        {
            var offset = target - transform.position;
            offset.y = 0f;

            var distance = offset.magnitude;
            if (distance <= _stopDistance) return;

            var direction = offset / distance;
            var step = Mathf.Min(_moveSpeed * deltaTime, distance - _stopDistance);
            transform.SetPositionAndRotation(transform.position + direction * step, Quaternion.LookRotation(direction));
        }
    }
}
