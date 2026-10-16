using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Weather;
using Godot;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestProductionDueLoad : Node
{
    private const int FacilityCount = 16384;
    private const int FarmCount = FacilityCount / 2;
    private const int EarlyPairs = 8;
    private const int SampleCount = 3;
    private const CropKind Crop = CropKind.Wheat;

    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        try
        {
            var samples = new List<Measurement>();
            foreach (bool sparse in new[] { true, false })
            {
                RunScenario(sparse, -1);
                for (int sample = 0; sample < SampleCount; sample++)
                    samples.AddRange(RunScenario(sparse, sample));
            }
            string outputPath = ProjectSettings.GlobalizePath("res://build/issue65-validation/production-load.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new
            {
                Scope = "真实生产模块与共享到期索引；非完整FarmGame、非图形FPS",
                FacilityCount,
                Farms = FarmCount,
                Processors = FacilityCount - FarmCount,
                WarmupsPerScenario = 1,
                SamplesPerScenario = SampleCount,
                Runtime = System.Environment.Version.ToString(),
                Measurements = samples,
            }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (Measurement sample in samples)
                GD.Print($"生产到期负载：{sample.Scenario} 样本{sample.Sample + 1} {sample.Stage} " +
                    $"秒{sample.Second}，成功读取{sample.SuccessfulDueReads}，农田完成{sample.FarmsCompleted}，" +
                    $"加工完成{sample.ProcessorsCompleted}，{sample.ElapsedMilliseconds:F4} ms，" +
                    $"热区当前线程分配{sample.AllocatedBytes} B");
            GD.Print($"生产到期负载证据：{outputPath}");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"生产到期负载验证失败：{exception}");
            return false;
        }
    }

    private static Measurement[] RunScenario(bool sparse, int sample)
    {
        var fixture = new Fixture(sparse);
        long dueSecond = fixture.FirstDueSecond;
        var calm = fixture.Measure(dueSecond - 1, sample, "平静");
        fixture.Verify(0, 0);
        Require(calm.SuccessfulDueReads == 0, "平静区间不应完成生产");

        var first = fixture.Measure(dueSecond, sample, "首批到期");
        int expectedPairs = sparse ? EarlyPairs : FarmCount;
        fixture.Verify(expectedPairs, expectedPairs);
        Require(first.FarmsCompleted == expectedPairs && first.ProcessorsCompleted == expectedPairs &&
            first.SuccessfulDueReads == expectedPairs * 2, "首批应在准确到期秒完整结算");
        if (!sparse)
            return new[] { calm, first };

        var remaining = fixture.Measure(dueSecond + 1, sample, "尾批到期");
        fixture.Verify(FarmCount, FarmCount);
        Require(remaining.FarmsCompleted == FarmCount - EarlyPairs &&
            remaining.ProcessorsCompleted == FarmCount - EarlyPairs &&
            remaining.SuccessfulDueReads == FacilityCount - EarlyPairs * 2, "尾批不能漏结算或延迟");
        return new[] { calm, first, remaining };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        private readonly SimulationDueIndex _due = new(FacilityCount);
        private readonly FarmingSystem _farms;
        private readonly ProcessingSystem _processors;
        private readonly GoodsInventory _inventory = new();
        private readonly bool[] _completed = new bool[FacilityCount];
        private readonly bool _sparse;
        private int _rawOutput;
        private int _productOutput;

        internal long FirstDueSecond { get; }

        internal Fixture(bool sparse)
        {
            _sparse = sparse;
            _farms = new FarmingSystem(FacilityCount, _due, 65065);
            _processors = new ProcessingSystem(FacilityCount, _due);
            var crop = CropCatalog.Get(Crop);
            int growthUnits = crop.GrowthDays * GameTimeUnits.PerDay;
            int processingUnits = crop.ProcessingHalfDays * GameTimeUnits.PerHalfDay;
            FirstDueSecond = (growthUnits + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond;
            long processingSeconds = (processingUnits + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond;
            long processorStartSecond = FirstDueSecond - processingSeconds;
            Require(processorStartSecond >= 1 && processorStartSecond < FirstDueSecond,
                "错峰开工必须晚于农田后组开工且早于第一批到期");
            Require((processorStartSecond * GameTimeUnits.PerSecond + processingUnits + GameTimeUnits.PerSecond - 1) /
                GameTimeUnits.PerSecond == FirstDueSecond &&
                ((processorStartSecond + 1) * GameTimeUnits.PerSecond + processingUnits + GameTimeUnits.PerSecond - 1) /
                GameTimeUnits.PerSecond == FirstDueSecond + 1, "错峰后的真实加工目标必须与对应农田目标相同");
            _inventory.AddRaw(Crop, FarmCount * ProcessingSystem.MaxBatchQuantity);
            for (int index = 0; index < FarmCount; index++)
            {
                if (sparse && index == EarlyPairs)
                    AdvanceClocks(1);
                _farms.Place(index, Crop, WeatherKind.Sunny);
                _farms.SupplyWater(index);
                Require(_farms.TryWork(index, new GameCalendar((uint)_due.CurrentSecond).Snapshot), "负载农田播种失败");
            }
            AdvanceClocks(processorStartSecond);
            for (int index = 0; index < FarmCount; index++)
            {
                if (sparse && index == EarlyPairs)
                    AdvanceClocks(processorStartSecond + 1);
                _processors.Place(FarmCount + index, Crop);
                Require(_processors.TryStart(FarmCount + index, _inventory), "负载加工开批失败");
            }
            Require(_due.Count == FacilityCount && _inventory.GetRaw(Crop) == 0,
                "初始化必须建立全部真实生产预约并完整领取原料");
            Require(_due.TryGetNext(out long next) && next == FirstDueSecond, "最近真实预约时间不正确");
        }

        private void AdvanceClocks(long second)
        {
            _due.AdvanceTo(second);
            _farms.AdvanceTo(second);
            _processors.AdvanceTo(second);
        }

        internal Measurement Measure(long second, int sample, string stage)
        {
            int farmsCompleted = 0;
            int processorsCompleted = 0;
            int successfulReads = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            AdvanceClocks(second);
            while (_due.TryPeekDue(out _, out int anchor))
            {
                successfulReads++;
                Require(!_completed[anchor], "同一生产实例不能重复完成");
                if (anchor < FarmCount)
                {
                    Require(_farms.CompleteGrowth(anchor, _inventory, out CropHarvestResult harvest), "到期农田必须立即成功收获");
                    Require(harvest.Crop == Crop && harvest.Quantity > 0, "真实收获结果错误");
                    _inventory.AddRaw(harvest.Crop, harvest.Quantity);
                    _rawOutput += harvest.Quantity;
                    farmsCompleted++;
                }
                else
                {
                    Require(_processors.Complete(anchor, _inventory, out CropKind crop, out int quantity), "到期加工必须立即成功完工");
                    Require(crop == Crop && quantity == ProcessingSystem.MaxBatchQuantity, "锁定批量必须完整产出");
                    _inventory.AddProduct(crop, quantity);
                    _productOutput += quantity;
                    processorsCompleted++;
                }
                _completed[anchor] = true;
            }
            long end = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            return new Measurement(_sparse ? "稀疏" : "集中", sample, stage, second,
                successfulReads, successfulReads + 1, farmsCompleted, processorsCompleted,
                _due.Count, _inventory.GetRaw(Crop), _inventory.GetProduct(Crop),
                (end - start) * 1000.0 / Stopwatch.Frequency, allocated);
        }

        internal void Verify(int expectedFarmCompletions, int expectedProcessorCompletions)
        {
            Require(_due.Count == FacilityCount - expectedFarmCompletions - expectedProcessorCompletions,
                "未到期预约应全部保留，已完成预约应全部释放");
            Require(_inventory.GetRaw(Crop) == _rawOutput && _inventory.GetProduct(Crop) == _productOutput &&
                _productOutput == expectedProcessorCompletions * ProcessingSystem.MaxBatchQuantity,
                "真实库存必须等于全部实际产出，不能丢失或重复入库");
            for (int index = 0; index < FarmCount; index++)
            {
                bool farmDone = index < expectedFarmCompletions;
                bool processorDone = index < expectedProcessorCompletions;
                Require(_completed[index] == farmDone && _completed[FarmCount + index] == processorDone,
                    "完成实例集合不正确");
                long target = FirstDueSecond + (_sparse && index >= EarlyPairs ? 1 : 0);
                int expectedRemaining = checked((int)Math.Max(0, target - _due.CurrentSecond));
                FarmSnapshot farm = _farms.Get(index);
                ProcessorSnapshot processor = _processors.Get(FarmCount + index);
                Require(farm.Stage == (farmDone ? CropStage.None : CropStage.Growing) &&
                    farm.RemainingSeconds == (farmDone ? 0 : expectedRemaining), "农田状态或只读剩余时间错误");
                Require(processor.BatchQuantity == (processorDone ? 0 : ProcessingSystem.MaxBatchQuantity) &&
                    processor.RemainingSeconds == (processorDone ? 0 : expectedRemaining), "加工状态或只读剩余时间错误");
            }
        }
    }

    private sealed record Measurement(string Scenario, int Sample, string Stage, long Second,
        int SuccessfulDueReads, int DueReadCalls, int FarmsCompleted, int ProcessorsCompleted,
        int PendingEvents, int RawStock, int ProductStock, double ElapsedMilliseconds, long AllocatedBytes);
}
