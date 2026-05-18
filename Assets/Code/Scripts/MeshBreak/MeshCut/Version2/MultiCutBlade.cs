using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UsefulAttribute;
using Debug = UnityEngine.Debug;

public class MultiCutBlade : MonoBehaviour
{
    [SerializeField] private float _LimitMs = 5;
    [SerializeField] private MeshCutObjectPool _pool;
    [SerializeField] private PhysicsMaterial _slipperyMaterial; // 断面用（滑る）
    [SerializeField] private PhysicsMaterial _defaultMaterial; // 外殻用（通常）

    private MultiMeshCut _slicer = new();

    private UniTask _cutTask;

    private void Awake()
    {
        _slicer.LimitMs = _LimitMs;
    }

    [MethodExecutor("切断")]
    public async void Test()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Vector3 center = box.transform.TransformPoint(box.center);
        Vector3 halfExtents = box.size * 0.5f;
        Quaternion orientation = box.transform.rotation;
        Collider[] hits = Physics.OverlapBox(center, halfExtents, orientation);

        List<CuttableObject> cuttables = new List<CuttableObject>();
        HashSet<GameObject> addedObjects = new HashSet<GameObject>();
        foreach (Collider hit in hits)
        {
            GameObject obj = hit.gameObject;

            if (addedObjects.Contains(obj))
                continue; // 既に追加済みならスキップ

            CuttableObject cuttable = obj.GetComponent<CuttableObject>();
            if (cuttable != null)
            {
                cuttables.Add(cuttable);
                addedObjects.Add(obj); // 追加済みとして記録
            }
        }

        Debug.Log(cuttables.Count);

        if (cuttables.Count > 0)
        {
            await ExecuteCut(cuttables.ToArray());
        }
        else
        {
            Debug.Log("見つかりませんでした");
        }
    }

    /// <summary>
    /// 指定した複数のオブジェクトを一枚の刃で一括切断します
    /// </summary>
    public async UniTask ExecuteCut(CuttableObject[] targets)
    {
        if (targets == null || targets.Length == 0) return;

        Stopwatch st = Stopwatch.StartNew();

        // 自分自身をBladeにする
        NativePlane blade = new NativePlane(transform.position, transform.up);

        // 切断を実行
        await _slicer.Cut(targets, blade);

        Debug.Log("--------------------切断処理完了---------------------");

        // プールから必要な数だけ破片オブジェクトを一括取得
        // ターゲット1つにつき前後2つの破片が必要
        int requiredCount = targets.Length * 2;
        var fragmentStubs = _pool.GetObjects(requiredCount);

        Stopwatch frameStopwatch = Stopwatch.StartNew();

        // 4. 結果を各破片に反映
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];

            // Front側 (index: i*2)
            var frontData = fragmentStubs[i * 2];
            ApplyResult(frontData, _slicer.CutMesh[i * 2], _slicer.SamplingPoints[i * 2], target, blade);

            // Back側 (index: i*2 + 1)
            var backData = fragmentStubs[i * 2 + 1];
            ApplyResult(backData, _slicer.CutMesh[i * 2 + 1], _slicer.SamplingPoints[i * 2 + 1], target, blade);

            // 元のオブジェクトを非アクティブ化
            target.gameObject.SetActive(false);

            await CheckTime(frameStopwatch, _LimitMs);
        }

        Debug.Log($"--------------------全体処理時間 {st.ElapsedMilliseconds}ms---------------------");
    }

    private void ApplyResult(
        CuttableObject cuttable,
        Mesh mesh,
        List<Vector3> samplingPoints,
        CuttableObject original,
        NativePlane worldBlade)
    {
        GameObject fragObj = cuttable.gameObject;

        // Transform同期
        fragObj.transform.SetPositionAndRotation(
            original.transform.position,
            original.transform.rotation
        );
        fragObj.transform.localScale = original.transform.localScale;

        // メッシュ設定 
        cuttable.Mesh.sharedMesh = mesh;

        // マテリアルコピー処理
        var originalRenderer = original.Renderer;
        var fragmentRenderer = cuttable.Renderer;

        if (originalRenderer != null && fragmentRenderer != null)
        {
            Material[] originalMaterials = originalRenderer.sharedMaterials;

            // 長さを伸ばした配列を作成（断面用）
            Material[] newMaterials = new Material[originalMaterials.Length + 1];

            // 外殻マテリアルをコピー
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                newMaterials[i] = originalMaterials[i];
            }

            // 最後尾に CapMaterial を設定
            newMaterials[^1] = cuttable.CapMaterial;

            fragmentRenderer.sharedMaterials = newMaterials;
        }

        // アクティブ化
        fragObj.SetActive(true);

        cuttable.SetupCollider(worldBlade, samplingPoints);

        // 物理初速の継承
        if (original.Rig && cuttable.Rig)
        {
            cuttable.Rig.linearVelocity = original.Rig.linearVelocity;
            cuttable.Rig.angularVelocity = original.Rig.angularVelocity;
        }
    }

    private async UniTask CheckTime(Stopwatch stopwatch, float limitMs = 5f)
    {
        if (stopwatch.ElapsedMilliseconds > limitMs)
        {
            await UniTask.Yield();
            stopwatch.Restart();
            Debug.Log($"処理時間が長すぎたため、次のフレームに送りました。");
        }
    }


