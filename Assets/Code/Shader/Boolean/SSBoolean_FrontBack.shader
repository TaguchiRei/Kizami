Shader "ScreenSpaceBoolean/FrontBack"
{
    // デプス取得用。デプス＋1chマスクだけを出力する。
    //   Pass 0: 前面（通常向き）のデプス + HasFrontマスク
    //   Pass 1: 背面（裏返し）のデプス   + HasBackマスク
    //
    // 使うPass
    //   Subtractee（削られる側）… Pass 0 と Pass 1 の両方
    //                              前面＝削る前の可視面 / 背面＝貫通判定の出口
    //   Subtractor（削る側）  … Pass 0 のみ
    //                              前面＝削り区間の入口。
    //                              出口はCarveパスがラスタライズしながら求める
    //
    // マスク
    //   実際に描かれたピクセルへ 1 を書くマスク(R8)を同時に出力し、
    //   カメラがメッシュの内部にいて面が近クリップ面で切られた状態の判定に使う。
    //
    // ZTestとクリア値はペアで意味を決める
    //   LEqual（手前が勝つ）→ RTは遠クリップでクリア
    //   GEqual（奥が勝つ）  → RTは近クリップでクリア
    //   クリア値の指定は ScreenSpaceBooleanFeature 側にあるので、
    //   ここのZTestを変えるなら向こうのClearDepth～も必ず合わせること。
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
            // Carve / Lit と同じ式でクリップ座標を求める。
            // 後段の ZTest Equal はクリップ座標のビット単位の一致を前提にしている
            o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        // 「このピクセルに面が描かれた」という事実だけを1で記録する。
        // デプス自体はZWriteでハードウェアが書く
        half4 Frag() : SV_Target { return half4(1, 0, 0, 0); }
        ENDHLSL

        Pass // 0: front + HasFront（一番手前の前面を採用するのでLEqual。RTはfar=1でクリア）
        {
            Cull Back    // 前面だけ
            ZTest LEqual // 手前が勝つ = 最初に当たる面
            ZWrite On
            ColorMask R  // Rチャンネルのマスクだけ書く

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // ZTestは「意味」で指定する（LEqual=手前が通る / GEqual=奥が通る）。
        // reversed-Zプラットフォームでの比較の反転はUnityが内部で行う。
        // 対応するRTのクリア値も同じ表現（1=遠クリップ）で指定すること。
        //
        // 一番「奥」の背面を採るので、複数のSubtracteeが重なるとその一番奥が
        // 選ばれて互いに干渉する。手前の背面(LEqual)は干渉が減る代わりに、
        // 貫通しすぎて本来物体があるはずの場所に背景が抜ける。
        Pass // 1: back + HasBack（一番奥の背面を採用するのでGEqual。RTはnear=0でクリア）
        {
            Cull Front   // 背面だけ
            ZTest GEqual // 奥が勝つ = 最後に抜ける面
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}