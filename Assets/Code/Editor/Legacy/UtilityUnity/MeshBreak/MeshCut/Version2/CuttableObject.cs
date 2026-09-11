// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulTools.Utility.Runtime.Utility;
using Random = UnityEngine.Random;

public abstract class CuttableObject : MonoBehaviour, IRecyclable
{
    public bool IsCuttable { get; set; } = true;
    public int RecycleId { get; set; }
    public int MeshId { get; set; }

    public Rigidbody CuttableRigidbody;
    public Renderer CuttableRenderer;

    public void OnRecycle()
    {
        ReuseAction?.Invoke();
        gameObject.SetActive(false);
    }

    public Action ReuseAction;

    public Material CapMaterial;
    public MeshFilter CuttableMeshFilter;

    [SerializeField] protected PhysicsMaterial _physicsMaterial;
    [SerializeField] protected int _colliderNum;

    [Header("Collider設定")] [SerializeField, Range(0.5f, 1f), Tooltip("基本縮小率")]
    protected float _baseShrink = 0.95f;

    [SerializeField, Range(0.5f, 1f), Tooltip("低密度なクラスタの差異の最小縮小率")]
    protected float _densityShrinkMin = 0.85f;

    [SerializeField, Min(1), Tooltip("密度閾値")]
    protected int _densityThreshold = 10;

    [SerializeField, Min(0f), Tooltip("最大半径制限")]
    protected float _maxRadius = 0.5f;

    protected void Awake()
    {
        if (CuttableMeshFilter == null)
        {
            TryGetComponent(out CuttableMeshFilter);
        }

        if (CuttableRigidbody == null)
        {
            TryGetComponent(out CuttableRigidbody);
        }


        if (CuttableRenderer == null)
        {
            TryGetComponent(out CuttableRenderer);
        }

        return;
    }

    public abstract void SetupCollider(NativePlane worldBlade, List<Vector3> samplingPoints);

    #region 旧コード

    /*

    /// <summary>
    /// クラスタリングを利用してコライダーの適切な位置を指定
    /// </summary>
    /// <param name="clusteringSample"></param>
    /// <returns></returns>
    protected List<Vector3> ClusteringVerts(List<Vector3> clusteringSample)
    {
        //_colliderNum = Mathf.Max(_colliderNum, 10);

        int sampleCount = clusteringSample.Count;
        int clusterCount = _colliderNum;

        List<Vector3> centers = new(clusterCount);

        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;

        for (int i = 0; i < sampleCount; i++)
        {
            var s = clusteringSample[i];

            if (s.x > maxX) maxX = s.x;
            if (s.y > maxY) maxY = s.y;
            if (s.z > maxZ) maxZ = s.z;

            if (s.x < minX) minX = s.x;
            if (s.y < minY) minY = s.y;
            if (s.z < minZ) minZ = s.z;
        }

        // ランダムな中心を作成
        for (int i = 0; i < clusterCount - 6; i++)
        {
            centers.Add(new Vector3(
                Random.Range(minX, maxX),
                Random.Range(minY, maxY),
                Random.Range(minZ, maxZ)
            ));
        }

        float midX = (minX + maxX) * 0.5f;
        float midY = (minY + maxY) * 0.5f;
        float midZ = (minZ + maxZ) * 0.5f;

        centers.Add(new Vector3(midX, midY, maxZ));
        centers.Add(new Vector3(midX, midY, minZ));
        centers.Add(new Vector3(midX, maxY, midZ));
        centers.Add(new Vector3(midX, minY, midZ));
        centers.Add(new Vector3(maxX, midY, midZ));
        centers.Add(new Vector3(minX, midY, midZ));

        int[] belongCluster = new int[sampleCount];
        Vector3[] sum = new Vector3[clusterCount];
        int[] count = new int[clusterCount];

        const int maxIteration = 20;
        const float epsilon = 1e-6f;

        for (int iter = 0; iter < maxIteration; iter++)
        {
            // 初期化
            for (int i = 0; i < clusterCount; i++)
            {
                sum[i] = Vector3.zero;
                count[i] = 0;
            }

            // 近傍のクラスタを捜索
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 point = clusteringSample[i];

                float minDist = float.MaxValue;
                int nearest = 0;

                for (int j = 0; j < clusterCount; j++)
                {
                    float dist = (centers[j] - point).sqrMagnitude;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = j;
                    }
                }

                belongCluster[i] = nearest;
                sum[nearest] += point;
                count[nearest]++;
            }

            // 重心移動
            bool moved = false;

            for (int i = 0; i < clusterCount; i++)
            {
                if (count[i] == 0) continue;

                Vector3 newCenter = sum[i] / count[i];

                if ((newCenter - centers[i]).sqrMagnitude > epsilon)
                {
                    centers[i] = newCenter;
                    moved = true;
                }
            }

            if (!moved)
                break;
        }

        return centers;
    }
*/

    #endregion
}
#endif
