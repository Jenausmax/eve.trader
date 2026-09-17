using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Строка, прочитанная из озера: конверт плюс колонки набора как они лежат.
/// </summary>
/// <param name="Envelope">Общая часть строки — два времени, ключ, происхождение.</param>
/// <param name="Region">Регион партиции.</param>
/// <param name="Values">Колонки набора по именам.</param>
public sealed record FactRow(
    FactEnvelope Envelope,
    RegionId Region,
    IReadOnlyDictionary<string, object?> Values) : IBitemporalFact
{
    public string FactKey => Envelope.FactKey;

    public DateTimeOffset KnownAt => Envelope.KnownAt;

    public ObservationId Observation => Envelope.Observation;
}
