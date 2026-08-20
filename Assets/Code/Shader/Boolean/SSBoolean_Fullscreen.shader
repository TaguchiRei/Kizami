Shader "Hidden/ScreenSpaceBoolean/Fullscreen"
{
    // ============================================================================
    // 合成デプスを扱うフルスクリーンパス2種。どちらも色は書かずデプスだけを作る。
    //   Pass 0 (ComposeInit)   … 工程3。削る前の可視面で合成デプスを初期化する
    //   Pass 1 (CompositeCopy) … 工程4b。合成デプスを複製してread/writeを分ける
    // 頂点はDrawProceduralの3頂点フルスクリーン三角形（メッシュ不要）。
    // ============================================================================
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SSBoolean_Common.hlsl"

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uv          : TEXCOORD0;
        };

        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings o;
            o.positionHCS = GetFullScreenTriangleVertexPosition(vertexID);
            o.uv = GetFullScreenTriangleTexCoord(vertexID);
            return o;
        }

        struct FragOut { float depth : SV_Depth; };
        ENDHLSL

        // ------------------------------------------------------------------------
        // Pass 0: 合成デプス初期化
        //
        // Subtracteeの前面デプスとマスクから「削る前の可視面」を1枚に組み立てる。
        // ここで決めた値が、以降のCarveパスで削られていく出発点になる。
        //
        // マスクを見て3通りに分岐するのがこのパスの全て。特に2番目の分岐が
        // 「カメラがSubtracteeの中に入っている」ケースの入口になっている。
        // ------------------------------------------------------------------------
        Pass
        {
            Name "ComposeInit"
            Cull Off
            ZTest Always // 全ピクセルを無条件で初期化する
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_SubtracteeFrontDepth); SAMPLER(sampler_SubtracteeFrontDepth);
            TEXTURE2D(_SubtracteeHasFront);   SAMPLER(sampler_SubtracteeHasFront);
            TEXTURE2D(_SubtracteeHasBack);    SAMPLER(sampler_SubtracteeHasBack);

            FragOut Frag(Varyings i)
            {
                float hasFront = SAMPLE_TEXTURE2D(_SubtracteeHasFront, sampler_SubtracteeHasFront, i.uv).r;
                float hasBack  = SAMPLE_TEXTURE2D(_SubtracteeHasBack,  sampler_SubtracteeHasBack,  i.uv).r;

                float d;
                if (hasFront > 0.5)
                {
                    // 通常ケース：前面デプスがそのまま可視サーフェス
                    d = SAMPLE_TEXTURE2D(_SubtracteeFrontDepth, sampler_SubtracteeFrontDepth, i.uv).r;
                }
                else if (hasBack > 0.5)
                {
                    // 前面が無いのに背面はある = カメラがSubtractee内部にいる。
                    // カメラ位置に可視サーフェスがあるものとして扱うことで、
                    // Carveパスが「削れた空間の内側にいる」状態を正しく判定できる。
                    // この値は番兵で、最終的なカメラデプスには書き込まれない。
                    d = SSB_NEAR_Z;
                }
                else
                {
                    // Subtracteeが存在しない場所
                    d = SSB_FAR_Z;
                }

                FragOut o;
                o.depth = d;
                return o;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------
        // Pass 1: 合成デプスの単純コピー（ping-pong用）
        //
        // GPUは同じデプスバッファをZTest対象にしながらテクスチャとして同時に
        // サンプルできない。そこでCarveの直前に中身を別RTへ複製し、
        // 「読む側」と「ZTestして書く側」を物理的に分ける。
        // ------------------------------------------------------------------------
        Pass
        {
            Name "CompositeCopy"
            Cull Off
            ZTest Always
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_CompositeSrcDepth); SAMPLER(sampler_CompositeSrcDepth);

            FragOut Frag(Varyings i)
            {
                FragOut o;
                o.depth = SAMPLE_TEXTURE2D(_CompositeSrcDepth, sampler_CompositeSrcDepth, i.uv).r;
                return o;
            }
            ENDHLSL
        }
    }
}
