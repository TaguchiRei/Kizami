Shader "Kizami/BarrierShard"
{
    // 割れたバリアの破片のシェーダー。両面を加算で描き、面が視線に正対するほど明るく、斜めになるほど暗くして、回る破片のきらめきを出す。
    Properties
    {
        [HDR] _Color("Color", Color) = (0.5, 1.6, 2.4, 1)
        _FaceOnMin("Brightness At Grazing Angle", Range(0, 1)) = 0.25
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FaceOnMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half faceOn = abs(dot(normalize(input.normalWS), viewDirWS));
                return half4(_Color.rgb * lerp(_FaceOnMin, 1.0h, faceOn), 1.0h);
            }
            ENDHLSL
        }
    }
}
