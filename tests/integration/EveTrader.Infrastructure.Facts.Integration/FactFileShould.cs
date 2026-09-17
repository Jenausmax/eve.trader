using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Схема файла набора: конверт плюс колонки. Проверяется записью и чтением настоящего
/// файла — схема, совпадающая только на бумаге, ничего не стоит.
/// </summary>
public sealed class FactFileShould
{
    [Fact]
    public async Task RoundTripEnvelopeAndEveryColumnType()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var id = ObservationId.From("obs-types");
        var batch = FactBatch.Of(
            FactSet.BookFeatures,
            Sample.TheForge,
            id,
            Sample.DayOnly(1),
            [
                new FactEnvelope("k/1", EventTime.At(Sample.Day(1)), Sample.Day(2), id, StaticDataVersion.From("sde-1")),
                new FactEnvelope("k/2", EventTime.Between(Sample.Day(1), Sample.Day(1).AddMinutes(5)), Sample.Day(2), id, StaticDataVersion.From("sde-1")),
            ],
            [
                FactColumn.OfInt64("orders", [3, 4]),
                FactColumn.OfDouble("spread", [1.25, 2.5]),
                FactColumn.OfString("side", ["buy", "sell"]),
            ]);

        _ = await lake.Writer.WriteAsync(batch, Sample.Covering("obs-types", observedDay: 1), token).ConfigureAwait(true);

        List<FactRow> rows = await lake.Rows
            .ReadAsync(FactSet.BookFeatures, TimeRange.Between(Sample.Day(1), Sample.Day(8)), null, token)
            .ToListAsync(token).ConfigureAwait(true);

        rows.Count.ShouldBe(2);

        FactRow instant = rows.Single(static row => row.FactKey == "k/1");
        instant.Envelope.EventTime.Kind.ShouldBe(EventTimeKind.Instant);
        instant.Values["orders"].ShouldBe(3L);
        instant.Values["spread"].ShouldBe(1.25d);
        instant.Values["side"].ShouldBe("buy");

        FactRow interval = rows.Single(static row => row.FactKey == "k/2");
        interval.Envelope.EventTime.Kind.ShouldBe(EventTimeKind.Interval);
        interval.Envelope.EventTime.Instant.ShouldBeNull();
        interval.Envelope.EventTime.To.ShouldBe(Sample.Day(1).AddMinutes(5));
    }

    [Fact]
    public async Task SortRowsByFactKeyInsideTheFile()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var id = ObservationId.From("obs-order");
        var keys = new[] { "k/zeta", "k/alpha", "k/mu" };

        var batch = FactBatch.Of(
            FactSet.BookFeatures,
            Sample.TheForge,
            id,
            Sample.DayOnly(1),
            [.. keys.Select(key => new FactEnvelope(key, EventTime.At(Sample.Day(1)), Sample.Day(2), id, StaticDataVersion.None))],
            [FactColumn.OfInt64("position", [0, 1, 2])]);

        _ = await lake.Writer.WriteAsync(batch, Sample.Covering("obs-order", observedDay: 1), token).ConfigureAwait(true);

        var file = lake.Layout.FileFor(FactSet.BookFeatures, Sample.DayOnly(1), Sample.TheForge, id);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> stored = await ParquetCoverageLog.ReadFileAsync(file, token).ConfigureAwait(true);

        stored.Select(row => (string)row[FactColumnNames.FactKey]!)
            .ShouldBe(["k/alpha", "k/mu", "k/zeta"]);

        // Колонки набора переставлены вместе с конвертом, а не остались на прежних местах.
        stored.Select(row => Convert.ToInt64(row["position"], System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe([1L, 2L, 0L]);
    }

    [Fact]
    public void RejectAColumnWhoseLengthDoesNotMatchTheRows()
    {
        var id = ObservationId.From("obs-bad");

        _ = Should.Throw<ArgumentException>(() => FactBatch.Of(
            FactSet.BookFeatures,
            Sample.TheForge,
            id,
            Sample.DayOnly(1),
            [new FactEnvelope("k/1", EventTime.At(Sample.Day(1)), Sample.Day(2), id, StaticDataVersion.None)],
            [FactColumn.OfInt64("orders", [1, 2])]));
    }

    [Fact]
    public void RejectRowsThatBelongToAnotherObservation()
    {
        var id = ObservationId.From("obs-mine");
        var other = ObservationId.From("obs-theirs");

        _ = Should.Throw<ArgumentException>(() => FactBatch.Of(
            FactSet.BookFeatures,
            Sample.TheForge,
            id,
            Sample.DayOnly(1),
            [new FactEnvelope("k/1", EventTime.At(Sample.Day(1)), Sample.Day(2), other, StaticDataVersion.None)],
            []));
    }
}
