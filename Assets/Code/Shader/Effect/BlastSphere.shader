Shader "Kizami/BlastSphere"
{
    Properties
    {
        [HDR] _Color1("Color1", Color) = (1, 0, 0, 1)
        [HDR] _Color2("Color2", Color) = (0, 1, 0, 1)
        [HDR] _Color3("Color3", Color) = (0, 0, 1, 1)
        _Tilling("Tilling", Vector) = (1, 1, 0, 0)
        _Offset("Offset", Vector) = (0, 0, 0, 0)
        _NoiseLevel("NoiseLevel", Range(0, 1)) = 0.5
        _Intensity("Intensity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 色の帯を決めるノイズの細かさと、溶けるノイズの細かさ
            #define COLOR_NOISE_SCALE 10.0
            #define DISSOLVE_NOISE_SCALE 23.46

            CBUFFER_START(UnityPerMaterial)
                half4 _Color1;
                half4 _Color2;
                half4 _Color3;
                float4 _Tilling;
                float4 _Offset;
                float _NoiseLevel;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 GradientNoiseDirection(float2 p)
            {
                p = p % 289;
                float x = (34 * p.x + 1) * p.x % 289 + p.y;
                x = (34 * x + 1) * x % 289;
                x = frac(x / 41) * 2 - 1;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            // Shader Graph の Gradient Noise ノードと同じ。0〜1 に収める
            float GradientNoise(float2 uv, float scale)
            {
                float2 p = uv * scale;
                float2 ip = floor(p);
                float2 fp = frac(p);
                float d00 = dot(GradientNoiseDirection(ip), fp);
                float d01 = dot(GradientNoiseDirection(ip + float2(0, 1)), fp - float2(0, 1));
                float d10 = dot(GradientNoiseDirection(ip + float2(1, 0)), fp - float2(1, 0));
                float d11 = dot(GradientNoiseDirection(ip + float2(1, 1)), fp - float2(1, 1));
                fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
                return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y) + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // ノイズの高い所を Color1、中ほどを Color2、低い所を Color3 にする。溶けるノイズが _NoiseLevel を超えた所は描かない
            half4 frag(Varyings input) : SV_Target
            {
                float colorNoise = GradientNoise(input.uv * _Tilling.xy + _Offset.xy, COLOR_NOISE_SCALE);
                float dissolveNoise = GradientNoise(input.uv * _Tilling.xy + _Offset.xy + 1, DISSOLVE_NOISE_SCALE);
                clip(_NoiseLevel - dissolveNoise);

                half3 color = colorNoise > 0.66 ? _Color1.rgb : (colorNoise > 0.33 ? _Color2.rgb : _Color3.rgb);
                return half4(color * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
