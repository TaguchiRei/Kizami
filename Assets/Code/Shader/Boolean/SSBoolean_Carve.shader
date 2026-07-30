Shader "Hidden/ScreenSpaceBoolean/Carve"
{
    // Subtractorの背面（Cull Front）を描画しながら、そのピクセルの
    // 「現在の可視サーフェス」がこのSubtractorの[前面, 背面)区間に
    // 入っているかを判定し、入っていれば背面デプスまで削り込む。
    //
    // 【重要】カメラがこのSubtractorの内部にいる場合、前面(Cull Back)が
    // ラスタライズされず _SubtractorHasFront が0のままになる。
    // その場合は前面デプス＝nearZ（カメラ直前）とみなすことで、
    // カメラ自身から背面までを正しく削り込む。
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Front
            ZTest GEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CompositeSrcDepth);     SAMPLER(sampler_CompositeSrcDepth);
            TEXTURE2D(_SubtractorFrontDepth);  SAMPLER(sampler_SubtractorFrontDepth);
            TEXTURE2D(_SubtractorHasFront);    SAMPLER(sampler_SubtractorHasFront);
            TEXTURE2D(_SubtracteeBackDepth);   SAMPLER(sampler_SubtracteeBackDepth);
            TEXTURE2D(_SubtracteeHasBack);     SAMPLER(sampler_SubtracteeHasBack);
            float _SSBooleanNearZ;
            float _SSBooleanFarZ;

            struct Attributes { float4 positionOS : POSITION; };
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

            // 「奥」かどうかの判定。reversed-Zでは値が小さいほど奥
            // (実機で挙動がおかしい場合はここを反転してみてください)
            bool IsFartherOrEqual(float a, float b)
            {
            #if UNITY_REVERSED_Z
                return a <= b;
            #else
                return a >= b;
            #endif
            }

            struct FragOut { float depth : SV_Depth; };

            FragOut Frag(Varyings i)
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;

                float currentSurface = SAMPLE_TEXTURE2D(_CompositeSrcDepth, sampler_CompositeSrcDepth, uv).r;

                float srHasFront = SAMPLE_TEXTURE2D(_SubtractorHasFront, sampler_SubtractorHasFront, uv).r;
                float srFront = srHasFront > 0.5
                    ? SAMPLE_TEXTURE2D(_SubtractorFrontDepth, sampler_SubtractorFrontDepth, uv).r
                    : _SSBooleanNearZ; // カメラがSubtractor内部にいる場合のフォールバック

                // 自分自身(背面)のクリップスペースZ = このピクセルでのSubtractor出口(srBack)
                float srBack = i.positionHCS.z;

                // 現在の可視サーフェスがこのSubtractorの範囲[srFront, srBack)に
                // 入っていなければ、このSubtractorはこのピクセルには無関係
                if (!IsFartherOrEqual(currentSurface, srFront)) discard;

                float seHasBack = SAMPLE_TEXTURE2D(_SubtracteeHasBack, sampler_SubtracteeHasBack, uv).r;
                float seBack = seHasBack > 0.5
                    ? SAMPLE_TEXTURE2D(_SubtracteeBackDepth, sampler_SubtracteeBackDepth, uv).r
                    : _SSBooleanFarZ; // Subtracteeの境界が不明な場合は制約なしとして扱う

                // Subtractorの出口(srBack)がまだSubtracteeの内側なら、そこが新しい可視面
                // (穴の内壁)。Subtracteeの範囲を超えていたら完全に貫通（先に何も無い）
                float newDepth = IsFartherOrEqual(seBack, srBack) ? srBack : _SSBooleanFarZ;

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