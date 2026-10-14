using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 1 階分ずつベイクしたボクセルのモデルを、縦に積み重ねた大きな建物。ステージシーンへ置く。
    /// 階ごとに別の VoxelModelLoader にし、削ったときの分離の判定を削った階だけで済ませる。
    /// 階は Awake で作り、各 VoxelModelLoader の Start で読み込む。
    /// 模様が階をまたいで続くよう、階ごとにマテリアルを複製し、_PatternOffset の Y に「階の番号 × 階の高さ」を書く。
    /// MaterialPropertyBlock は SRP Batcher の対象から外れる為、使わない。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/LargeObjectVoxel.md">説明ドキュメント: 巨大なオブジェクトへのボクセルの適用</seealso>
    public sealed class VoxelBuilding : MonoBehaviour
    {
        private static readonly int _patternOffsetId = Shader.PropertyToID("_PatternOffset");

        private readonly List<VoxelModelLoader> _floors = new();
        private readonly List<Material> _floorMaterials = new();
        private readonly List<IDisposable> _subscriptions = new();

        [SerializeField]
        [Tooltip("1 階のモデル")]
        private VoxelModelAsset _groundFloor;

        [SerializeField]
        [Tooltip("2 階から上の階のモデル")]
        private VoxelModelAsset _middleFloor;

        [SerializeField]
        [Tooltip("一番上に積む屋上のモデル。未設定なら積まない")]
        private VoxelModelAsset _roof;

        [SerializeField, Min(1)]
        [Tooltip("屋上を除いた階の数")]
        private int _floorCount = 5;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 階の高さ（m）。ボクセルの大きさの倍数にする")]
        private float _floorHeight = 4f;

        [SerializeField]
        [Tooltip("各階のボクセルの大きさ・チャンクの大きさ・再メッシュ化の上限")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("融解・蒸発した分の送り先と、素材の融点の設定")]
        private VoxelMeltSystem _meltSystem;

        /// <summary> 下から順の階。屋上を積んだ場合は最後の要素 </summary>
        public IReadOnlyList<VoxelModelLoader> Floors => _floors;

        public float FloorHeight => _floorHeight;

        /// <summary>
        /// floorIndex 階のモデル。1 階・中間階・屋上の順に決まる。
        /// </summary>
        private VoxelModelAsset GetFloorModel(int floorIndex)
        {
            if (floorIndex == 0) return _groundFloor;

            return floorIndex < _floorCount ? _middleFloor : _roof;
        }

        /// <summary>
        /// 階を作り、読み込んだら階ごとのマテリアルを渡す。
        /// </summary>
        private void CreateFloor(int floorIndex, VoxelModelAsset model)
        {
            var floorObject = new GameObject($"Floor_{floorIndex:00}")
            {
                layer = gameObject.layer
            };
            floorObject.transform.SetParent(transform, false);
            floorObject.transform.localPosition = new Vector3(0f, floorIndex * _floorHeight, 0f);

            var loader = floorObject.AddComponent<VoxelModelLoader>();
            loader.SetModel(model);
            loader.SetQuality(_quality);
            loader.SetMeltSystem(_meltSystem);

            var materials = CreateFloorMaterials(model, floorIndex);
            _subscriptions.Add(loader.RegisterOnLoaded(loaded =>
            {
                foreach (var part in loaded.Parts)
                {
                    part.SetMaterials(materials);
                }
            }));

            _floors.Add(loader);
        }

        /// <summary>
        /// モデルの素材のマテリアルを複製し、模様を階の高さだけずらす。同じマテリアルは階の中で 1 つの複製を使い回す。
        /// </summary>
        private VoxelMaterialEntry[] CreateFloorMaterials(VoxelModelAsset model, int floorIndex)
        {
            var offset = new Vector4(0f, floorIndex * _floorHeight, 0f, 0f);
            var copies = new Dictionary<Material, Material>();
            var entries = new VoxelMaterialEntry[model.Materials.Count];

            for (var i = 0; i < entries.Length; i++)
            {
                var source = model.Materials[i].Material;
                if (source != null && !copies.TryGetValue(source, out var copy))
                {
                    copy = new Material(source) { name = $"{source.name}_Floor{floorIndex:00}" };
                    if (copy.HasProperty(_patternOffsetId)) copy.SetVector(_patternOffsetId, offset);
                    copies.Add(source, copy);
                    _floorMaterials.Add(copy);
                }

                entries[i] = new VoxelMaterialEntry(source != null ? copies[source] : null, model.Materials[i].Strength);
            }

            return entries;
        }

        private void Awake()
        {
            if (_groundFloor == null || _middleFloor == null || _quality == null)
            {
                UsefulLogger.LogError("1 階・中間階のモデル、または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            var layerCount = _floorCount + (_roof != null ? 1 : 0);
            for (var i = 0; i < layerCount; i++)
            {
                CreateFloor(i, GetFloorModel(i));
            }
        }

        private void OnDestroy()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();

            foreach (var material in _floorMaterials)
            {
                Destroy(material);
            }

            _floorMaterials.Clear();
        }
    }
}
