using System.Collections.Generic;
using FarmExchange.Farming;
using FarmExchange.Gameplay;

namespace FarmExchange.Cultivation;

public enum CultivationMode { Immediate, PrepareNext }

public readonly record struct CultivationEntry(int Id, CropKind Crop, int StartDay)
{
    public int LengthDays => CropCatalog.Get(Crop).GrowthDays;
}

public sealed record CultivationPlanRequest(
    string Name, CultivationMode Mode, IReadOnlyList<CultivationEntry> Entries);

public sealed record CultivationValidation(string? Error, IReadOnlyList<int> RiskEntryIds)
{
    public bool Success => Error == null;
}

public readonly record struct CultivationCommandResult(int Id, string? Error)
{
    public bool Success => Error == null;
}
