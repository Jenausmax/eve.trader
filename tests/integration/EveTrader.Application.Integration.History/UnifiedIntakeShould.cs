using EveTrader.Application.Facts;
using EveTrader.Application.History;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Esi;
using EveTrader.Infrastructure.Esi.History;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace EveTrader.Application.Integration.History;

/// <summary>
/// Сценарий спеки <c>market-observation/intake</c> §«Импорт архива попадает в то же
/// хранилище». Проверка дешёвая и содержательная: если стадии после приёма где-то
/// различают источник, различие вылезет здесь — на простом наборе.
/// </summary>
public sealed class UnifiedIntakeShould
{
    private static DateOnly Day(int day) => new(2026, 1, day);

    private static DateTimeOffset At(int day, int hour = 0) => new(2026, 1, day, hour, 0, 0, TimeSpan.Zero);

    private static TimeRange January => TimeRange.Between(At(1), At(31));

    private static EsiMarketHistorySource Esi(StubEsi stub)
    {
        var client = new HttpClient(stub, disposeHandler: false)
        {
            BaseAddress = new Uri("https://esi.evetech.net/latest/"),
        };

        return new EsiMarketHistorySource(
            client, new EsiOptions(), TimeProvider.System, NullLogger<EsiMarketHistorySource>.Instance);
    }

    [Fact]
    public async Task PutArchiveAndLiveFactsIntoOneSetWithOneSchema()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Архив отдаёт 1 января, живой ESI — 2 января. Набор один и тот же.
        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 100, At(2))), At(2));
        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        var esi = new StubEsi();
        esi.History[(10000002, 34)] = [(Day(2), 250)];
        _ = await fixture.Import.RunAsync(
            Esi(esi),
            new MarketHistoryScope(January, [RegionId.From(10000002)], [34], new Dictionary<DateOnly, DateTimeOffset>()),
            StaticDataVersion.From("sde-test"),
            token).ConfigureAwait(true);

        List<FactRow> rows = await fixture.Rows
            .ReadAsync(FactSet.HistoryDaily, January, null, token)
            .ToListAsync(token)
            .ConfigureAwait(true);

        rows.Count.ShouldBe(2);

        // Ни одна колонка не выдаёт происхождения: схема у обоих одна.
        rows.Select(static row => string.Join(',', row.Values.Keys.Order(StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(1);

        rows.ShouldAllBe(static row => row.Envelope.EventTime.Kind == EventTimeKind.Interval);
        rows.ShouldAllBe(static row => row.Region == RegionId.From(10000002));
        rows.Select(static row => row.Values["volume"]).Order().ShouldBe([100L, 250L]);
    }

    [Fact]
    public async Task KeepOriginOnlyInTheCoverageLog()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 100, At(2))), At(2));
        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        var esi = new StubEsi();
        esi.History[(10000002, 34)] = [(Day(2), 250)];
        _ = await fixture.Import.RunAsync(
            Esi(esi),
            new MarketHistoryScope(January, [RegionId.From(10000002)], [34], new Dictionary<DateOnly, DateTimeOffset>()),
            StaticDataVersion.From("sde-test"),
            token).ConfigureAwait(true);

        // Происхождение известно — но только из журнала покрытия, не из самих фактов.
        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(January, [], token)
            .ConfigureAwait(true);

        coverage.Select(static entry => entry.Source).Order(StringComparer.Ordinal)
            .ShouldBe(["esi", "everef-archive"]);
        coverage.ShouldAllBe(static entry => entry.ObservationStep == TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task RefuseAnEsiScopeThatNamesNeitherRegionsNorTypes()
    {
        using var fixture = new ImportFixture();
        var esi = new StubEsi();

        _ = await Should.ThrowAsync<ArgumentException>(() => fixture.Import.RunAsync(
            Esi(esi),
            new MarketHistoryScope(January, [], [], new Dictionary<DateOnly, DateTimeOffset>()),
            StaticDataVersion.From("sde-test"),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TakeKnownAtFromTheSourceNotFromOurOwnClock()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        var esi = new StubEsi { LastModified = At(9, 7) };
        esi.History[(10000002, 34)] = [(Day(2), 250)];

        _ = await fixture.Import.RunAsync(
            Esi(esi),
            new MarketHistoryScope(January, [RegionId.From(10000002)], [34], new Dictionary<DateOnly, DateTimeOffset>()),
            StaticDataVersion.From("sde-test"),
            token).ConfigureAwait(true);

        FactRow row = (await fixture.Rows
            .ReadAsync(FactSet.HistoryDaily, January, null, token)
            .ToListAsync(token)
            .ConfigureAwait(true)).Single();

        row.Envelope.KnownAt.ShouldBe(At(9, 7));
    }
}
