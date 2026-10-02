using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;

namespace EveTrader.Application.Series;

/// <summary>
/// Чтение рядов на момент знания — через порт плоских строк.
///
/// Хранилище отдаёт строки набора, отобранные по партициям и горизонту знания; какая
/// версия факта действительна на момент — решается в C# (<see cref="Bitemporal" />), а
/// строки разбираются в доменные типы здесь же. Отдельного «запроса рядов» в хранилище
/// нет и не должно быть.
/// </summary>
public sealed class SeriesFactReader(IFactRowReader rows)
{
    /// <param name="region">Регион.</param>
    /// <param name="definitions">Какие ряды нужны.</param>
    /// <param name="ends">Интервал, в который попадают концы окон, включая правую границу.</param>
    /// <param name="asOf">
    /// Момент знания: строки, узнанные позже, в выдачу не попадают. <see langword="null" />
    /// — последняя известная версия.
    /// </param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task<SeriesComputed> ReadAsync(
        RegionId region,
        IReadOnlyList<SeriesDefinition> definitions,
        TimeRange ends,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        HashSet<string> wanted = [.. definitions.Select(static definition => definition.Key)];

        var windows = new List<SeriesWindowVerdict>();
        var points = new List<SeriesPoint>();

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.FeatureSeries, ends, asOf, cancellationToken)
            .ConfigureAwait(false))
        {
            if (row.Region != region
                || row.Envelope.EventTime.To < ends.From
                || row.Envelope.EventTime.To > ends.To
                || !row.Values.TryGetValue(SeriesFacts.Definition, out var key)
                || key is not string definition
                || !wanted.Contains(definition))
            {
                continue;
            }

            if (SeriesFactRows.IsWindow(row))
            {
                windows.Add(SeriesFactRows.Window(row));
            }
            else
            {
                points.Add(SeriesFactRows.Point(row));
            }
        }

        return new SeriesComputed(windows, points);
    }
}
