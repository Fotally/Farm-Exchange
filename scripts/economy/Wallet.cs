using System;

namespace FarmExchange.Economy;

internal sealed class Wallet
{
    internal int BalanceCents { get; private set; }

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
        if (BalanceCents < amountCents)
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
}
