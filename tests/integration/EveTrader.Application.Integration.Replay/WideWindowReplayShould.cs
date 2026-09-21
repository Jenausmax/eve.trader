using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.Replay;

/// <summary>
/// Реплей наблюдений с широким интервалом сбора — такие даёт архив снимков: полный
/// обход рынка занимает у источника минуты, и <c>issued</c> правки вполне может попасть
/// внутрь интервала, за который собирался предыдущий снимок.
///
/// У живого сбора интервал — двадцать секунд, и этот случай почти не встречается.
/// У архива он встречается постоянно, и приёмка на нём разошлась.
/// </summary>
public sealed class WideWindowReplayShould
{
    private static readonly RegionId Forge = RegionId.From(10000002);

    private static readonly TimeRange Window =
        TimeRange.Between(Snapshot(-60), Snapshot(600));

    private static DateTimeOffset Snapshot(int minutes) =>
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    /// <summary>Наблюдение архивной формы: интервал сбора в четыре минуты.</summary>
    private static RegionObservation Observation(int minute, params OrderSnapshot[] orders) =>
        new(
            Forge,
            ObservationId.From($"archive-{Forge.Value}-{Snapshot(minute):yyyyMMddTHHmmssZ}"),
            TimeRange.Between(Snapshot(minute), Snapshot(minute + 4)),
            CoverageOutcome.Success,
            TimeSpan.FromMinutes(30),
            orders,
            1,
            1,
            IsBaseline: false,
            FailureReason: null);

    private static OrderSnapshot Order(long id, decimal price, int issuedMinute) =>
        new(id, 34, 60003760, false, IskPrice.FromIsk(price), 100, 100, 90,
            Snapshot(issuedMinute).ToUnixTimeSeconds());

    [Fact]
    public async Task NotLoseARepriceWhoseIssuedFallsInsideThePreviousCollectionWindow()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("wide-original");
        using var replayed = new ReplayLake("wide-replayed");

        // Первый снимок собирался с 12:00 до 12:04 и увидел цену 100.
        // Второй снимок в 12:30 видит цену 120 с issued 12:02 — то есть правка
        // случилась внутри окна первого снимка, но после того, как его страницу сняли.
        var run = new RecordedRun("archive",
        [
            Observation(0, Order(1, 100m, issuedMinute: -60)),
            Observation(30, Order(1, 120m, issuedMinute: 2)),
            Observation(60, Order(1, 120m, issuedMinute: 2)),
        ]);

        IntakeReport first = await original.RunAsync(run, token).ConfigureAwait(true);

        first.Written.ShouldBe(3);

        IntakeReport second = await replayed
            .RunAsync(original.Replay(Window), token)
            .ConfigureAwait(true);

        second.Written.ShouldBe(3);

        List<FactRow> before = await EventsAsync(original, token).ConfigureAwait(true);
        List<FactRow> after = await EventsAsync(replayed, token).ConfigureAwait(true);

        // Прогон записал перестановку цены. Реплей обязан записать её же — иначе
        // восстановленный стакан навсегда останется со старой ценой, и расхождение
        // будет накапливаться с каждым таким ордером.
        before.Count(IsReprice).ShouldBe(1);
        after.Count(IsReprice).ShouldBe(1);

        after.Select(Shape).Order(StringComparer.Ordinal)
            .ShouldBe(before.Select(Shape).Order(StringComparer.Ordinal));
    }

    private static bool IsReprice(FactRow row) =>
        Convert.ToInt64(row.Values[OrderEventFacts.Kind], System.Globalization.CultureInfo.InvariantCulture)
        == (long)OrderEventKind.Repriced;

    private static string Shape(FactRow row) =>
        string.Join('|',
            row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={pair.Value}"));

    private static async Task<List<FactRow>> EventsAsync(ReplayLake lake, CancellationToken cancellationToken)
    {
        var rows = new List<FactRow>();

        await foreach (FactRow row in lake.Rows
            .ReadAsync(FactSet.OrderEvents, Window, null, cancellationToken)
            .ConfigureAwait(true))
        {
            rows.Add(row);
        }

        return rows;
    }
}
