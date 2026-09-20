using EveTrader.Application.BackgroundServices;

namespace EveTrader.Application.Workers;

/// <summary>Счётчики цикла обслуживания.</summary>
public sealed record CompactionCycle(
    [property: Counter("unconfirmed_swept")] int UnconfirmedSwept,
    [property: Counter("raw_pages_swept")] int RawPagesSwept) : ICycleCounters;
