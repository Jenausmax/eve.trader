using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;

namespace EveTrader.Application.Series;

/// <summary>
/// Плоские строки озера обратно в доменные типы — для рядов и всего, что их читает.
///
/// Хранилище отдало строки; что они значат, решается здесь, на C#. Обратное
/// преобразование событий уже живёт у реплея, признаков стакана и рядов — здесь.
/// </summary>
internal static class SeriesFactRows
{
    /// <summary>
    /// Признаки стакана из строки. Пороги приходят снаружи: в строке они живут только
    /// в именах колонок, и колонку порога, которой в файле нет, нельзя отличить от
    /// несуществующего порога.
    ///
    /// Глубина и число ордеров по стороне собираются целиком либо не собираются вовсе:
    /// сторона, у которой не хватает хоть одного порога, — это признак, записанный до
    /// появления счётчиков, и частичный список выдавал бы чужой порог за свой.
    /// </summary>
    public static BookFeatures Features(FactRow row, IReadOnlyList<int> thresholds)
    {
        var hasBid = Int64Of(row, BookFeatureFacts.HasBid) == 1;
        var hasAsk = Int64Of(row, BookFeatureFacts.HasAsk) == 1;

        return new BookFeatures(
            (int)Int64Of(row, BookFeatureFacts.TypeId),
            Int64Of(row, BookFeatureFacts.LocationId),
            hasBid ? IskPrice.FromCents(Int64Of(row, BookFeatureFacts.BestBidCents)) : null,
            hasAsk ? IskPrice.FromCents(Int64Of(row, BookFeatureFacts.BestAskCents)) : null,
            (int)Int64Of(row, BookFeatureFacts.BuyOrders),
            (int)Int64Of(row, BookFeatureFacts.SellOrders),
            hasBid ? Complete(row, thresholds, static threshold => BookFeatureFacts.DepthColumn(isBuy: true, threshold)) : [],
            hasAsk ? Complete(row, thresholds, static threshold => BookFeatureFacts.DepthColumn(isBuy: false, threshold)) : [],
            hasBid ? Counts(row, thresholds, isBuy: true) : [],
            hasAsk ? Counts(row, thresholds, isBuy: false) : [],
            row.Envelope.EventTime.From,
            row.Observation,
            Int64Of(row, BookFeatureFacts.Incomplete) == 1);
    }

    /// <summary>Строка набора рядов — приговор окну.</summary>
    public static bool IsWindow(FactRow row) => Int64Of(row, SeriesFacts.RowKind) == SeriesFacts.WindowRow;

    /// <summary>Определение ряда из строки — со сверкой с записанным ключом.</summary>
    public static SeriesDefinition Definition(FactRow row)
    {
        var definition = SeriesDefinition.Of(
            (SeriesKind)Int64Of(row, SeriesFacts.Kind),
            TimeSpan.FromSeconds(Int64Of(row, SeriesFacts.WindowSeconds)),
            TimeSpan.FromSeconds(Int64Of(row, SeriesFacts.StepSeconds)),
            (int)Int64Of(row, SeriesFacts.BandBasisPoints));

        return row.Values.TryGetValue(SeriesFacts.Definition, out var key) && key is string recorded
            && !string.Equals(recorded, definition.Key, StringComparison.Ordinal)
            ? throw new InvalidOperationException(
                $"Строка ряда '{row.FactKey}' несёт определение '{recorded}', а из колонок выводится '{definition.Key}'")
            : definition;
    }

    public static SeriesWindowVerdict Window(FactRow row) =>
        new(
            Definition(row),
            row.Region,
            new SeriesWindow(
                TimeRange.Between(row.Envelope.EventTime.From, row.Envelope.EventTime.To),
                (SeriesAdmission)Int64Of(row, SeriesFacts.Admission),
                (CoverageState)Int64Of(row, SeriesFacts.CoverageState),
                (int)Int64Of(row, SeriesFacts.PartialObservations)));

    public static SeriesPoint Point(FactRow row) =>
        new(
            Definition(row),
            row.Region,
            (int)Int64Of(row, SeriesFacts.TypeId),
            Int64Of(row, SeriesFacts.LocationId),
            (SeriesSide)Int64Of(row, SeriesFacts.Side),
            TimeRange.Between(row.Envelope.EventTime.From, row.Envelope.EventTime.To),
            DoubleOf(row, SeriesFacts.Value),
            Int64Of(row, SeriesFacts.Incomplete) == 1);

    public static long Int64Of(FactRow row, string column) =>
        Optional(row, column) ?? 0L;

    /// <summary>Целое, которого может не быть: пустая колонка и отсутствующая — одно и то же «нет значения».</summary>
    public static long? Optional(FactRow row, string column) =>
        row.Values.TryGetValue(column, out var value) && value is not null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : null;

    public static double DoubleOf(FactRow row, string column) =>
        row.Values.TryGetValue(column, out var value) && value is not null
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : 0d;

    public static IReadOnlyList<long> Complete(FactRow row, IReadOnlyList<int> thresholds, Func<int, string> column)
    {
        var values = new long[thresholds.Count];

        for (var index = 0; index < values.Length; index++)
        {
            if (Optional(row, column(thresholds[index])) is not { } value)
            {
                return [];
            }

            values[index] = value;
        }

        return values;
    }

    public static IReadOnlyList<int> Counts(FactRow row, IReadOnlyList<int> thresholds, bool isBuy) =>
    [
        .. Complete(row, thresholds, threshold => BookFeatureFacts.OrdersWithinColumn(isBuy, threshold))
            .Select(static count => (int)count),
    ];
}
