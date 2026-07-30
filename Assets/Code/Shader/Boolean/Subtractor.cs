using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    // 削る側（Subtractor）にアタッチする。
    // frontMaterial には ScreenSpaceBoolean/FrontBack を割り当てたマテリアルをセットする。
    // carveMaterial には Hidden/ScreenSpaceBoolean/Carve を割り当てたマテリアルをセットする。
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractor : MonoBehaviour
    {
        [SerializeField] Material frontMaterial;
        [SerializeField] Material carveMaterial;

        const int FrontPass = 0; // FrontBack.shader Pass0 (Cull Back)

        static readonly HashSet<Subtractor> instances = new HashSet<Subtractor>();
        public static IReadOnlyCollection<Subtractor> GetAll() => instances;

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
            if (frontMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, frontMaterial, 0, FrontPass);
        }

        public void IssueDrawCarve(CommandBuffer cb)
        {
            if (carveMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, carveMaterial, 0, 0);
        }
    }
}