#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        float _planeSize = 10.0f;
        int _gridCount = 10;

        Vector3 planePos = transform.position;
        Vector3 right = transform.right;
        Vector3 forward = transform.forward;

        Color _planeColor = new(0f, 1f, 1f, 0.15f);
        Color _outlineColor = Color.cyan;
        Color _gridColor = new(0f, 1f, 1f, 0.3f);

        // デプス（Zテスト）を有効にして描画
        UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        // === 中央（基準サイズ）の平面 ===
        Vector3 r = right * _planeSize;
        Vector3 f = forward * _planeSize;

        Vector3 p1 = planePos + r + f;
        Vector3 p2 = planePos + r - f;
        Vector3 p3 = planePos - r - f;
        Vector3 p4 = planePos - r + f;

        UnityEditor.Handles.color = _planeColor;
        UnityEditor.Handles.DrawSolidRectangleWithOutline(
            new[] { p1, p2, p3, p4 },
            _planeColor,
            _outlineColor
        );

        // === グリッド線 ===
        UnityEditor.Handles.color = _gridColor;
        for (int i = 1; i < _gridCount; i++)
        {
            float t = i / (float)_gridCount;
            Vector3 startH = Vector3.Lerp(p4, p1, t);
            Vector3 endH = Vector3.Lerp(p3, p2, t);
            UnityEditor.Handles.DrawLine(startH, endH);

            Vector3 startV = Vector3.Lerp(p1, p2, t);
            Vector3 endV = Vector3.Lerp(p4, p3, t);
            UnityEditor.Handles.DrawLine(startV, endV);
        }

        DrawOutline(planePos, right, forward, _planeSize, Color.green);

        DrawOutline(planePos, right, forward, _planeSize * 1.5f, Color.green);

        DrawOutline(planePos, right, forward, _planeSize * 0.5f, Color.green);

        DrawOutline(planePos, right, forward, _planeSize * 0.25f, Color.green);

        // Zテスト設定を戻す
        UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
    }

    /// <summary>
    /// 任意サイズの外枠を描画する補助メソッド
    /// </summary>
    private void DrawOutline(Vector3 center, Vector3 right, Vector3 forward, float size, Color color)
    {
        Vector3 r = right * size;
        Vector3 f = forward * size;

        Vector3 p1 = center + r + f;
        Vector3 p2 = center + r - f;
        Vector3 p3 = center - r - f;
        Vector3 p4 = center - r + f;

        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawLine(p1, p2);
        UnityEditor.Handles.DrawLine(p2, p3);
        UnityEditor.Handles.DrawLine(p3, p4);
        UnityEditor.Handles.DrawLine(p4, p1);
    }

#endif
}