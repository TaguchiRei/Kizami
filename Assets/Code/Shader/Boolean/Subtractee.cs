using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScreenSpaceBoolean
{
    // ========================================================================
    // 削られる側（Subtractee）にアタッチする。
    //
    // このコンポーネント自体は何も描かない。ScreenSpaceBooleanFeature から
    // 「今このコマンドバッファに前面を積んで」と頼まれたときに DrawRenderer を
    // 発行するだけの窓口。実際の描画順序と描き先はFeature側が決める。
    //
    // 見た目用のマテリアルはRendererに普通に付ける（SSBoolean_Lit）。
    // ここに挿すdepthMaterialはデプス取得専用で、見た目には一切出ない。
    // ========================================================================
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class Subtractee : MonoBehaviour
    {
        // SSBoolean_FrontBack.shader を割り当てたマテリアル（デプス取得専用）
        [SerializeField] Material depthMaterial;

        const int FrontPass = 0; // Cull Back  … 削る前の可視面を取る
        const int BackPass = 1;  // Cull Front … 貫通判定に使う出口を取る

        // Featureは「シーン内の全Subtractee」をまとめて描く必要があるが、
        // FindObjectsOfTypeを毎フレーム呼ぶわけにいかないので自己登録方式にしている。
        // OnEnable/OnDisableで出入りするため、無効化したオブジェクトは自動的に外れる。
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