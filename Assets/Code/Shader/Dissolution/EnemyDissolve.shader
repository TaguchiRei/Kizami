Shader "Kizami/EnemyDissolve"
{
    // 敵の体から外れた部位（見た目用の物）を、ノイズの模様で消していくシェーダー。
    // 消えた割合 _DissolveAmount（0〜1）は、EnemyDebris が MaterialPropertyBlock で毎フレーム渡す。
    // ノイズはオブジェクト空間の位置から作るので、UV の無いメッシュでも模様が出る。
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _DissolveAmount("Dissolve Amount", Range(0, 1)) = 0
        _NoiseScale("Noise Scale", Float) = 4
        [HDR] _EdgeColor("Edge Color", Color) = (2, 0.8, 0.2, 1)
        _EdgeWidth("Edge Width", Range(0, 0.3)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            // 切り抜いた穴から裏面が見えるよう、両面を描く
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _DissolveAmount;
                float _NoiseScale;
                half4 _EdgeColor;
                half _EdgeWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
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
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float noise = ValueNoise(input.positionOS * _NoiseScale);

                // _DissolveAmount が 0 のときは全面を残し、1 のときは全面を消す
                float threshold = _DissolveAmount * (1.0 + _EdgeWidth);
                clip(noise - threshold);

                float3 normalWS = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                Light mainLight = GetMainLight();
                half3 diffuse = mainLight.color * saturate(dot(normalWS, mainLight.direction));
                half3 color = _BaseColor.rgb * (diffuse + SampleSH(normalWS));

                // 消える境界の近くを縁の色で光らせる
                half edge = step(noise, threshold + _EdgeWidth) * step(0.001, _DissolveAmount);
                color = lerp(color, _EdgeColor.rgb, edge);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
