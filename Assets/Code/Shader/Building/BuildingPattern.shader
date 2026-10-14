Shader "Kizami/BuildingPattern"
{
    // 大きな建物の階を、テクスチャと UV を使わずにオブジェクト空間の座標から塗るシェーダー。
    // メッシュのままの階とボクセルの階で同じ模様になり、落ちる建物でも模様が表面を滑らない。
    // ボクセルの格子はオブジェクト空間の原点にそろうので、ブロックの寸法をボクセルの大きさの約数にし、削れて浮き出た段差と描いた目地の位置を合わせる。
    // 建物全体を 1 つの続いた模様にする為、階ごとに _PatternOffset の Y へ「階の番号 × 階の高さ」を書く。
    // _JointWidth が 0 の素材は、ブロックを描かずにノイズの色むらだけで塗る。
    Properties
    {
        [MainColor] _BaseColor("Block Color", Color) = (0.72, 0.42, 0.32, 1)
        _JointColor("Joint Color", Color) = (0.78, 0.76, 0.72, 1)
        _BlockSize("Block Size (width, height)", Vector) = (0.4, 0.2, 0, 0)
        _JointWidth("Joint Width", Float) = 0.05
        _NoiseScale("Noise Scale", Float) = 1.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.25
        _PatternOffset("Pattern Offset", Vector) = (0, 0, 0, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // SRP Batcher の対象にする為、全パスで同じ CBUFFER を使う
    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _JointColor;
        float4 _BlockSize;
        float _JointWidth;
        float _NoiseScale;
        half _NoiseStrength;
        float4 _PatternOffset;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 patternPosition : TEXCOORD0;
                float3 normalOS    : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // 格子点の乱数を三重線形補間した 0〜1 の値ノイズ
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), u.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), u.x), u.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), u.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), u.x), u.y),
                    u.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;

                output.patternPosition = input.positionOS.xyz + _PatternOffset.xyz;
                output.normalOS = input.normalOS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.patternPosition;
                float3 n = abs(input.normalOS);

                // 法線の向きが最も強い軸に垂直な平面へ投影する
                float2 uv = n.y >= n.x && n.y >= n.z ? p.xz : (n.x >= n.z ? p.zy : p.xy);

                // 段ごとにブロックを半分ずらす
                float row = floor(uv.y / _BlockSize.y);
                uv.x += frac(row * 0.5) * _BlockSize.x;
                float2 cell = uv / _BlockSize.xy;
                float2 local = frac(cell);
                float3 blockId = float3(floor(cell), row);

                float2 jointHalf = _JointWidth / _BlockSize.xy * 0.5;
                float2 pixel = fwidth(cell);
                float2 inner = smoothstep(jointHalf - pixel, jointHalf + pixel, local)
                    * smoothstep(jointHalf - pixel, jointHalf + pixel, 1.0 - local);
                half hasBlocks = step(1e-5, _JointWidth);
                half block = lerp(1.0, inner.x * inner.y, hasBlocks);

                half variation = (Hash(blockId) - 0.5) * 0.3 * hasBlocks
                    + (ValueNoise(p * _NoiseScale) + 0.5 * ValueNoise(p * _NoiseScale * 2.0) - 0.75) * _NoiseStrength;
                half3 blockColor = _BaseColor.rgb * (1.0 + variation);
                half3 albedo = lerp(_JointColor.rgb, blockColor, block);

                // 1 ピクセルにブロックが収まらない遠さでは、ちらつかないよう目地とブロックの平均の色へ寄せる
                half jointRatio = 1.0 - (1.0 - 2.0 * jointHalf.x) * (1.0 - 2.0 * jointHalf.y);
                half3 average = lerp(_BaseColor.rgb, _JointColor.rgb, jointRatio);
                half fade = saturate((max(pixel.x, pixel.y) - 0.15) / 0.35);
                albedo = lerp(albedo, average, fade);

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 diffuse = mainLight.color * saturate(dot(normalWS, mainLight.direction))
                    * mainLight.shadowAttenuation;
                return half4(albedo * (diffuse + SampleSH(normalWS)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
