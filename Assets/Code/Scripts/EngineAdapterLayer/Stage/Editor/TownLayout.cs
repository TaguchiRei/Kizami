using System.Collections.Generic;

namespace Kizami.Editor
{
    /// <summary>
    /// 街のマップの道の線と、道の種類ごとの幅。寸法は道路の部品（Stage_Road_*_01）と、区間14C の計画書のゾーンに合わせる。
    /// </summary>
    public static class TownLayout
    {
        // 中通りの線の、中心からの距離（m）。中通りの環は 90〜110 m のゾーン
        public const float MIDDLE_OFFSET = 100f;

        public static readonly RoadKind Main = new("Main", 13.6f);
        public static readonly RoadKind Middle = new("Middle", 7.2f);
        public static readonly RoadKind Narrow = new("Narrow", 2.4f);

        /// <summary>
        /// 大通り 2 本（X 軸・Z 軸の上）と、X・Z が ±MIDDLE_OFFSET の中通り 4 本。
        /// </summary>
        public static List<RoadLine> CreateRoadLines()
        {
            return new List<RoadLine>
            {
                new(Main, true, 0f),
                new(Main, false, 0f),
                new(Middle, true, -MIDDLE_OFFSET),
                new(Middle, true, MIDDLE_OFFSET),
                new(Middle, false, -MIDDLE_OFFSET),
                new(Middle, false, MIDDLE_OFFSET),
            };
        }
    }

    /// <summary>
    /// 道の種類。Name は道路の部品の名前の一部、HalfWidth は歩道を含めた幅の半分（m）。
    /// </summary>
    public sealed class RoadKind
    {
        public readonly string Name;
        public readonly float HalfWidth;

        public RoadKind(string name, float halfWidth)
        {
            Name = name;
            HalfWidth = halfWidth;
        }
    }

    /// <summary>
    /// マップの端から端まで通る 1 本の道。AlongX なら X の向きに延び、Offset は Z の位置。そうでなければ Z の向きに延び、Offset は X の位置。
    /// </summary>
    public readonly struct RoadLine
    {
        public readonly RoadKind Kind;
        public readonly bool AlongX;
        public readonly float Offset;

        public RoadLine(RoadKind kind, bool alongX, float offset)
        {
            Kind = kind;
            AlongX = alongX;
            Offset = offset;
        }
    }
}
