using System.Data.Common;
using System.Runtime.CompilerServices;
using DuckDB.NET.Data;
using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;

namespace EveTrader.Infrastructure.Facts.Query;

/// <summary>
/// Порт плоских строк. SQL здесь делает ровно три вещи: отсекает партиции, отбирает
/// подтверждённые покрытием строки и отбрасывает то, что на запрошенный момент ещё не
/// было известно.
///
/// Выбор последней версии каждого факта — это уже вычисление, и оно сделано в C#
/// (<see cref="Bitemporal" />), хотя оконная функция DuckDB справилась бы одним запросом.
/// Причина не в возможностях SQL: признак, посчитанный в SQL для обучения и в C# в бою,
/// расходится, и расхождение выглядит на бэктесте как отличная модель, а в бою как
/// случайная. Граница проходит по потребителю результата, а не по сложности запроса.
/// </summary>
public sealed class DuckDbFactRowReader(LakeLayout layout) : IFactRowReader
{
    public async IAsyncEnumerable<FactRow> ReadAsync(
        FactSet set,
        TimeRange observed,
        DateTimeOffset? asOf,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<FactRow> rows = await SelectAsync(set, observed, asOf, cancellationToken).ConfigureAwait(false);

        foreach (FactRow row in Bitemporal.Pick(rows))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return row;
        }
    }

    /// <summary>
    /// Отбор без вычисления: партиции, подтверждение покрытием, горизонт знания.
    /// Возвращает все версии — выбор версии происходит выше.
    /// </summary>
    public async Task<IReadOnlyList<FactRow>> SelectAsync(
        FactSet set,
        TimeRange observed,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        if (!layout.HasFiles(set) || !layout.HasFiles(FactSet.Coverage))
        {
            return [];
        }

        var sql =
            $"""
             SELECT *
             FROM read_parquet({DuckDb.Literal(layout.SetGlob(set))}, hive_partitioning = 1) AS facts
             WHERE facts.{FactColumnNames.ObservedDate} >= {DuckDb.Literal(DateOnly.FromDateTime(observed.From.UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))}
               AND facts.{FactColumnNames.ObservedDate} <= {DuckDb.Literal(DateOnly.FromDateTime(observed.To.UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))}
               AND facts.{FactColumnNames.Observation} IN (
                   SELECT {CoverageSchema.Observation}
                   FROM read_parquet({DuckDb.Literal(layout.SetGlob(FactSet.Coverage))}, hive_partitioning = 1))
             {(asOf is { } instant ? $"AND facts.{FactColumnNames.KnownAt} <= {DuckDb.Timestamp(instant)}" : string.Empty)}
             """;

        await using DuckDBConnection connection = DuckDb.Open();
        await using DuckDBCommand command = connection.CreateCommand();

        // CA2100: путь к озеру приходит из конфигурации, не от пользователя, и проходит
        // через DuckDb.Literal. Параметризовать нечего: read_parquet принимает путь только
        // литералом, параметр в его позиции DuckDB не принимает.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<FactRow>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);

            for (var column = 0; column < reader.FieldCount; column++)
            {
                values[reader.GetName(column)] = await reader.IsDBNullAsync(column, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(column);
            }

            rows.Add(ToRow(values));
        }

        return rows;
    }

    /// <summary>
    /// План запроса за интервал. Отсечение партиций проверяется им, а не выдачей:
    /// выдача совпадёт и без отсечения — движок просто прочитает лишние файлы.
    /// </summary>
    public Task<string> ExplainAsync(FactSet set, TimeRange observed, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(observed.From.UtcDateTime)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = DateOnly.FromDateTime(observed.To.UtcDateTime)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var sql = "EXPLAIN ANALYZE SELECT * FROM read_parquet("
            + DuckDb.Literal(layout.SetGlob(set))
            + ", hive_partitioning = 1) AS facts WHERE facts."
            + FactColumnNames.ObservedDate + " >= " + DuckDb.Literal(from)
            + " AND facts." + FactColumnNames.ObservedDate + " <= " + DuckDb.Literal(to);

        return DuckDb.TextAsync(sql, cancellationToken);
    }

    public static FactRow ToRow(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var kind = (EventTimeKind)Convert.ToInt32(values[FactColumnNames.EventTimeKind], System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset from = CoverageSchema.Utc(values[FactColumnNames.EventFrom]);
        DateTimeOffset to = CoverageSchema.Utc(values[FactColumnNames.EventTo]);

        var envelope = new FactEnvelope(
            (string)values[FactColumnNames.FactKey]!,
            kind == EventTimeKind.Instant ? EventTime.At(from) : EventTime.Between(from, to),
            CoverageSchema.Utc(values[FactColumnNames.KnownAt]),
            ObservationId.From((string)values[FactColumnNames.Observation]!),
            values[FactColumnNames.StaticDataVersion] is string version && version.Length > 0
                ? StaticDataVersion.From(version)
                : StaticDataVersion.None);

        var region = RegionId.From(Convert.ToInt32(values[FactColumnNames.Region], System.Globalization.CultureInfo.InvariantCulture));

        var payload = values
            .Where(static pair => !FactColumnNames.Envelope.Contains(pair.Key)
                && pair.Key is not (FactColumnNames.Region or FactColumnNames.ObservedDate))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        return new FactRow(envelope, region, payload);
    }
}
