Shader "ScreenSpaceBoolean/FrontBack"
{
    // Pass 0: 前面（通常向き）のデプス + HasFrontマスクを書く
    // Pass 1: 背面（裏返し）のデプス + HasBackマスクを書く
    //
    // 【変更点】カメラがメッシュ内部に入り該当面がラスタライズされない
    // ケースを検出できるよう、深度に加えて「実際に描画されたか」を示す
    // マスク(R8, 1=描画された)を同時に出力する。
    // Subtractee / Subtractor どちらの前面デプス取得にも共用する。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionHCS : SV_POSITION; };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        half4 Frag() : SV_Target { return half4(1, 0, 0, 0); }
        ENDHLSL

        Pass // 0: front + HasFront
        {
            Cull Back
            ZTest LEqual
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass // 1: back + HasBack（一番奥の背面を採用。reversed-Z前提でZTest GEqual）
        {
            Cull Front
            ZTest GEqual
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}