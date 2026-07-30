Shader "Hidden/ScreenSpaceBoolean/CompositeSubtraction"
{
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

            // 【追加】Mask.shaderと同じ「未処理/貫通」マーカー値
            float _SSBooleanFarZ;

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

                // 【修正】ハードコードされた 1.0 ではなく、実際に使っているマーカー値と比較
                if (abs(d - _SSBooleanFarZ) < 1e-6) discard;

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }
    }
}