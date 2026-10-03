using System.Collections.Generic;
using FarmExchange.Gameplay;

namespace FarmExchange.Cultivation;

public sealed record CultivationPlanSnapshot(
    int Id, string Name, CultivationMode Mode, IReadOnlyList<CultivationEntry> Entries,
    int ReferencingFarms, int NextEntryId);

public readonly record struct FarmCultivationSnapshot(
    int? PlanId, string? PlanName, CropKind? PreparedCrop, long? PreparedTimeUnits,
    bool IsResting, bool WaitingForGrowthStart);
