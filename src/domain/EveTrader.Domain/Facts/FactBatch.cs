namespace EveTrader.Domain.Facts;

/// <summary>
/// Порция строк одного наблюдения в один набор и одну партицию. Единица записи в озеро:
/// она целиком становится фактами либо целиком не становится — промежуточного состояния
/// у наблюдения нет.
/// </summary>
public sealed record FactBatch
{
    private FactBatch(
        FactSet set,
        RegionId region,
        ObservationId observation,
        DateOnly observedDate,
        IReadOnlyList<FactEnvelope> envelopes,
        IReadOnlyList<FactColumn> columns)
    {
        Set = set;
        Region = region;
        Observation = observation;
        ObservedDate = observedDate;
        Envelopes = envelopes;
        Columns = columns;
    }

    public FactSet Set { get; }

    public RegionId Region { get; }

    public ObservationId Observation { get; }

    /// <summary>Дата наблюдения — партиция озера.</summary>
    public DateOnly ObservedDate { get; }

    public IReadOnlyList<FactEnvelope> Envelopes { get; }

    public IReadOnlyList<FactColumn> Columns { get; }

    public int RowCount => Envelopes.Count;

    public static FactBatch Of(
        FactSet set,
        RegionId region,
        ObservationId observation,
        DateOnly observedDate,
        IReadOnlyList<FactEnvelope> envelopes,
        IReadOnlyList<FactColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        ArgumentNullException.ThrowIfNull(columns);

        if (envelopes.Any(envelope => envelope.Observation != observation))
        {
            throw new ArgumentException(
                "Все строки порции принадлежат одному наблюдению: иначе подтверждение покрытием перестаёт быть всё-или-ничего",
                nameof(envelopes));
        }

        FactColumn? mismatched = columns.FirstOrDefault(column => column.Length != envelopes.Count);
        if (mismatched is not null)
        {
            throw new ArgumentException(
                $"Колонка '{mismatched.Name}' содержит {mismatched.Length} значений при {envelopes.Count} строках",
                nameof(columns));
        }

        IGrouping<string, FactColumn>? duplicate = columns
            .GroupBy(column => column.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicate is not null
            ? throw new ArgumentException($"Колонка '{duplicate.Key}' объявлена дважды", nameof(columns))
            : new FactBatch(set, region, observation, observedDate, [.. envelopes], [.. columns]);
    }
}
