using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    /// <summary>
    /// 削る側にアタッチし、ScreenSpaceEmbeddedFeature の求めに応じてステンシルで削り込む DrawRenderer を積むコンポーネント。
    /// maskMaterial には SSEmbedded_Mask.shader を割り当てたマテリアルをセットする。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class EmbeddedSubtractor : MonoBehaviour
    {
        [SerializeField] Material maskMaterial;

        const int StencilMarkPass  = 0; // 前面をステンシルでマーク
        const int CarveDepthPass   = 1; // マーク領域の裏面を逆ZTestでえぐる
        const int PunchThroughPass = 2; // Subtracteeの裏面デプスと比較して貫通判定
        const int ClearStencilPass = 3; // マークを消す

        static readonly HashSet<EmbeddedSubtractor> instances = new HashSet<EmbeddedSubtractor>();
        public static IReadOnlyCollection<EmbeddedSubtractor> GetAll() => instances;

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

        public void IssueDrawMask(CommandBuffer cb)
        {
            if (maskMaterial == null || cachedRenderer == null) return;

            cb.DrawRenderer(cachedRenderer, maskMaterial, 0, StencilMarkPass);
            cb.DrawRenderer(cachedRenderer, maskMaterial, 0, CarveDepthPass);
            cb.DrawRenderer(cachedRenderer, maskMaterial, 0, PunchThroughPass);
            cb.DrawRenderer(cachedRenderer, maskMaterial, 0, ClearStencilPass);
        }
    }
}
