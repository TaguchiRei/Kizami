using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceBoolean
{
    // ============================================================================
    // スクリーンスペース・ブーリアン（Subtractee から Subtractor を引く）
    // ----------------------------------------------------------------------------
    // ■ 何をするものか
    //   メッシュを実際に加工せず、「カメラから見える面のデプス」だけを画面空間で
    //   組み替えることで、Subtractor(A)でSubtractee(B)を削ったように見せる。
    //   ジオメトリは一切変わらないので、削る形も位置も毎フレーム自由に動かせる。
    //
    // ■ 基本アイデア
    //   1本の視線に沿って並べると、B\A の可視面は次のように決まる。
    //
    //     カメラ →----- B前面 ====== A前面 ~~~~~~ A背面 ====== B背面 -----→
    //                    │           │            │
    //                    │           └ ここから削られる
    //                    │                        └ 削った後の可視面（穴の内壁）
    //                    └ 削る前の可視面
    //
    //   ・可視面が A の区間 [A前面, A背面] の外にある → B前面のまま
    //   ・区間の中にある                             → 可視面を A背面 まで押し込む
    //   ・A背面 が B背面 より奥                      → 貫通。そのピクセルには何も無い
    //
    // ■ ステンシルは使わない
    //   Embedded版はステンシルで領域を囲ってから掘るが、こちらは「合成デプス」
    //   というデプスRTを段階的に作り替えていく方式。カメラがAやBの内部に入っても
    //   破綻させられるのはこちらの利点。
    //
    // ■ パスの流れ（RecordRenderGraph）
    //   1. Bの前面デプス + HasFrontマスク
    //   2. Bの背面デプス + HasBackマスク
    //   3. 合成デプスをBの前面で初期化（＝削る前の可視面）
    //   4. Subtractorごとに
    //        a. Aの前面デプス + HasFrontマスク
    //        b. 合成デプスを複製（read/writeを分けるため）
    //        c. Aの背面を描きながら合成デプスを削り込む   ← アルゴリズム本体
    //   5. 合成デプスをカメラの本物のデプスバッファへ書き戻す
    //
    //   この後、URPの通常の不透明描画が ZTest Equal で色を乗せる
    //   （SSBoolean_Lit.shader）。つまりこのFeature自体は色を一切描かず、
    //   「どのデプスに面があることにするか」だけを決めている。
    //
    // ■ Aの背面デプスだけRTに保存していない理由
    //   4cではAの背面を実際にラスタライズしながら判定するので、そのフラグメント
    //   自身のSV_POSITION.zがそのまま「Aの出口」になる。保存する必要がない。
    //   RTに持っているのは _SubtracteeFrontDepth / _SubtracteeBackDepth /
    //   _SubtractorFrontDepth の3枚だけ。
    //
    // ■ HasFront / HasBack マスクが必要な理由
    //   カメラがメッシュの内部に入ると、その面は近クリップ面で切られてラスタ
    //   ライズされない。デプスRTを見ただけでは「奥に何も無い」のか「カメラの
    //   後ろにあって描かれなかった」のかを区別できないため、実際に描かれたか
    //   どうかをR8の別テクスチャに記録している。
    //   これが「削れた部分の中にカメラが入っても映る」ための土台になっている。
    //
    // ■ 使い方
    //   ・削られる側に Subtractee、削る側に Subtractor をアタッチ
    //   ・Universal Renderer の Renderer Features にこのFeatureを追加
    //   ・見た目用マテリアルは SSBoolean_Lit を使う
    //     （Subtractor側は _Cull = Front にすると穴の内壁が見える）
    //
    // ■ 既知の制限
    //   ・Subtracteeを複数置くと前面/背面デプスが1枚に統合されるので互いに干渉する
    //     （手前の物体を貫通した穴の先に、奥の物体の前面が出てこない等）
    //   ・影は削る前の形で落ちる（ShadowCasterは通常描画のまま）
    //   ・URPのDepth Primingが有効だと削る前のデプスと競合する
    // ============================================================================
    public class ScreenSpaceBooleanFeature : ScriptableRendererFeature
    {
        // Hidden/ScreenSpaceBoolean/Fullscreen
        // Pass0 = 合成デプスの初期化 / Pass1 = 合成デプスのコピー
        [SerializeField] Material composeMaterial;

        // Hidden/ScreenSpaceBoolean/CompositeSubtraction
        // 完成した合成デプスをカメラの本物のデプスバッファへ書き戻す
        [SerializeField] Material compositeMaterial;

        // Subtractorが複数あるとき、削り込みは「1つ前の結果」を入力に逐次処理される。
        // そのため処理順によっては1周では削り残しが出る（Aで削った面をBがさらに削る、
        // という連鎖が順番次第で1周に収まらない）。周回数を増やすと収束するが、
        // そのぶんパス数が線形に増える。
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
            // 削る側か削られる側のどちらかが1つも無いなら合成デプスは元のデプスと
            // 同じ内容にしかならないので、まるごとスキップする
            if (compositeMaterial == null || composeMaterial == null) return;
            if (Subtractee.GetAll().Count == 0 || Subtractor.GetAll().Count == 0) return;

            renderer.EnqueuePass(pass);
        }
    }

    class ScreenSpaceBooleanPass : ScriptableRenderPass
    {
        // ---- Shader Property IDs ----
        // 中間RTはRenderGraphが管理するのでマテリアルに直接挿せない。
        // 各パスの直前に cmd.SetGlobalTexture でグローバルへ挿してシェーダに渡す。
        // 削られる側(B)：一番手前の前面 = 削る前の可視面
        static readonly int SubtracteeFrontDepthId = Shader.PropertyToID("_SubtracteeFrontDepth");
        static readonly int SubtracteeHasFrontId = Shader.PropertyToID("_SubtracteeHasFront");
        // 削られる側(B)：一番奥の背面 = 貫通したかどうかの判定に使う出口
        static readonly int SubtracteeBackDepthId = Shader.PropertyToID("_SubtracteeBackDepth");
        static readonly int SubtracteeHasBackId = Shader.PropertyToID("_SubtracteeHasBack");

        // 削る側(A)：一番手前の前面 = 削り区間の入口。
        // 出口(A背面)はCarveパスでラスタライズしながら求めるのでRTを持たない。
        static readonly int SubtractorFrontDepthId = Shader.PropertyToID("_SubtractorFrontDepth");
        static readonly int SubtractorHasFrontId = Shader.PropertyToID("_SubtractorHasFront");

        // 削り込みの入力になる「1つ前の合成デプス」
        static readonly int CompositeSrcId = Shader.PropertyToID("_CompositeSrcDepth");
        // 完成した合成デプス（カメラデプスへの書き戻し用）
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

            // デプス専用RTの雛形。
            // 画面と同解像度・Point・MSAA無しなのは、後でピクセルを1対1で読み戻すため。
            // 補間が入るとデプスの比較が意味を失うのでFilterModeは必ずPoint。
            var depthDesc = new TextureDesc(camDesc.width, camDesc.height)
            {
                colorFormat = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth24,
                msaaSamples = MSAASamples.None,
                clearBuffer = false, // クリア値はパスごとに使い分けるので自前でClearする
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            // 「その面が実際に描かれたか」を記録する1chマスクの雛形。
            // 0 = 描かれなかった（＝カメラの後ろ or そもそも物体が無い）
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

            // 合成デプスのping-pong用2枚。
            // GPUは「同じデプスバッファをZTest対象にしながら同時にテクスチャとして
            // サンプルする」ことができないので、read側とwrite側を必ず別RTにする。
            depthDesc.name = "_CompositeDepthA";
            TextureHandle compositeA = renderGraph.CreateTexture(depthDesc);
            depthDesc.name = "_CompositeDepthB";
            TextureHandle compositeB = renderGraph.CreateTexture(depthDesc);

            depthDesc.name = "_SubtractorFrontDepthRT";
            TextureHandle subtractorFront = renderGraph.CreateTexture(depthDesc);
            colorDesc.name = "_SubtractorHasFrontRT";
            TextureHandle subtractorHasFront = renderGraph.CreateTexture(colorDesc);

            // デプスだけを書くパスでも、SetRenderTargetにはカラーRTを1枚渡す必要がある。
            // 中身は使わない（各シェーダは ColorMask 0 で色を書かない）。
            colorDesc.name = "_SSBooleanDummyColor";
            TextureHandle dummyColor = renderGraph.CreateTexture(colorDesc);

            // ============================================================
            // 1) Subtractee前面デプス + HasFrontマスク
            //
            //    「削る前の可視面」を取る工程。Cull Backで前面だけを描き、
            //    ZTest LEqualなので一番手前が勝つ。RTは遠クリップでクリアする。
            //
            //    カメラがSubtracteeの内部にいると前面が近クリップで切られて
            //    1枚も描かれず、HasFrontが0のままになる。それは「物体が無い」
            //    ではなく「内部にいる」のサインとして工程3で使う。
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
            //
            //    「Subtracteeの出口」を取る工程。Cull Frontで背面だけを描き、
            //    ZTest GEqualで奥を勝たせるので、RTは近クリップでクリアする。
            //
            //    工程4cで「Subtractorの出口がまだSubtracteeの中か」を判定するのに
            //    使う。出口を追い越していたらそのピクセルは貫通＝何も残らない。
            //
            //    一番奥を採るのは、Subtracteeが複数あるとき「穴の先に何も無い」より
            //    「多少おかしくても面が埋まっている」方を選ぶため。手前の背面を
            //    採ると複数配置時の干渉は減るが、貫通しすぎて背景が抜ける。
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
            // 3) 合成デプス初期化（これ以降、この1枚を削って完成形にしていく）
            //
            //    HasFront==1            : frontDepthを採用（＝削る前の可視面）
            //    HasFront==0, HasBack==1: カメラがSubtractee内部 → nearZ(番兵)
            //    HasFront==0, HasBack==0: 何も無い → farZ(番兵)
            //
            //    nearZ番兵は「カメラ位置そのものに可視面がある」という意味。
            //    こうしておくと工程4cの区間判定が内部にいる場合もそのまま通り、
            //    削れた空間の中にカメラが入っても内壁が残る。
            //    ただし実在する面ではないので、工程5でカメラデプスには書かない。
            // ============================================================
            // 以降 compositeRead が「最新の結果」、compositeWrite が「次の書き込み先」。
            // Subtractorを1つ処理するたびに入れ替える。
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
            // 4) Subtractorによる削り込み（複数周×複数Subtractor、逐次処理）
            //
            //    Subtractor1つにつき3パス使う。まとめて処理できないのは、
            //    削り判定に「そのSubtractor単体の前面デプス」が必要なため。
            //    全部まとめて描くと手前のSubtractorの前面で上書きされてしまう。
            // ============================================================
            for (int pass = 0; pass < subtractorPasses; pass++)
            {
                foreach (var subtractor in Subtractor.GetAll())
                {
                    // 4a) このSubtractorの前面デプス + HasFront = 削り区間の入口。
                    //     カメラがSubtractor内部にいるとここが空(HasFront==0)になり、
                    //     Carve側で「入口＝カメラ位置」とみなすフォールバックが効く。
                    //     これが「削れた穴の中に入っても内壁が見える」の核心部分。
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
                    //     ZTest対象にしながらテクスチャとして同時サンプルできないので必要）。
                    //
                    //     4cではcompositeWriteをZTest対象（書き込み先）にしつつ、
                    //     同じ内容をcompositeReadからテクスチャとして読む。
                    //     つまりこのコピーは無駄ではなく、4cのZTestを成立させる前提。
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

                    // 4c) このSubtractorで削る（アルゴリズム本体）
                    //
                    //     Subtractorの背面をCull Frontで描き、そのフラグメント自身の
                    //     デプスを「Subtractorの出口」として使う。
                    //     現在の可視面が [入口, 出口] の中にあれば、可視面を出口まで
                    //     押し込む＝穴の内壁が新しい可視面になる。
                    //     詳しい判定は SSBoolean_Carve.shader を参照。
                    //
                    //     ZTest GEqualなので「今より奥へ」しか動かせない。これにより
                    //     無関係なSubtractorが手前へ引き戻す事故が起きない。
                    //
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
            //
            //    ここまでで合成デプスは「ブーリアン後に見えるべき面の深度」に
            //    なっている。それをカメラのデプスバッファへ焼き込むことで、
            //    この後に走るURPの通常の不透明描画が
            //      ・SSBoolean_Lit の ZTest Equal → 一致した面だけ色が乗る
            //      ・他のシーンオブジェクト       → 通常のZTestで前後関係が決まる
            //    という形で勝手に正しい絵になる。
            //
            //    番兵値(far/near)のピクセルはシェーダ側でdiscardされ、カメラデプスは
            //    クリア値のまま残る＝そこはブーリアンに関与せず通常描画が見える。
            //    RenderPassEvent.BeforeRenderingOpaques で走るのはこのため。
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
