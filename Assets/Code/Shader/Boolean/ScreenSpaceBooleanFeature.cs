using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceBoolean
{
    public class ScreenSpaceBooleanFeature : ScriptableRendererFeature
    {
        [SerializeField] Material composeMaterial; // Hidden/ScreenSpaceBoolean/Fullscreen (Pass0:Init, Pass1:Copy)
        [SerializeField] Material compositeMaterial; // Hidden/ScreenSpaceBoolean/CompositeSubtraction（最終カメラデプスへのコピー）
        [SerializeField, Min(1)] int subtractorPasses = 2;

        ScreenSpaceBooleanPass pass;

        public override void Create()
        {
            pass = new ScreenSpaceBooleanPass(composeMaterial, compositeMaterial, subtractorPasses)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (compositeMaterial == null || composeMaterial == null) return;
            if (Subtractee.GetAll().Count == 0 || Subtractor.GetAll().Count == 0) return;

            renderer.EnqueuePass(pass);
        }
    }

    class ScreenSpaceBooleanPass : ScriptableRenderPass
    {
        // ---- Shader Property IDs ----
        static readonly int SubtracteeFrontDepthId = Shader.PropertyToID("_SubtracteeFrontDepth");
        static readonly int SubtracteeHasFrontId = Shader.PropertyToID("_SubtracteeHasFront");
        static readonly int SubtracteeBackDepthId = Shader.PropertyToID("_SubtracteeBackDepth");
        static readonly int SubtracteeHasBackId = Shader.PropertyToID("_SubtracteeHasBack");

        static readonly int SubtractorFrontDepthId = Shader.PropertyToID("_SubtractorFrontDepth");
        static readonly int SubtractorHasFrontId = Shader.PropertyToID("_SubtractorHasFront");

        static readonly int CompositeSrcId = Shader.PropertyToID("_CompositeSrcDepth");
        static readonly int SubtractionDepthId = Shader.PropertyToID("_SubtractionDepth");

        const int ComposePass_Init = 0;
        const int ComposePass_Copy = 1;

        // ClearRenderTargetのdepth引数は「1=遠クリップ / 0=近クリップ」という
        // プラットフォーム非依存の表現で渡す（reversed-Zへの変換はUnityが行う）。
        // ここに生のクリップ空間Z（reversed-Zならfar=0）を渡すと near/far が
        // 入れ替わり、以降のZTestが全て素通り or 全て棄却になる。
        const float ClearDepthFar = 1f;
        const float ClearDepthNear = 0f;

        readonly Material composeMaterial;
        readonly Material compositeMaterial;
        readonly int subtractorPasses;

        public ScreenSpaceBooleanPass(Material composeMaterial, Material compositeMaterial, int subtractorPasses)
        {
            this.composeMaterial = composeMaterial;
            this.compositeMaterial = compositeMaterial;
            this.subtractorPasses = Mathf.Max(1, subtractorPasses);
        }

        // ---------- PassData ----------
        class CaptureFrontPassData
        {
            public TextureHandle frontDepth, hasFront;
        }

        class CaptureBackPassData
        {
            public TextureHandle backDepth, hasBack;
        }

        class ComposeInitPassData
        {
            public TextureHandle frontDepth, hasFront, hasBack;
            public TextureHandle destDepth, dummyColor;
            public Material material;
        }

        class SubtractorFrontPassData
        {
            public TextureHandle frontDepth, hasFront;
            public Subtractor subtractor;
        }

        class CopyPassData
        {
            public TextureHandle srcDepth, destDepth, dummyColor;
            public Material material;
        }

        class CarvePassData
        {
            public TextureHandle srcCompositeDepth, destCompositeDepth;
            public TextureHandle subtractorFrontDepth, subtractorHasFront;
            public TextureHandle subtracteeBackDepth, subtracteeHasFront, subtracteeHasBack;
            public TextureHandle dummyColor;
            public Subtractor subtractor;
        }

        class FinalCopyPassData
        {
            public TextureHandle compositeDepth;
            public TextureHandle cameraColor, cameraDepth;
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
            };
            var colorDesc = new TextureDesc(camDesc.width, camDesc.height)
            {
                colorFormat = GraphicsFormat.R8_UNorm,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                clearBuffer = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            depthDesc.name = "_SubtracteeFrontDepthRT";
            TextureHandle subtracteeFront = renderGraph.CreateTexture(depthDesc);
            colorDesc.name = "_SubtracteeHasFrontRT";
            TextureHandle subtracteeHasFront = renderGraph.CreateTexture(colorDesc);

            depthDesc.name = "_SubtracteeBackDepthRT";
            TextureHandle subtracteeBack = renderGraph.CreateTexture(depthDesc);
            colorDesc.name = "_SubtracteeHasBackRT";
            TextureHandle subtracteeHasBack = renderGraph.CreateTexture(colorDesc);

            depthDesc.name = "_CompositeDepthA";
            TextureHandle compositeA = renderGraph.CreateTexture(depthDesc);
            depthDesc.name = "_CompositeDepthB";
            TextureHandle compositeB = renderGraph.CreateTexture(depthDesc);

            depthDesc.name = "_SubtractorFrontDepthRT";
            TextureHandle subtractorFront = renderGraph.CreateTexture(depthDesc);
            colorDesc.name = "_SubtractorHasFrontRT";
            TextureHandle subtractorHasFront = renderGraph.CreateTexture(colorDesc);

            colorDesc.name = "_SSBooleanDummyColor";
            TextureHandle dummyColor = renderGraph.CreateTexture(colorDesc);

            // ============================================================
            // 1) Subtractee前面デプス + HasFrontマスク
            //    一番手前の前面を採る（ZTest LEqual）ので、遠クリップでクリアする
            // ============================================================
            using (var builder =
                   renderGraph.AddUnsafePass<CaptureFrontPassData>("SSBoolean_SubtracteeFront", out var pd))
            {
                pd.frontDepth = subtracteeFront;
                pd.hasFront = subtracteeHasFront;
                builder.UseTexture(subtracteeFront, AccessFlags.Write);
                builder.UseTexture(subtracteeHasFront, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CaptureFrontPassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetViewProjectionMatrices(cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix());
                    cmd.SetRenderTarget(data.hasFront, data.frontDepth);
                    cmd.ClearRenderTarget(true, true, Color.black, ClearDepthFar);
                    foreach (var s in Subtractee.GetAll())
                        s.IssueDrawFront(cmd);
                });
            }

            // ============================================================
            // 2) Subtractee背面デプス + HasBackマスク（一番奥の背面を採用）
            //    ZTest GEqualで奥を勝たせるので、近クリップでクリアする
            // ============================================================
            using (var builder = renderGraph.AddUnsafePass<CaptureBackPassData>("SSBoolean_SubtracteeBack", out var pd))
            {
                pd.backDepth = subtracteeBack;
                pd.hasBack = subtracteeHasBack;
                builder.UseTexture(subtracteeBack, AccessFlags.Write);
                builder.UseTexture(subtracteeHasBack, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CaptureBackPassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetViewProjectionMatrices(cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix());
                    cmd.SetRenderTarget(data.hasBack, data.backDepth);
                    cmd.ClearRenderTarget(true, true, Color.black, ClearDepthNear);
                    foreach (var s in Subtractee.GetAll())
                        s.IssueDrawBack(cmd);
                });
            }

            // ============================================================
            // 3) 合成デプス初期化
            //    HasFront==1            : frontDepthを採用
            //    HasFront==0, HasBack==1: カメラがSubtractee内部 → nearZ(番兵)
            //    HasFront==0, HasBack==0: 何も無い → farZ(番兵)
            // ============================================================
            TextureHandle compositeRead = compositeA;
            TextureHandle compositeWrite = compositeB;

            using (var builder = renderGraph.AddUnsafePass<ComposeInitPassData>("SSBoolean_ComposeInit", out var pd))
            {
                pd.frontDepth = subtracteeFront;
                pd.hasFront = subtracteeHasFront;
                pd.hasBack = subtracteeHasBack;
                pd.destDepth = compositeRead;
                pd.dummyColor = dummyColor;
                pd.material = composeMaterial;

                builder.UseTexture(subtracteeFront, AccessFlags.Read);
                builder.UseTexture(subtracteeHasFront, AccessFlags.Read);
                builder.UseTexture(subtracteeHasBack, AccessFlags.Read);
                builder.UseTexture(compositeRead, AccessFlags.Write);
                builder.UseTexture(dummyColor, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((ComposeInitPassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    cmd.SetGlobalTexture(SubtracteeFrontDepthId, data.frontDepth);
                    cmd.SetGlobalTexture(SubtracteeHasFrontId, data.hasFront);
                    cmd.SetGlobalTexture(SubtracteeHasBackId, data.hasBack);
                    cmd.SetRenderTarget(data.dummyColor, data.destDepth);
                    cmd.ClearRenderTarget(true, true, Color.black, ClearDepthFar);
                    cmd.DrawProcedural(Matrix4x4.identity, data.material, ComposePass_Init, MeshTopology.Triangles, 3,
                        1);
                });
            }

            // ============================================================
            // 4) Subtractorによる削り込み（複数パス×複数Subtractor、逐次処理）
            // ============================================================
            for (int pass = 0; pass < subtractorPasses; pass++)
            {
                foreach (var subtractor in Subtractor.GetAll())
                {
                    // 4a) このSubtractorの前面デプス + HasFront
                    //     カメラがSubtractor内部にいるとここが空(HasFront==0)になり、
                    //     Carve側でnearZフォールバックが効く
                    using (var builder =
                           renderGraph.AddUnsafePass<SubtractorFrontPassData>("SSBoolean_SubtractorFront", out var pd))
                    {
                        pd.frontDepth = subtractorFront;
                        pd.hasFront = subtractorHasFront;
                        pd.subtractor = subtractor;
                        builder.UseTexture(subtractorFront, AccessFlags.Write);
                        builder.UseTexture(subtractorHasFront, AccessFlags.Write);
                        builder.AllowPassCulling(false);

                        builder.SetRenderFunc((SubtractorFrontPassData data, UnsafeGraphContext ctx) =>
                        {
                            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                            cmd.SetViewProjectionMatrices(cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix());
                            cmd.SetRenderTarget(data.hasFront, data.frontDepth);
                            cmd.ClearRenderTarget(true, true, Color.black, ClearDepthFar);
                            data.subtractor.IssueDrawFront(cmd);
                        });
                    }

                    // 4b) 現在の合成デプスを複製（read/write分離のため。GPUは同一デプスを
                    //     ZTest対象にしながらテクスチャとして同時サンプルできないので必要）
                    using (var builder = renderGraph.AddUnsafePass<CopyPassData>("SSBoolean_CompositeCopy", out var pd))
                    {
                        pd.srcDepth = compositeRead;
                        pd.destDepth = compositeWrite;
                        pd.dummyColor = dummyColor;
                        pd.material = composeMaterial;
                        builder.UseTexture(compositeRead, AccessFlags.Read);
                        builder.UseTexture(compositeWrite, AccessFlags.Write);
                        builder.UseTexture(dummyColor, AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((CopyPassData data, UnsafeGraphContext ctx) =>
                        {
                            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                            cmd.SetGlobalTexture(CompositeSrcId, data.srcDepth);
                            cmd.SetRenderTarget(data.dummyColor, data.destDepth);
                            cmd.DrawProcedural(Matrix4x4.identity, data.material, ComposePass_Copy,
                                MeshTopology.Triangles, 3, 1);
                        });
                    }

                    // 4c) このSubtractorで削る
                    //     Cull Front / ZTest GEqual で compositeWrite に直接書き込む。
                    //     4bの結果を読んでZTestするのでReadWriteで宣言する（Writeだけだと
                    //     RenderGraphが「全面上書き」と判断して直前の内容を捨てうる）
                    using (var builder = renderGraph.AddUnsafePass<CarvePassData>("SSBoolean_Carve", out var pd))
                    {
                        pd.srcCompositeDepth = compositeRead;
                        pd.destCompositeDepth = compositeWrite;
                        pd.subtractorFrontDepth = subtractorFront;
                        pd.subtractorHasFront = subtractorHasFront;
                        pd.subtracteeBackDepth = subtracteeBack;
                        pd.subtracteeHasFront = subtracteeHasFront;
                        pd.subtracteeHasBack = subtracteeHasBack;
                        pd.dummyColor = dummyColor;
                        pd.subtractor = subtractor;

                        builder.UseTexture(compositeRead, AccessFlags.Read);
                        builder.UseTexture(compositeWrite, AccessFlags.ReadWrite);
                        builder.UseTexture(subtractorFront, AccessFlags.Read);
                        builder.UseTexture(subtractorHasFront, AccessFlags.Read);
                        builder.UseTexture(subtracteeBack, AccessFlags.Read);
                        builder.UseTexture(subtracteeHasFront, AccessFlags.Read);
                        builder.UseTexture(subtracteeHasBack, AccessFlags.Read);
                        builder.UseTexture(dummyColor, AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((CarvePassData data, UnsafeGraphContext ctx) =>
                        {
                            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                            cmd.SetViewProjectionMatrices(cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix());
                            cmd.SetGlobalTexture(CompositeSrcId, data.srcCompositeDepth);
                            cmd.SetGlobalTexture(SubtractorFrontDepthId, data.subtractorFrontDepth);
                            cmd.SetGlobalTexture(SubtractorHasFrontId, data.subtractorHasFront);
                            cmd.SetGlobalTexture(SubtracteeBackDepthId, data.subtracteeBackDepth);
                            cmd.SetGlobalTexture(SubtracteeHasFrontId, data.subtracteeHasFront);
                            cmd.SetGlobalTexture(SubtracteeHasBackId, data.subtracteeHasBack);
                            cmd.SetRenderTarget(data.dummyColor, data.destCompositeDepth);
                            data.subtractor.IssueDrawCarve(cmd);
                        });
                    }

                    // read/writeを入れ替えて次のSubtractorが最新結果を読めるようにする
                    (compositeRead, compositeWrite) = (compositeWrite, compositeRead);
                }
            }

            // ============================================================
            // 5) 結果をカメラの本物のデプスバッファへコピー
            //    番兵値(far/near)のピクセルはシェーダ側でdiscardされ、
            //    通常のシーン描画がそのまま見える
            // ============================================================
            using (var builder =
                   renderGraph.AddUnsafePass<FinalCopyPassData>("SSBoolean_CopyToCameraDepth", out var pd))
            {
                pd.compositeDepth = compositeRead;
                pd.cameraColor = resourceData.activeColorTexture;
                pd.cameraDepth = resourceData.activeDepthTexture;
                pd.material = compositeMaterial;

                builder.UseTexture(compositeRead, AccessFlags.Read);
                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Write);
                builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((FinalCopyPassData data, UnsafeGraphContext ctx) =>
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
