using FarmExchange.Farming;
using FarmExchange.Time;

namespace FarmExchange.Workers;

internal sealed class WorkerScheduler
{
    private int _nextFarmIndex;

    internal bool WorkOne(FarmingSystem farming, CalendarSnapshot calendar)
    {
        for (int offset = 0; offset < farming.CellCount; offset++)
        {
            int index = (_nextFarmIndex + offset) % farming.CellCount;
            if (!farming.TryWork(index, calendar))
                continue;
            _nextFarmIndex = (index + 1) % farming.CellCount;
            return true;
        }
        return false;
    }
}
