Shader "Kizami/AbyssFog"
{
    // 奈落に張る水平な板に使い、板より下の物をフォグに沈めて底なしに見せるシェーダー。
    // 板の面から奥の物までの距離を深度テクスチャから求め、距離が長いほど不透明にする。奥に何もない所は完全にフォグの色になる。
    // 板の面にも _SurfaceAlpha の分だけフォグの色を乗せ、崖の下の端がくっきり切れて見えないようにする。
    // 深度テクスチャのない環境（スマホの URP 設定）では _ABYSS_DEPTH_OFF を有効にし、板を不透明なフォグの色で塗る。
    Properties
    {
        [MainColor] _FogColor("Fog Color", Color) = (0.08, 0.09, 0.12, 1)
        _DeepColor("Deep Color", Color) = (0.02, 0.02, 0.04, 1)
        _Density("Density (per meter)", Range(0.001, 0.5)) = 0.06
        _SurfaceAlpha("Surface Alpha", Range(0, 1)) = 0.6
        _NoiseScale("Noise Scale", Float) = 0.02
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.3
        _NoiseSpeed("Noise Speed", Float) = 3
        [Toggle(_ABYSS_DEPTH_OFF)] _DepthOff("Depth Texture Off", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ABYSS_DEPTH_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // SRP Batcher の対象にする為、プロパティをすべて CBUFFER に入れる
            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                half4 _DeepColor;
                float _Density;
                half _SurfaceAlpha;
                float _NoiseScale;
                half _NoiseStrength;
                float _NoiseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            // 格子点の乱数を双線形補間した 0〜1 の値ノイズ
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionHCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 板の上をゆっくり流れる 2 つのノイズで、フォグの濃さと色にむらを付ける
                float2 drift = _Time.y * _NoiseSpeed * float2(1.0, 0.6);
                float noise = ValueNoise((input.positionWS.xz + drift) * _NoiseScale) * 0.6
                            + ValueNoise((input.positionWS.xz - drift) * _NoiseScale * 2.3) * 0.4;
                half variation = lerp(1.0h, noise * 2.0h, _NoiseStrength);

            #if defined(_ABYSS_DEPTH_OFF)
                return half4(_FogColor.rgb * variation, 1.0h);
            #else
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEyeDepth = input.screenPos.w;
                float thickness = max(0.0, sceneEyeDepth - surfaceEyeDepth);

                half fog = 1.0h - exp(-thickness * _Density * variation);
                half3 color = lerp(_FogColor.rgb, _DeepColor.rgb, fog) * variation;
                half alpha = saturate(_SurfaceAlpha + (1.0h - _SurfaceAlpha) * fog);
                return half4(color, alpha);
            #endif
            }
            ENDHLSL
        }
    }
}
