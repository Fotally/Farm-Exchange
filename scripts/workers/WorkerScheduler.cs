using FarmExchange.Farming;

namespace FarmExchange.Workers;

internal sealed class WorkerScheduler
{
    private int _nextFarmIndex;

    internal bool WorkOne(FarmingSystem farming)
    {
        for (int offset = 0; offset < farming.CellCount; offset++)
        {
            int index = (_nextFarmIndex + offset) % farming.CellCount;
            if (!farming.TryWork(index))
                continue;
            _nextFarmIndex = (index + 1) % farming.CellCount;
            return true;
        }
        return false;
    }
}
