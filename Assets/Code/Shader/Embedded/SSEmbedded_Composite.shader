Shader "Hidden/ScreenSpaceEmbedded/CompositeSubtraction"
{
    // フルスクリーン三角形を1枚描いて、Execute() の2)で作った
    // _SubtractionDepth をカメラの本物のデプスバッファへ書き写す。
    // 何も削られていない（=1.0のまま）ピクセルは discard して既存のデプスを残す。
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
                if (d >= 1.0) discard;

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }
    }
}
