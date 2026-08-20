Shader "ScreenSpaceBoolean/Lit"
{
    // ========================================================================
    // Subtractee / Subtractor 本体の"見た目"を描くシェーダー。
    // Renderer Feature が確定させた合成デプスに対して ZTest Equal で色だけを乗せる。
    //
    // ■ ZTest Equal である理由
    //   Featureは「ブーリアン後に見えるべき面はこのデプス」というところまでを
    //   カメラデプスへ焼いてある。ZWrite Off / ZTest Equal にしておくと、
    //   そのデプスとぴったり一致した面＝残すべき面だけが描かれ、削られた面は
    //   自動的に落ちる。マスクもクリップも要らない。
    //
    //   逆に言うと、頂点のクリップ座標がデプスを書いた側（FrontBack / Carve）と
    //   ビット単位で一致していないと面ごと消える。Vertで式を揃えているのはこのため。
    //
    // ■ マテリアルごとの設定
    //   Subtractee用マテリアル : Cull = Back（通常通り）
    //   Subtractor用マテリアル : Cull = Front
    //     削る側は普段は見えないが、削った穴の内壁＝Subtractorの背面だけは
    //     見せたいので背面を描く。Carveパスが可視面として書き込んでいるのも
    //     この背面なので、ZTest Equal がそこだけ通る。
    //     （Fragでの法線は裏面基準になるので IS_FRONT_VFACE で反転している）
    //
    // ■ 影について
    //   ShadowCasterは通常描画のままなので、影は「削る前の形」で落ちる。
    // ========================================================================
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull [_Cull]
            ZWrite Off  // デプスはFeatureが確定済み。ここでは触らない
            ZTest Equal // 合成デプスと一致した面＝残すべき面だけ色を乗せる

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                // ZTest Equal で合成デプスと一致させる必要があるので、クリップ座標は
                // デプスを書いた側（FrontBack / Carve）とまったく同じ式で求める。
                // GetVertexPositionInputs は world 経由で計算するため、最下位ビットが
                // ずれてサーフェスが丸ごと消えることがある。
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionWS  = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS    = TransformObjectToWorldNormal(v.normalOS);
                o.uv          = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(i.normalWS);
                if (!IS_FRONT_VFACE(frontFace, true, false)) normalWS = -normalWS;

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseTex.rgb * _BaseColor.rgb;
                surfaceData.alpha = 1;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = 0;
                surfaceData.normalTS = half3(0, 0, 1);

                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        // 通常の影を落とす（削れる前の元の形で落ちる点に注意。README参照）
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirWS = _LightDirection;
#endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
                o.positionHCS = positionCS;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
