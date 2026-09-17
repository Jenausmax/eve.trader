namespace EveTrader.Domain.Facts;

/// <summary>
/// Обязательная часть каждой строки факта: два времени, ключ, происхождение и версия
/// статических данных. Колонки конкретных наборов приезжают со своими изменениями —
/// здесь зафиксировано только общее.
/// </summary>
/// <param name="FactKey">Ключ факта; уточнение несёт тот же ключ.</param>
/// <param name="EventTime">Время, к которому относится факт: момент либо интервал.</param>
/// <param name="KnownAt">Время, когда факт стал известен системе.</param>
/// <param name="Observation">Наблюдение, подтверждающее строку.</param>
/// <param name="StaticData">Версия статических данных, если факт от них зависит.</param>
public sealed record FactEnvelope(
    string FactKey,
    EventTime EventTime,
    DateTimeOffset KnownAt,
    ObservationId Observation,
    StaticDataVersion StaticData) : IBitemporalFact;
