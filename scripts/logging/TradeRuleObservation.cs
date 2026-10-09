using System.Collections.Generic;
using FarmExchange.Inventory;
using FarmExchange.Trading;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>只收集单次交易原顺序已经执行的规则结果，结束后一次提交。</summary>
 */
internal sealed class TradeRuleObservation
{
    private static readonly LogEventDescriptor Event = new(100, "RuleChecked",
        "FarmExchange.Trading.TradingService", LogLevel.Debug, false);
    private readonly GameLog _context;
    private readonly CommodityId? _commodity;
    private readonly bool _buy;
    private readonly bool _order;
    private readonly TradeRuleScope _scope;
    private readonly Dictionary<string, bool> _checks = new();
    private bool _ended;

    internal TradeRuleObservation(GameLog context, CommodityId? commodity, bool buy, bool order, TradeRuleScope scope)
    { _context = context; _commodity = commodity; _buy = buy; _order = order; _scope = scope; }

    internal void Checked(TradeRuleCheck check, bool satisfied) => _context.Observe(() =>
    {
        if (_ended) return;
        _checks.Add(check.ToString(), satisfied);
        if (!satisfied) Complete(false);
    });

    internal void Accepted() => _context.Observe(() => Complete(true));

    private void Complete(bool accepted)
    {
        if (_ended) return;
        _ended = true;
        var fields = _context.Context(_order ? "Orders" : "Command");
        fields["RuleKind"] = "Trade";
        if (_commodity.HasValue) fields["Commodity"] = _commodity.Value.Crop + "." + _commodity.Value.Kind;
        fields["Side"] = _buy ? "Buy" : "Sell";
        fields["RequestMode"] = _scope.ToString();
        fields["CheckResults"] = _checks;
        fields["Outcome"] = accepted ? "Success" : "Rejected";
        _context.Diagnostics.Capture(Event, "交易实际规则检查完成", fields);
    }
}
