Shader "Hidden/ScreenSpaceBoolean/CompositeSubtraction"
{
    // 合成デプスをカメラの本物のデプスバッファへ書き戻す。
    // 番兵値のピクセルは「ブーリアン結果としての可視サーフェスが無い」という意味なので
    // 書き込まずに捨て、通常のシーン描画にそのまま任せる。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZTest LEqual
            ZWrite On
            ColorMask 0

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
