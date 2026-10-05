using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    /// <summary>
    /// 削られる側にアタッチし、ScreenSpaceEmbeddedFeature の求めに応じて前面・背面デプスの DrawRenderer を積むコンポーネント。
    /// depthMaterial には SSEmbedded_FrontBack.shader を割り当てたマテリアルをセットする。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class EmbeddedSubtractee : MonoBehaviour
    {
        [SerializeField] Material depthMaterial;

        const int FrontPass = 0; // Cull Back
        const int BackPass = 1; // Cull Front

        static readonly HashSet<EmbeddedSubtractee> instances = new();
        public static IReadOnlyCollection<EmbeddedSubtractee> GetAll() => instances;

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