#ifndef SSBOOLEAN_COMMON_INCLUDED
#define SSBOOLEAN_COMMON_INCLUDED

// ============================================================
// デプス表現についての注意（ここを間違えると全部壊れる）
// ------------------------------------------------------------
// 1) ShaderLabの ZTest / ClearRenderTarget の depth 引数は「意味」で書く。
//    LEqual = 手前が通る / GEqual = 奥が通る、clear depth は 1=遠クリップ。
//    reversed-Z への変換はUnityが内部で行うので、プラットフォーム分岐は不要。
//
// 2) 一方、SV_Depth に書く値・デプステクスチャからサンプルした値は
//    「生のクリップ空間Z」。reversed-Z では 1 が近く 0 が遠くなる。
//    HLSL側で大小比較するときは必ず下のヘルパーを使う。
//
// この2つの表現を混ぜたのが今までの不具合の原因だったので、
// 生の値が要るところは SSB_NEAR_Z / SSB_FAR_Z だけを使う。
// ============================================================

#if UNITY_REVERSED_Z
    #define SSB_NEAR_Z 1.0
    #define SSB_FAR_Z  0.0
#else
    #define SSB_NEAR_Z 0.0
    #define SSB_FAR_Z  1.0
#endif

// a が b と同じか、より奥にあるか（生のクリップ空間Z同士で比較する）
bool SSB_IsFartherOrEqual(float a, float b)
{
#if UNITY_REVERSED_Z
    return a <= b;
#else
    return a >= b;
#endif
}

// 「何も無い」を表す番兵値かどうか
bool SSB_IsFarMarker(float d)
{
    return abs(d - SSB_FAR_Z) < 1e-6;
}

// 「カメラがSubtractee内部にいる」を表す番兵値かどうか。
// 可視サーフェスがカメラ位置そのものにあるという意味で、
// CSGの区間判定には使うが、最終的なカメラデプスには書き込まない。
bool SSB_IsNearMarker(float d)
{
    return abs(d - SSB_NEAR_Z) < 1e-6;
}

#endif // SSBOOLEAN_COMMON_INCLUDED
