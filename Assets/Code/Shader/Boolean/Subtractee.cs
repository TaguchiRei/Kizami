using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    /// <summary>
    /// 削られる側にアタッチし、ScreenSpaceBooleanFeature の求めに応じてデプス取得用の DrawRenderer を積むコンポーネント。
    /// 描画順序と描き先は Feature 側が決める。見た目用のマテリアル（SSBoolean_Lit）は Renderer に普通に付ける。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractee : MonoBehaviour
    {
        // SSBoolean_FrontBack.shader を割り当てたマテリアル（デプス取得専用）
        [SerializeField] Material depthMaterial;

        const int FrontPass = 0; // Cull Back  … 削る前の可視面を取る
        const int BackPass = 1;  // Cull Front … 貫通判定に使う出口を取る

        // 有効なSubtracteeの一覧。OnEnable/OnDisableで自己登録・解除する。
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

        // 前面デプス + HasFrontマスクを積む（Feature 工程1）
        public void IssueDrawFront(CommandBuffer cb)
        {
            if (depthMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, depthMaterial, 0, FrontPass);
        }

        // 背面デプス + HasBackマスクを積む（Feature 工程2）
        public void IssueDrawBack(CommandBuffer cb)
        {
            if (depthMaterial != null && cachedRenderer != null)
                cb.DrawRenderer(cachedRenderer, depthMaterial, 0, BackPass);
        }
    }
}