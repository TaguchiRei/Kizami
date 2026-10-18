using UnityEngine;
using UnityEngine.Rendering;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// フィニッシャーのビームや危険範囲の帯 1 本の見た目。円柱を持つ。
    /// EnemyFinisherBeams が初期化のときに作って使い回す。当たりの判定は持たない。
    /// </summary>
    public sealed class EnemyBeam
    {
        private readonly GameObject _cylinder;
        private readonly Transform _cylinderTransform;

        /// <param name="parent">円柱を置く親。拡大率を1に保つこと</param>
        /// <param name="material">円柱のマテリアル</param>
        public EnemyBeam(Transform parent, Material material)
        {
            _cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cylinder.name = nameof(EnemyBeam);
            Object.Destroy(_cylinder.GetComponent<Collider>());
            var cylinderRenderer = _cylinder.GetComponent<MeshRenderer>();
            cylinderRenderer.sharedMaterial = material;
            cylinderRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cylinderRenderer.receiveShadows = false;
            _cylinderTransform = _cylinder.transform;
            _cylinderTransform.SetParent(parent, false);
            _cylinder.SetActive(false);
        }

        /// <summary>
        /// from から to までの円柱を出す。
        /// </summary>
        /// <param name="from">撃つ所（ワールド座標）</param>
        /// <param name="to">止まる所（ワールド座標）</param>
        /// <param name="radius">円柱の半径（m）</param>
        public void ShowBeam(Vector3 from, Vector3 to, float radius)
        {
            var axis = to - from;
            var length = axis.magnitude;
            if (length <= 0f)
            {
                _cylinder.SetActive(false);
                return;
            }

            // Unity の円柱は高さ2・半径0.5で、Y 軸に沿って立つ
            _cylinderTransform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, axis));
            _cylinderTransform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            _cylinder.SetActive(true);
        }

        /// <summary>
        /// 円柱を消す。
        /// </summary>
        public void Hide()
        {
            _cylinder.SetActive(false);
        }
    }
}
