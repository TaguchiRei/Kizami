Shader "Hidden/ScreenSpaceBoolean/CompositeSubtraction"
{
    // 工程5。完成した合成デプスをカメラの本物のデプスバッファへ書き戻す。
    //
    // このパスはBeforeRenderingOpaquesで走り、直後のURPの通常の不透明描画がここで焼いたデプスを前提に動く。
    //   ・SSBoolean_Lit は ZTest Equal なので、合成デプスと一致した面だけ
    //     色が乗る（＝ブーリアン後に見えるべき面だけが描かれる）
    //   ・それ以外のシーンオブジェクトは通常のZTestで前後関係が決まる
    //
    // 番兵値のピクセルは discard してカメラデプスをクリア値のまま残し、そこは通常のシーン描画が見える。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZTest LEqual // カメラデプスのクリア値より手前なら書ける
            ZWrite On    // このパスの目的はカメラデプスの書き換えそのもの
            ColorMask 0  // デプスだけを書く

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

                // farZ番兵: 貫通した / Subtracteeの外
                if (SSB_IsFarMarker(d)) discard;

                // nearZ番兵: カメラがSubtractee内部にいて、削り込みの後も番兵のまま残ったピクセル。
                // nearZを書くと画面全体を遮る見えない壁になるので、背面カリングされた通常のメッシュと同じく捨てる。
                if (SSB_IsNearMarker(d)) discard;

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }
    }
}
