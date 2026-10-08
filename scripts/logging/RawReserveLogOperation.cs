using System;
using FarmExchange.Gameplay;

namespace FarmExchange.Logging;

/**
 * <summary>保存一次原料底线请求及修改前值，不执行经营命令。</summary>
 */
public sealed class RawReserveLogOperation
{
    private readonly GameplayLog _owner;
    private readonly CommandObservation _command;
    private readonly CropKind _crop;
    private readonly int _quantity;
    private readonly int? _before;

    internal RawReserveLogOperation(GameplayLog owner, CommandObservation command, CropKind crop, int quantity, int? before)
    { _owner = owner; _command = command; _crop = crop; _quantity = quantity; _before = before; }

    /**
     * <summary>在原底线设置完成后投影实际结果；重复终结无副作用。</summary>
     * <param name="result">本次真实业务返回的失败码或 None。</param>
     */
    public void Complete(RawReserveFailure result) => _owner.ReserveCompleted(_command, _crop, _quantity, _before, result);

    /**
     * <summary>记录原异常并终结关联，不消费异常或重试。</summary>
     * <param name="error">原业务异常，调用方保持原传播。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
