using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;

namespace FarmExchange.Weather;

/**
 * <summary>唯一拥有天气预排、循环位置和当前活动区域。</summary>
 * <remarks>自然局只执行开局生成的排程；显式局仅接受开发测试矩形输入。</remarks>
 */
public sealed class WeatherSystem
{
    private readonly record struct ScheduledRegion(uint StartSeconds, uint EndSeconds,
        Rect2I Cells, WeatherKind Kind);

    private readonly WeatherRules _rules;
    private readonly ScheduledRegion[] _schedule;
    private readonly uint[] _firstBoundaries;
    private readonly uint[] _cycleBoundaries;
    private readonly Dictionary<int, WeatherKind> _farmWeather = new();
    private IReadOnlyList<WeatherRegionSnapshot> _activeRegions = Array.AsReadOnly(Array.Empty<WeatherRegionSnapshot>());
    private uint _elapsedSeconds;
    private ulong _nextChange;

    public WeatherMode Mode { get; }
    public uint CycleSeconds => _rules.CycleSeconds;

    /**
     * <summary>当前活动区域的只读独立快照，不包含未来天气。</summary>
     * <remarks>活动状态未变时复用同一引用；实际变化发布新快照，已返回对象不被改写，失败设置／清除及无变化推进保留引用。</remarks>
     */
    public IReadOnlyList<WeatherRegionSnapshot> ActiveRegions => _activeRegions;

    /**
     * <summary>创建局天气，并在自然模式下一次生成完整循环排程。</summary>
     * <param name="seed">独立天气随机种子，不消费开局或行情随机流。</param>
     * <param name="rules">局组装点选定的已验证不可变配置；可共享值，不共享运行状态。</param>
     * <param name="mode">本局固定的自然或显式输入模式。</param>
     * <param name="elapsedSeconds">局开始的累计经营秒。</param>
     */
    public WeatherSystem(int seed, WeatherRules rules, WeatherMode mode = WeatherMode.Natural, uint elapsedSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        _rules = rules;
        _schedule = mode == WeatherMode.Natural ? BuildSchedule(new Random(seed)) : Array.Empty<ScheduledRegion>();
        _firstBoundaries = _schedule.SelectMany(region => new[] { region.StartSeconds, region.EndSeconds })
            .Where(seconds => seconds <= CycleSeconds).Distinct().Order().ToArray();
        _cycleBoundaries = _schedule.SelectMany(region => new[] { region.StartSeconds, region.EndSeconds % CycleSeconds })
            .Distinct().Order().ToArray();
        AdvanceTo(elapsedSeconds);
    }

