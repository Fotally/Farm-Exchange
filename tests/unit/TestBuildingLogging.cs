using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Logging;
using FarmExchange.Trading;
using static TestLogging;

public static class TestBuildingLogging
{
    public static bool RunChecks()
    {
        try
        {
            CheckPlacementAndRemoval();
            CheckFrozenFunds();
            CheckPausedProcessorAndReserve();
            CheckOriginsAndIsolation();
            CheckObservationLifecycle();
            return true;
        }
        catch (Exception error) { GD.PrintErr("建造与原料底线日志测试失败：" + error); return false; }
    }

    private static string[] Events(StringWriter text, string name) => Lines(text.ToString()).Where(line => HasEvent(line, name)).ToArray();

    private static void CheckPlacementAndRemoval()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        int originalLength = text.ToString().Length;
        Require(game.CheckPlacement(new(0, 0), BuildingKind.Farm, CropKind.Wheat).Allowed, "合法预览失败");
        Require(text.ToString().Length == originalLength, "预览被记录成提交命令");
        Require(game.TryPlace(new(0, 0), BuildingKind.Farm, CropKind.Wheat, CommandOrigin.Scenario).Success, "任意锚点建造失败");
        Require(game.TryPlace(new(1, 1), BuildingKind.Processor, CropKind.Radish).Failure == LandFailure.Occupied, "子格占用未拒绝");
        Require(game.TryPlace(new(383, 383), BuildingKind.Farm, CropKind.Wheat).Failure == LandFailure.OutOfBounds, "部分越界未拒绝");
        Require(game.TryPlace(new(-1, 0), BuildingKind.Road, CropKind.Wheat).Failure == LandFailure.OutOfBounds, "负坐标未拒绝");
        Require(game.TryPlace(new(10, 10), (BuildingKind)999, CropKind.Wheat).Failure == LandFailure.InvalidBuilding, "非法建筑未拒绝");
        Require(game.TryPlace(new(10, 10), BuildingKind.Farm, (CropKind)999).Failure == LandFailure.InvalidCrop, "非法作物未拒绝");
        Require(game.TryPlace(new(10, 10), BuildingKind.Road, (CropKind)999).Success, "道路不应验证无关作物");
        string[] placed = Events(text, "BuildingPlaced");
        Require(placed.Length == 7 && placed[0].Contains("CommandOrigin: \"Scenario\"") &&
            placed[0].Contains("Cell: { X: 0, Y: 0 }") && placed[0].Contains("Anchor: { X: 0, Y: 0 }") &&
            placed[0].Contains("ChargedCents: 1000") && placed[0].Contains("MoneyBeforeCents: 5000") &&
            placed[0].Contains("MoneyAfterCents: 4000"), "建造实际费用或锚点错误");
        foreach (string rejected in placed.Skip(1).Take(5))
            Require(rejected.Contains("Outcome: \"Rejected\"") && rejected.Contains("Anchor: null") &&
                rejected.Contains("ChargedCents: 0") && rejected.Contains("MoneyBeforeCents: 4000") &&
                rejected.Contains("MoneyAfterCents: 4000"), "建造拒绝记录伪造扣费、锚点或修改资源");
        Require(placed[4].Contains("BuildingKind: \"999\"") && placed[5].Contains("Crop: \"999\"") &&
            !placed[6].Contains("Crop:") && placed[6].Contains("ChargedCents: 100"), "非法输入丢失或道路领域事件含作物");
        string roadRequest = Events(text, "CommandReceived").Last();
        Require(roadRequest.Contains("BuildingKind: \"Road\", Crop: \"999\""), "道路原始作物输入丢失");

