using System;

namespace FarmExchange.Economy;

internal sealed class Wallet
{
    internal int BalanceCents { get; private set; }
    internal int FrozenCents { get; private set; }
    internal int AvailableCents => BalanceCents - FrozenCents;

    internal Wallet(int initialCents)
    {
        if (initialCents < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCents));
        BalanceCents = initialCents;
    }

    internal bool TrySpend(int amountCents)
    {
        if (amountCents < 0)
            throw new ArgumentOutOfRangeException(nameof(amountCents));
        if (AvailableCents < amountCents)
            return false;
        BalanceCents -= amountCents;
        return true;
    }

    internal void Credit(int amountCents)
    {
        if (amountCents < 0)
            throw new ArgumentOutOfRangeException(nameof(amountCents));
        BalanceCents = checked(BalanceCents + amountCents);
    }

    /**
     * <summary>完整替换一张委托的冻结金额，失败保持余额和冻结额不变。</summary>
     * <param name="previousCents">该委托原冻结金额，单位为分。</param>
     * <param name="nextCents">新冻结金额，单位为分。</param>
     * <returns>可用资金加该单原额度足够时返回 true。</returns>
     */
    internal bool TryReplaceFrozen(int previousCents, int nextCents)
    {
        if (previousCents < 0 || previousCents > FrozenCents || nextCents < 0)
            throw new ArgumentOutOfRangeException(nameof(previousCents));
        if (nextCents > AvailableCents + previousCents)
            return false;
        FrozenCents = FrozenCents - previousCents + nextCents;
        return true;
    }

    /**
     * <summary>提交已完整预检的委托支出，并释放该单全部冻结金额。</summary>
     * <param name="expenseCents">实际含费支出，单位为分。</param>
     * <param name="frozenCents">该单拥有的冻结金额，持续单为零。</param>
     * <remarks>只能使用可用现金与本单冻结额；不消费其他单冻结资金。</remarks>
     */
    internal void SpendForOrder(int expenseCents, int frozenCents)
    {
        if (expenseCents < 0 || frozenCents < 0 || frozenCents > FrozenCents ||
            expenseCents > AvailableCents + frozenCents)
            throw new InvalidOperationException("委托支出与预检不一致");
        FrozenCents -= frozenCents;
        BalanceCents -= expenseCents;
    }
}
