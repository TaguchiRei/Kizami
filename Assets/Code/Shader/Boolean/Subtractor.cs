using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    /// <summary>
    /// 削る側にアタッチし、ScreenSpaceBooleanFeature の求めに応じて入口デプスの取得と削り込みの DrawRenderer を積むコンポーネント。
    /// 削り区間の出口は Carve パスが背面をラスタライズしながら求めるので、背面デプスを取るパスは持たない。
    /// 見た目用のマテリアル（SSBoolean_Lit, _Cull = Front）は Renderer に普通に付ける。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractor : MonoBehaviour
    {
        // ScreenSpaceBoolean/FrontBack を割り当てたマテリアル（入口デプス取得用）
        [SerializeField] Material frontMaterial;
        // Hidden/ScreenSpaceBoolean/Carve を割り当てたマテリアル（削り込み本体）
        [SerializeField] Material carveMaterial;

        const int FrontPass = 0; // FrontBack.shader Pass0 (Cull Back) … 削り区間の入口

        // 有効なSubtractorの一覧。OnEnable/OnDisableで自己登録・解除する。
        // Feature側では前面デプスをSubtractor単位で持つ必要があるため、1体ずつ個別に処理される。
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