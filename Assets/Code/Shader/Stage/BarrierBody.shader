Shader "Kizami/BarrierBody"
{
    // ディフェンダーが張る球のバリア本体のシェーダー。球の外側の面を加算で描き、面は薄く、六角形の格子の線と縁のフレネルを明るく見せる。
    // 模様はオブジェクト空間の方位と仰角から作り、UV を使わない。方位の 1 周に六角形がちょうど _HexCount 個並び、継ぎ目を出さない。
    // 色は _BaseColor の rgb で、アルファは全体の強さ。ArmorPanel が耐久値の割合に応じて _BaseColor を MaterialPropertyBlock で変える。
    Properties
    {
        [HDR] _BaseColor("Color", Color) = (0.4, 0.75, 1, 0.25)
        _Gain("Gain", Range(0, 10)) = 4
        _HexCount("Hex Count (around)", Float) = 64
        _LineWidth("Line Width", Range(0.01, 0.3)) = 0.05
        _LineIntensity("Line Intensity", Range(0, 2)) = 0.8
        _FaceIntensity("Face Intensity", Range(0, 1)) = 0.12
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Gain;
                float _HexCount;
                half _LineWidth;
                half _LineIntensity;
                half _FaceIntensity;
                half _FresnelPower;
                half _FresnelIntensity;
            CBUFFER_END

            // 六角形の格子の横の周期と縦の周期。隣の列は半周期ずれる
            static const float2 HEX_PERIOD = float2(1.0, 1.7320508);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            float2 Repeat(float2 p, float2 period)
            {
                return p - period * floor(p / period);
            }

            // 六角形の中心からの距離。中心で 0、辺で 0.5
            float HexDistance(float2 p)
            {
                float2 a = Repeat(p, HEX_PERIOD) - HEX_PERIOD * 0.5;
                float2 b = Repeat(p - HEX_PERIOD * 0.5, HEX_PERIOD) - HEX_PERIOD * 0.5;
                float2 local = abs(dot(a, a) < dot(b, b) ? a : b);
                return max(dot(local, normalize(HEX_PERIOD)), local.x);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.directionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float elevation = asin(clamp(direction.y, -1.0, 1.0));
                float azimuth = atan2(direction.z, direction.x);

                float cells = _HexCount / TWO_PI;
                half hexLine = smoothstep(0.5h - _LineWidth, 0.5h, HexDistance(float2(azimuth, elevation) * cells));

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0h - saturate(abs(dot(normalize(input.normalWS), viewDirWS))), _FresnelPower);

                half intensity = _FaceIntensity + hexLine * _LineIntensity + fresnel * _FresnelIntensity;
                return half4(_BaseColor.rgb * (intensity * _BaseColor.a * _Gain), 1.0h);
            }
            ENDHLSL
        }
    }
}
