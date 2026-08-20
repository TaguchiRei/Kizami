Shader "Hidden/ScreenSpaceBoolean/Carve"
{
    // ============================================================================
    // 削り込み本体。ScreenSpaceBooleanFeature の工程4c から呼ばれる。
    // ----------------------------------------------------------------------------
    // ■ 立ち位置
    //   Subtractor(A)の「背面」を1枚描くパス。色は書かず、合成デプスだけを更新する。
    //   A の背面を描いているそのフラグメントが、視線に沿って見たときの
    //   「A の出口」そのものになっている。だから A の背面デプスをRTに保存する
    //   工程は存在しない（入口だけ _SubtractorFrontDepth に保存してある）。
    //
    // ■ 1ピクセルで見ている4つの深度
    //     currentSurface … 今の可視面（＝1つ前の合成デプス）
    //     srFront        … A の入口（_SubtractorFrontDepth）
    //     srBack         … A の出口（このフラグメント自身）
    //     seBack         … B の出口（_SubtracteeBackDepth）
    //
    //     カメラ →--- currentSurface --- srFront ~~~~ srBack --- seBack --→
    //                                    └── この区間が削られる ──┘
    //
    // ■ やること
    //     currentSurface が [srFront, srBack] の中  → 可視面を srBack へ押し込む
    //     さらに srBack が seBack より奥            → 貫通。farZ番兵を書く
    //     区間の外                                  → discard（無関係）
    //
    // ■ カメラが A の内部に入った場合（このシェーダの肝）
    //   A の前面は近クリップ面で切られてラスタライズされず、_SubtractorHasFront が
    //   0 のままになる。深度だけ見ても「入口が奥に無い」のか「カメラの後ろにあって
    //   描かれなかった」のか区別できないので、マスクで判定して
    //   「入口＝カメラ位置(nearZ)」とみなす。
    //   こうすると区間が [カメラ, srBack] になり、カメラ自身から出口までが削られる。
    //   ＝削れた穴の中に入っても、穴の内壁が可視面として残る。
    //
    //   カメラが B の内部に入っている場合も同様の対処が要るが、そちらは
    //   工程3（SSBoolean_Fullscreen の ComposeInit）で currentSurface に
    //   nearZ番兵を入れることで処理済み。ここではその番兵をそのまま比較に使える。
    // ============================================================================
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Front  // Aの背面＝出口だけを描く
            ZTest GEqual // 「今より奥へ」しか動かさない。手前へ引き戻す事故を防ぐ
            ZWrite On   // 更新後の可視面デプスを書き込む
            ColorMask 0 // 色は一切書かない。このパスの成果物はデプスだけ

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SSBoolean_Common.hlsl"

            TEXTURE2D(_CompositeSrcDepth);     SAMPLER(sampler_CompositeSrcDepth);
            TEXTURE2D(_SubtractorFrontDepth);  SAMPLER(sampler_SubtractorFrontDepth);
            TEXTURE2D(_SubtractorHasFront);    SAMPLER(sampler_SubtractorHasFront);
            TEXTURE2D(_SubtracteeBackDepth);   SAMPLER(sampler_SubtracteeBackDepth);
            TEXTURE2D(_SubtracteeHasFront);    SAMPLER(sampler_SubtracteeHasFront);
            TEXTURE2D(_SubtracteeHasBack);     SAMPLER(sampler_SubtracteeHasBack);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos   : TEXCOORD0; // 中間RTを引くためのスクリーンUV
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                // デプスを書く他のシェーダ（FrontBack / Lit）と同じ式を使う。
                // ここがずれると後段の ZTest Equal が一致しなくなる
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionHCS);
                return o;
            }

            struct FragOut { float depth : SV_Depth; };

            FragOut Frag(Varyings i)
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;

                // --- そもそも削る相手がこのピクセルに居るか ---------------------
                float seHasFront = SAMPLE_TEXTURE2D(_SubtracteeHasFront, sampler_SubtracteeHasFront, uv).r;
                float seHasBack  = SAMPLE_TEXTURE2D(_SubtracteeHasBack,  sampler_SubtracteeHasBack,  uv).r;

                // Subtracteeが前面も背面も写っていない＝ここには削る対象が無い。
                // Subtractorだけが画面を覆っている領域を誤って書き換えないための早期棄却
                if (seHasFront < 0.5 && seHasBack < 0.5) discard;

                // --- 現在の可視面 -----------------------------------------------
                // 1つ前の合成デプス。工程3の初期値、または前のSubtractorが削った結果。
                // カメラがSubtractee内部にいる場合はnearZ番兵が入っている
                float currentSurface = SAMPLE_TEXTURE2D(_CompositeSrcDepth, sampler_CompositeSrcDepth, uv).r;

                // --- Aの入口 -----------------------------------------------------
                float srHasFront = SAMPLE_TEXTURE2D(_SubtractorHasFront, sampler_SubtractorHasFront, uv).r;
                float srFront = srHasFront > 0.5
                    ? SAMPLE_TEXTURE2D(_SubtractorFrontDepth, sampler_SubtractorFrontDepth, uv).r
                    : SSB_NEAR_Z; // カメラがSubtractor内部にいる場合のフォールバック

                // --- Aの出口 = このフラグメント自身 -------------------------------
                // フラグメントのSV_POSITION.zは既にw除算済みのウィンドウ空間デプスなので
                // ここで .w で割ってはいけない
                float srBack = i.positionHCS.z;

                // --- 区間判定 ----------------------------------------------------
                // 現在の可視面がこのSubtractorの範囲[srFront, srBack)に
                // 入っていなければ、このSubtractorはこのピクセルには無関係。
                // 奥側(srBackより奥)のはみ出しは下のZTest GEqualが弾いてくれる
                if (!SSB_IsFartherOrEqual(currentSurface, srFront)) discard;

                // --- Bの出口 -----------------------------------------------------
                float seBack = seHasBack > 0.5
                    ? SAMPLE_TEXTURE2D(_SubtracteeBackDepth, sampler_SubtracteeBackDepth, uv).r
                    : SSB_FAR_Z; // Subtracteeの境界が不明な場合は制約なしとして扱う

                // --- 新しい可視面を決める ----------------------------------------
                // Subtractorの出口(srBack)がまだSubtracteeの内側なら、そこが新しい可視面
                // (穴の内壁)。Subtracteeの範囲を超えていたら完全に貫通（先に何も無い）
                float newDepth = SSB_IsFartherOrEqual(seBack, srBack) ? srBack : SSB_FAR_Z;

                FragOut o;
                o.depth = newDepth;
                return o;
                // ZTest GEqualにより、newDepthが現在のcompositeDepth(コピー直後=
                // currentSurfaceと同値)より奥である場合のみ実際に書き込まれる
            }
            ENDHLSL
        }
    }
}
