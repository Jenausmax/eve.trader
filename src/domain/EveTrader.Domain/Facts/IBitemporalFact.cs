namespace EveTrader.Domain.Facts;

/// <summary>
/// Минимум, которого достаточно для би-темпорального отбора: чем факт опознаётся и
/// когда о нём узнали.
/// </summary>
public interface IBitemporalFact
{
    /// <summary>Ключ факта: уточнённая версия несёт тот же ключ, что и прежняя.</summary>
    string FactKey { get; }

    /// <summary>Когда факт стал известен системе.</summary>
    DateTimeOffset KnownAt { get; }

    /// <summary>Наблюдение, из которого факт получен. Служит детерминированным тай-брейком.</summary>
    ObservationId Observation { get; }
}
