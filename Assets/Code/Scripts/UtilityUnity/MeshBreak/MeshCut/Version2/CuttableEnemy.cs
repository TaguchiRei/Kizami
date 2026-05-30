using System.Collections.Generic;
using UnityEngine;

public class CuttableEnemy : CuttableObject
{
    [SerializeField] private BoxCollider _collider;

    /*
     private void Awake()
     {
        _colliderNum = Mathf.Max(_colliderNum, 6);

        _colliders = new List<SphereCollider>(_colliderNum);

        for (int i = 0; i < _colliderNum; i++)
        {
            var col = gameObject.AddComponent<SphereCollider>();

            col.enabled = false;
            col.sharedMaterial = _physicsMaterial;

            _colliders.Add(col);
        }
     }
     */

    public override void SetupCollider(NativePlane worldBlade, List<Vector3> samplingPoints)
    {
        var bounds = CuttableMeshFilter.sharedMesh.bounds;

        Vector3 center = bounds.center;
        Vector3 size = bounds.size;

        // 切断面法線をローカル空間へ変換
        Vector3 localNormal =
            transform.InverseTransformDirection(worldBlade.Normal);

        localNormal.Normalize();

        // 法線が最も向いている軸を取得
        int axis;
        float absX = Mathf.Abs(localNormal.x);
        float absY = Mathf.Abs(localNormal.y);
        float absZ = Mathf.Abs(localNormal.z);

        if (absX >= absY && absX >= absZ)
            axis = 0;
        else if (absY >= absZ)
            axis = 1;
        else
            axis = 2;

        // 切断面頂点群の平均位置
        Vector3 cutCenter = Vector3.zero;

        foreach (var p in samplingPoints)
        {
            cutCenter += p;
        }

        cutCenter /= samplingPoints.Count;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        switch (axis)
        {
            case 0:
            {
                bool positiveSide = localNormal.x > 0f;

                if (positiveSide)
                    max.x = cutCenter.x;
                else
                    min.x = cutCenter.x;

                break;
            }

            case 1:
            {
                bool positiveSide = localNormal.y > 0f;

                if (positiveSide)
                    max.y = cutCenter.y;
                else
                    min.y = cutCenter.y;

                break;
            }

            case 2:
            {
                bool positiveSide = localNormal.z > 0f;

                if (positiveSide)
                    max.z = cutCenter.z;
                else
                    min.z = cutCenter.z;

                break;
            }
        }

        bounds.SetMinMax(min, max);

        _collider.center = bounds.center;
        _collider.size = bounds.size;
        /*
         int sampleCount = samplingPoints.Count;

        if (sampleCount == 0)
        {
            DisableUnusedColliders(0);
            return;
        }

        // ワールド -> ローカル変換
        List<Vector3> localPoints = new(sampleCount);

        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        for (int i = 0; i < sampleCount; i++)
        {
            localPoints.Add(worldToLocal.MultiplyPoint3x4(samplingPoints[i]));
        }

        // クラスタリング
        List<Vector3> centers = ClusteringVerts(localPoints);

        int clusterCount = centers.Count;

        int[] belongCluster = new int[sampleCount];
        int[] clusterVertCount = new int[clusterCount];

        // 所属クラスタ探索
        for (int i = 0; i < sampleCount; i++)
        {
            float minDist = float.MaxValue;
            int nearest = 0;

            for (int j = 0; j < clusterCount; j++)
            {
                float dist = (centers[j] - localPoints[i]).sqrMagnitude;

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = j;
                }
            }

            belongCluster[i] = nearest;
            clusterVertCount[nearest]++;
        }

        // Collider設定
        for (int i = 0; i < clusterCount; i++)
        {
            float maxDistSq = 0f;

            for (int v = 0; v < sampleCount; v++)
            {
                if (belongCluster[v] != i)
                    continue;

                float distSq =
                    (centers[i] - localPoints[v]).sqrMagnitude;

                if (distSq > maxDistSq)
                    maxDistSq = distSq;
            }

            float radius = Mathf.Sqrt(maxDistSq);

            radius *= _baseShrink;

            if (clusterVertCount[i] < _densityThreshold)
            {
                float t =
                    1f - (clusterVertCount[i] / (float)_densityThreshold);

                float densityShrink =
                    Mathf.Lerp(_baseShrink, _densityShrinkMin, t);

                radius *= densityShrink;
            }

            radius = Mathf.Min(radius, _maxRadius);

            SphereCollider col = _colliders[i];

            col.enabled = true;
            col.center = centers[i];
            col.radius = radius;
        }

        DisableUnusedColliders(clusterCount);
        */
    }
}