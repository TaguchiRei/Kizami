using System;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// かけらが何かのコライダーにぶつかったことを知らせる。かけらのプレハブに、CuttableObject と一緒に付ける。
    /// </summary>
    [RequireComponent(typeof(CuttableObject))]
    public sealed class FragmentContactReporter : MonoBehaviour
    {
        /// <summary> ぶつかったときに呼ばれる。引数はぶつかったかけら </summary>
        public Action<CuttableObject> Touched;

        private CuttableObject _fragment;

        private void Awake()
        {
            _fragment = GetComponent<CuttableObject>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Touched?.Invoke(_fragment);
        }
    }
}
