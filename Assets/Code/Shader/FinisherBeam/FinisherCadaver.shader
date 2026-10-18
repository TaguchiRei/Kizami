Shader "Kizami/FinisherCadaver"
{
    // フィニッシャーが吸収した敵の死骸。旧作の Dissolve.shadergraph と同じく、
    // _NoiseLevel が大きいほどノイズの模様に沿って現れ、現れる縁が _DissolveEmissionColor で光る。
    // _BaseColor と _NoiseLevel は、アニメーションが書く。
    Properties
    {
        _BaseColor("Base Color", Color) = (0.33, 0.33, 0.33, 1)
        _NoiseLevel("Noise Level", Float) = 1.1
        _NoiseStrength("Noise Scale", Float) = 5
        [HDR] _DissolveEmissionColor("Dissolve Emission Color", Color) = (1, 0.096, 0, 1)
        _EmissionStrength("Emission Strength", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _NoiseLevel;
                float _NoiseStrength;
                half4 _DissolveEmissionColor;
                float _EmissionStrength;
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float noise = GradientNoise(input.uv, _NoiseStrength);

                // 現れるのは _NoiseLevel + 0.1 以上の所。_NoiseLevel を越えた縁だけが光る
                clip(step(noise, _NoiseLevel + 0.1) - 0.5);
                float edge = 1 - step(noise, _NoiseLevel);

                Light mainLight = GetMainLight();
                float3 normal = normalize(input.normalWS);
                half3 lighting = mainLight.color * saturate(abs(dot(normal, mainLight.direction))) + SampleSH(normal);
                half3 color = _BaseColor.rgb * lighting + _DissolveEmissionColor.rgb * _EmissionStrength * edge;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
