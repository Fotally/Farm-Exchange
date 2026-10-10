using System;
using FarmExchange.Inventory;
using FarmExchange.Trading;

namespace FarmExchange.Logging;

/**
 * <summary>关联一笔单商品买卖请求与一次真实完成结果，不执行经营命令。</summary>
 */
public sealed class TradeLogOperation
{
    private readonly TradingLog _context;
    private readonly CommandObservation _command;
    private readonly CommodityId _commodity;
    private readonly int? _requestedQuantity;
    private readonly bool _buy;
    private readonly TradeObservation? _before;

    internal TradeLogOperation(TradingLog context, CommandObservation command, CommodityId commodity,
        int? requestedQuantity, bool buy, TradeObservation? before)
    {
        _context = context;
        _command = command;
        _commodity = commodity;
        _requestedQuantity = requestedQuantity;
        _buy = buy;
        _before = before;
    }

    /**
     * <summary>观察原交易命令的真实结算或正常拒绝，并结束本次关联。</summary>
     * <param name="result">原业务调用返回的实际 TradeResult，不能用预测结果替代。</param>
     * <remarks>调用方在业务提交后、后续领取原料等操作前调用；重复终结无副作用。</remarks>
     */
    public void Complete(TradeResult result)
    {
        _context.TradeCompleted(_command, _commodity, _requestedQuantity, _buy, result, _before);
    }

    /**
     * <summary>在原交易命令的异常接收点记录原异常并结束本次关联。</summary>
     * <param name="error">原业务异常；调用方仍按原语义传播。</param>
     * <remarks>不消费异常、不执行业务、不重试；重复终结无副作用。</remarks>
     */
    public void Faulted(Exception error)
    {
        _command.Faulted(error);
    }
}
