using EveTrader.Application.Live;
using EveTrader.Application.Reporting;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.Live;

/// <summary>
/// Сценарий спеки <c>market-observation/intake</c> §«Расход ресурсов наблюдаем».
///
/// Проверяется и то, и другое: замеры доезжают до экспорта — то есть метрику есть кому
/// прочитать, — и отчёт о расходе за интервал строится после прогона, из того, что
/// осталось на диске. Первое без второго было бы счётчиком, который живёт только внутри
/// процесса сбора; второе без первого — отчётом, по которому нельзя понять, что
/// происходит прямо сейчас.
/// </summary>
public sealed class ResourceUsageShould
{
    private const int Forge = 10000002;

    private static readonly TimeRange Window = TimeRange.Between(
        LiveFixture.Start.AddHours(-1), LiveFixture.Start.AddHours(2));

    [Fact]
    public async Task CountRequestsTrafficAndUnchangedResponses()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] =
        [
            LiveFixture.Page((1, 100m, 10, false), (2, 99m, 20, true)),
            LiveFixture.Page((3, 101m, 30, false)),
        ];

        LiveCollector collector = fixture.Collector(LiveFixture.Forge);

        CollectionCycle first = await collector
            .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        first.Observed.ShouldBe(1);

        // Второй цикл — условный запрос: источник отвечает «не изменилось» и тела не шлёт.
        fixture.Stub.ETags[Forge] = "\"abc\"";
        _ = fixture.Stub.NotModified.Add(Forge);
        fixture.Clock.Now = LiveFixture.Start.AddMinutes(10);

        CollectionCycle second = await collector
            .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        second.Unchanged.ShouldBe(0, "первый ответ валидатора ещё не дал — он приходит со вторым");

        ResourceUsage usage = await fixture.Usage.ForAsync(Window, token).ConfigureAwait(true);

        usage.Observations.ShouldBeGreaterThan(0);
        usage.Requests.ShouldBeGreaterThan(0);
        usage.BytesReceived.ShouldBeGreaterThan(0);
        _ = usage.TrafficKnownFrom.ShouldNotBeNull();
    }

    [Fact]
    public async Task ReportTheFractionOfUnchangedResponses()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.ETags[Forge] = "\"abc\"";
        _ = fixture.Stub.NotModified.Add(Forge);

        LiveCollector collector = fixture.Collector(LiveFixture.Forge);

        // Первый цикл приносит валидатор, второй и третий получают «не изменилось».
        for (var cycle = 0; cycle < 3; cycle++)
        {
            fixture.Clock.Now = LiveFixture.Start.AddMinutes(10 * cycle);

            _ = await collector
                .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
                .ConfigureAwait(true);
        }

        ResourceUsage usage = await fixture.Usage.ForAsync(Window, token).ConfigureAwait(true);

        usage.NotModified.ShouldBe(2);
        usage.Observations.ShouldBe(3);
        usage.NotModifiedFraction.ShouldBe(2d / 3d, 0.001d);
    }

    /// <summary>
    /// Метрика, которую некому прочитать, от объявления не появляется. Здесь и
    /// проверяется, что инструменты действительно доезжают до подписчика.
    /// </summary>
    [Fact]
    public async Task MakeEveryDeclaredInstrumentVisibleInTheExport()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] =
        [
            LiveFixture.Page((1, 100m, 10, false)),
            LiveFixture.Page((2, 99m, 20, true)),
        ];
        fixture.Stub.ErrorLimitRemain = 97;

        LiveCollector collector = fixture.Collector(LiveFixture.Forge);

        _ = await collector
            .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        fixture.Metrics.TotalOf("observation.source.requests").ShouldBe(2d, "по странице на запрос");
        fixture.Metrics.TotalOf("observation.source.bytes").ShouldBeGreaterThan(0d);
        fixture.Metrics.CountOf("observation.region.duration").ShouldBe(1L);
        fixture.Metrics.LastOf("observation.source.error_budget_remaining").ShouldBe(97d);
        fixture.Metrics.CountOf("observation.orders.changed_fraction").ShouldBe(1L);

        // Первое наблюдение региона — базовая линия: каждый ордер виден как появление,
        // и доля изменившихся равна единице.
        fixture.Metrics.LastOf("observation.orders.changed_fraction").ShouldBe(1d);
    }

    [Fact]
    public async Task CountConditionalResponsesWithoutTraffic()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.ETags[Forge] = "\"abc\"";
        _ = fixture.Stub.NotModified.Add(Forge);

        LiveCollector collector = fixture.Collector(LiveFixture.Forge);

        _ = await collector
            .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        var bytesAfterFirst = fixture.Metrics.TotalOf("observation.source.bytes");

        fixture.Clock.Now = LiveFixture.Start.AddMinutes(10);

        _ = await collector
            .RunCycleAsync(DiffOptions.Default, FeatureOptions.Default, StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        fixture.Metrics.TotalOf("observation.source.not_modified").ShouldBe(1d);

        // Условный ответ тела не несёт — трафик на нём не растёт. Ради этого условные
        // запросы и существуют.
        fixture.Metrics.TotalOf("observation.source.bytes").ShouldBe(bytesAfterFirst);
    }
}
