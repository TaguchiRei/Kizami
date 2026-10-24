Shader "Kizami/BarrierDurability"
{
    // ディフェンダーのバリアの耐久値を表示する、バリアより一回り大きい球のシェーダー。球の外側の面を加算で描く。
    // 四角の格子はオブジェクト空間の方位と仰角から作り、UV を使わない。方位の 1 周に _SlotsPerRing 個、仰角の範囲に _RingCount 段を並べ、段ごとに _RowShift 個ずつ方位をずらす。
    // _GroupSize 個で 1 区分けとし、区分けの先頭から _Durability 個だけ明るく描く。耐久が 1 減ると、全区分けから 1 個ずつ消える。
    // 頭頂部には、真上を頂点にした _GroupSize 個の二等辺三角形を放射状に並べる（10 個なら 36° ずつ）。耐久が 1 減るごとに、番号の大きい三角形から 1 個ずつ消える。
    // 三角形の底辺は真上からの角度 _CapRadius の位置で、隣どうしの辺には _CapGap の隙間を空ける。
    // 三角形と四角の帯の間は、同じ 36° ずつの _GroupSize 個の台形で覆う。台形は三角形とは逆の向きに、番号の小さいものから消える。
    // 四角の群れと頭頂部の三角形は、真上から見て時計回りに、台形は反時計回りに、毎秒 _RotationSpeed 度で回る。時間は Time.time なので、スローモードでは遅くなる。
    // 消えた四角と三角形は _EmptyIntensity の暗さで残し、全体の数が読めるようにする。_Durability と _GroupSize は ArmorPanel が MaterialPropertyBlock で渡す。
    Properties
    {
        [HDR] _Color("Color", Color) = (0.5, 1.6, 2.4, 1)
        _Durability("Durability", Float) = 10
        _SlotsPerRing("Slots Per Ring", Float) = 100
        _RingCount("Ring Count", Float) = 7
        _GroupSize("Group Size", Float) = 10
        _RowShift("Row Shift (slots per ring)", Float) = 1
        _ElevationMin("Elevation Min (deg)", Range(-90, 90)) = 0
        _ElevationMax("Elevation Max (deg)", Range(-90, 90)) = 24
        _CellGap("Cell Gap", Range(0, 0.45)) = 0.1
        _EmptyIntensity("Empty Intensity", Range(0, 1)) = 0.08
        _CapRadius("Cap Radius From Pole (deg)", Range(1, 89)) = 30
        _CapGap("Cap Gap (deg)", Range(0, 5)) = 0.6
        _CapTipGap("Cap Tip Gap (deg)", Range(0, 10)) = 2
        _RotationSpeed("Rotation Speed (deg/s)", Float) = 15
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Durability;
                float _SlotsPerRing;
                float _RingCount;
                float _GroupSize;
                float _RowShift;
                float _ElevationMin;
                float _ElevationMax;
                half _CellGap;
                half _EmptyIntensity;
                float _CapRadius;
                float _CapGap;
                float _CapTipGap;
                float _RotationSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
            };

            float Modulo(float x, float period)
            {
                return x - period * floor(x / period);
            }

            // 0 から 1 の座標 f の四角の中なら 1。両端から gap の幅は抜き、縁は 1 ピクセル程度ぼかす
            half CellMask(float f, float gap)
            {
                float edge = max(fwidth(f), 1e-4);
                return saturate((f - gap) / edge) * saturate((1.0 - gap - f) / edge);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.directionOS = input.positionOS.xyz;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float elevation = asin(clamp(direction.y, -1.0, 1.0));
                float azimuth = atan2(direction.z, direction.x);

                // 真上から見て時計回りに回る四角の群れと三角形、反時計回りに回る台形。pattern の方位は、画素の方位に spin を足し引きして求める
                float spin = fmod(radians(_RotationSpeed) * _Time.y, TWO_PI);
                float clockwiseAzimuth = azimuth + spin;
                float counterClockwiseAzimuth = azimuth - spin;

                // 真上からの角度。真上から四角の帯の上端までを、真上を頂点にした 1 区分けぶんの扇に割り、
                // 扇の中を真上からの距離 along（扇の中心の向きに測った距離）で、頭頂部の三角形と台形に分ける
                float poleDistance = acos(clamp(direction.y, -1.0, 1.0));
                float bandTop = radians(90.0 - _ElevationMax);
                if (poleDistance <= bandTop)
                {
                    float sectorAngle = TWO_PI / _GroupSize;
                    float halfSector = sectorAngle * 0.5;
                    float capRadius = radians(_CapRadius);
                    // 三角形の頂点（底辺の両端）が届く、真上からの最大の角度。台形はこの内側へ入らない
                    float capCorner = capRadius / cos(halfSector);
                    float gap = radians(_CapGap);
                    float edge = max(fwidth(poleDistance), 1e-4);
                    half mask;
                    half shapeLit;
                    if (poleDistance <= capCorner)
                    {
                        float angleFromStart = Modulo(clockwiseAzimuth + PI, TWO_PI);
                        float sector = floor(angleFromStart / sectorAngle);
                        float delta = angleFromStart - (sector + 0.5) * sectorAngle;
                        float along = poleDistance * cos(delta);
                        float sideDistance = poleDistance * sin(halfSector - abs(delta));
                        mask = saturate((sideDistance - gap) / edge) * saturate((capRadius - along - gap) / edge)
                            * saturate((poleDistance - radians(_CapTipGap)) / edge);
                        shapeLit = sector < _Durability ? 1.0h : _EmptyIntensity;
                    }
                    else
                    {
                        float angleFromStart = Modulo(counterClockwiseAzimuth + PI, TWO_PI);
                        float sector = floor(angleFromStart / sectorAngle);
                        float delta = angleFromStart - (sector + 0.5) * sectorAngle;
                        float along = poleDistance * cos(delta);
                        float sideDistance = poleDistance * sin(halfSector - abs(delta));
                        float trapezoidOuter = bandTop * cos(halfSector);
                        mask = saturate((sideDistance - gap) / edge) * saturate((along - capCorner - gap) / edge)
                            * saturate((trapezoidOuter - along - gap) / edge);
                        // 三角形とは逆の向きに、番号の小さい台形から消える
                        shapeLit = sector >= _GroupSize - _Durability ? 1.0h : _EmptyIntensity;
                    }

                    return half4(_Color.rgb * (mask * shapeLit), 1.0h);
                }

                float elevationMin = radians(_ElevationMin);
                float elevationMax = radians(_ElevationMax);
                float ringF = (elevation - elevationMin) / (elevationMax - elevationMin) * _RingCount;
                if (ringF < 0.0 || ringF >= _RingCount) discard;

                float slotF = (clockwiseAzimuth / TWO_PI + 0.5) * _SlotsPerRing;
                float ring = floor(ringF);
                float slot = Modulo(floor(slotF) - ring * _RowShift, _SlotsPerRing);
                float indexInGroup = Modulo(slot, _GroupSize);

                // 方位の継ぎ目で fwidth が跳ねないよう、四角の中の座標は frac の前の値から取る
                half mask = CellMask(frac(slotF), _CellGap) * CellMask(frac(ringF), _CellGap);
                half lit = indexInGroup < _Durability ? 1.0h : _EmptyIntensity;
                return half4(_Color.rgb * (mask * lit), 1.0h);
            }
            ENDHLSL
        }
    }
}
