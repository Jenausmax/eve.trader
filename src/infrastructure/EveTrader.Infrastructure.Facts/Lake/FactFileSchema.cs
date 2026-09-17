using EveTrader.Domain.Facts;
using Parquet.Schema;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Схема файла набора: конверт плюс колонки набора. Регион и дата наблюдения в файл не
/// пишутся — они живут в пути партиции и приходят при чтении из него.
/// </summary>
public static class FactFileSchema
{
    public static ParquetSchema For(IReadOnlyList<FactColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var fields = new List<Field>
        {
            new DataField<string>(FactColumnNames.FactKey),
            new DataField<int>(FactColumnNames.EventTimeKind),
            new DataField<DateTime>(FactColumnNames.EventFrom),
            new DataField<DateTime>(FactColumnNames.EventTo),
            new DataField<DateTime>(FactColumnNames.KnownAt),
            new DataField<string>(FactColumnNames.Observation),
            new DataField<string>(FactColumnNames.StaticDataVersion),
        };

        fields.AddRange(columns.Select(Field));

        return new ParquetSchema(fields);
    }

    public static DataField Field(FactColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.Type switch
        {
            FactColumnType.Int64 => new DataField<long>(column.Name),
            FactColumnType.Double => new DataField<double>(column.Name),
            FactColumnType.String => new DataField<string>(column.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column.Type, "Неизвестный тип колонки"),
        };
    }

    /// <summary>
    /// Раскладывает порцию в строки файла. Порядок — по ключу факта: детерминированный
    /// порядок внутри файла нужен затем, что реплей обязан давать побитово тот же
    /// результат, включая порядок строк.
    /// </summary>
    public static IReadOnlyCollection<IDictionary<string, object?>> Rows(FactBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var order = Enumerable.Range(0, batch.RowCount)
            .OrderBy(index => batch.Envelopes[index].FactKey, StringComparer.Ordinal)
            .ThenBy(index => index)
            .ToArray();

        var rows = new List<IDictionary<string, object?>>(batch.RowCount);

        foreach (var index in order)
        {
            FactEnvelope envelope = batch.Envelopes[index];

            var row = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [FactColumnNames.FactKey] = envelope.FactKey,
                [FactColumnNames.EventTimeKind] = (int)envelope.EventTime.Kind,
                [FactColumnNames.EventFrom] = envelope.EventTime.From.UtcDateTime,
                [FactColumnNames.EventTo] = envelope.EventTime.To.UtcDateTime,
                [FactColumnNames.KnownAt] = envelope.KnownAt.UtcDateTime,
                [FactColumnNames.Observation] = envelope.Observation.Value,
                [FactColumnNames.StaticDataVersion] = envelope.StaticData.Value,
            };

            foreach (FactColumn column in batch.Columns)
            {
                row[column.Name] = column.Values.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }
}
