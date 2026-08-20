using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceBoolean
{
    // Project Settings > Graphics で使っている Universal Renderer アセットの
    // "Renderer Features" リストにこの Feature を追加して使う。
    //
    // Unity 6 の Render Graph（デフォルト）で動くように、Unsafe Pass
    // （素のCommandBufferをそのまま使える抜け道API）で実装しています。
    // Compatibility Mode は Unity 6.3 で非推奨/非サポートになったため使いません。
    public class ScreenSpaceEmbeddedFeature : ScriptableRendererFeature
    {
        [SerializeField] Material compositeMaterial;

        ScreenSpaceEmbeddedPass pass;

        public override void Create()
        {
            pass = new ScreenSpaceEmbeddedPass(compositeMaterial)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (compositeMaterial == null) return;
            if (EmbeddedSubtractee.GetAll().Count == 0 || EmbeddedSubtractor.GetAll().Count == 0) return;

            renderer.EnqueuePass(pass);
        }
    }

    class ScreenSpaceEmbeddedPass : ScriptableRenderPass
    {
        static readonly int BackDepthId = Shader.PropertyToID("_SubtracteeBackDepth");
        static readonly int SubtractionDepthId = Shader.PropertyToID("_SubtractionDepth");

        readonly Material compositeMaterial;

        public ScreenSpaceEmbeddedPass(Material compositeMaterial)
        {
            this.compositeMaterial = compositeMaterial;
        }

        class BackDepthPassData
        {
            public TextureHandle backDepth;
        }

        class CompositePassData
        {
            public TextureHandle backDepth;
            public TextureHandle compositeDepth;
            public TextureHandle dummyColor;
        }

        class CopyToCameraPassData
        {
            public TextureHandle compositeDepth;
            public TextureHandle cameraColor;
            public TextureHandle cameraDepth;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            var camDesc = cameraData.cameraTargetDescriptor;

            var depthDesc = new TextureDesc(camDesc.width, camDesc.height)
            {
                colorFormat = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth24,
                msaaSamples = MSAASamples.None,
                clearBuffer = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_SubtracteeBackDepthRT"
            };
            TextureHandle backDepth = renderGraph.CreateTexture(depthDesc);

            depthDesc.name = "_SubtractionDepthRT";
            TextureHandle compositeDepth = renderGraph.CreateTexture(depthDesc);

            var colorDesc = new TextureDesc(camDesc.width, camDesc.height)
            {
                colorFormat = GraphicsFormat.R8_UNorm,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                clearBuffer = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_SubtractionDummyColor"
            };
            TextureHandle dummyColor = renderGraph.CreateTexture(colorDesc);

            // 1) 全 Subtractee の裏面デプスを保存する
            using (var builder = renderGraph.AddUnsafePass<BackDepthPassData>("SSBoolean_BackDepth", out var passData))
            {
                passData.backDepth = backDepth;
                builder.UseTexture(backDepth, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BackDepthPassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetRenderTarget(data.backDepth);
                    cmd.ClearRenderTarget(true, false, Color.black, 0f);
                    foreach (var s in EmbeddedSubtractee.GetAll())
                        s.IssueDrawBack(cmd);
                });
            }

            // 2) EmbeddedSubtractee を描いた上から Subtractor で削る
            using (var builder = renderGraph.AddUnsafePass<CompositePassData>("SSBoolean_Composite", out var passData))
            {
                passData.backDepth = backDepth;
                passData.compositeDepth = compositeDepth;
                passData.dummyColor = dummyColor;
                builder.UseTexture(backDepth, AccessFlags.Read);
                builder.UseTexture(compositeDepth, AccessFlags.Write);
                builder.UseTexture(dummyColor, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CompositePassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetGlobalTexture(BackDepthId, data.backDepth);
                    cmd.SetRenderTarget(data.dummyColor, data.compositeDepth);
                    cmd.ClearRenderTarget(true, true, Color.black, 1f);
                    foreach (var s in EmbeddedSubtractee.GetAll())
                        s.IssueDrawFront(cmd);
                    foreach (var s in EmbeddedSubtractor.GetAll())
                        s.IssueDrawMask(cmd);
                });
            }

            // 3) 結果をカメラの本物のデプスバッファへコピー
            using (var builder =
                   renderGraph.AddUnsafePass<CopyToCameraPassData>("SSBoolean_CopyToCameraDepth", out var passData))
            {
                passData.compositeDepth = compositeDepth;
                passData.cameraColor = resourceData.activeColorTexture;
                passData.cameraDepth = resourceData.activeDepthTexture;
                passData.material = compositeMaterial;

                builder.UseTexture(compositeDepth, AccessFlags.Read);
                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Write);
                builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CopyToCameraPassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetGlobalTexture(SubtractionDepthId, data.compositeDepth);
                    cmd.SetRenderTarget(data.cameraColor, data.cameraDepth);
                    cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}