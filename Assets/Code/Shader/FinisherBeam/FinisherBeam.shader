Shader "Kizami/FinisherBeam"
{
    Properties
    {
        [HDR] _Color1("Color1", Color) = (1, 0, 0, 1)
        [HDR] _Color2("Color2", Color) = (0, 1, 0.042, 1)
        [HDR] _Color3("Color3", Color) = (0, 1, 0.991, 1)
        _Emission("Emission", Float) = 10
        _NoiseLevel("NoiseLevel", Range(0, 1)) = 0.295
        _Tilling("Tilling (xy)", Vector) = (1, -6.9, 0, 0)
        _Offset("Offset (xy)", Vector) = (0, 0, 0, 0)
        _ScrollSpeed("ScrollSpeed", Float) = 0.3
        _BeamLength("BeamLength", Float) = 1489.6
        _CoreAmount("CoreAmount", Range(0, 1)) = 0.6
        _CoreSharpness("CoreSharpness", Range(0.5, 8)) = 3
        _EdgeSoftness("EdgeSoftness", Range(0.01, 1)) = 0.35
        _SoftDistance("SoftDistance", Float) = 3
        [Toggle] _ZWrite("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color1;
                half4 _Color2;
                half4 _Color3;
                float _Emission;
                float _NoiseLevel;
                float4 _Tilling;
                float4 _Offset;
                float _ScrollSpeed;
                float _BeamLength;
                float _CoreAmount;
                float _CoreSharpness;
                float _EdgeSoftness;
                float _SoftDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            float2 GradientDir(float2 p)
            {
                p = p % 289;
                float x = (34 * p.x + 1) * p.x % 289 + p.y;
                x = (34 * x + 1) * x % 289;
                x = frac(x / 41) * 2 - 1;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            // Shader Graph の Gradient Noise ノードと同じ値を返す
            float GradientNoise(float2 uv, float scale)
            {
                float2 p = uv * scale;
                float2 ip = floor(p);
                float2 fp = frac(p);
                float d00 = dot(GradientDir(ip), fp);
                float d01 = dot(GradientDir(ip + float2(0, 1)), fp - float2(0, 1));
                float d10 = dot(GradientDir(ip + float2(1, 0)), fp - float2(1, 0));
                float d11 = dot(GradientDir(ip + float2(1, 1)), fp - float2(1, 1));
                fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
                return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y) + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 tilling = float2(_Tilling.x, _Tilling.y * _BeamLength / 1489.6);
                float2 scroll = float2(0, _Time.y * _ScrollSpeed);

                // 色の帯と透明度は、旧作の Beam.shadergraph の 2 枚のノイズと同じ
                float noiseColor = GradientNoise(input.uv * tilling + _Offset.xy + scroll, 10);
                float noiseAlpha = GradientNoise(input.uv * tilling + _Offset.xy + float2(1, 1) + scroll, 23.46);
                half3 color = lerp(lerp(_Color1.rgb, _Color2.rgb, step(noiseColor, 0.66)), _Color3.rgb, step(noiseColor, 0.33));
                float alpha = step(noiseAlpha, _NoiseLevel);

                // 円柱の中心ほど白く焼けた芯にし、縁を柔らかく消す
                float facing = saturate(abs(dot(normalize(input.normalWS), normalize(input.viewDirWS))));
                color = lerp(color, half3(1, 1, 1), pow(facing, _CoreSharpness) * _CoreAmount);
                alpha *= smoothstep(0, _EdgeSoftness, facing);

                // 地面や建物と交わる所を柔らかく消す
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragmentDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                alpha *= saturate((sceneDepth - fragmentDepth) / max(_SoftDistance, 0.001));

                return half4(color * _Emission * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
