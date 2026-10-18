Shader "Kizami/FinisherTelegraph"
{
    Properties
    {
        _MainTex("Warning Texture", 2D) = "white" {}
        [HDR] _Tint("Tint", Color) = (2, 0.35, 0.1, 1)
        _SignSize("SignSize", Float) = 8
        _ScrollSpeed("ScrollSpeed", Float) = 6
        _PulseSpeed("PulseSpeed", Float) = 6
        _PulseAmount("PulseAmount", Range(0, 1)) = 0.35
        _Alpha("Alpha", Range(0, 1)) = 0.3
        _EdgeSoftness("EdgeSoftness", Range(0.01, 1)) = 0.5
        _SoftDistance("SoftDistance", Float) = 3
        _BeamLength("BeamLength", Float) = 100
        _BeamRadius("BeamRadius", Float) = 16
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _SignSize;
                float _ScrollSpeed;
                float _PulseSpeed;
                float _PulseAmount;
                float _Alpha;
                float _EdgeSoftness;
                float _SoftDistance;
                float _BeamLength;
                float _BeamRadius;
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
                // 警告の標識を、_SignSize（m）の大きさで円柱の周りと射線に沿って並べ、射線に沿って流す
                float2 repeat = float2(6.2832 * _BeamRadius / _SignSize, _BeamLength / _SignSize);
                float2 uv = input.uv * repeat + float2(0, -_Time.y * _ScrollSpeed / _SignSize);
                half4 sign = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                float pulse = 1 - _PulseAmount * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
                float facing = saturate(abs(dot(normalize(input.normalWS), normalize(input.viewDirWS))));
                float alpha = _Alpha * pulse * smoothstep(0, _EdgeSoftness, facing);

                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragmentDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                alpha *= saturate((sceneDepth - fragmentDepth) / max(_SoftDistance, 0.001));

                return half4(sign.rgb * _Tint.rgb * alpha, 0);
            }
            ENDHLSL
        }
    }
}
