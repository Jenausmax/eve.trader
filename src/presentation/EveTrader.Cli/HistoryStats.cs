using EveTrader.Domain.Facts;
using EveTrader.Domain.History;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;

namespace EveTrader.Cli;

/// <summary>
/// Замеры по импортированной дневной истории. Считаются запросом: это отчёт оператору,
/// а не признак — граница вычислений проходит по потребителю результата.
/// </summary>
/// <param name="Rows">Всего строк.</param>
/// <param name="Types">Торгуемых типов.</param>
/// <param name="Regions">Активных регионов.</param>
/// <param name="Days">Суток.</param>
/// <param name="Earliest">Первые сутки.</param>
/// <param name="Latest">Последние сутки.</param>
public sealed record HistoryStats(
    long Rows,
    long Types,
    long Regions,
    long Days,
    DateOnly Earliest,
    DateOnly Latest)
{
    public static async Task<HistoryStats> MeasureAsync(
        LakeLayout layout,
        TimeRange within,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.HasFiles(FactSet.HistoryDaily))
        {
            return new HistoryStats(0, 0, 0, 0, DateOnly.MinValue, DateOnly.MinValue);
        }

        var from = DateOnly.FromDateTime(within.From.UtcDateTime)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = DateOnly.FromDateTime(within.To.UtcDateTime)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var sql =
            "SELECT count(*), count(DISTINCT " + DailyHistoryFacts.TypeId + "), count(DISTINCT "
            + DailyHistoryFacts.Region + "), count(DISTINCT " + FactColumnNames.ObservedDate + "), min("
            + FactColumnNames.ObservedDate + "), max(" + FactColumnNames.ObservedDate + ") FROM read_parquet("
            + DuckDb.Literal(layout.SetGlob(FactSet.HistoryDaily)) + ", hive_partitioning = 1) WHERE "
            + FactColumnNames.ObservedDate + " >= " + DuckDb.Literal(from) + " AND "
            + FactColumnNames.ObservedDate + " <= " + DuckDb.Literal(to);

        var text = await DuckDb.TextAsync(sql, cancellationToken).ConfigureAwait(false);
        var cells = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return new HistoryStats(
            long.Parse(cells[0], System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(cells[1], System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(cells[2], System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(cells[4], System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(cells[5], System.Globalization.CultureInfo.InvariantCulture));
    }
}
