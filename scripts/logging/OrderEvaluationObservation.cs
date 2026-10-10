using System.Collections.Generic;
using FarmExchange.Trading;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>保留选定订单本次真实求值的前八组、合计三十二条条件样本。</summary>
 */
internal sealed class OrderEvaluationObservation
{
    private static readonly LogEventDescriptor Event = new(104, "OrderEvaluated",
        "FarmExchange.Trading.TradeOrderBook", LogLevel.Trace, false);
    private readonly GameLog _context;
    private readonly int _id;
    private readonly int _groupCount;
    private readonly List<List<Dictionary<string, object?>>> _groups = new();
    private readonly List<int> _groupSizes = new();
    private int _visitedGroups;
    private long _visitedConditions;
    private int _sampleCount;
    private bool _ended;

    internal OrderEvaluationObservation(GameLog context, int id, int groupCount)
    { _context = context; _id = id; _groupCount = groupCount; }

    internal void BeginGroup(int count) => _context.Observe(() =>
    {
        _visitedGroups++;
        if (_visitedGroups > 8 || _sampleCount >= 32) return;
        _groups.Add(new());
        _groupSizes.Add(count);
    });

    internal void Condition(TradeOrderCondition condition, int actual, bool satisfied) => _context.Observe(() =>
    {
        _visitedConditions++;
        if (_visitedGroups > 8 || _sampleCount >= 32) return;
        _groups[^1].Add(new()
        {
            ["Factor"] = condition.Factor.ToString(),
            ["Comparison"] = condition.Comparison.ToString(),
            ["Value"] = condition.Value,
            ["Actual"] = actual,
            ["Satisfied"] = satisfied,
        });
        _sampleCount++;
    });

    internal void Complete(TradeOrderEvaluation evaluation, bool conditionGroupsSatisfied, int quantity, int? budgetCents) =>
        _context.Observe(() =>
        {
            if (_ended) return;
            _ended = true;
            var fields = _context.Context("Orders");
            fields["OrderId"] = _id;
            fields["ConditionSatisfied"] = conditionGroupsSatisfied && evaluation.Blockers == TradeOrderBlocker.None &&
                evaluation.Failure == TradeFailure.None;
            fields["ConditionGroupsSatisfied"] = conditionGroupsSatisfied;
            fields["WaitingCategories"] = TradeOrderLog.Categories(evaluation);
            fields["EvaluationCoverage"] = evaluation.Complete ? "Complete" : "ActualShortCircuit";
            fields["Conditions"] = _groups;
            fields["ConfiguredConditionGroupCount"] = _groupCount;
            fields["EvaluatedConditionGroupCount"] = _visitedGroups;
            fields["EvaluatedConditionCount"] = _visitedConditions;
            fields["ComputedQuantity"] = quantity;
            if (budgetCents.HasValue) fields["ComputedBudgetCents"] = budgetCents.Value;
            var paths = new List<string>();
            var counts = new Dictionary<string, object?>();
            for (int index = 0; index < _groups.Count; index++)
                if (_groups[index].Count < _groupSizes[index])
                {
                    string path = "Conditions[" + index + "]";
                    paths.Add(path);
                    counts[path] = new Dictionary<string, object?> { ["ItemCount"] = _groupSizes[index] };
                }
            if (_groups.Count < _visitedGroups)
            {
                paths.Add("Conditions");
                counts["Conditions"] = new Dictionary<string, object?> { ["ItemCount"] = _visitedGroups };
            }
            if (paths.Count > 0)
            {
                fields["Truncated"] = true;
                fields["TruncatedFields"] = paths;
                fields["TruncatedOriginalCounts"] = counts;
            }
            _context.Diagnostics.Capture(Event, "选定订单实际求值完成", fields);
        });
}
