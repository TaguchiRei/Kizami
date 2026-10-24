Shader "Kizami/SwordEnhanceOutline"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 0, 0, 1)
        _Width("Width", Float) = 0.01
        _WobbleAmount("WobbleAmount", Range(0, 1)) = 0.5
        _WobbleFrequency("WobbleFrequency", Float) = 8
        _WobbleSpeed("WobbleSpeed", Float) = 3
        _NoiseScale("NoiseScale", Float) = 10
        _NoiseScroll("NoiseScroll", Vector) = (0, 1, 0, 0)
        _Threshold("Threshold", Range(0, 1)) = 0.5
        [Toggle] _Invert("Invert", Float) = 0
        _EdgeWidth("EdgeWidth", Range(0.01, 0.5)) = 0.1
        _EdgeBoost("EdgeBoost", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            // 裏面だけを膨らませて描くと、元のメッシュの外側にふちが残る
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _WobbleAmount;
                float _WobbleFrequency;
                float _WobbleSpeed;
                float _NoiseScale;
                float4 _NoiseScroll;
                float _Threshold;
                float _Invert;
                float _EdgeWidth;
                float _EdgeBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // OutlineNormalBaker が UV の 3 番目に焼いた、継ぎ目で割れない法線
                float3 smoothNormalOS : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // 0〜1 の 3D バリューノイズ
            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float x00 = lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x);
                float x10 = lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x);
                float x01 = lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x);
                float x11 = lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            // 太さをノイズで場所ごとに変えて、時間でゆらす
            Varyings vert(Attributes input)
            {
                float wobble = ValueNoise(input.positionOS.xyz * _WobbleFrequency + float3(0, 0, _Time.y * _WobbleSpeed));
                float width = _Width * lerp(1.0 - _WobbleAmount, 1.0 + _WobbleAmount, wobble);

                Varyings output;
                output.positionOS = input.positionOS.xyz;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz + normalize(input.smoothNormalOS) * width);
                return output;
            }

            // ノイズが _Threshold を超えた所だけ描く。_Invert が 1 のときは反転して、反対の所を描く
            half4 frag(Varyings input) : SV_Target
            {
                float noise = ValueNoise(input.positionOS * _NoiseScale + _NoiseScroll.xyz * _Time.y);
                float value = lerp(noise, 1.0 - noise, _Invert);
                clip(value - _Threshold);

                float edge = 1.0 - saturate((value - _Threshold) / _EdgeWidth);
                return half4(_Color.rgb * (1.0 + edge * _EdgeBoost), 1);
            }
            ENDHLSL
        }
    }
}
