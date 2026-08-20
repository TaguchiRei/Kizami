using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    // ========================================================================
    // 削る側（Subtractor）にアタッチする。
    //
    // Subtracteeと違い、こちらは背面デプスを取るパスを持たない。
    // 「削り区間の出口」はCarveパスが背面をラスタライズしながらその場で求めるので、
    // 事前に保存する必要がないため。取るのは入口＝前面デプスだけ。
    //
    // 見た目用のマテリアル（SSBoolean_Lit, _Cull = Front）はRendererに普通に付ける。
    // ここに挿す2つはどちらもデプス計算専用で、色としては出ない。
    // ========================================================================
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractor : MonoBehaviour
    {
        // ScreenSpaceBoolean/FrontBack を割り当てたマテリアル（入口デプス取得用）
        [SerializeField] Material frontMaterial;
        // Hidden/ScreenSpaceBoolean/Carve を割り当てたマテリアル（削り込み本体）
        [SerializeField] Material carveMaterial;

        const int FrontPass = 0; // FrontBack.shader Pass0 (Cull Back) … 削り区間の入口

        // Subtracteeと同じ自己登録方式。ただしこちらはFeature側で1体ずつ
        // 個別に処理される（前面デプスをSubtractor単位で持つ必要があるため）。
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

        // 前面デプス + HasFrontマスクを積む（Feature 工程4a）
        public void IssueDrawFront(CommandBuffer cb)
        {
            if (frontMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, frontMaterial, 0, FrontPass);
        }

        // 背面を描きながら合成デプスを削り込む（Feature 工程4c）。
        // Carveシェーダは Cull Front なので、ここで描かれるのは背面＝削り区間の出口
        public void IssueDrawCarve(CommandBuffer cb)
        {
            if (carveMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, carveMaterial, 0, 0);
        }
    }
}