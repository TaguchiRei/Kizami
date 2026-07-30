using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    // 削られる側（Subtractee）にアタッチする。
    // depthMaterial には SSBoolean_FrontBack.shader を割り当てたマテリアルをセットする。
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractee : MonoBehaviour
    {
        [SerializeField] Material depthMaterial;

        const int FrontPass = 0; // Cull Back
        const int BackPass = 1;  // Cull Front

        static readonly HashSet<Subtractee> instances = new HashSet<Subtractee>();
        public static IReadOnlyCollection<Subtractee> GetAll() => instances;

        Renderer cachedRenderer;

        void OnEnable()
        {
            cachedRenderer = GetComponent<Renderer>();
            instances.Add(this);
        }

        void OnDisable()
        {
            instances.Remove(this);
        }

        public void IssueDrawFront(CommandBuffer cb)
        {
            if (depthMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, depthMaterial, 0, FrontPass);
        }

        public void IssueDrawBack(CommandBuffer cb)
        {
            if (depthMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, depthMaterial, 0, BackPass);
        }
    }
}