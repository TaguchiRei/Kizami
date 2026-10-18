using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 塔の上の部分の、側面 1 つ。面の上に置いた敵のスポーン位置（EnemySpawnPoint）を子に持つ。
    /// 塔が倒れて止まったとき、TowerTopple が真上を向いた面を選び、その面の子のスポーン位置だけを解放する。
    /// </summary>
    public sealed class TowerFace : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("面の外向きの法線（この GameObject のローカル空間）")]
        private Vector3 _normal = Vector3.right;

        /// <summary> 面の外向きの法線（ワールド空間） </summary>
        public Vector3 WorldNormal => transform.TransformDirection(_normal);

        /// <summary>
        /// 法線を設定する。塔の面を組み立てるエディタ拡張が使う。
        /// </summary>
        public void SetNormal(Vector3 normal)
        {
            _normal = normal;
        }
    }
}
