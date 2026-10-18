using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace Kizami.Editor
{
    /// <summary>
    /// 街のマップの配置を、ゾーンの規則と乱数のシードから決める。シーンには触らず、置く物の一覧（TownPlan）だけを返す。
    /// 組み立ての順は、大通り → 中通り → 区画を小道で分ける → アーケード → 道に沿った建物。寸法は道路の部品（Stage_Road_*_01）と区間14C の計画書のゾーンに合わせる。
    /// 区画は大通りと中通りに囲まれた長方形で、辺が SPLIT_THRESHOLD より長い区画は、小道で SUB_BLOCK_LENGTH 前後の小区画に分ける。
    /// 建物は小区画の道に面した辺に沿って、正面（モデルの +Z）を道へ向けて並べる。幅の広い道に面した辺から先に埋め、重なる位置・円の外へはみ出す位置・天照塔の脚にかかる位置には置かない。
    /// 中心から 100 m の中通りの内側は商業エリア、外側はビル街で、置く建物の種類を変える。ビル街の小道沿いには小さめの建物を置く。
    /// 敵の生産施設は、建物より先に、敵の区画ごとに1つ置く。ビル街の小道沿いを優先し、置けなければ道路の上にも置く。
    /// 小物は、大通りと中通りの歩道に街灯（商業エリアではベンチも）、交差点に信号、崖の手前の道の端にガードレール、骨組みのビルの前に工事用のフェンスとバリケードを置く。
    /// 天照塔の通り穴の中とアーケードの屋根の下には、歩道の小物を置かない。
    /// </summary>
    public static class TownLayout
    {
        public const float MAP_RADIUS = 350f;

        // 中通りの線の、中心からの距離（m）。中通りの環は 90〜110 m のゾーン
        public const float MIDDLE_OFFSET = 100f;

        // マップの半径から、道の外側の角を内側へ下げる余白（m）
        private const float EDGE_MARGIN = 2f;
        private const float ROTARY_RADIUS = 40f;
        private const float STRAIGHT_LENGTH = 16f;

        // 区画の辺がこれより長いと小道で分ける（m）と、分けたあとの小区画の辺の目安（m）
        private const float SPLIT_THRESHOLD = 110f;
        private const float SUB_BLOCK_LENGTH = 80f;

        // 建物を道の歩道の縁から下げる距離と、建物どうしの最小のすき間（m）
        private const float SETBACK = 1f;
        private const float BUILDING_MARGIN = 1.5f;

        // 建物の間に空ける路地の幅の範囲（m）と、置けなかったときに辺に沿って進める距離（m）
        private const float ALLEY_MIN = 2f;
        private const float ALLEY_MAX = 6f;
        private const float SCAN_STEP = 4f;

        // 建物の角を収める半径（m）。崖のふちに少し余白を残す
        private const float BUILDING_RADIUS = 344f;

        // 天照塔の芯の半径と、脚の届く半径・脚の中心線からの半幅（m）。脚は対角線の 4 方向に延びる
        private const float TOWER_CORE_RADIUS = 48f;
        private const float TOWER_LEG_REACH = 100f;
        private const float TOWER_LEG_HALF_WIDTH = 24f;

        // アーケードの 1 モジュールの長さと、東西の棟の中心の通りの中心からの距離（m）、店舗の奥行き（m）
        private const float ARCADE_MODULE_LENGTH = 12f;
        private const float ARCADE_HALF_SPACING = 7.6f;
        private const float ARCADE_SHOP_DEPTH = 8f;

        // 崩壊のコアの置き場（Town_CollapseSite）の、原点から見た範囲の中心（X, m）と、まわりの余白を足した広さ（m）。長辺はモデルの X
        private const float SITE_CENTER_X = 5f;
        private const float SITE_LENGTH = 18f;
        private const float SITE_WIDTH = 10f;

        // 歩道の上面と車道の上面の高さ（m）。道路の部品に合わせる
        private const float SIDEWALK_TOP = 0.25f;
        private const float ROAD_TOP = 0.05f;

        // 街灯の間隔と、街灯を縁石から歩道の内へ下げる距離（m）
        private const float LIGHT_SPACING = 32f;
        private const float CURB_INSET = 0.6f;

        // この半径の内側の道は天照塔の通り穴なので、歩道の小物を置かない（m）
        private const float TUNNEL_RADIUS = 80f;

        // 工事用のフェンス・ガードレールの 1 枚の幅（m）
        private const float FENCE_WIDTH = 2f;
        private const float GUARDRAIL_WIDTH = 4f;

        public static readonly RoadKind Main = new("Main", 13.6f, 4f);
        public static readonly RoadKind Middle = new("Middle", 7.2f, 2.4f);
        public static readonly RoadKind Narrow = new("Narrow", 2.4f, 0f);

        private static readonly BuildingType _largeFactory = new("EnemyFactory_Large", 14.4f, 14.4f);
        private static readonly BuildingType _smallFactory = new("EnemyFactory_Small", 8f, 8f);

        private static readonly BuildingType[] _commercial =
        {
            new("Apartment", 16f, 10.4f),
            new("MixedUse", 12f, 16f),
            new("Rowhouse", 16f, 8f),
            new("Amusement", 40f, 32f),
            new("Frame", 24f, 24f),
        };

        private static readonly BuildingType[] _business =
        {
            new("HighRiseA", 32f, 32f),
            new("HighRiseB", 36f, 36f),
            new("Glass", 28f, 20f),
            new("Parking", 40f, 28f),
            new("Amusement", 40f, 32f),
            new("Frame", 24f, 24f),
        };

        private static readonly BuildingType[] _businessNarrow =
        {
            new("MixedUse", 12f, 16f),
            new("Apartment", 16f, 10.4f),
            new("Rowhouse", 16f, 8f),
            new("Glass", 28f, 20f),
            new("Frame", 24f, 24f),
        };

        /// <summary>
        /// 街の配置を決める。
        /// </summary>
        /// <param name="seed">乱数のシード</param>
        /// <param name="sectionSize">敵の区画の大きさ（m）。生産施設を区画ごとに1つ置く</param>
        /// <param name="skippedSections">生産施設を置かない区画の番号</param>
        public static TownPlan Plan(int seed, float sectionSize, IReadOnlyCollection<Vector2Int> skippedSections)
        {
            var random = new Random(seed);
            var plan = new TownPlan();
            var lines = new List<RoadLine>
            {
                new(Main, true, 0f),
                new(Main, false, 0f),
                new(Middle, true, -MIDDLE_OFFSET),
                new(Middle, true, MIDDLE_OFFSET),
                new(Middle, false, -MIDDLE_OFFSET),
                new(Middle, false, MIDDLE_OFFSET),
            };

            var subBlocks = new List<SubBlock>();
            foreach (var block in CreateBlocks(lines))
                SplitBlock(block, lines, subBlocks);

            var occupied = new List<Rect>();
            var arcade = PlanArcade(plan, occupied);
            var crossings = PlanRoads(plan, lines);
            PlanFactories(plan, subBlocks, occupied, random, sectionSize, skippedSections);
            PlanCollapseSite(plan, subBlocks, occupied, random);

            // 幅の広い道に面した辺から先に埋める
            var sides = new List<(SubBlock Block, BlockSide Side)>();
            foreach (var subBlock in subBlocks)
            {
                foreach (var side in subBlock.Sides)
                {
                    if (side.Kind != null) sides.Add((subBlock, side));
                }
            }

            sides.Sort((a, b) => b.Side.Kind.HalfWidth.CompareTo(a.Side.Kind.HalfWidth));
            foreach (var (subBlock, side) in sides)
                PlanBuildings(plan, subBlock, side, occupied, random);

            PlanStreetProps(plan, lines, crossings, arcade);
            return plan;
        }

        /// <summary>
        /// 大通りと中通りに囲まれた区画。辺に接する道の種類も持つ。マップの端に接する辺の道は null。
        /// </summary>
        private static List<SubBlock> CreateBlocks(List<RoadLine> lines)
        {
            var xIntervals = Intervals(lines, false);
            var zIntervals = Intervals(lines, true);
            var blocks = new List<SubBlock>();
            foreach (var x in xIntervals)
            {
                foreach (var z in zIntervals)
                {
                    var rect = Rect.MinMaxRect(x.Min, z.Min, x.Max, z.Max);
                    var commercial = Mathf.Abs(rect.center.x) < MIDDLE_OFFSET && Mathf.Abs(rect.center.y) < MIDDLE_OFFSET;
                    blocks.Add(new SubBlock(rect, commercial, x.MinKind, x.MaxKind, z.MinKind, z.MaxKind));
                }
            }

            return blocks;
        }

        /// <summary>
        /// 1 つの軸の上で、道にふさがれていない区間。alongX が false なら、Z の向きに延びる線（X の位置）で X を区切る。
        /// </summary>
        private static List<Interval> Intervals(List<RoadLine> lines, bool alongX)
        {
            var limit = MAP_RADIUS - EDGE_MARGIN;
            var crossing = lines.FindAll(line => line.AlongX == alongX);
            crossing.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            var intervals = new List<Interval>();
            var cursor = -limit;
            RoadKind cursorKind = null;
            foreach (var line in crossing)
            {
                intervals.Add(new Interval(cursor, line.Offset - line.Kind.HalfWidth, cursorKind, line.Kind));
                cursor = line.Offset + line.Kind.HalfWidth;
                cursorKind = line.Kind;
            }

            intervals.Add(new Interval(cursor, limit, cursorKind, null));
            return intervals;
        }

        /// <summary>
        /// 区画の長い辺を小道で等分し、小区画にする。小道は区画の中だけを通し、両端は区画を囲む道に突き当てる。
        /// </summary>
        private static void SplitBlock(SubBlock block, List<RoadLine> lines, List<SubBlock> output)
        {
            var rect = block.Rect;
            var xCuts = Cuts(rect.xMin, rect.xMax);
            var zCuts = Cuts(rect.yMin, rect.yMax);
            foreach (var x in xCuts) lines.Add(new RoadLine(Narrow, false, x, rect.yMin, rect.yMax));
            foreach (var z in zCuts) lines.Add(new RoadLine(Narrow, true, z, rect.xMin, rect.xMax));

            var xIntervals = SplitInterval(new Interval(rect.xMin, rect.xMax, block.XMinKind, block.XMaxKind), xCuts);
            var zIntervals = SplitInterval(new Interval(rect.yMin, rect.yMax, block.ZMinKind, block.ZMaxKind), zCuts);
            foreach (var x in xIntervals)
            {
                foreach (var z in zIntervals)
                    output.Add(new SubBlock(Rect.MinMaxRect(x.Min, z.Min, x.Max, z.Max), block.Commercial, x.MinKind, x.MaxKind, z.MinKind, z.MaxKind));
            }
        }

        /// <summary>
        /// 区間を小道で等分する位置。区間が SPLIT_THRESHOLD 以下なら分けない。
        /// </summary>
        private static List<float> Cuts(float min, float max)
        {
            var cuts = new List<float>();
            var length = max - min;
            if (length <= SPLIT_THRESHOLD) return cuts;

            var parts = Mathf.Max(2, Mathf.RoundToInt(length / SUB_BLOCK_LENGTH));
            for (var i = 1; i < parts; i++)
                cuts.Add(min + length * i / parts);
            return cuts;
        }

        private static List<Interval> SplitInterval(Interval interval, List<float> cuts)
        {
            var result = new List<Interval>();
            var cursor = interval.Min;
            var cursorKind = interval.MinKind;
            foreach (var cut in cuts)
            {
                result.Add(new Interval(cursor, cut - Narrow.HalfWidth, cursorKind, Narrow));
                cursor = cut + Narrow.HalfWidth;
                cursorKind = Narrow;
            }

            result.Add(new Interval(cursor, interval.Max, cursorKind, interval.MaxKind));
            return result;
        }

        /// <summary>
        /// 北（+Z）の中通りの、西の大通りとの間の 1 区間をアーケードにする。区間の両端は、北端を閉じた端のモジュールを外へ向けて置く。
        /// 店舗は中通りの両側の歩道の外に建つので、その範囲を建物の置き場所から外す。屋根の下の通りの範囲を返す。
        /// </summary>
        private static Rect PlanArcade(TownPlan plan, List<Rect> occupied)
        {
            var start = -MIDDLE_OFFSET + Middle.HalfWidth;
            var end = -Main.HalfWidth;
            var count = Mathf.FloorToInt((end - start) / ARCADE_MODULE_LENGTH);
            var first = (start + end) * 0.5f - count * ARCADE_MODULE_LENGTH * 0.5f;
            for (var i = 0; i < count; i++)
            {
                var along = first + ARCADE_MODULE_LENGTH * (i + 0.5f);
                var isFirst = i == 0;
                var isLast = i == count - 1;
                // 通りはモデルの Z の向き。+X へ延ばすので 90°、最後のモジュールは閉じた端を +X へ向けて 270°
                var yaw = isLast && !isFirst ? 270f : 90f;
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var center = new Vector3(along, 0f, MIDDLE_OFFSET);
                var prefix = isFirst || isLast ? "Arcade_End_" : "Arcade_";
                plan.Buildings.Add(new PartPlacement(prefix + "West", center + rotation * new Vector3(ARCADE_HALF_SPACING, 0f, 0f), yaw));
                plan.Buildings.Add(new PartPlacement(prefix + "East", center + rotation * new Vector3(-ARCADE_HALF_SPACING, 0f, 0f), yaw));
            }

            var x0 = first;
            var x1 = first + count * ARCADE_MODULE_LENGTH;
            occupied.Add(Rect.MinMaxRect(x0, MIDDLE_OFFSET - Middle.HalfWidth - ARCADE_SHOP_DEPTH, x1, MIDDLE_OFFSET - Middle.HalfWidth));
            occupied.Add(Rect.MinMaxRect(x0, MIDDLE_OFFSET + Middle.HalfWidth, x1, MIDDLE_OFFSET + Middle.HalfWidth + ARCADE_SHOP_DEPTH));
            return Rect.MinMaxRect(x0, MIDDLE_OFFSET - Middle.HalfWidth, x1, MIDDLE_OFFSET + Middle.HalfWidth);
        }

        /// <summary>
        /// 線どうしの交点に幅の広いほうの十字路を置き、その間を長さ 16 m の直線で埋める。最後の 1 枚に端数が出ないよう、区間の直線は同じ倍率で道の向きに伸び縮みさせる。
        /// </summary>
        private static List<Crossing> PlanRoads(TownPlan plan, List<RoadLine> lines)
        {
            plan.Roads.Add(new PartPlacement("Rotary", Vector3.zero, 0f));

            var crossings = new List<Crossing>();
            for (var i = 0; i < lines.Count; i++)
            {
                for (var j = i + 1; j < lines.Count; j++)
                {
                    var a = lines[i];
                    var b = lines[j];
                    if (a.AlongX == b.AlongX || !a.Covers(b.Offset) || !b.Covers(a.Offset)) continue;
                    var x = a.AlongX ? b.Offset : a.Offset;
                    var z = a.AlongX ? a.Offset : b.Offset;
                    // 中心の交点はロータリーが受け持つ
                    if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) continue;
                    var wide = a.Kind.HalfWidth >= b.Kind.HalfWidth ? a.Kind : b.Kind;
                    // 十字路の角が崖にかかる交点は、どちらの道もそこまで届かないので置かない
                    var corner = new Vector2(Mathf.Abs(x), Mathf.Abs(z)) + Vector2.one * wide.HalfWidth;
                    if (corner.magnitude > MAP_RADIUS - EDGE_MARGIN) continue;
                    crossings.Add(new Crossing(new Vector2(x, z), wide));
                }
            }

            foreach (var crossing in crossings)
                plan.Roads.Add(new PartPlacement(crossing.Kind.Name + "_Cross", new Vector3(crossing.Center.x, 0f, crossing.Center.y), 0f));

            foreach (var line in lines)
            {
                foreach (var (start, end) in OpenSpans(line, crossings))
                    FillStraight(plan, line, start, end);
            }

            return crossings;
        }

        /// <summary>
        /// 線の上で、交点・ロータリー・崖にふさがれていない区間（線に沿った座標の範囲）。
        /// </summary>
        private static List<(float Start, float End)> OpenSpans(RoadLine line, List<Crossing> crossings)
        {
            var spans = new List<(float, float)>();
            var outer = line.Kind.HalfWidth + Mathf.Abs(line.Offset);
            var radius = MAP_RADIUS - EDGE_MARGIN;
            if (outer >= radius) return spans;

            var reach = Mathf.Sqrt(radius * radius - outer * outer);
            var low = Mathf.Max(-reach, line.Min);
            var high = Mathf.Min(reach, line.Max);

            var blocked = new List<(float Start, float End)>();
            if (Mathf.Approximately(line.Offset, 0f)) blocked.Add((-ROTARY_RADIUS, ROTARY_RADIUS));
            foreach (var crossing in crossings)
            {
                var across = line.AlongX ? crossing.Center.y : crossing.Center.x;
                if (!Mathf.Approximately(across, line.Offset)) continue;
                var along = line.AlongX ? crossing.Center.x : crossing.Center.y;
                blocked.Add((along - crossing.Kind.HalfWidth, along + crossing.Kind.HalfWidth));
            }

            blocked.Sort((a, b) => a.Start.CompareTo(b.Start));
            var cursor = low;
            foreach (var (start, end) in blocked)
            {
                if (start > cursor) spans.Add((cursor, Mathf.Min(start, high)));
                cursor = Mathf.Max(cursor, end);
            }

            if (cursor < high) spans.Add((cursor, high));
            spans.RemoveAll(span => span.Item2 - span.Item1 < 0.5f);
            return spans;
        }

        private static void FillStraight(TownPlan plan, RoadLine line, float start, float end)
        {
            var length = end - start;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / STRAIGHT_LENGTH));
            var tile = length / count;
            for (var i = 0; i < count; i++)
            {
                var along = start + tile * (i + 0.5f);
                var position = line.AlongX ? new Vector3(along, 0f, line.Offset) : new Vector3(line.Offset, 0f, along);
                plan.Roads.Add(new PartPlacement(line.Kind.Name + "_Straight", position, line.AlongX ? 90f : 0f, tile / STRAIGHT_LENGTH));
            }
        }

        /// <summary>
        /// 小区画の 1 辺に沿って、端から順に建物を置く。種類は毎回並びを混ぜ、置ける最初の種類を選ぶ。
        /// </summary>
        private static void PlanBuildings(TownPlan plan, SubBlock block, BlockSide side, List<Rect> occupied, Random random)
        {
            var types = block.Commercial ? _commercial : side.Kind == Narrow ? _businessNarrow : _business;
            var rect = block.Rect;
            var alongX = side.Edge is Edge.ZMin or Edge.ZMax;
            var a0 = alongX ? rect.xMin : rect.yMin;
            var a1 = alongX ? rect.xMax : rect.yMax;
            var depthLimit = (alongX ? rect.height : rect.width) - BUILDING_MARGIN * 2f;
            var order = new List<BuildingType>(types);

            var cursor = a0 + BUILDING_MARGIN;
            while (cursor < a1 - BUILDING_MARGIN)
            {
                Shuffle(order, random);
                var placed = false;
                foreach (var type in order)
                {
                    if (cursor + type.Width > a1 - BUILDING_MARGIN || type.Depth > depthLimit) continue;
                    var footprint = Footprint(rect, side.Edge, cursor, type);
                    if (!CanPlace(footprint, occupied)) continue;

                    occupied.Add(footprint);
                    var center = footprint.center;
                    plan.Buildings.Add(new PartPlacement(type.Name, new Vector3(center.x, 0f, center.y), FrontYaw(side.Edge)));
                    if (type.Name == "Frame") PlanConstructionFront(plan, rect, side, footprint);
                    cursor += type.Width + Mathf.Lerp(ALLEY_MIN, ALLEY_MAX, (float)random.NextDouble());
                    placed = true;
                    break;
                }

                if (!placed) cursor += SCAN_STEP;
            }
        }

        /// <summary>
        /// 敵の区画ごとに生産施設を1つ置く。区画は原点を中心にした sectionSize 四方の格子で、EnemySpawnSystem の区画と同じ。skippedSections の区画には置かない。
        /// 区画の番号の和が偶数なら大きい施設、奇数なら小さい施設を先に試す。
        /// まず区画の中の、ビル街の小道に面した辺の真ん中に正面を小道へ向けて置く。置けなければ、道路の上も含めて区画の中心に近い所から探す。
        /// </summary>
        private static void PlanFactories(TownPlan plan, List<SubBlock> subBlocks, List<Rect> occupied, Random random,
            float sectionSize, IReadOnlyCollection<Vector2Int> skippedSections)
        {
            var candidates = new List<(SubBlock Block, BlockSide Side)>();
            foreach (var subBlock in subBlocks)
            {
                if (subBlock.Commercial) continue;
                foreach (var side in subBlock.Sides)
                {
                    if (side.Kind == Narrow) candidates.Add((subBlock, side));
                }
            }

            Shuffle(candidates, random);
            var count = Mathf.CeilToInt((MAP_RADIUS - sectionSize * 0.5f) / sectionSize);
            for (var z = -count; z <= count; z++)
            {
                for (var x = -count; x <= count; x++)
                {
                    var section = new Vector2Int(x, z);
                    if (Contains(skippedSections, section)) continue;

                    var center = new Vector2(x, z) * sectionSize;
                    var area = new Rect(center - Vector2.one * (sectionSize * 0.5f), Vector2.one * sectionSize);
                    var types = (x + z) % 2 == 0 ? new[] { _largeFactory, _smallFactory } : new[] { _smallFactory, _largeFactory };
                    foreach (var type in types)
                    {
                        if (TryPlaceFactoryOnAlley(plan, candidates, occupied, area, type) ||
                            TryPlaceFactoryNearCenter(plan, occupied, area, type)) break;
                    }
                }
            }
        }

        private static bool Contains(IReadOnlyCollection<Vector2Int> sections, Vector2Int section)
        {
            foreach (var other in sections)
            {
                if (other == section) return true;
            }

            return false;
        }

        /// <summary>
        /// 区画 area の中にある、小道に面した辺の真ん中に施設を置く。施設の範囲が区画からはみ出す所は使わない。
        /// </summary>
        private static bool TryPlaceFactoryOnAlley(TownPlan plan, List<(SubBlock Block, BlockSide Side)> candidates,
            List<Rect> occupied, Rect area, BuildingType type)
        {
            foreach (var (block, side) in candidates)
            {
                var alongX = side.Edge is Edge.ZMin or Edge.ZMax;
                var middle = alongX ? block.Rect.center.x : block.Rect.center.y;
                var footprint = Footprint(block.Rect, side.Edge, middle - type.Width * 0.5f, type);
                if (!ContainsRect(area, footprint) || !CanPlace(footprint, occupied)) continue;

                AddFactory(plan, occupied, footprint, type, FrontYaw(side.Edge));
                return true;
            }

            return false;
        }

        /// <summary>
        /// 区画 area の中を SCAN_STEP おきに、中心に近い位置から順に試して施設を置く。道路の上にも置く。
        /// </summary>
        private static bool TryPlaceFactoryNearCenter(TownPlan plan, List<Rect> occupied, Rect area, BuildingType type)
        {
            var points = new List<Vector2>();
            var halfWidth = type.Width * 0.5f;
            var halfDepth = type.Depth * 0.5f;
            for (var z = area.yMin + halfDepth; z <= area.yMax - halfDepth; z += SCAN_STEP)
            {
                for (var x = area.xMin + halfWidth; x <= area.xMax - halfWidth; x += SCAN_STEP)
                    points.Add(new Vector2(x, z));
            }

            points.Sort((a, b) => (a - area.center).sqrMagnitude.CompareTo((b - area.center).sqrMagnitude));
            foreach (var point in points)
            {
                var footprint = new Rect(point.x - halfWidth, point.y - halfDepth, type.Width, type.Depth);
                if (!CanPlace(footprint, occupied)) continue;

                AddFactory(plan, occupied, footprint, type, 0f);
                return true;
            }

            return false;
        }

        private static bool ContainsRect(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
        }

        private static void AddFactory(TownPlan plan, List<Rect> occupied, Rect footprint, BuildingType type, float yaw)
        {
            occupied.Add(footprint);
            plan.Buildings.Add(new PartPlacement(type.Name, new Vector3(footprint.center.x, 0f, footprint.center.y), yaw));
        }

        /// <summary>
        /// 崩壊のコアの置き場を、ビル街の小区画の真ん中に、建物より先に確保する。道に面さない中庭になる。向きは 90 度ごとに乱数で選ぶ。
        /// </summary>
        private static void PlanCollapseSite(TownPlan plan, List<SubBlock> subBlocks, List<Rect> occupied, Random random)
        {
            var candidates = subBlocks.FindAll(block => !block.Commercial);
            Shuffle(candidates, random);
            var yaw = random.Next(4) * 90f;
            var turned = yaw is 90f or 270f;
            var size = turned ? new Vector2(SITE_WIDTH, SITE_LENGTH) : new Vector2(SITE_LENGTH, SITE_WIDTH);
            foreach (var block in candidates)
            {
                var center = block.Rect.center;
                var footprint = new Rect(center - size * 0.5f, size);
                if (!CanPlace(footprint, occupied)) continue;

                occupied.Add(footprint);
                var offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(SITE_CENTER_X, 0f, 0f);
                plan.Sites.Add(new PartPlacement("CollapseSite", new Vector3(center.x - offset.x, 0f, center.y - offset.z), yaw));
                return;
            }

            Debug.LogWarning("崩壊のコアの置き場を置ける小区画がありません。");
        }

        /// <summary>
        /// 骨組みのビルの正面の、道との間に工事用のフェンスを並べ、正面の真ん中の道の端にバリケードを置く。
        /// </summary>
        private static void PlanConstructionFront(TownPlan plan, Rect block, BlockSide side, Rect footprint)
        {
            var alongX = side.Edge is Edge.ZMin or Edge.ZMax;
            var outward = side.Edge is Edge.ZMax or Edge.XMax ? 1f : -1f;
            var edge = side.Edge switch
            {
                Edge.ZMax => block.yMax,
                Edge.ZMin => block.yMin,
                Edge.XMax => block.xMax,
                _ => block.xMin,
            };

            var fenceAcross = edge - outward * SETBACK * 0.5f;
            var a0 = alongX ? footprint.xMin : footprint.yMin;
            var a1 = alongX ? footprint.xMax : footprint.yMax;
            var count = Mathf.FloorToInt((a1 - a0) / FENCE_WIDTH);
            var first = (a0 + a1) * 0.5f - count * FENCE_WIDTH * 0.5f;
            for (var i = 0; i < count; i++)
            {
                var along = first + FENCE_WIDTH * (i + 0.5f);
                plan.Props.Add(new PartPlacement("ConstructionFence", AlongAcross(alongX, along, fenceAcross, 0f), alongX ? 0f : 90f));
            }

            // 歩道があれば歩道の上、なければ車道の端に置く
            var top = side.Kind.SidewalkWidth > 0f ? SIDEWALK_TOP : ROAD_TOP;
            var barricadeAcross = edge + outward * Mathf.Max(1f, side.Kind.SidewalkWidth * 0.5f);
            plan.Props.Add(new PartPlacement("Barricade", AlongAcross(alongX, (a0 + a1) * 0.5f, barricadeAcross, top), alongX ? 0f : 90f));
        }

        /// <summary>
        /// 大通りと中通りの歩道に、両側互い違いに街灯を並べ、商業エリアでは街灯の間にベンチを置く。交差点には信号、崖の手前の道の端にはガードレールを置く。
        /// 街灯の腕と信号の腕は車道へ向け、ベンチは車道に向けて座る向きにする。
        /// </summary>
        private static void PlanStreetProps(TownPlan plan, List<RoadLine> lines, List<Crossing> crossings, Rect arcade)
        {
            foreach (var line in lines)
            {
                if (line.Kind == Narrow) continue;

                var curb = line.Kind.HalfWidth - line.Kind.SidewalkWidth;
                var spans = OpenSpans(line, crossings);
                foreach (var (start, end) in spans)
                {
                    var side = 1f;
                    for (var along = start + LIGHT_SPACING * 0.5f; along < end - 1f; along += LIGHT_SPACING)
                    {
                        side = -side;
                        var position = AlongAcross(line.AlongX, along, line.Offset + side * (curb + CURB_INSET), SIDEWALK_TOP);
                        var flat = new Vector2(position.x, position.z);
                        if (flat.magnitude < TUNNEL_RADIUS || arcade.Contains(flat)) continue;

                        var toRoad = AlongAcross(line.AlongX, 0f, -side, 0f);
                        plan.Props.Add(new PartPlacement("StreetLight", position, YawFacing(toRoad)));

                        var commercial = Mathf.Abs(flat.x) < MIDDLE_OFFSET && Mathf.Abs(flat.y) < MIDDLE_OFFSET;
                        if (!commercial || along + LIGHT_SPACING * 0.5f > end - 1f) continue;

                        var benchAcross = line.Offset + side * (line.Kind.HalfWidth - 0.6f);
                        var bench = AlongAcross(line.AlongX, along + LIGHT_SPACING * 0.5f, benchAcross, SIDEWALK_TOP);
                        if (!arcade.Contains(new Vector2(bench.x, bench.z))) plan.Props.Add(new PartPlacement("Bench", bench, YawFacing(-toRoad)));
                    }
                }

                PlanGuardrails(plan, line, spans);
            }

            foreach (var crossing in crossings)
            {
                if (crossing.Kind == Narrow) continue;

                var corner = crossing.Kind.HalfWidth - crossing.Kind.SidewalkWidth * 0.5f;
                foreach (var sign in new[] { 1f, -1f })
                {
                    var position = new Vector3(crossing.Center.x + sign * corner, SIDEWALK_TOP, crossing.Center.y + sign * corner);
                    plan.Props.Add(new PartPlacement("TrafficLight", position, YawFacing(new Vector3(-sign, 0f, 0f))));
                }
            }
        }

        /// <summary>
        /// 崖の手前で終わる道の端に、道を横切ってガードレールを並べる。
        /// </summary>
        private static void PlanGuardrails(TownPlan plan, RoadLine line, List<(float Start, float End)> spans)
        {
            if (spans.Count == 0) return;

            var count = Mathf.FloorToInt(line.Kind.HalfWidth * 2f / GUARDRAIL_WIDTH);
            foreach (var along in new[] { spans[0].Start + 0.5f, spans[^1].End - 0.5f })
            {
                for (var i = 0; i < count; i++)
                {
                    var across = line.Offset - count * GUARDRAIL_WIDTH * 0.5f + GUARDRAIL_WIDTH * (i + 0.5f);
                    plan.Props.Add(new PartPlacement("Guardrail_Straight", AlongAcross(line.AlongX, along, across, ROAD_TOP), line.AlongX ? 90f : 0f));
                }
            }
        }

        /// <summary>
        /// 線に沿った座標と横切る座標から、ワールドの位置を作る。alongX なら along が X、across が Z。
        /// </summary>
        private static Vector3 AlongAcross(bool alongX, float along, float across, float height)
        {
            return alongX ? new Vector3(along, height, across) : new Vector3(across, height, along);
        }

        /// <summary>
        /// モデルの -Z を direction へ向ける Y の回転（度）。街灯・信号の腕と、ベンチの背もたれはモデルの -Z の向き。
        /// </summary>
        private static float YawFacing(Vector3 direction)
        {
            return Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 辺に沿った位置 along から幅 type.Width の、道から SETBACK 下げた建物の範囲（x・z）。
        /// </summary>
        private static Rect Footprint(Rect block, Edge edge, float along, BuildingType type)
        {
            return edge switch
            {
                Edge.ZMax => Rect.MinMaxRect(along, block.yMax - SETBACK - type.Depth, along + type.Width, block.yMax - SETBACK),
                Edge.ZMin => Rect.MinMaxRect(along, block.yMin + SETBACK, along + type.Width, block.yMin + SETBACK + type.Depth),
                Edge.XMax => Rect.MinMaxRect(block.xMax - SETBACK - type.Depth, along, block.xMax - SETBACK, along + type.Width),
                _ => Rect.MinMaxRect(block.xMin + SETBACK, along, block.xMin + SETBACK + type.Depth, along + type.Width),
            };
        }

        /// <summary>
        /// 正面（モデルの +Z）を辺の外の道へ向ける Y の回転（度）。
        /// </summary>
        private static float FrontYaw(Edge edge)
        {
            return edge switch
            {
                Edge.ZMax => 0f,
                Edge.XMax => 90f,
                Edge.ZMin => 180f,
                _ => 270f,
            };
        }

        private static bool CanPlace(Rect footprint, List<Rect> occupied)
        {
            var corners = new[]
            {
                new Vector2(footprint.xMin, footprint.yMin), new Vector2(footprint.xMax, footprint.yMin),
                new Vector2(footprint.xMin, footprint.yMax), new Vector2(footprint.xMax, footprint.yMax),
            };
            foreach (var corner in corners)
            {
                if (corner.magnitude > BUILDING_RADIUS) return false;
            }

            if (TouchesTower(footprint, corners)) return false;

            var padded = Rect.MinMaxRect(footprint.xMin - BUILDING_MARGIN, footprint.yMin - BUILDING_MARGIN,
                footprint.xMax + BUILDING_MARGIN, footprint.yMax + BUILDING_MARGIN);
            foreach (var other in occupied)
            {
                if (padded.Overlaps(other)) return false;
            }

            return true;
        }

        /// <summary>
        /// 建物の範囲が天照塔の芯か脚にかかるか。脚は対角線から TOWER_LEG_HALF_WIDTH 以内で、中心から TOWER_LEG_REACH 以内の帯とみなす。
        /// </summary>
        private static bool TouchesTower(Rect footprint, Vector2[] corners)
        {
            var points = new List<Vector2>(corners) { footprint.center };
            foreach (var point in points)
            {
                var radius = point.magnitude;
                if (radius < TOWER_CORE_RADIUS) return true;
                var fromDiagonal = Mathf.Abs(Mathf.Abs(point.x) - Mathf.Abs(point.y)) / Mathf.Sqrt(2f);
                if (radius < TOWER_LEG_REACH && fromDiagonal < TOWER_LEG_HALF_WIDTH) return true;
            }

            return false;
        }

        private static void Shuffle<T>(List<T> list, Random random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private enum Edge
        {
            XMin,
            XMax,
            ZMin,
            ZMax,
        }

        /// <summary> 区画の 1 辺と、その外に接する道の種類。マップの端なら Kind は null </summary>
        private readonly struct BlockSide
        {
            public readonly Edge Edge;
            public readonly RoadKind Kind;

            public BlockSide(Edge edge, RoadKind kind)
            {
                Edge = edge;
                Kind = kind;
            }
        }

        /// <summary> 道に囲まれた長方形の区画。Rect の y は Z </summary>
        private sealed class SubBlock
        {
            public readonly Rect Rect;
            public readonly bool Commercial;
            public readonly RoadKind XMinKind;
            public readonly RoadKind XMaxKind;
            public readonly RoadKind ZMinKind;
            public readonly RoadKind ZMaxKind;
            public readonly BlockSide[] Sides;

            public SubBlock(Rect rect, bool commercial, RoadKind xMinKind, RoadKind xMaxKind, RoadKind zMinKind, RoadKind zMaxKind)
            {
                Rect = rect;
                Commercial = commercial;
                XMinKind = xMinKind;
                XMaxKind = xMaxKind;
                ZMinKind = zMinKind;
                ZMaxKind = zMaxKind;
                Sides = new[]
                {
                    new BlockSide(Edge.XMin, xMinKind), new BlockSide(Edge.XMax, xMaxKind),
                    new BlockSide(Edge.ZMin, zMinKind), new BlockSide(Edge.ZMax, zMaxKind),
                };
            }
        }

        /// <summary> 1 つの軸の上の区間と、両端に接する道の種類 </summary>
        private readonly struct Interval
        {
            public readonly float Min;
            public readonly float Max;
            public readonly RoadKind MinKind;
            public readonly RoadKind MaxKind;

            public Interval(float min, float max, RoadKind minKind, RoadKind maxKind)
            {
                Min = min;
                Max = max;
                MinKind = minKind;
                MaxKind = maxKind;
            }
        }

        private readonly struct Crossing
        {
            public readonly Vector2 Center;
            public readonly RoadKind Kind;

            public Crossing(Vector2 center, RoadKind kind)
            {
                Center = center;
                Kind = kind;
            }
        }

        /// <summary> 建物の種類。Name はプレハブの名前の一部、Width は正面の幅（モデルの X）、Depth は奥行き（モデルの Z）（m） </summary>
        private readonly struct BuildingType
        {
            public readonly string Name;
            public readonly float Width;
            public readonly float Depth;

            public BuildingType(string name, float width, float depth)
            {
                Name = name;
                Width = width;
                Depth = depth;
            }
        }
    }

    /// <summary>
    /// TownLayout が決めた、置く物の一覧。
    /// </summary>
    public sealed class TownPlan
    {
        public readonly List<PartPlacement> Roads = new();
        public readonly List<PartPlacement> Buildings = new();
        public readonly List<PartPlacement> Sites = new();
        public readonly List<PartPlacement> Props = new();
    }

    /// <summary>
    /// 置く物 1 つ。Name は部品やプレハブの名前の一部、Yaw は Y の回転（度）、LengthScale は道路の部品を道の向き（モデルの Z）に伸ばす倍率。
    /// </summary>
    public readonly struct PartPlacement
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float LengthScale;

        public PartPlacement(string name, Vector3 position, float yaw, float lengthScale = 1f)
        {
            Name = name;
            Position = position;
            Yaw = yaw;
            LengthScale = lengthScale;
        }
    }

    /// <summary>
    /// 道の種類。Name は道路の部品の名前の一部、HalfWidth は歩道を含めた幅の半分、SidewalkWidth は片側の歩道の幅（m）。
    /// </summary>
    public sealed class RoadKind
    {
        public readonly string Name;
        public readonly float HalfWidth;
        public readonly float SidewalkWidth;

        public RoadKind(string name, float halfWidth, float sidewalkWidth)
        {
            Name = name;
            HalfWidth = halfWidth;
            SidewalkWidth = sidewalkWidth;
        }
    }

    /// <summary>
    /// 1 本の道。AlongX なら X の向きに延び、Offset は Z の位置。そうでなければ Z の向きに延び、Offset は X の位置。
    /// Min・Max は道が通る、線に沿った座標の範囲。大通りと中通りはマップの端まで、小道は区画の中だけを通す。
    /// </summary>
    public readonly struct RoadLine
    {
        public readonly RoadKind Kind;
        public readonly bool AlongX;
        public readonly float Offset;
        public readonly float Min;
        public readonly float Max;

        public RoadLine(RoadKind kind, bool alongX, float offset, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            Kind = kind;
            AlongX = alongX;
            Offset = offset;
            Min = min;
            Max = max;
        }

        /// <summary>
        /// 線に沿った座標 along が、道の通る範囲の内側にあるか。
        /// </summary>
        public bool Covers(float along)
        {
            return along > Min && along < Max;
        }
    }
}
