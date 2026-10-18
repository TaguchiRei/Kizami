Shader "Kizami/BarrierDome"
{
    // 街の上を覆うエネルギーバリアのドームのシェーダー。半球のメッシュの裏面を、内側から見る前提で加算で描く。
    // 模様はオブジェクト空間の向き（方位と仰角）から作り、UV を使わない。方位の 1 周に六角形がちょうど _HexCount 個並ぶようにし、継ぎ目を出さない。
    // 明るさは、六角形の格子の線・視線と面が斜めになるほど明るいフレネル・下から上へ流れる光の帯の和。
    // 地平線の近くは _HorizonFade の仰角まで薄くし、遠景のビルと外壁が透けて見えるようにする。
    Properties
    {
        [HDR] _Color("Color", Color) = (0.3, 0.7, 1.4, 1)
        _HexCount("Hex Count (around)", Float) = 96
        _LineWidth("Line Width", Range(0.01, 0.3)) = 0.06
        _LineIntensity("Line Intensity", Range(0, 2)) = 0.35
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 0.25
        _BandIntensity("Band Intensity", Range(0, 2)) = 0.4
        _BandFrequency("Band Frequency", Float) = 4
        _BandSpeed("Band Speed", Float) = 0.05
        _BandSharpness("Band Sharpness", Range(1, 64)) = 16
        _HorizonFade("Horizon Fade (radian)", Range(0.01, 1)) = 0.35
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
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher の対象にする為、プロパティをすべて CBUFFER に入れる
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _HexCount;
                half _LineWidth;
                half _LineIntensity;
                half _FresnelPower;
                half _FresnelIntensity;
                half _BandIntensity;
                float _BandFrequency;
                float _BandSpeed;
                half _BandSharpness;
                half _HorizonFade;
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
                float elevation = asin(saturate(direction.y));
                float azimuth = atan2(direction.z, direction.x);

                // 方位の 1 周が _HexCount 個の六角形の幅になる座標
                float cells = _HexCount / TWO_PI;
                float2 hexUV = float2(azimuth, elevation) * cells;
                half hexLine = smoothstep(0.5h - _LineWidth, 0.5h, HexDistance(hexUV));

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0h - saturate(abs(dot(normalize(input.normalWS), viewDirWS))), _FresnelPower);

                half band = pow(0.5h + 0.5h * sin((elevation * _BandFrequency - _Time.y * _BandSpeed) * TWO_PI), _BandSharpness);

                half horizon = smoothstep(0.0h, _HorizonFade, elevation);
                half intensity = (hexLine * _LineIntensity + fresnel * _FresnelIntensity + band * _BandIntensity) * horizon;
                return half4(_Color.rgb * intensity, 1.0h);
            }
            ENDHLSL
        }
    }
}
