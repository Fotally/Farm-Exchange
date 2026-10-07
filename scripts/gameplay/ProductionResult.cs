using Godot;
using FarmExchange.Farming;
using FarmExchange.Land;

namespace FarmExchange.Gameplay;

public enum ProductionResultKind { Sow, Water, Harvest, Product }

/**
 * <summary>最近经营秒的真实成功结果；只供表现读取，不是持久历史或待执行任务。</summary>
 * <param name="ElapsedSeconds">该完整经营秒结束时的累计秒数。</param>
 * <param name="AnchorCell">发生结果的设施锚点。</param>
 * <param name="CropKind">实际作物或加工品对应作物。</param>
 * <param name="Kind">已成功完成的动作或产出。</param>
 * <param name="Quantity">收获或加工数量；作业结果为零。</param>
 * <param name="WorkerNumber">成功作业的工人编号；自动产出为零。</param>
 */
public readonly record struct ProductionResult(uint ElapsedSeconds, Vector2I AnchorCell,
    CropKind CropKind, ProductionResultKind Kind, int Quantity, int WorkerNumber = 0)
{
    internal FarmWorkRequest? CompletedWork { get; init; }
    internal BuildingSpaceSnapshot? Space { get; init; }
}
