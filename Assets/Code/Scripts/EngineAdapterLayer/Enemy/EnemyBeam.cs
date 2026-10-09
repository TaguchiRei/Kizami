using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// フィニッシャーのビーム 1 本の見た目。ビームの円柱と、攻撃の範囲を地面に示すデカールを持つ。
    /// EnemyFinisherAttack が初期化のときに作って使い回す。当たりの判定は持たない。
    /// </summary>
    /// <remarks>
    /// デカールは URP の Decal Projector で、描くには Renderer に Decal Renderer Feature が要る。
    /// </remarks>
    public sealed class EnemyBeam
    {
        private readonly GameObject _cylinder;
        private readonly Transform _cylinderTransform;
        private readonly DecalProjector _decal;

        /// <param name="parent">円柱とデカールを置く親。拡大率を 1 に保つこと</param>
        /// <param name="beamMaterial">円柱のマテリアル</param>
        /// <param name="decalPrefab">デカールのプレハブ。投影の向きと奥行き（size の z）はプレハブのものを使い、幅だけを半径に合わせる</param>
        public EnemyBeam(Transform parent, Material beamMaterial, DecalProjector decalPrefab)
        {
            _cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cylinder.name = nameof(EnemyBeam);
            Object.Destroy(_cylinder.GetComponent<Collider>());
            var cylinderRenderer = _cylinder.GetComponent<MeshRenderer>();
            cylinderRenderer.sharedMaterial = beamMaterial;
            cylinderRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cylinderRenderer.receiveShadows = false;
            _cylinderTransform = _cylinder.transform;
            _cylinderTransform.SetParent(parent, false);
            _cylinder.SetActive(false);

            if (decalPrefab != null)
            {
                _decal = Object.Instantiate(decalPrefab, parent);
                _decal.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 攻撃の範囲を示すデカールを、point を中心に下向きに出す。
        /// </summary>
        /// <param name="point">範囲の中心（ワールド座標）。ビームの止まる所</param>
        /// <param name="radius">範囲の半径（m）</param>
        public void ShowDecal(Vector3 point, float radius)
        {
            if (_decal == null) return;

            _decal.transform.SetPositionAndRotation(point, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            _decal.size = new Vector3(radius * 2f, radius * 2f, _decal.size.z);
            _decal.pivot = Vector3.zero;
            _decal.gameObject.SetActive(true);
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

            // Unity の円柱は高さ 2・半径 0.5 で、Y 軸に沿って立つ
            _cylinderTransform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, axis));
            _cylinderTransform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            _cylinder.SetActive(true);
        }

        /// <summary>
        /// 円柱を消す。デカールは残す。
        /// </summary>
        public void HideBeam()
        {
            _cylinder.SetActive(false);
        }

        /// <summary>
        /// 円柱とデカールを消す。
        /// </summary>
        public void Hide()
        {
            _cylinder.SetActive(false);
            if (_decal != null) _decal.gameObject.SetActive(false);
        }
    }
}
