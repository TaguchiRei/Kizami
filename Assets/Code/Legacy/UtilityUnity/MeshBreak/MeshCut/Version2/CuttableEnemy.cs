// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEngine;

public class CuttableEnemy : CuttableObject
{
    [SerializeField] private BoxCollider _bodyCollider;
    [SerializeField] private BoxCollider _cutSurfaceCollider;

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

    public override void SetupCollider(
        NativePlane worldBlade,
        List<Vector3> samplingPoints)
    {
        // ---------- 本体Collider ----------

        var localPlanePos =
            transform.InverseTransformPoint(worldBlade.Position);

        var localPlaneNormal =
            transform.InverseTransformDirection(worldBlade.Normal);

        Bounds bodyBounds = default;
        bool initialized = false;

        foreach (var point in samplingPoints)
        {
            float distance =
                Vector3.Dot(point - localPlanePos, localPlaneNormal);

            // 切断面より内側だけ採用
            if (distance < 0f)
            {
                if (!initialized)
                {
                    bodyBounds = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bodyBounds.Encapsulate(point);
                }
            }
        }

        if (initialized)
        {
            _bodyCollider.center = bodyBounds.center;
            _bodyCollider.size = bodyBounds.size;
        }

        // ---------- 切断面Collider ----------

        var meshBounds = CuttableMeshFilter.sharedMesh.bounds;

        Vector3 localNormalAbs = new(
            Mathf.Abs(localPlaneNormal.x),
            Mathf.Abs(localPlaneNormal.y),
            Mathf.Abs(localPlaneNormal.z));

        Vector3 cutSize;
        Vector3 cutCenter = localPlanePos;

        const float thickness = 0.02f;

        if (localNormalAbs.x >= localNormalAbs.y &&
            localNormalAbs.x >= localNormalAbs.z)
        {
            cutSize = new Vector3(
                thickness,
                meshBounds.size.y,
                meshBounds.size.z);
        }
        else if (localNormalAbs.y >= localNormalAbs.z)
        {
            cutSize = new Vector3(
                meshBounds.size.x,
                thickness,
                meshBounds.size.z);
        }
        else
        {
            cutSize = new Vector3(
                meshBounds.size.x,
                meshBounds.size.y,
                thickness);
        }

        _cutSurfaceCollider.center = cutCenter;
        _cutSurfaceCollider.size = cutSize;

        #region 旧コード

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

        #endregion
    }
}
#endif
