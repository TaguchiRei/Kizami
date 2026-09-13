using System.Collections.Generic;
using UnityEngine;

namespace Kizami.Voxel
{
    /// <summary>
    /// VoxelModelAsset の各パーツを、同じ相対パスにある Transform の VoxelObject へ読み込む。
    /// VoxelObject が無ければ追加する。
    /// 読み込むと、読み込み前からこの階層にあった MeshRenderer（元のモデルの見た目）を全て非表示にする。
    /// </summary>
    public sealed class VoxelModelLoader : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("読み込む事前ベイク済みのモデル")]
        private VoxelModelAsset _model;

        [SerializeField]
        [Tooltip("読み込み時のボクセルの大きさ・チャンクの大きさ")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("ボクセルのマテリアル。未設定なら、各パーツの元の MeshRenderer のマテリアルを使う")]
        private Material _materialOverride;

        [SerializeField]
        [Tooltip("Start で読み込むか")]
        private bool _loadOnStart = true;

        private readonly List<VoxelObject> _voxelObjects = new();
        private MeshRenderer[] _sourceRenderers;

        public VoxelQualitySettings Quality => _quality;
        public IReadOnlyList<VoxelObject> VoxelObjects => _voxelObjects;

        /// <summary>
        /// 品質設定を差し替える。次の Load から反映される。
        /// </summary>
        public void SetQuality(VoxelQualitySettings quality)
        {
            _quality = quality;
        }

        /// <summary>
        /// 全パーツを現在の品質設定で読み込む。読み込み済みなら作り直す。
        /// </summary>
        public void Load()
        {
            if (_model == null || _quality == null)
            {
                Debug.LogError("VoxelModelAsset または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            _voxelObjects.Clear();

            foreach (var part in _model.Parts)
            {
                var target = string.IsNullOrEmpty(part.Path) ? transform : transform.Find(part.Path);
                if (target == null)
                {
                    Debug.LogWarning($"パーツの読み込み先 '{part.Path}' が見つからない為、読み込みを飛ばします。", this);
                    continue;
                }

                if (!target.TryGetComponent<VoxelObject>(out var voxelObject))
                {
                    voxelObject = target.gameObject.AddComponent<VoxelObject>();
                }

                voxelObject.SetQuality(_quality);
                voxelObject.SetMaterial(ResolveMaterial(target));
                voxelObject.LoadSdf(part);
                _voxelObjects.Add(voxelObject);
            }

            foreach (var sourceRenderer in _sourceRenderers)
            {
                if (sourceRenderer != null) sourceRenderer.enabled = false;
            }
        }

        private void Awake()
        {
            // チャンクの MeshRenderer が生成される前に、元のモデルの MeshRenderer だけを控えておく
            _sourceRenderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        private void Start()
        {
            if (_loadOnStart) Load();
        }

        /// <summary>
        /// 上書き指定 → 読み込み先の元の MeshRenderer → 階層内で最初の元の MeshRenderer の順にマテリアルを探す。
        /// </summary>
        private Material ResolveMaterial(Transform target)
        {
            if (_materialOverride != null) return _materialOverride;

            if (target.TryGetComponent<MeshRenderer>(out var targetRenderer)) return targetRenderer.sharedMaterial;

            return _sourceRenderers.Length > 0 ? _sourceRenderers[0].sharedMaterial : null;
        }
    }
}
