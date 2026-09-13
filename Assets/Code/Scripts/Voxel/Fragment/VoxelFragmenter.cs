using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.Voxel
{
    /// <summary>
    /// VoxelObject が削られて内側が複数の塊に分かれたら、最も大きい塊を残し、
    /// 他の塊を Rigidbody 付きの破片として切り離すコンポーネント。
    ///
    /// 分離の判定は、削る編集があったフレームの LateUpdate で 1 回だけ行う。
    /// 破片にも VoxelFragmenter を付ける為、破片がさらに削られた場合も同じように分かれる。
    /// 破片の当たり判定は凸包（VoxelColliderMode.ConvexHull）。
    /// </summary>
    [RequireComponent(typeof(VoxelObject))]
    // VoxelObject の LateUpdate（再メッシュ化）より先に分離し、分離前の形を 1 フレーム描画しない為に先に動かす
    [DefaultExecutionOrder(-100)]
    public sealed class VoxelFragmenter : MonoBehaviour
    {
        [SerializeField, Min(1)]
        [Tooltip("内側サンプルがこの数より少ない塊は、破片にせず消す")]
        private int _minFragmentSamples = 8;

        [SerializeField, Min(0.001f)]
        [Tooltip("破片の密度（kg/m³）。質量は 内側サンプル数 × ボクセルの体積 × 密度")]
        private float _density = 500f;

        [SerializeField]
        [Tooltip("破片がこの高さ（ワールド空間の Y）より下に落ちたら破棄する")]
        private float _destroyBelowY = -20f;

        private VoxelObject _voxelObject;
        private bool _isFragment;
        private bool _needsSplitCheck;

        private void Awake()
        {
            _voxelObject = GetComponent<VoxelObject>();
            _voxelObject.Edited += OnEdited;
        }

        private void OnDestroy()
        {
            if (_voxelObject != null) _voxelObject.Edited -= OnEdited;
        }

        private void LateUpdate()
        {
            if (_isFragment && transform.position.y < _destroyBelowY)
            {
                Destroy(gameObject);
                return;
            }

            if (!_needsSplitCheck) return;

            _needsSplitCheck = false;
            Split();
        }

        private void OnEdited(VoxelCsgOperation operation)
        {
            // 埋める編集では塊が分かれない
            if (operation == VoxelCsgOperation.Subtract) _needsSplitCheck = true;
        }

        /// <summary>
        /// 塊に分け、最も大きい塊以外を元のボリュームから消す。小さすぎない塊は破片として生成する。
        /// </summary>
        private void Split()
        {
            var volume = _voxelObject.Volume;
            if (volume == null) return;

            var labels = new NativeArray<int>(volume.Layout.SampleTotal, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var components = new NativeList<VoxelComponent>(Allocator.TempJob);

            try
            {
                volume.LabelComponents(labels, components);
                if (components.Length <= 1) return;

                var keepIndex = 0;
                for (var i = 1; i < components.Length; i++)
                {
                    if (components[i].SampleCount > components[keepIndex].SampleCount) keepIndex = i;
                }

                for (var i = 0; i < components.Length; i++)
                {
                    if (i == keepIndex) continue;

                    var component = components[i];
                    if (component.SampleCount >= _minFragmentSamples)
                    {
                        SpawnFragment(volume.ExtractComponent(labels, component), component.SampleCount);
                    }

                    _voxelObject.EraseComponent(labels, component);
                }

                _voxelObject.FlushRemesh();
            }
            finally
            {
                labels.Dispose();
                components.Dispose();
            }
        }

        /// <summary>
        /// 取り出したボリュームを持つ破片を、この Transform と同じ位置・向き・大きさで生成する。
        /// 元に Rigidbody があれば、その速度を引き継ぐ。
        /// </summary>
        private void SpawnFragment(VoxelVolume volume, int sampleCount)
        {
            var fragmentObject = new GameObject($"{name}_Fragment")
            {
                layer = gameObject.layer
            };
            fragmentObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            fragmentObject.transform.localScale = transform.lossyScale;

            var fragmentVoxel = fragmentObject.AddComponent<VoxelObject>();
            fragmentVoxel.SetQuality(_voxelObject.Quality);
            fragmentVoxel.SetMaterial(_voxelObject.Material);
            fragmentVoxel.SetColliderMode(VoxelColliderMode.ConvexHull);
            fragmentVoxel.LoadVolume(volume);

            // Rigidbody より先に凸包コライダーを作っておき、質量の中心と慣性を形状から求めさせる
            fragmentVoxel.FlushRemesh();

            var layout = volume.Layout;
            var worldVoxelSize = layout.VoxelSize * transform.lossyScale.x;
            var body = fragmentObject.AddComponent<Rigidbody>();
            body.mass = math.max(sampleCount * worldVoxelSize * worldVoxelSize * worldVoxelSize * _density, 0.01f);
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var sourceBody = GetComponentInParent<Rigidbody>();
            if (sourceBody != null)
            {
                var localCenter = layout.Origin + (float3)layout.CellCount * (layout.VoxelSize * 0.5f);
                body.linearVelocity = sourceBody.GetPointVelocity(transform.TransformPoint(localCenter));
                body.angularVelocity = sourceBody.angularVelocity;
            }

            var fragmenter = fragmentObject.AddComponent<VoxelFragmenter>();
            fragmenter._minFragmentSamples = _minFragmentSamples;
            fragmenter._density = _density;
            fragmenter._destroyBelowY = _destroyBelowY;
            fragmenter._isFragment = true;
        }
    }
}
