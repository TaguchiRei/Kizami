Shader "Kizami/Glass"
{
    // 窓のガラスと、その破片に共通で使う半透明のシェーダー。
    // 正面から見ると透け、斜めから見るほどフレネルで不透明になり、反射プローブ（なければ空）を映す。主光源の鏡面反射を足す。
    // 透過は背景を歪めずにアルファで混ぜるだけにする。屈折は画面のコピーを要し、窓の数だけ重くなる為。
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (0.55, 0.7, 0.78, 0.18)
        _EdgeAlpha("Edge Alpha", Range(0, 1)) = 0.85
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4
        _Reflection("Reflection Strength", Range(0, 1)) = 0.8
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher の対象にする為、プロパティをすべて CBUFFER に入れる
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeAlpha;
                half _FresnelPower;
                half _Reflection;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _FresnelPower);

                half perceptualRoughness = 1.0h - _Smoothness;
                float3 reflectDir = reflect(-viewDirWS, normalWS);
                half3 environment = GlossyEnvironmentReflection(reflectDir, input.positionWS, perceptualRoughness, 1.0h);

                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half specularPower = exp2(10.0h * _Smoothness + 1.0h);
                half3 specular = mainLight.color * pow(saturate(dot(normalWS, halfDir)), specularPower);

                half reflection = lerp(0.1h, 1.0h, fresnel) * _Reflection;
                half3 color = lerp(_BaseColor.rgb, environment, reflection) + specular;
                half alpha = saturate(lerp(_BaseColor.a, _EdgeAlpha, fresnel) + Luminance(specular));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
