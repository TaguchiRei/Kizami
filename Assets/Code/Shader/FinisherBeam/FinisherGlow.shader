Shader "Kizami/FinisherGlow"
{
    Properties
    {
        [HDR] _Tint("Tint", Color) = (0.4, 1, 0.9, 1)
        _Intensity("Intensity", Float) = 3
        _Falloff("Falloff", Range(0.5, 8)) = 3
        _RingRadius("RingRadius", Range(0, 1)) = 0.7
        _RingWidth("RingWidth", Range(0.01, 0.5)) = 0.08
        _RingIntensity("RingIntensity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Intensity;
                float _Falloff;
                float _RingRadius;
                float _RingWidth;
                float _RingIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // 中心ほど明るい光と、一定の半径のリングを足す。頂点カラーを掛けるので、パーティクルの色と透明度でも調整できる
            half4 frag(Varyings input) : SV_Target
            {
                float r = length(input.uv - 0.5) * 2;
                float glow = pow(saturate(1 - r), _Falloff);
                float ring = exp(-pow((r - _RingRadius) / _RingWidth, 2)) * saturate(1 - r * r);
                half3 color = _Tint.rgb * (glow * _Intensity + ring * _RingIntensity) * input.color.rgb * input.color.a;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
