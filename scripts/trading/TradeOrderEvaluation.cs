using System;

namespace FarmExchange.Trading;

/**
 * <summary>订单本次实际判断遇到的阻塞类别，不包含未执行分支。</summary>
 */
[Flags]
internal enum TradeOrderBlocker
{
    None = 0, Price = 1, Stock = 2, Season = 4, LimitPrice = 8,
    TargetReached = 16, BudgetInsufficient = 32,
}

/**
 * <summary>原业务判断产生的稳定事实；完整检查标记只描述本次真实执行覆盖。</summary>
 */
internal readonly record struct TradeOrderEvaluation(TradeOrderBlocker Blockers,
    TradeOrderBlocker Primary, TradeFailure Failure, bool Complete);
