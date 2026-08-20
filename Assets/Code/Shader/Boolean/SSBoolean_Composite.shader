Shader "Hidden/ScreenSpaceBoolean/CompositeSubtraction"
{
    // ========================================================================
    // 工程5。完成した合成デプスをカメラの本物のデプスバッファへ書き戻す。
    //
    // ■ なぜデプスを書くだけで絵になるのか
    //   このパスはBeforeRenderingOpaquesで走る。つまりこの直後に来る
    //   URPの通常の不透明描画が、ここで焼いたデプスを前提に動く。
    //     ・SSBoolean_Lit は ZTest Equal なので、合成デプスと一致した面だけ
    //       色が乗る（＝ブーリアン後に見えるべき面だけが描かれる）
    //     ・それ以外のシーンオブジェクトは通常のZTestで前後関係が決まる
    //   このFeatureが色を一切描かなくて済むのはこの仕組みのおかげ。
    //
    // ■ 番兵値のピクセルは書かない
    //   「ブーリアン結果としての可視面が無い」という意味なので、書き込まずに
    //   捨てる。カメラデプスはクリア値のまま残り、そこは通常のシーン描画が
    //   そのまま見える。
    // ========================================================================
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZTest LEqual // カメラデプスのクリア値より手前なら書ける
            ZWrite On    // このパスの目的はカメラデプスの書き換えそのもの
            ColorMask 0  // 色は書かない

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SSBoolean_Common.hlsl"

            TEXTURE2D(_SubtractionDepth);
            SAMPLER(sampler_SubtractionDepth);

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionHCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            struct FragOut
            {
                float depth : SV_Depth;
            };

            FragOut Frag(Varyings i)
            {
                float d = SAMPLE_TEXTURE2D(_SubtractionDepth, sampler_SubtractionDepth, i.uv).r;

                // 貫通した / そもそもSubtracteeが無い
                if (SSB_IsFarMarker(d)) discard;

                // カメラがSubtractee内部にいて、どのSubtractorにも削られなかったピクセル。
                // ここにnearZを書くと「何も描かれないのに全部を遮る壁」になってしまうため、
                // 書き込まずに背面カリングされた通常のメッシュと同じ扱いにする。
                if (SSB_IsNearMarker(d)) discard;

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }
    }
}
