using EveTrader.Application.Facts;
using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.History;

/// <summary>
/// Сценарии спеки <c>market-facts/materialization</c> §«Вежливость к источнику архива»
/// и §«Дневная история материализуется целиком».
/// </summary>
public sealed class ArchiveImportShould
{
    private static DateOnly Day(int day) => new(2026, 1, day);

    private static DateTimeOffset At(int day, int hour = 0) =>
        new(2026, 1, day, hour, 0, 0, TimeSpan.Zero);

    private static TimeRange January => TimeRange.Between(At(1), At(31));

    [Fact]
    public async Task ImportEveryDayThatTheSourcePublishes()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 3; day++)
        {
            fixture.Stub.Days[Day(day)] = (
                Csv.Of(
                    (Day(day), 10000002, 34, 100 * day, At(day + 1)),
                    (Day(day), 10000043, 34, 200 * day, At(day + 1))),
                At(day + 1));
        }

        DailyHistoryImportReport report = await fixture
            .RunAsync(fixture.Archive(), January, token)
            .ConfigureAwait(true);

        report.DaysWritten.ShouldBe(3);
        report.RowsWritten.ShouldBe(6);
        report.Regions.ShouldBe(2);
        report.Types.ShouldBe(1);

        List<FactRow> rows = await fixture.Rows
            .ReadAsync(FactSet.HistoryDaily, January, null, token)
            .ToListAsync(token)
            .ConfigureAwait(true);

        rows.Count.ShouldBe(6);
    }

    [Fact]
    public async Task NotDownloadAgainWhatIsAlreadyLoaded()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 100, At(2))), At(2));

        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        fixture.Stub.BodiesServed.ShouldBe(1);

        // Второй прогон сам выясняет из покрытия, когда грузил эти сутки, и спрашивает условно.
        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        fixture.Stub.BodiesServed.ShouldBe(1, "тело файла не должно приезжать повторно");
        fixture.Stub.NotModifiedServed.ShouldBe(1);
    }

    [Fact]
    public async Task NeverExceedTheDeclaredParallelism()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 12; day++)
        {
            fixture.Stub.Days[Day(day)] = (Csv.Of((Day(day), 10000002, 34, day, At(day + 1))), At(day + 1));
        }

        _ = await fixture.RunAsync(fixture.Archive(parallelism: 2), January, token).ConfigureAwait(true);

        fixture.Stub.PeakParallelism.ShouldBeLessThanOrEqualTo(2);
        fixture.Stub.BodiesServed.ShouldBe(12);
    }

    [Fact]
    public async Task LoadBackfilledRowsAsANewVersionKeepingTheOld()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Первая публикация суток: известно, что знал источник на 2 января.
        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 100, At(2))), At(2));
        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        // Источник дополнил те же сутки задним числом: файл переписан, строка уточнена.
        fixture.Clock.Now = At(10);
        fixture.Stub.Days[Day(1)] = (
            Csv.Of((Day(1), 10000002, 34, 100, At(2)), (Day(1), 10000002, 34, 175, At(9))),
            At(9));

        DailyHistoryImportReport second = await fixture
            .RunAsync(fixture.Archive(), January, token)
            .ConfigureAwait(true);

        second.DaysWritten.ShouldBe(1);
        second.RowsWritten.ShouldBe(1, "дописывается только то, чего у нас не было");

        // Обе версии на месте, чтение на момент выбирает свою.
        List<FactRow> all = await fixture.Rows
            .SelectAsync(FactSet.HistoryDaily, January, null, token)
            .ContinueWith(static task => task.Result.ToList(), token, TaskContinuationOptions.None, TaskScheduler.Default)
            .ConfigureAwait(true);

        all.Count.ShouldBe(2);

        FactRow before = (await fixture.Rows.ReadAsync(FactSet.HistoryDaily, January, At(5), token).ToListAsync(token).ConfigureAwait(true)).Single();
        before.Values["volume"].ShouldBe(100L);

        FactRow after = (await fixture.Rows.ReadAsync(FactSet.HistoryDaily, January, null, token).ToListAsync(token).ConfigureAwait(true)).Single();
        after.Values["volume"].ShouldBe(175L);
    }

    [Fact]
    public async Task StayIdempotentWhenTheSameFileIsImportedTwice()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 100, At(2))), At(2));

        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);
        DailyHistoryImportReport again = await fixture
            .RunAsync(fixture.Archive(), January, token)
            .ConfigureAwait(true);

        again.DaysWritten.ShouldBe(0);

        // Файл не менялся, поэтому источник ответил 304 и тела не прислал: повторная
        // загрузка не происходит вовсе, а не отбрасывается после скачивания.
        again.DaysUnchanged.ShouldBe(1);
        fixture.Stub.NotModifiedServed.ShouldBe(1);
        fixture.Stub.BodiesServed.ShouldBe(1);

        List<FactRow> rows = await fixture.Rows
            .ReadAsync(FactSet.HistoryDaily, January, null, token)
            .ToListAsync(token)
            .ConfigureAwait(true);

        rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RecordWhatWasMaterialized()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 1, At(2))), At(2));
        fixture.Stub.Days[Day(2)] = (Csv.Of((Day(2), 10000043, 34, 2, At(3))), At(3));

        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        IReadOnlyList<MaterializedInterval> intervals = await fixture.Registry
            .ReadAsync(FactSet.HistoryDaily, token)
            .ConfigureAwait(true);

        intervals.Select(static interval => interval.Region.Value).Order().ShouldBe([10000002, 10000043]);
        intervals.ShouldAllBe(static interval => interval.Source == "everef-archive");
    }

    [Fact]
    public async Task ReadOnlyTheListingsOfTheRequestedYears()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Days[Day(1)] = (Csv.Of((Day(1), 10000002, 34, 1, At(2))), At(2));

        _ = await fixture.RunAsync(fixture.Archive(), January, token).ConfigureAwait(true);

        fixture.Stub.Requests.Count(static path => path.EndsWith('/')).ShouldBe(1);
    }

    [Fact]
    public async Task RecoverFromATruncatedDownloadInsteadOfAbortingTheRun()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 3; day++)
        {
            fixture.Stub.Days[Day(day)] = (Csv.Of((Day(day), 10000002, 34, day, At(day + 1))), At(day + 1));
        }

        // Вторые сутки обрываются дважды подряд, потом приходят целыми.
        fixture.Stub.TruncateAttempts[Day(2)] = 2;

        DailyHistoryImportReport report = await fixture
            .RunAsync(fixture.Archive(), January, token)
            .ConfigureAwait(true);

        fixture.Stub.TruncatedServed.ShouldBe(2);
        report.DaysWritten.ShouldBe(3, "оборванные сутки забираются повтором, а не теряются");
    }

    [Fact]
    public async Task SkipADayThatNeverArrivesAndKeepGoing()
    {
        using var fixture = new ImportFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 3; day++)
        {
            fixture.Stub.Days[Day(day)] = (Csv.Of((Day(day), 10000002, 34, day, At(day + 1))), At(day + 1));
        }

        // Эти сутки не придут никогда — попыток не хватит.
        fixture.Stub.TruncateAttempts[Day(2)] = 100;

        DailyHistoryImportReport report = await fixture
            .RunAsync(fixture.Archive(), January, token)
            .ConfigureAwait(true);

        // Прогон на восемь тысяч файлов не роняется из-за одних суток.
        report.DaysWritten.ShouldBe(2);

        // Пропущенные сутки остались без покрытия, то есть восполнимы следующим прогоном.
        IReadOnlyList<Domain.Coverage.CoverageEntry> coverage =
            await fixture.Coverage.ReadAsync(January, [], token).ConfigureAwait(true);

        coverage.ShouldNotContain(static entry => entry.Collected.From == At(2));
    }
}
