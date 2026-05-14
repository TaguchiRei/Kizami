using System.Collections.Generic;
using UnityEngine;
using UsefulAttribute;

public class MeshCutObjectPool : MonoBehaviour
{
    [ShowOnly] public bool IsGenerated { get; private set; }

    [SerializeField] private int _generateCapacity;
    [SerializeField] private GameObject _prefab;

    private RecycleBuffer<CuttableObject> _recycleBuffer;

    private async void Start()
    {
        IsGenerated = false;
        var objects = await InstantiateAsync(_prefab, _generateCapacity, transform);
        
        CuttableObject[] buffer = new CuttableObject[_generateCapacity];
        for (int i = 0; i < objects.Length; i++)
        {
            var obj = objects[i];
            obj.SetActive(false);
            buffer[i] = obj.GetComponent<CuttableObject>();
        }

        _recycleBuffer = new RecycleBuffer<CuttableObject>(buffer);
        IsGenerated = true;
    }

    public List<CuttableObject> GetObjects(int objectCount)
    {
        if (objectCount > _generateCapacity)
        {
            Debug.LogWarning("オブジェクトの要求量が生成数を超えています");
            objectCount = _generateCapacity;
        }

        List<CuttableObject> results = new(objectCount);
        for (int i = 0; i < objectCount; i++)
        {
            var item = _recycleBuffer.Get();
            // Get内で既にOnRecycleが呼ばれる(使用中の場合)
            results.Add(item);
        }

        return results;
    }

    public void ReleaseObject(CuttableObject releaseObject)
    {
        if (releaseObject == null) return;
        releaseObject.OnRecycle();
        _recycleBuffer.Release(releaseObject);
    }
}