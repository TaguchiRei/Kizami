using UnityEngine;
using UnityEngine.Rendering;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// フィニッシャーのビームや危険範囲の帯 1 本の見た目。円柱を持つ。
    /// EnemyFinisherBeams が初期化のときに作って使い回す。当たりの判定は持たない。
    /// </summary>
    /// <remarks>
    /// 円柱の長さと半径、ビームの模様の値は MaterialPropertyBlock で書く。マテリアルは共有のままなので、ビームと帯を同じシェーダーで何本出しても複製しない。
    /// 撃つ所の真後ろから見るビーム（羽天轟号）は、両端の蓋がない筒にする。手前の蓋が画面を覆わない為。筒のメッシュは共有する。敵のビームは蓋のある円柱のまま。
    /// </remarks>
    public sealed class EnemyBeam
    {
        /// <summary> 筒の周方向の分割数 </summary>
        private const int TUBE_SEGMENTS = 24;

        private static Mesh _tubeMesh;

        private static readonly int _beamLengthId = Shader.PropertyToID("_BeamLength");
        private static readonly int _beamRadiusId = Shader.PropertyToID("_BeamRadius");
        private static readonly int _noiseLevelId = Shader.PropertyToID("_NoiseLevel");
        private static readonly int _tillingId = Shader.PropertyToID("_Tilling");

        private readonly GameObject _cylinder;
        private readonly Transform _cylinderTransform;
        private readonly Renderer _renderer;
        private readonly MaterialPropertyBlock _properties = new();

        /// <param name="parent">円柱を置く親。拡大率を1に保つこと</param>
        /// <param name="isOpenEnded">両端の蓋がない筒にするか</param>
        /// <param name="materials">円柱のマテリアル。複数あれば同じ円柱に重ねて描く</param>
        public EnemyBeam(Transform parent, bool isOpenEnded, params Material[] materials)
        {
            if (isOpenEnded)
            {
                _cylinder = new GameObject(nameof(EnemyBeam), typeof(MeshFilter), typeof(MeshRenderer));
                _cylinder.GetComponent<MeshFilter>().sharedMesh = GetTubeMesh();
            }
            else
            {
                _cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _cylinder.name = nameof(EnemyBeam);
                Object.Destroy(_cylinder.GetComponent<Collider>());
            }

            var cylinderRenderer = _cylinder.GetComponent<MeshRenderer>();
            cylinderRenderer.sharedMaterials = materials;
            cylinderRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cylinderRenderer.receiveShadows = false;
            _renderer = cylinderRenderer;
            _cylinderTransform = _cylinder.transform;
            _cylinderTransform.SetParent(parent, false);
            _cylinder.SetActive(false);
        }

        /// <summary>
        /// 蓋のない筒のメッシュを返す。初めて呼んだときに作る。
        /// 寸法・軸・UV は Unity の円柱のプリミティブの側面と同じ（高さ2・半径0.5で Y 軸に沿って立ち、u が周方向、v が軸方向）。
        /// </summary>
        private static Mesh GetTubeMesh()
        {
            if (_tubeMesh != null) return _tubeMesh;

            // 継ぎ目で UV を 0 と 1 に分ける為、周方向は分割数 + 1 列の頂点を持つ
            var columns = TUBE_SEGMENTS + 1;
            var vertices = new Vector3[columns * 2];
            var normals = new Vector3[columns * 2];
            var uvs = new Vector2[columns * 2];
            for (var i = 0; i < columns; i++)
            {
                var u = (float)i / TUBE_SEGMENTS;
                var angle = u * Mathf.PI * 2f;
                var normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i] = normal * 0.5f + Vector3.down;
                vertices[i + columns] = normal * 0.5f + Vector3.up;
                normals[i] = normals[i + columns] = normal;
                uvs[i] = new Vector2(u, 0f);
                uvs[i + columns] = new Vector2(u, 1f);
            }

            var triangles = new int[TUBE_SEGMENTS * 6];
            for (var i = 0; i < TUBE_SEGMENTS; i++)
            {
                var t = i * 6;
                triangles[t] = i;
                triangles[t + 1] = i + columns;
                triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1;
                triangles[t + 4] = i + columns;
                triangles[t + 5] = i + 1 + columns;
            }

            _tubeMesh = new Mesh { name = "EnemyBeamTube", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            _tubeMesh.RecalculateBounds();
            return _tubeMesh;
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
            _properties.SetFloat(_beamLengthId, length);
            _properties.SetFloat(_beamRadiusId, radius);
            _renderer.SetPropertyBlock(_properties);
            _cylinder.SetActive(true);
        }

        /// <summary>
        /// ビームの模様の値を書く。次に ShowBeam を呼んだときから効く。
        /// </summary>
        /// <param name="noiseLevel">ノイズのしきい値。大きいほど濃く映る</param>
        /// <param name="tillingY">射線に沿った方向の模様の細かさ。符号で流れる向きが変わる</param>
        public void SetPattern(float noiseLevel, float tillingY)
        {
            _properties.SetFloat(_noiseLevelId, noiseLevel);
            _properties.SetVector(_tillingId, new Vector4(1f, tillingY, 0f, 0f));
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
