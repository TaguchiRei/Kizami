Shader "Hidden/ScreenSpaceEmbedded/FrontBack"
{
    // Pass 0: 前面（通常向き）のデプスを書く
    // Pass 1: 背面（裏返し）のデプスを書く（_SubtracteeBackDepth の保存用）
    // どちらも色は使わないので ColorMask 0。ハードウェアの ZWrite だけでデプスが書き込まれる。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        half4 Frag(Varyings i) : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass // 0: front
        {
            Cull Back
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass // 1: back
        {
            Cull Front
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
