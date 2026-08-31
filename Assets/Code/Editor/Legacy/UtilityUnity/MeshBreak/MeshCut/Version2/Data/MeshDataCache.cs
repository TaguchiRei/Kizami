// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEngine;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace MeshBreak.MeshCut.Version2
{
    public class MeshDataCache : InitializerBase
    {
        public static MeshDataCache Instance { get; private set; }

        private List<CachedMeshData> _cache = new();

        private void Start()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Initialize();
        }

        public override void Initialize()
        {
            base.Initialize();
            _cache.Clear();

            var objects = GetComponentsInChildren<CuttableObject>();
            List<Mesh> registeredMeshes = new();

            foreach (var cuttable in objects)
            {
                var mesh = cuttable.CuttableMeshFilter.sharedMesh;
                if (mesh == null) continue;

                int index = registeredMeshes.IndexOf(mesh);

                if (index == -1)
                {
                    registeredMeshes.Add(mesh);
                    _cache.Add(new CachedMeshData(mesh));
                    cuttable.MeshId = registeredMeshes.Count - 1;
                }
                else
                {
                    cuttable.MeshId = index;
                }
            }

            Debug.Log($"[MeshDataCache V2] Cache Completed. Cache Count: {_cache.Count}");
        }

        public void Get(int meshId, out CachedMeshData data)
        {
            if (_cache.Count <= meshId || meshId < 0)
            {
                Debug.LogError($"[MeshDataCache V2] IDの値が不正です {meshId}");
                data = null;
            }
            else
            {
                data = _cache[meshId];
            }
        }

        public void Unload()
        {
            _cache.Clear();
            Debug.Log("[MeshDataCache V2] キャッシュを解放しました。");
        }
    }
}
#endif