        Require(game.RemoveBuilding(new(2, 1)) == null && game.MoneyCents == 3900 &&
            game.GetPlot(new(0, 0)).Building == BuildingKind.None, "子格拆除没有移除整座或退款");
        Require(game.RemoveBuilding(new(2, 1)) != null && game.RemoveBuilding(new(-2, 0)) != null, "无建筑/越界拆除未拒绝");
        Require(game.RemoveBuilding(new(10, 10)) == null, "道路拆除失败");
        string[] removed = Events(text, "BuildingRemoved");
        Require(removed.Length == 4 && removed[0].Contains("Cell: { X: 2, Y: 1 }") &&
            removed[0].Contains("Anchor: { X: 0, Y: 0 }") && removed[0].Contains("BuildingKind: \"Farm\"") &&
            removed[0].Contains("Crop: \"Wheat\""), "拆除丢失原子格或已删除设施快照");
        Require(removed.All(line => line.Contains("ChargedCents: 0") && line.Contains("MoneyBeforeCents: 3900") &&
            line.Contains("MoneyAfterCents: 3900") && !line.Contains("FailureCode:")), "拆除退款或编造枚举失败码");
        Require(removed[1].Contains("Anchor: null") && removed[1].Contains("BuildingKind: null") &&
            removed[2].Contains("RejectionReason: \"地图外地块\"") && !removed[3].Contains("Crop:"), "拆除拒绝或道路字段错误");
        foreach (string finished in Events(text, "CommandFinished"))
            Require(!finished.Contains("ChargedCents:") && !finished.Contains("MoneyBeforeCents:"), "通用结束重复记账");
        AssertSequence(Lines(text.ToString()));
    }

    private static void CheckFrozenFunds()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        var request = new TradeOrderRequest(new(CropKind.Radish, CommodityKind.Raw), TradeOrderSide.Buy,
            TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 0, TradeOrderBudgetMode.FixedBudget,
            4500, 1, CashReserveMode.Amount, 0, new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 1) },
            });
        var order = game.CreateTradeOrder(request);
        Require(order.Success && game.FrozenMoneyCents == 4500, "冻结资金夹具失败");
        Require(game.TryPlace(new(0, 0), BuildingKind.Farm, CropKind.Wheat).Failure == LandFailure.InsufficientFunds,
            "建造动用冻结金额");
        string refused = Events(text, "BuildingPlaced").Single();
        Require(refused.Contains("FailureCode: \"LandFailure.InsufficientFunds\"") &&
            refused.Contains("AvailableMoneyBeforeCents: 500") && refused.Contains("AvailableMoneyAfterCents: 500") &&
            refused.Contains("FrozenMoneyBeforeCents: 4500") && refused.Contains("FrozenMoneyAfterCents: 4500"), "冻结资金拒绝没有真实资源边界");
        Require(game.CancelTradeOrder(order.Id).Success, "释放资金失败");
        for (int i = 0; i < 5; i++) Require(game.BuildFarm(new(i * 3, 0)) == null, "耗尽余额夹具失败");
        Require(game.BuildFarm(new(20, 0)) != null && game.MoneyCents == 0, "余额耗尽未拒绝建造");
        Require(Events(text, "BuildingPlaced").Last().Contains("MoneyAfterCents: 0"), "零余额拒绝日志错误");
    }

    private static void CheckPausedProcessorAndReserve()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        game.SetPaused(true);
        Require(game.BuildProcessor(new(0, 0), CropKind.Radish) == null, "旧加工场地创建失败");
        Require(game.SetRawReserve(CropKind.Radish, 2, CommandOrigin.Scenario) == RawReserveFailure.None, "底线设置失败");
        Require(game.Buy(new(CropKind.Radish, CommodityKind.Raw), 2).Success, "暂停原料买入失败");
        Require(game.SetRawReserve(CropKind.Radish, 0) == RawReserveFailure.None && game.GetRawStock(CropKind.Radish) == 2,
            "设置底线错误触发领取");
        Require(game.SetRawReserve(CropKind.Radish, -5) == RawReserveFailure.InvalidQuantity &&
            game.SetRawReserve((CropKind)999, 4) == RawReserveFailure.InvalidCrop, "底线非法请求未拒绝");
        Require(game.BuildProcessor(new(3, 0), CropKind.Radish) == null && game.GetRawStock(CropKind.Radish) == 0 &&
            game.GetPlot(new(0, 0)).RemainingSeconds > 0 && game.GetPlot(new(3, 0)).RemainingSeconds > 0 &&
            game.Calendar.ElapsedSeconds == 0, "暂停新建未保持原全场领取语义");
        string processor = Events(text, "BuildingPlaced").Last();
        Require(processor.Contains("IsPaused: true") && processor.Contains("Crop: \"Radish\"") &&
            !processor.Contains("StockBefore:") && !processor.Contains("RawConsumed"), "建造事件重复记录领取流量");
        Require(game.RemoveBuilding(new(4, 1)) == null && game.GetRawStock(CropKind.Radish) == 0, "拆除返还已投入原料");
        string[] reserves = Events(text, "RawReserveChanged");
        Require(reserves.Length == 4 && reserves[0].Contains("CommandOrigin: \"Scenario\"") &&
            reserves[0].Contains("PreviousReserveQuantity: 0") && reserves[0].Contains("ReserveQuantity: 2") &&
            reserves[1].Contains("PreviousReserveQuantity: 2") && reserves[1].Contains("ReserveQuantity: 0"), "底线前后值错误");
        Require(reserves[2].Contains("RequestedQuantity: -5") && reserves[2].Contains("PreviousReserveQuantity: 0") &&
            reserves[2].Contains("ReserveQuantity: 0") && reserves[2].Contains("FailureCode: \"RawReserveFailure.InvalidQuantity\"") &&
            reserves[3].Contains("Crop: \"999\"") && reserves[3].Contains("PreviousReserveQuantity: null") &&
            reserves[3].Contains("ReserveQuantity: null"), "拒绝底线丢失原值或伪造合法库存");
    }

    private static void CheckOriginsAndIsolation()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var traced = new FarmGame(17, log);
        using var disabledLog = RuntimeLog.Disabled();
        using var disabled = new FarmGame(17, disabledLog);
        using var brokenWriter = new BrokenWriter();
        using var brokenLog = RuntimeLog.Capture(brokenWriter, diagnostic: _ => { });
        using var broken = new FarmGame(17, brokenLog);
        using var plain = new FarmGame(17);
        foreach (var game in new[] { traced, disabled, broken, plain })
        {
            int before = text.ToString().Length;
            Action[] invalid =
            {
                () => game.TryPlace(new(0, 0), BuildingKind.Farm, CropKind.Wheat, (CommandOrigin)99),
                () => game.RemoveBuilding(new(0, 0), (CommandOrigin)99),
                () => game.SetRawReserve(CropKind.Wheat, 1, (CommandOrigin)99),
                () => game.BuildFarm(new(0, 0), (CommandOrigin)99),
                () => game.BuildProcessor(new(0, 0), CropKind.Wheat, (CommandOrigin)99),
            };
            foreach (Action action in invalid)
            {
                try { action(); Require(false, "非法来源被接受"); }
                catch (ArgumentOutOfRangeException) { }
            }
            Require(game.MoneyCents == 5000 && text.ToString().Length == before, "非法来源改动资源或输出命令");
            Require(game.BuildFarm(new(0, 0)) == null && game.RemoveBuilding(new(1, 1)) == null &&
                game.SetRawReserve(CropKind.Wheat, 5) == RawReserveFailure.None, "等价流程失败");
        }
        foreach (var game in new[] { disabled, broken, plain })
            Require(game.MoneyCents == traced.MoneyCents && game.GetRawReserve(CropKind.Wheat) == 5 &&
                game.GetBuildingSpaces().Select(space => space.AnchorCell).SequenceEqual(traced.GetBuildingSpaces().Select(space => space.AnchorCell)),
                "关闭采集或输出故障改变经营结果");
        Require(brokenLog.Health.FailureCount > 0, "写入故障未被观察");
    }

    private static void CheckObservationLifecycle()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17);
        using var context = log.BindGame(game, 17)!;
        var building = context.Gameplay.BeginPlace(new(0, 0), BuildingKind.Farm, CropKind.Wheat)!;
        building.Complete(game.TryPlace(new(0, 0), BuildingKind.Farm, CropKind.Wheat));
        building.Complete(new PlacementResult(LandFailure.None, 1000));
        building.Faulted(new InvalidOperationException("不应再次记录"));
        var reserve = context.Gameplay.BeginSetRawReserve(CropKind.Wheat, 3)!;
        reserve.Complete(game.SetRawReserve(CropKind.Wheat, 3));
        reserve.Complete(RawReserveFailure.None);
        reserve.Faulted(new InvalidOperationException("不应再次记录"));
        var removed = context.Gameplay.BeginRemove(new(1, 1))!;
        removed.Complete(game.RemoveBuilding(new(1, 1)));
        removed.Complete((string?)null);
        var failedBuilding = context.Gameplay.BeginRemove(new(0, 0))!;
        failedBuilding.Faulted(new InvalidOperationException("原拆除异常"));
        failedBuilding.Faulted(new InvalidOperationException("不应再次记录"));
        var failedReserve = context.Gameplay.BeginSetRawReserve(CropKind.Wheat, 4)!;
        failedReserve.Faulted(new InvalidOperationException("原底线异常"));
        failedReserve.Complete(RawReserveFailure.None);
        Require(Events(text, "CommandReceived").Length == 5 && Events(text, "CommandFinished").Length == 5 &&
            Events(text, "BuildingPlaced").Length == 1 && Events(text, "BuildingRemoved").Length == 1 &&
            Events(text, "RawReserveChanged").Length == 1 && Events(text, "BusinessException").Length == 2,
            "观察重复终结或异常没有一次结束");
        foreach (Action invalid in new Action[]
        {
            () => context.Gameplay.BeginPlace(new(0, 0), BuildingKind.Road, CropKind.Wheat, (CommandOrigin)99),
            () => context.Gameplay.BeginRemove(new(0, 0), (CommandOrigin)99),
            () => context.Gameplay.BeginSetRawReserve(CropKind.Wheat, 0, (CommandOrigin)99),
        })
        {
            try { invalid(); Require(false, "公开观察接受非法来源"); }
            catch (ArgumentOutOfRangeException) { }
        }
        context.Dispose();
        Require(context.Gameplay.BeginPlace(new(0, 0), BuildingKind.Road, CropKind.Wheat) == null &&
            context.Gameplay.BeginRemove(new(0, 0)) == null && context.Gameplay.BeginSetRawReserve(CropKind.Wheat, 0) == null,
            "局结束仍创建观察");
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("测试建造输出故障");
    }
}
