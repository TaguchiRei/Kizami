Shader "Custom/VertexDissolution"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _WaveCenter ("WaveCenter", Vector) = (0.5,0.5,0.5,0.5)
        _WaveScale ("WaveScale", Float) = 1
        _LowerY ("LowerY", Float) = 0
        _WaveIntensity("WaveIntensity", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _WaveCenter;
                float _WaveScale;
                float _LowerY;
                float _WaveIntensity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float dist = distance(IN.positionOS.xyz, _WaveCenter.xyz);
                float t = 1 - ((cos(dist * _WaveScale) + 1.0) * 0.5); //cosの戻り値が-1~1なので0~1にする
                float offset_y = lerp(IN.positionOS.y, _LowerY, t * _WaveIntensity);
                float4 calculated_pos = IN.positionOS;
                calculated_pos.y = offset_y;

                OUT.positionHCS = TransformObjectToHClip(calculated_pos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}