    /**
     * <summary>按累计经营秒同步天气，允许批量跨越事件与多个循环。</summary>
     * <param name="elapsedSeconds">不早于已同步时刻的经营秒。</param>
     * <remarks>不抽取随机数，不修改农田供水；零秒新局保持全图晴天。</remarks>
     */
    public void AdvanceTo(uint elapsedSeconds)
    {
        if (elapsedSeconds < _elapsedSeconds)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "天气不能倒退。");
        _elapsedSeconds = elapsedSeconds;
        if (Mode == WeatherMode.Explicit || elapsedSeconds < _nextChange)
            return;
        uint phase = elapsedSeconds % CycleSeconds;
        var active = new List<WeatherRegionSnapshot>();
        foreach (ScheduledRegion region in _schedule)
        {
            bool current = phase >= region.StartSeconds && phase < region.EndSeconds;
            bool precedingTail = elapsedSeconds >= CycleSeconds && region.EndSeconds > CycleSeconds &&
                phase < region.EndSeconds - CycleSeconds;
            if (current || precedingTail)
                active.Add(new WeatherRegionSnapshot(region.Cells, region.Kind));
        }
        if (!_activeRegions.SequenceEqual(active))
        {
            _activeRegions = active.AsReadOnly();
            _farmWeather.Clear();
        }
        _nextChange = (ulong)elapsedSeconds + GetNextEventSeconds(elapsedSeconds);
    }

    /**
     * <summary>查询某个基础格的真实天气，区域之外为晴天。</summary>
     * <param name="cell">地图内的基础格坐标。</param>
     * <returns>该格当前互斥天气。</returns>
     */
    public WeatherKind GetWeather(Vector2I cell)
    {
        if (cell.X < 0 || cell.Y < 0 || cell.X >= FarmGame.MapSize || cell.Y >= FarmGame.MapSize)
            throw new ArgumentOutOfRangeException(nameof(cell));
        for (int index = 0; index < _activeRegions.Count; index++)
        {
            WeatherRegionSnapshot region = _activeRegions[index];
            if (region.Cells.HasPoint(cell))
                return region.Kind;
        }
        return WeatherKind.Sunny;
    }

    /**
     * <summary>按整座农田全部占地查询天气，任一雨格优先，其次云格。</summary>
     * <param name="farm">土地模块提供的真实农田空间快照。</param>
     * <returns>供水、产量与详情共用的整田天气。</returns>
     */
    public WeatherKind GetFarmWeather(BuildingSpaceSnapshot farm)
    {
        if (farm.Building != BuildingKind.Farm)
            throw new ArgumentException("整田天气仅接受农田空间。", nameof(farm));
        if (_farmWeather.TryGetValue(farm.AnchorIndex, out WeatherKind cached))
            return cached;
        WeatherKind result = WeatherKind.Sunny;
        for (int index = 0; index < farm.Footprint.Offsets.Count; index++)
        {
            Vector2I offset = farm.Footprint.Offsets[index];
            WeatherKind kind = GetWeather(farm.AnchorCell + offset);
            if (kind == WeatherKind.Rain)
            {
                result = kind;
                break;
            }
            if (kind == WeatherKind.Cloudy)
                result = kind;
        }
        _farmWeather[farm.AnchorIndex] = result;
        return result;
    }

    /**
     * <summary>查询严格晚于指定经营秒的下一天气事件距离。</summary>
     * <param name="now">累计经营秒。</param>
     * <returns>严格正秒数；显式模式或无事件返回 uint.MaxValue。</returns>
     */
    public uint GetNextEventSeconds(uint now)
    {
        if (_cycleBoundaries.Length == 0)
            return uint.MaxValue;
        uint phase = now % CycleSeconds;
        uint[] boundaries = now < CycleSeconds ? _firstBoundaries : _cycleBoundaries;
        int index = Array.BinarySearch(boundaries, phase);
        index = index >= 0 ? index + 1 : ~index;
        if (index < boundaries.Length)
            return boundaries[index] - phase;
        return CycleSeconds - phase + _cycleBoundaries[0];
    }

    /**
     * <summary>显式局设置一个互斥矩形天气，持续到明确清除。</summary>
     * <param name="cells">完整位于地图内、宽高为正的基础格矩形。</param>
     * <param name="kind">多云或降雨；恢复晴天使用清除操作。</param>
     * <param name="reason">正常拒绝原因，成功为空。</param>
     * <returns>成功登记或失败且状态零修改。</returns>
     */
    public bool TrySetRegion(Rect2I cells, WeatherKind kind, out string reason)
    {
        if (!CheckExplicitRectangle(cells, out reason))
            return false;
        if (kind != WeatherKind.Cloudy && kind != WeatherKind.Rain)
            reason = "活动区域只能设置多云或降雨，恢复晴天请清除区域。";
        else if (_activeRegions.Count >= _rules.MaximumActiveRegions)
            reason = "活动天气区域已达到数量上限。";
        else if (_activeRegions.Any(region => region.Cells.Intersects(cells)))
            reason = "天气区域不能与已有区域重叠。";
        else
        {
            _activeRegions = _activeRegions.Append(new WeatherRegionSnapshot(cells, kind)).ToList().AsReadOnly();
            _farmWeather.Clear();
            return true;
        }
        return false;
    }

    /**
     * <summary>显式局完整移除指定活动矩形，恢复默认晴天。</summary>
     * <param name="cells">已登记区域的原矩形，不能部分裁剪。</param>
     * <param name="reason">正常拒绝原因，成功为空。</param>
     * <returns>成功清除或失败且状态零修改。</returns>
     */
    public bool TryClearRegion(Rect2I cells, out string reason)
    {
        if (!CheckExplicitRectangle(cells, out reason))
            return false;
        if (!_activeRegions.Any(region => region.Cells == cells))
        {
            reason = "不存在与指定矩形完全一致的天气区域。";
            return false;
        }
        _activeRegions = _activeRegions.Where(region => region.Cells != cells).ToList().AsReadOnly();
        _farmWeather.Clear();
        return true;
    }

    private bool CheckExplicitRectangle(Rect2I cells, out string reason)
    {
        reason = "";
        if (Mode != WeatherMode.Explicit)
            reason = "自然天气局不接受显式区域修改。";
        else if (cells.Position.X < 0 || cells.Position.Y < 0 || cells.Size.X <= 0 || cells.Size.Y <= 0 ||
            (long)cells.Position.X + cells.Size.X > FarmGame.MapSize ||
            (long)cells.Position.Y + cells.Size.Y > FarmGame.MapSize)
            reason = "天气矩形必须完整位于地图内且宽高为正。";
        return reason.Length == 0;
    }

    private ScheduledRegion[] BuildSchedule(Random random)
    {
        var accepted = new List<ScheduledRegion>();
        int cycleDays = checked(_rules.CycleYears * _rules.DaysPerYear);
        int startDay = 0;
        while (true)
        {
            startDay += random.Next(_rules.MinimumIntervalDays, _rules.MaximumIntervalDays + 1);
            if (startDay >= cycleDays)
                break;
            int durationDays = random.Next(_rules.MinimumDurationDays, _rules.MaximumDurationDays + 1);
            int width = random.Next(_rules.MinimumRegionSizeCells, _rules.MaximumRegionSizeCells + 1);
            int height = random.Next(_rules.MinimumRegionSizeCells, _rules.MaximumRegionSizeCells + 1);
            var cells = new Rect2I(random.Next(FarmGame.MapSize - width + 1),
                random.Next(FarmGame.MapSize - height + 1), width, height);
            WeatherKind kind = random.Next(_rules.CloudyWeight + _rules.RainWeight) < _rules.CloudyWeight
                ? WeatherKind.Cloudy : WeatherKind.Rain;
            var candidate = new ScheduledRegion(DayStartSeconds(startDay),
                DayStartSeconds(startDay + durationDays), cells, kind);
            if (CanAccept(candidate, accepted))
                accepted.Add(candidate);
        }
        return accepted.ToArray();
    }

    private bool CanAccept(ScheduledRegion candidate, List<ScheduledRegion> accepted)
    {
        var overlaps = new List<(long Start, long End)>();
        foreach (ScheduledRegion region in accepted)
            for (int cycleOffset = -1; cycleOffset <= 1; cycleOffset++)
            {
                long start = region.StartSeconds + (long)cycleOffset * CycleSeconds;
                long end = region.EndSeconds + (long)cycleOffset * CycleSeconds;
                if (start >= candidate.EndSeconds || end <= candidate.StartSeconds)
                    continue;
                if (region.Cells.Intersects(candidate.Cells))
                    return false;
                overlaps.Add((start, end));
            }
        foreach (long point in overlaps.Select(overlap => overlap.Start).Append(candidate.StartSeconds))
        {
            if (point < candidate.StartSeconds || point >= candidate.EndSeconds)
                continue;
            int activeCount = 1 + overlaps.Count(overlap => overlap.Start <= point && point < overlap.End);
            if (activeCount > _rules.MaximumActiveRegions)
                return false;
        }
        return true;
    }

    private static uint DayStartSeconds(int day) => checked((uint)(
        ((long)day * GameTimeUnits.PerDay + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond));
}
