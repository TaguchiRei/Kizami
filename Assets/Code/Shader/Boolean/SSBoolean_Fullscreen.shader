Shader "Hidden/ScreenSpaceBoolean/Fullscreen"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

        struct FragOut { float depth : SV_Depth; };
        ENDHLSL

        // Pass 0: 合成デプス初期化
        Pass
        {
            Name "ComposeInit"
            Cull Off
            ZTest Always
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_SubtracteeFrontDepth); SAMPLER(sampler_SubtracteeFrontDepth);
            TEXTURE2D(_SubtracteeHasFront);   SAMPLER(sampler_SubtracteeHasFront);
            TEXTURE2D(_SubtracteeBackDepth);  SAMPLER(sampler_SubtracteeBackDepth);
            TEXTURE2D(_SubtracteeHasBack);    SAMPLER(sampler_SubtracteeHasBack);
            float _SSBooleanNearZ;
            float _SSBooleanFarZ;

            FragOut Frag(Varyings i)
            {
                float hasFront = SAMPLE_TEXTURE2D(_SubtracteeHasFront, sampler_SubtracteeHasFront, i.uv).r;
                float hasBack  = SAMPLE_TEXTURE2D(_SubtracteeHasBack,  sampler_SubtracteeHasBack,  i.uv).r;

                float d;
                if (hasFront > 0.5)
                {
                    // 通常ケース：前面デプスがそのまま可視サーフェス
                    d = SAMPLE_TEXTURE2D(_SubtracteeFrontDepth, sampler_SubtracteeFrontDepth, i.uv).r;
                }
                else if (hasBack > 0.5)
                {
                    // 前面が無いのに背面はある = カメラがSubtractee内部にいる
                    d = _SSBooleanNearZ;
                }
                else
                {
                    // Subtracteeが存在しない場所
                    d = _SSBooleanFarZ;
                }

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }

        // Pass 1: 合成デプスの単純コピー（ping-pong用）
        Pass
        {
            Name "CompositeCopy"
            Cull Off
            ZTest Always
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_CompositeSrcDepth); SAMPLER(sampler_CompositeSrcDepth);

            FragOut Frag(Varyings i)
            {
                FragOut o;
                o.depth = SAMPLE_TEXTURE2D(_CompositeSrcDepth, sampler_CompositeSrcDepth, i.uv).r;
                return o;
            }
            ENDHLSL
        }
    }
}