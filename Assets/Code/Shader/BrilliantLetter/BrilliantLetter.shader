Shader "Kizami/BrilliantLetter"
{
    // Brilliant Skill の文字と、その輪郭線の殻と、切ったかけらに共通で使う、単色の簡易シェーディングのシェーダー。
    // 出た直後の文字を敵や建物より手前に描く為に、深度テストの方式を _ZTest で変えられる（Always にすると手前に重なる）。
    // 面の向きで明るさを少し変え、ボクセル風の箱の立体感を出す。
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
        [Toggle] _ZWrite("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 固定の向きの光で、暗い面でも元の色の6割までに抑える
                float3 lightDirection = normalize(float3(0.4, 0.8, -0.5));
                half shade = saturate(dot(normalize(input.normalWS), lightDirection) * 0.5 + 0.5);
                return half4(_BaseColor.rgb * lerp(0.6, 1.0, shade), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
