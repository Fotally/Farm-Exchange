using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Godot;
using BigInteger = System.Numerics.BigInteger;

namespace FarmExchange.Terrain;

internal sealed class TerrainPond
{
    internal required TerrainPoint Center;
    internal required TerrainPoint[] Shore;
    internal required TerrainPoint[] Hull;
    internal required TerrainPoint[] Envelope;
    internal required Vector2I[] WaterVertices;
    internal required TerrainNumber Area;
    internal double MinX, MaxX, MinY, MaxY;
    internal void SetBounds()
    {
        // 整格外包只用于提前排除明显远处对象，不参与几何接受判定。
        MinX = Math.Floor(Envelope.Min(p => p.X)) - 1; MaxX = Math.Ceiling(Envelope.Max(p => p.X)) + 1;
        MinY = Math.Floor(Envelope.Min(p => p.Y)) - 1; MaxY = Math.Ceiling(Envelope.Max(p => p.Y)) + 1;
    }
    internal bool Near(TerrainPoint p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    internal bool Near(TerrainPoint a, TerrainPoint b) => Math.Max(a.X, b.X) >= MinX && Math.Min(a.X, b.X) <= MaxX && Math.Max(a.Y, b.Y) >= MinY && Math.Min(a.Y, b.Y) <= MaxY;
}

internal static class TerrainGenerator
{
    private const int LayoutBudget = 4;
    private const int CandidateBudget = 256;
    private static readonly Lazy<TerrainPoint[][][]> Templates = new(LoadTemplates);
    internal static void PrepareTemplates() => _ = Templates.Value;
    private static TerrainNumber Number(decimal n) => TerrainNumber.FromDecimal(n);
    internal static TerrainPond[] Generate(int seed, TerrainRules rules, out int candidates, CancellationToken cancellationToken = default)
    {
        candidates = 0;
        if (rules.TotalPondCount > (long)rules.MapSize * rules.MapSize)
            throw new TerrainCreationException("容量", "每座独立池塘至少占用一个不同水顶点，数量超出地图容量。");
        string rejection = "尚无满足约束的布局";
        for (int layout = 0; layout < LayoutBudget; layout++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var random = new TerrainRandom(unchecked((ulong)(uint)seed ^ ((ulong)layout * 0x9e3779b97f4a7c15UL) ^ 0x5445525241494e31UL));
            var ponds = new List<TerrainPond>();
            int expected = 0;
            foreach ((TerrainRules.PondBand band, bool near, int count) in new[]
            {
                (rules.Small, true, 1), (rules.Large, false, rules.Large.Count),
                (rules.Medium, false, rules.Medium.Count), (rules.Small, false, rules.Small.Count - 1)
            })
            {
                expected += count;
                for (int ordinal = 0; ordinal < count; ordinal++)
                {
                    bool accepted = false;
                    for (int candidate = 0; candidate < CandidateBudget; candidate++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        candidates++;
                        TerrainNumber longAxis = near ? Number(rules.NearPond.LongAxis) : ChooseAxis(band.LongAxis.Min, band.LongAxis.Max, rules, random);
                        TerrainNumber shortAxis = near ? Number(rules.NearPond.ShortAxis) : ChooseAxis(band.ShortAxis.Min, band.ShortAxis.Max, rules, random);
                        if (longAxis < shortAxis) { rejection = "候选长轴小于短轴"; continue; }
                        // 有理单位圆参数；不调用跨平台末位未保证的三角函数。
                        TerrainNumber t = near ? 0 : new TerrainNumber(random.NextInt(511) - 255, 256);
                        TerrainPoint axis = new((1 - t * t) / (1 + t * t), 2 * t / (1 + t * t));
                        TerrainPoint center;
                        if (near)
                        {
                            TerrainNumber middle = new(rules.MapSize - 1, 2);
                            TerrainNumber offset = Number(rules.NearPond.CenterDistance.Min) + shortAxis / 2 + new TerrainNumber(1, 2);
                            if (candidate > 0) offset += random.Range(0, Number(rules.NearPond.CenterDistance.Max - rules.NearPond.CenterDistance.Min));
                            center = new(middle + new TerrainNumber(1, 2), middle + offset);
                        }
                        else if (!ChooseCenter(rules, longAxis, random, out center))
                        { rejection = "保护区外没有容纳该档参考椭圆的分区"; break; }
                        TerrainPond? pond = Build(center, axis, longAxis, shortAxis, rules, band, near, out rejection, cancellationToken);
                        if (pond is null) continue;
                        bool separated = true;
                        foreach (TerrainPond previous in ponds)
                        {
                            if (TerrainGeometry.ConvexPolygonsTouch(pond.Envelope, previous.Envelope))
                            { separated = false; rejection = "候选圆净空外包相交"; break; }
                            // 凸包距离是最终岸线距离的下界，以精确下界认证硬间距。
                            if (!SeparatedBy(pond.Hull, previous.Hull, Number(rules.MinimumPondSeparation), cancellationToken))
                            { separated = false; rejection = "最终岸线间距的精确凸包下界不足"; break; }
                        }
                        if (!separated) continue;
                        ponds.Add(pond); accepted = true; break;
                    }
                    if (!accepted) break;
                }
                if (ponds.Count != expected) break;
            }
            if (ponds.Count != rules.TotalPondCount) continue;
            TerrainNumber area = 0;
            foreach (TerrainPond pond in ponds) area += pond.Area;
            TerrainNumber coverage = area / rules.MapSize / rules.MapSize;
            if (coverage < Number(rules.WaterCoverage.Min) || coverage > Number(rules.WaterCoverage.Max))
            { rejection = "最终有理岸线面积不在配置允许范围"; continue; }
            return ponds.ToArray();
        }
        throw new TerrainCreationException("有限候选", $"已用尽 {LayoutBudget} 次完整布局、每座最多 {CandidateBudget} 次候选；最后未通过条件：{rejection}。未更改任何配置。");
    }
    // 目标覆盖率移动候选尺寸分布；最终岸线面积仍按硬区间独立验收。
    private static TerrainNumber ChooseAxis(decimal minimum, decimal maximum, TerrainRules rules, TerrainRandom random)
    {
        TerrainNumber fraction = rules.WaterCoverage.Max == rules.WaterCoverage.Min ? new TerrainNumber(1, 2)
            : Number((rules.WaterCoverage.Target - rules.WaterCoverage.Min) / (rules.WaterCoverage.Max - rules.WaterCoverage.Min));
        TerrainNumber sample = (random.Range(0, 1) + fraction) / 2;
        return Number(minimum) + Number(maximum - minimum) * sample;
    }
    private static bool ChooseCenter(TerrainRules rules, TerrainNumber longAxis, TerrainRandom random, out TerrainPoint center)
    {
        TerrainNumber margin = rules.MapEdgeLandWidth + longAxis / 2 + 1;
        TerrainNumber maximum = rules.MapSize - 1 - margin;
        TerrainNumber left = rules.ProtectionCells.Position.X - longAxis / 2 - Number(rules.CenterProtection.ShoreClearance) - 1;
        TerrainNumber right = rules.ProtectionCells.End.X + longAxis / 2 + Number(rules.CenterProtection.ShoreClearance);
        TerrainNumber top = rules.ProtectionCells.Position.Y - longAxis / 2 - Number(rules.CenterProtection.ShoreClearance) - 1;
        TerrainNumber bottom = rules.ProtectionCells.End.Y + longAxis / 2 + Number(rules.CenterProtection.ShoreClearance);
        var regions = new List<(TerrainNumber X0, TerrainNumber Y0, TerrainNumber X1, TerrainNumber Y1)>();
        Add(margin, margin, left, maximum); Add(right, margin, maximum, maximum);
        Add(TerrainNumber.Max(margin, left), margin, TerrainNumber.Min(maximum, right), top);
        Add(TerrainNumber.Max(margin, left), bottom, TerrainNumber.Min(maximum, right), maximum);
        void Add(TerrainNumber x0, TerrainNumber y0, TerrainNumber x1, TerrainNumber y1)
        { if (x1 > x0 && y1 > y0) regions.Add((x0, y0, x1, y1)); }
        TerrainNumber total = 0;
        foreach (var r in regions) total += (r.X1 - r.X0) * (r.Y1 - r.Y0);
        if (total == 0) { center = default; return false; }
        TerrainNumber selected = random.Range(0, total);
        foreach (var r in regions)
        {
            TerrainNumber area = (r.X1 - r.X0) * (r.Y1 - r.Y0);
            if (selected < area) { center = new(random.Range(r.X0, r.X1), random.Range(r.Y0, r.Y1)); return true; }
            selected -= area;
        }
        throw new InvalidOperationException("半开分区随机必须命中实际可用区域。");
    }
    private static TerrainPond? Build(TerrainPoint center, TerrainPoint axis, TerrainNumber longAxis, TerrainNumber shortAxis,
        TerrainRules rules, TerrainRules.PondBand band, bool near, out string rejection, CancellationToken cancellationToken)
    {
        rejection = "候选岸线尚未验证";
        var water = new HashSet<Vector2I>();
        int range = (int)Math.Ceiling(longAxis.ToDouble() / 2) + 2;
        TerrainNumber half = new(1, 2);
        TerrainPoint perpendicular = new(-axis.ExactY, axis.ExactX);
        TerrainNumber[] coefficients =
        {
            2 * axis.ExactX / longAxis, 2 * axis.ExactY / longAxis,
            (-axis.ExactX - axis.ExactY - 2 * TerrainGeometry.Dot(center, axis)) / longAxis,
            2 * perpendicular.ExactX / shortAxis, 2 * perpendicular.ExactY / shortAxis,
            (-perpendicular.ExactX - perpendicular.ExactY - 2 * TerrainGeometry.Dot(center, perpendicular)) / shortAxis
        };
        BigInteger denominator = BigInteger.One;
        foreach (TerrainNumber coefficient in coefficients) denominator = denominator / BigInteger.GreatestCommonDivisor(denominator, coefficient.Denominator) * coefficient.Denominator;
        BigInteger[] integer = coefficients.Select(c => c.Numerator * (denominator / c.Denominator)).ToArray();
        BigInteger denominatorSquared = denominator * denominator;
        for (int y = Math.Max(0, (int)center.Y - range); y <= Math.Min(rules.MapSize, (int)center.Y + range); y++)
            for (int x = Math.Max(0, (int)center.X - range); x <= Math.Min(rules.MapSize, (int)center.X + range); x++)
            {
                BigInteger u = integer[0] * x + integer[1] * y + integer[2];
                BigInteger v = integer[3] * x + integer[4] * y + integer[5];
                if (u * u + v * v <= denominatorSquared) water.Add(new(x, y));
            }
        var cells = new HashSet<Vector2I>();
        foreach (Vector2I vertex in water)
            foreach (Vector2I offset in new[] { Vector2I.Zero, new Vector2I(-1, 0), new Vector2I(-1, -1), new Vector2I(0, -1) }) cells.Add(vertex + offset);
        if (cells.Count == 0) { rejection = "参考椭圆未形成水域"; return null; }
        foreach (Vector2I cell in cells)
            if (cell.X < rules.MapEdgeLandWidth || cell.Y < rules.MapEdgeLandWidth || cell.X >= rules.MapSize - rules.MapEdgeLandWidth || cell.Y >= rules.MapSize - rules.MapEdgeLandWidth || rules.ProtectionCells.HasPoint(cell))
            { rejection = "含水格侵入地图边缘或中心完整陆地"; return null; }
        var neighbors = new Dictionary<TerrainPoint, List<TerrainPoint>>();
        foreach (Vector2I cell in cells.OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int mask = (water.Contains(cell) ? 1 : 0) | (water.Contains(cell + Vector2I.Right) ? 2 : 0) | (water.Contains(cell + Vector2I.One) ? 4 : 0) | (water.Contains(cell + Vector2I.Down) ? 8 : 0);
            foreach (TerrainPoint[] line in Templates.Value[mask])
                for (int i = 1; i < line.Length; i++)
                {
                    TerrainPoint a = TerrainPoint.Add(line[i - 1], new TerrainPoint(cell.X - half, cell.Y - half));
                    TerrainPoint b = TerrainPoint.Add(line[i], new TerrainPoint(cell.X - half, cell.Y - half));
                    if (!neighbors.TryGetValue(a, out var aa)) neighbors[a] = aa = new();
                    if (!neighbors.TryGetValue(b, out var bb)) neighbors[b] = bb = new();
                    aa.Add(b); bb.Add(a);
                }
        }
        if (neighbors.Count < 3 || neighbors.Values.Any(n => n.Count != 2)) { rejection = "岸线接缝未形成封闭二度图"; return null; }
        var shore = new List<TerrainPoint>();
        TerrainPoint first = neighbors.Keys.OrderBy(p => p.ExactX).ThenBy(p => p.ExactY).First(), current = first;
        TerrainPoint? previousPoint = null;
        do
        {
            shore.Add(current);
            TerrainPoint next = neighbors[current][0] == previousPoint ? neighbors[current][1] : neighbors[current][0];
            previousPoint = current; current = next;
        } while (current != first && shore.Count <= neighbors.Count);
        if (current != first || shore.Count != neighbors.Count) { rejection = "候选存在多个水体闭环或内部岛屿"; return null; }
        TerrainNumber area2 = TerrainGeometry.TwiceArea(shore);
        if (area2 < 0) { shore.Reverse(); area2 = -area2; }
        for (int i = 0; i < shore.Count; i++)
            if (TerrainGeometry.Turn(shore[i], shore[(i + 1) % shore.Count], center) <= 0)
            { rejection = "最终岸线不是单一严格星形池塘"; return null; }
        TerrainNumber minU = TerrainGeometry.Dot(shore[0], axis), maxU = minU, minV = TerrainGeometry.Dot(shore[0], perpendicular), maxV = minV;
        TerrainNumber perturb = Number(rules.MaximumOutlinePerturbation);
        TerrainNumber inner = (1 - perturb) * (1 - perturb), outer = (1 + perturb) * (1 + perturb);
        const int shoreScale = 1 << 24;
        BigInteger normalizedScaleSquared = denominatorSquared * shoreScale * shoreScale;
        (BigInteger U, BigInteger V) NormalizeInteger(TerrainPoint p)
        {
            BigInteger x = p.ExactX.Numerator * (shoreScale / p.ExactX.Denominator) + shoreScale / 2;
            BigInteger y = p.ExactY.Numerator * (shoreScale / p.ExactY.Denominator) + shoreScale / 2;
            return (integer[0] * x + integer[1] * y + integer[2] * shoreScale,
                integer[3] * x + integer[4] * y + integer[5] * shoreScale);
        }
        for (int i = 0; i < shore.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TerrainPoint point = shore[i], next = shore[(i + 1) % shore.Count];
            TerrainNumber u = TerrainGeometry.Dot(point, axis), v = TerrainGeometry.Dot(point, perpendicular);
            minU = TerrainNumber.Min(minU, u); maxU = TerrainNumber.Max(maxU, u); minV = TerrainNumber.Min(minV, v); maxV = TerrainNumber.Max(maxV, v);
            var a = NormalizeInteger(point); var b = NormalizeInteger(next);
            BigInteger lengthSquared = a.U * a.U + a.V * a.V;
            BigInteger du = b.U - a.U, dv = b.V - a.V, segmentSquared = du * du + dv * dv;
            BigInteger projection = -(a.U * du + a.V * dv);
            bool inside;
            if (projection <= 0) inside = lengthSquared * inner.Denominator < inner.Numerator * normalizedScaleSquared;
            else if (projection >= segmentSquared) inside = (b.U * b.U + b.V * b.V) * inner.Denominator < inner.Numerator * normalizedScaleSquared;
            else
            {
                BigInteger cross = a.U * dv - a.V * du;
                inside = cross * cross * inner.Denominator < inner.Numerator * normalizedScaleSquared * segmentSquared;
            }
            if (lengthSquared * outer.Denominator > outer.Numerator * normalizedScaleSquared || inside)
            { rejection = "最终岸线相对参考半轴的径向扰动超限"; return null; }
        }
        if (maxU - minU < Number(band.LongAxis.Min) || maxU - minU > Number(band.LongAxis.Max) || maxV - minV < Number(band.ShortAxis.Min) || maxV - minV > Number(band.ShortAxis.Max) || maxU - minU < maxV - minV)
        { rejection = "最终岸线主轴投影跨度超限"; return null; }
        TerrainNumber radius = Number(rules.WorkerFootRadius);
        TerrainPoint[] hull = TerrainGeometry.ConvexHull(shore);
        TerrainPoint[] envelope = TerrainGeometry.Envelope(hull, radius);
        TerrainNumber mapMin = radius - half, mapMax = rules.MapSize - half - radius;
        if (envelope.Any(p => p.ExactX <= mapMin || p.ExactY <= mapMin || p.ExactX >= mapMax || p.ExactY >= mapMax))
        { rejection = "圆净空外包触及地图内缩边界"; return null; }
        TerrainNumber x0 = rules.ProtectionCells.Position.X - half, y0 = rules.ProtectionCells.Position.Y - half;
        TerrainNumber x1 = rules.ProtectionCells.End.X - half, y1 = rules.ProtectionCells.End.Y - half;
        TerrainPoint[] protection = { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };
        if (!SeparatedBy(hull, protection, Number(rules.CenterProtection.ShoreClearance), cancellationToken))
        { rejection = "最终岸线到中心保护区的精确距离下界不足"; return null; }
        if (near)
        {
            TerrainPoint mapCenter = new(new TerrainNumber(rules.MapSize - 1, 2), new TerrainNumber(rules.MapSize - 1, 2));
            TerrainNumber distance2 = TerrainGeometry.PointDistanceSquared(mapCenter, shore[0], shore[1]);
            for (int i = 1; i < shore.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                distance2 = TerrainNumber.Min(distance2, TerrainGeometry.PointDistanceSquared(mapCenter, shore[i], shore[(i + 1) % shore.Count]));
            }
            TerrainNumber minimum = Number(rules.NearPond.CenterDistance.Min), maximum = Number(rules.NearPond.CenterDistance.Max);
            if (distance2 < minimum * minimum || distance2 > maximum * maximum) { rejection = "近景最终岸线距地图中心超限"; return null; }
        }
        var pond = new TerrainPond { Center = center, Shore = shore.ToArray(), Hull = hull, Envelope = envelope, WaterVertices = water.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray(), Area = area2 / 2 };
        pond.SetBounds(); return pond;
    }
    private static bool SeparatedBy(IReadOnlyList<TerrainPoint> a, IReadOnlyList<TerrainPoint> b, TerrainNumber distance, CancellationToken cancellationToken)
    {
        TerrainNumber gapX = TerrainNumber.Max(0, TerrainNumber.Max(a.Min(p => p.ExactX) - b.Max(p => p.ExactX), b.Min(p => p.ExactX) - a.Max(p => p.ExactX)));
        TerrainNumber gapY = TerrainNumber.Max(0, TerrainNumber.Max(a.Min(p => p.ExactY) - b.Max(p => p.ExactY), b.Min(p => p.ExactY) - a.Max(p => p.ExactY)));
        if ((gapX > 0 || gapY > 0) && gapX * gapX + gapY * gapY >= distance * distance) return true;
        if (TerrainGeometry.ConvexPolygonsTouch(a, b)) return false;
        TerrainNumber squared = distance * distance;
        for (int i = 0; i < a.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int j = 0; j < b.Count; j++)
                if (TerrainGeometry.SegmentDistanceSquared(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count]) < squared) return false;
        }
        return true;
    }
    private static TerrainPoint[][][] LoadTemplates()
    {
        const string source = "res://assets/gameplay/terrain/shoreline_templates.json";
        using FileAccess file = FileAccess.Open(source, FileAccess.ModeFlags.Read);
        if (file is null) throw new TerrainCreationException("岸线模板", $"无法读取 {source}。");
        using JsonDocument document = JsonDocument.Parse(file.GetAsText());
        JsonElement root = document.RootElement;
        if (root.GetProperty("generationVersion").GetInt32() != 1) throw new TerrainCreationException("岸线模板", "不支持的生成版本。");
        int denominator = root.GetProperty("coordinateDenominator").GetInt32();
        var result = new TerrainPoint[16][][];
        result[0] = result[15] = Array.Empty<TerrainPoint[]>();
        foreach (JsonElement row in root.GetProperty("templates").EnumerateArray())
        {
            int mask = row.GetProperty("mask").GetInt32();
            result[mask] = row.GetProperty("contours").EnumerateArray().Select(line => line.EnumerateArray().Select(p => new TerrainPoint(new TerrainNumber(p[0].GetInt32(), denominator), new TerrainNumber(p[1].GetInt32(), denominator))).ToArray()).ToArray();
        }
        if (result.Any(t => t is null)) throw new TerrainCreationException("岸线模板", "水角模板不完整。");
        return result;
    }
    private sealed class TerrainRandom
    {
        private ulong _state;
        internal TerrainRandom(ulong seed) { Next(); _state += seed; Next(); }
        private uint Next()
        {
            ulong previous = _state; _state = unchecked(previous * 6364136223846793005UL + 1442695040888963407UL);
            uint x = (uint)(((previous >> 18) ^ previous) >> 27); int rotation = (int)(previous >> 59);
            return (x >> rotation) | (x << ((-rotation) & 31));
        }
        internal int NextInt(int maximum) => (int)(Next() % (uint)maximum);
        internal TerrainNumber Range(TerrainNumber min, TerrainNumber max) => min + (max - min) * new TerrainNumber(Next(), 4294967296L);
    }
}
