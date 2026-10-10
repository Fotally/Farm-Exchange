using System;
using FarmExchange.Trading;

namespace FarmExchange.Logging;

/**
 * <summary>关联全部加工品一次真实结算和完整逐商品明细，不执行交易。</summary>
 */
public sealed class ProductSaleLogOperation
{
    private readonly TradingLog _context;
    private readonly CommandObservation _command;
    private readonly TradeObservation[]? _before;

    internal ProductSaleLogOperation(TradingLog context, CommandObservation command, TradeObservation[]? before)
    {
        _context = context;
        _command = command;
        _before = before;
    }

    /**
     * <summary>记录原子结算的真实合计、执行价格和商品资源前后值。</summary>
     * <param name="result">真实结算模块返回的完整结果，不能重新查询报价组装。</param>
     * <remarks>共用观察保证重复完成或异常终结无副作用。</remarks>
     */
    public void Complete(ProductSaleResult result) => _context.ProductsCompleted(_command, result, _before);

    /**
     * <summary>记录原业务异常，调用方继续原样传播。</summary>
     * <param name="error">实际异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
