namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Имена колонок конверта. Одинаковы у всех наборов: по ним читают би-темпоральный
/// отбор и подтверждение покрытием, не зная ничего про конкретный набор.
/// </summary>
public static class FactColumnNames
{
    public const string FactKey = "fact_key";
    public const string EventTimeKind = "event_time_kind";
    public const string EventFrom = "event_from";
    public const string EventTo = "event_to";
    public const string KnownAt = "known_at";
    public const string Observation = "observation_id";
    public const string StaticDataVersion = "static_data_version";

    /// <summary>Колонки, приходящие из пути партиции, а не из файла.</summary>
    public const string ObservedDate = "observed_date";
    public const string Region = "region";

    public static IReadOnlyList<string> Envelope { get; } =
    [
        FactKey, EventTimeKind, EventFrom, EventTo, KnownAt, Observation, StaticDataVersion,
    ];
}
