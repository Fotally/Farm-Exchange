using System;
using System.Collections.Generic;
using System.Linq;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Cultivation;

internal sealed class CultivationPlanBook
{
    private const int DaysPerYear = 336;
    private const long YearUnits = DaysPerYear * GameTimeUnits.PerDay;
    private readonly FarmingSystem _farming;
    private readonly Dictionary<int, Plan> _plans = new();
    private readonly Dictionary<int, Binding> _bindings = new();
    private int _nextId = 1;

    internal CultivationPlanBook(FarmingSystem farming) => _farming = farming;

    internal static CultivationValidation Validate(CultivationPlanRequest request)
    {
        var risks = new List<int>();
        if (string.IsNullOrWhiteSpace(request.Name))
            return new("请输入耕作表名称", risks.AsReadOnly());
        if (!Enum.IsDefined(request.Mode))
            return new("无效执行方式", risks.AsReadOnly());
        var ids = new HashSet<int>();
        foreach (CultivationEntry entry in request.Entries)
        {
            if (entry.Id <= 0 || !ids.Add(entry.Id))
                return new("作物条编号必须为正数且不能重复", risks.AsReadOnly());
            if (!CropCatalog.IsDefined(entry.Crop) || entry.StartDay < 0 || entry.StartDay >= DaysPerYear)
                return new("无效作物或年度日期", risks.AsReadOnly());
            CropDefinition crop = CropCatalog.Get(entry.Crop);
            for (int day = 0; day < crop.GrowthDays; day++)
            {
                Season season = (Season)(((entry.StartDay + day) % DaysPerYear) / 84);
                if ((crop.GrowingSeasons & (GrowingSeasons)(1 << (int)season)) == 0)
                {
                    risks.Add(entry.Id);
                    break;
                }
            }
        }
        CultivationEntry[] sorted = request.Entries.OrderBy(entry => entry.StartDay).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            CultivationEntry current = sorted[i];
            CultivationEntry next = sorted[(i + 1) % sorted.Length];
            int nextDay = next.StartDay + (i == sorted.Length - 1 ? DaysPerYear : 0);
            int gap = nextDay - current.StartDay - current.LengthDays;
            if (gap < 0)
                return new("作物条重叠（包含冬春年度首尾）", risks.AsReadOnly());
            if (current.Crop != next.Crop && gap < 1)
                return new("不同作物之间至少留一个游戏日", risks.AsReadOnly());
        }
        return new(null, risks.AsReadOnly());
    }

    internal CultivationCommandResult Create(CultivationPlanRequest request)
    {
        CultivationValidation check = Validate(request);
        if (!check.Success)
            return new(0, check.Error);
        int id = _nextId++;
        _plans.Add(id, new Plan(id, request));
        return new(id, null);
    }

    internal CultivationCommandResult Update(int id, CultivationPlanRequest request, long now)
    {
        if (!_plans.ContainsKey(id))
            return new(id, "耕作表不存在");
        CultivationValidation check = Validate(request);
        if (!check.Success)
            return new(id, check.Error);
        _plans[id] = new Plan(id, request);
        foreach ((int index, Binding binding) in _bindings)
            if (binding.PlanId == id)
                ResetArrangement(index, binding, now);
        return new(id, null);
    }

    internal bool Contains(int id) => _plans.ContainsKey(id);

    internal IReadOnlyList<CultivationPlanSnapshot> GetSnapshots() => Array.AsReadOnly(
        _plans.Values.Select(plan => new CultivationPlanSnapshot(plan.Id, plan.Name, plan.Mode,
            Array.AsReadOnly((CultivationEntry[])plan.Entries.Clone()),
            _bindings.Values.Count(binding => binding.PlanId == plan.Id))).ToArray());

    internal FarmCultivationSnapshot GetFarm(int index)
    {
        if (!_bindings.TryGetValue(index, out Binding? binding))
            return default;
        Plan? plan = binding.PlanId is int id ? _plans[id] : null;
        FarmSnapshot farm = _farming.Get(index);
        return new(binding.PlanId, plan?.Name, binding.ManualCrop ?? binding.Prepared?.Entry.Crop,
            binding.Prepared?.Start, !farm.SowingEnabled && farm.Stage == CropStage.None,
            farm.Stage == CropStage.Seeded);
    }

    internal void Apply(int id, IReadOnlyList<int> indices, long now)
    {
        foreach (int index in indices)
        {
            var binding = new Binding { PlanId = id };
            if (_bindings.TryGetValue(index, out Binding? previous) && previous.PlanId == id)
                binding.Executed.UnionWith(previous.Executed);
            _bindings[index] = binding;
            ResetArrangement(index, binding, now);
        }
    }

    internal void TakeManualControl(int index, CropKind crop, bool preserveCurrent)
    {
        _bindings.Remove(index);
        if (preserveCurrent && _farming.Get(index).Stage != CropStage.None)
        {
            _bindings[index] = new Binding { ManualCrop = crop, InProgress = true };
            _farming.SetSowingEnabled(index, false);
            return;
        }
        _farming.RestartCrop(index, crop);
        _farming.SetSowingEnabled(index, true);
    }

    internal void RemoveFarm(int index) => _bindings.Remove(index);
    internal void Clear() => _bindings.Clear();

    /**
     * <summary>在换日清理之前记录工人已经完成的实际计划播种。</summary>
     * <remarks>只登记执行凭据，不查询安排、不启用目标或推进经营。</remarks>
     */
    internal void RecordSownCrops()
    {
        foreach ((int index, Binding binding) in _bindings)
            RecordSown(binding, _farming.Get(index));
    }

    private static void RecordSown(Binding binding, FarmSnapshot farm)
    {
        if (farm.Stage != CropStage.None && binding.Active is Occurrence sown)
            binding.Executed.Add(new ExecutedEntry(sown.Entry.Id, sown.Year));
    }

    internal void Harvested(int index, long now)
    {
        if (!_bindings.TryGetValue(index, out Binding? binding))
            return;
        if (binding.ManualCrop is CropKind manual)
        {
            TakeManualControl(index, manual, false);
            return;
        }
        Plan plan = _plans[binding.PlanId!.Value];
        Occurrence? target = binding.Prepared is Occurrence prepared &&
            prepared.Start + prepared.Entry.LengthDays * GameTimeUnits.PerDay > now
            ? prepared : Locate(plan, now, binding.Executed);
        // 工人相位仍使用步进开始的日历；计划目标只能在日历推进后的同步中启用。
        binding.Prepared = target;
        binding.Completion = now;
        binding.InProgress = true;
        _farming.SetSowingEnabled(index, false);
    }

    // 只检查已缓存的事件和完成时间变化；日期未到事件时不重新定位年度表。
    internal void Synchronize(long now)
    {
        foreach ((int index, Binding binding) in _bindings.ToArray())
        {
            FarmSnapshot farm = _farming.Get(index);
            if (binding.ManualCrop is CropKind manual)
            {
                if (farm.Stage == CropStage.None)
                {
                    _farming.RestartCrop(index, manual);
                    _farming.SetSowingEnabled(index, true);
                    _bindings.Remove(index);
                }
                continue;
            }
            Plan plan = _plans[binding.PlanId!.Value];
            PruneExecuted(binding, now);
            RecordSown(binding, farm);
            if (binding.NextEvent is long due && due <= now &&
                (plan.Mode == CultivationMode.Immediate || farm.Stage == CropStage.None))
            {
                Activate(index, binding, Locate(plan, now, binding.Executed), now);
                farm = _farming.Get(index);
            }
            if (farm.Stage != CropStage.None)
            {
                binding.InProgress = true;
                _farming.SetSowingEnabled(index, false);
                long? completion = farm.Stage == CropStage.Growing ? now + farm.RemainingTimeUnits : null;
                if (binding.Completion != completion)
                {
                    binding.Completion = completion;
                    binding.Prepared = completion is long end ? Locate(plan, end, binding.Executed) : null;
                }
            }
            else if (binding.InProgress ||
                (binding.Active is Occurrence interrupted &&
                    binding.Executed.Contains(new ExecutedEntry(interrupted.Entry.Id, interrupted.Year))))
            {
                binding.InProgress = false;
                // 被换季清除或当前轮结束后，只执行当前仍有效的位置，不补历史条。
                Occurrence? target = binding.Completion is long completed && completed <= now &&
                    binding.Prepared is Occurrence prepared &&
                    prepared.Start + prepared.Entry.LengthDays * GameTimeUnits.PerDay > now
                    ? prepared : Locate(plan, now, binding.Executed);
                Activate(index, binding, target, now);
            }
        }
    }

    private void ResetArrangement(int index, Binding binding, long now)
    {
        FarmSnapshot farm = _farming.Get(index);
        binding.Active = null;
        binding.Completion = farm.Stage == CropStage.Growing ? now + farm.RemainingTimeUnits : null;
        binding.InProgress = farm.Stage != CropStage.None;
        Plan plan = _plans[binding.PlanId!.Value];
        PruneExecuted(binding, now);
        binding.Prepared = binding.Completion is long end ? Locate(plan, end, binding.Executed) : null;
        binding.NextEvent = NextBoundary(plan, now);
        _farming.SetSowingEnabled(index, false);
        if (!binding.InProgress)
            Activate(index, binding, Locate(plan, now, binding.Executed), now);
    }

    private void Activate(int index, Binding binding, Occurrence? target, long now)
    {
        Plan plan = _plans[binding.PlanId!.Value];
        binding.InProgress = false;
        binding.Completion = null;
        binding.Prepared = target;
        binding.Active = target is Occurrence current && current.Start <= now ? current : null;
        if (binding.Active is Occurrence active)
        {
            _farming.RestartCrop(index, active.Entry.Crop);
            _farming.SetSowingEnabled(index, true);
        }
        else
        {
            // 到休耕日期先中断未收获一轮；水分依照立即改种规则保留。
            _farming.RestartCrop(index, _farming.Get(index).CropKind);
            _farming.SetSowingEnabled(index, false);
        }
        binding.NextEvent = NextBoundary(plan, now);
    }

    private static Occurrence? Locate(Plan plan, long at, HashSet<ExecutedEntry> executed)
    {
        long year = at / YearUnits;
        Occurrence? next = null;
        foreach (CultivationEntry entry in plan.Entries)
        {
            for (long candidateYear = year - 1; candidateYear <= year + 1; candidateYear++)
            {
                long start = candidateYear * YearUnits + entry.StartDay * GameTimeUnits.PerDay;
                var occurrence = new Occurrence(entry, start);
                if (executed.Contains(new ExecutedEntry(entry.Id, candidateYear)))
                    continue;
                long end = start + entry.LengthDays * GameTimeUnits.PerDay;
                if (start <= at && at < end)
                    return occurrence;
                if (start > at && (next == null || start < next.Value.Start))
                    next = occurrence;
            }
        }
        return next;
    }

    private static long? NextBoundary(Plan plan, long now)
    {
        long year = now / YearUnits;
        long? next = null;
        foreach (CultivationEntry entry in plan.Entries)
        {
            for (long candidateYear = year - 1; candidateYear <= year + 1; candidateYear++)
            {
                long start = candidateYear * YearUnits + entry.StartDay * GameTimeUnits.PerDay;
                foreach (long point in new[] { start, start + entry.LengthDays * GameTimeUnits.PerDay })
                    if (point > now && (next == null || point < next.Value))
                        next = point;
            }
        }
        return next;
    }

    private static void PruneExecuted(Binding binding, long now)
    {
        long year = now / YearUnits;
        if (binding.ExecutedYear == year)
            return;
        binding.Executed.RemoveWhere(entry => entry.Year < year - 1);
        binding.ExecutedYear = year;
    }

    private readonly record struct Occurrence(CultivationEntry Entry, long Start)
    {
        internal long Year => (Start - Entry.StartDay * GameTimeUnits.PerDay) / YearUnits;
    }
    private readonly record struct ExecutedEntry(int Id, long Year);

    private sealed class Binding
    {
        internal int? PlanId;
        internal CropKind? ManualCrop;
        internal Occurrence? Active;
        internal readonly HashSet<ExecutedEntry> Executed = new();
        internal long ExecutedYear = -1;
        internal Occurrence? Prepared;
        internal long? Completion;
        internal long? NextEvent;
        internal bool InProgress;
    }

    private sealed class Plan
    {
        internal readonly int Id;
        internal readonly string Name;
        internal readonly CultivationMode Mode;
        internal readonly CultivationEntry[] Entries;
        internal Plan(int id, CultivationPlanRequest request)
        {
            Id = id;
            Name = request.Name.Trim();
            Mode = request.Mode;
            Entries = request.Entries.OrderBy(entry => entry.StartDay).ToArray();
        }
    }
}
