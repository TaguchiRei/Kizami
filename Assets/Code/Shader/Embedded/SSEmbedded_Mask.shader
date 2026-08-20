Shader "Hidden/ScreenSpaceEmbedded/Mask"
{
    // Pass 0: 前面をステンシルにマーク（Ref 1）
    // Pass 1: マークされた領域だけ、裏面を逆ZTestで描いて「えぐる」
    // Pass 2: えぐった深さを _SubtracteeBackDepth と比較し、
    //         Subtractee の裏側より奥まで削れていたら完全に貫通させる
    // Pass 3: ステンシルのマークをクリア（Ref 0）
    //
    // 注意（reversed-Z）: プラットフォームによって depth の 0/1 の向きが逆になります
    // (D3D/Metal/Vulkan は reversed-Z、OpenGL は non-reversed が一般的)。
    // 実機・実環境でエフェクトが反転しているように見えたら、
    // 各 Pass の ZTest (LEqual<->GEqual) と Pass2 の比較演算子 (<=) を反転させてください。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_SubtracteeBackDepth);
        SAMPLER(sampler_SubtracteeBackDepth);

        struct Attributes
        {
            float4 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float4 screenPos   : TEXCOORD0;
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
            o.screenPos = ComputeScreenPos(o.positionHCS);
            return o;
        }

        half4 FragSimple(Varyings i) : SV_Target
        {
            return 0;
        }

        struct FragOut
        {
            half4 color : SV_Target;
            float depth : SV_Depth;
        };
        ENDHLSL

        Pass // 0: stencil mark
        {
            Cull Back
            ZTest LEqual
            ZWrite Off
            ColorMask 0
            Stencil { Ref 1  Comp Always  Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSimple
            ENDHLSL
        }

        Pass // 1: carve (reverse ZTest on back faces, marked area only)
        {
            Cull Front
            ZTest GEqual
            ZWrite On
            ColorMask 0
            Stencil { Ref 1  Comp Equal }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSimple
            ENDHLSL
        }

        Pass // 2: punch-through check
        {
            Cull Front
            ZTest GEqual
            ZWrite On
            Stencil { Ref 1  Comp Equal }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            FragOut Frag(Varyings i)
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float subtracteeBack  = SAMPLE_TEXTURE2D(_SubtracteeBackDepth, sampler_SubtracteeBackDepth, uv).r;
                float subtractorDepth = i.positionHCS.z / i.positionHCS.w;

                // Subtractee の裏側より手前にしか削れていない場合は何もしない
                if (subtractorDepth <= subtracteeBack) discard;

                FragOut o;
                o.color = 1;
                o.depth = 1.0; // 完全に貫通させる = 一番奥の値にする
                return o;
            }
            ENDHLSL
        }

        Pass // 3: clear stencil
        {
            Cull Back
            ZTest Always
            ZWrite Off
            ColorMask 0
            Stencil { Ref 0  Comp Always  Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSimple
            ENDHLSL
        }
    }
}
