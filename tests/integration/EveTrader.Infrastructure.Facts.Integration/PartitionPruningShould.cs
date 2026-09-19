using System.Text.RegularExpressions;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Партиционирование по дате наблюдения и региону. Проверяется планом запроса, а не
/// выдачей: без отсечения выдача была бы той же, движок просто прочитал бы все файлы.
/// </summary>
public sealed partial class PartitionPruningShould
{
    /// <summary>Строка появляется только когда отсечение состоялось.</summary>
    [GeneratedRegex(@"Scanning Files:\s*(\d+)\s*/\s*(\d+)")]
    private static partial Regex ScanningFiles { get; }

    /// <summary>Сколько файлов движок прочитал на самом деле. Есть в плане всегда.</summary>
    [GeneratedRegex(@"Total Files Read:\s*(\d+)")]
    private static partial Regex FilesRead { get; }

    [Fact]
    public async Task ReadOnlyPartitionsOfTheRequestedInterval()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 4; day++)
        {
            _ = await lake.Writer.WriteAsync(
                Sample.History($"obs-{day}", calendarDay: day, volume: day, knownAt: Sample.Day(day + 1)),
                [Sample.Covering($"obs-{day}", observedDay: day)],
                token).ConfigureAwait(true);
        }

        var plan = await lake.Rows.ExplainAsync(
            FactSet.HistoryDaily,
            TimeRange.Between(Sample.Day(2), Sample.Day(2).AddHours(23)),
            token).ConfigureAwait(true);

        var compact = Compact(plan);
        Match scanned = ScanningFiles.Match(compact);

        scanned.Success.ShouldBeTrue($"план не показывает отсечения:{Environment.NewLine}{plan}");
        scanned.Groups[2].Value.ShouldBe("4", "в озере четыре суточные партиции");
        scanned.Groups[1].Value.ShouldBe("1", "запрошены одни сутки — читаться должна одна партиция");
        FilesRead.Match(compact).Groups[1].Value.ShouldBe("1");
    }

    [Fact]
    public async Task ReadEveryPartitionWhenTheWholeIntervalIsRequested()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var day = 1; day <= 4; day++)
        {
            _ = await lake.Writer.WriteAsync(
                Sample.History($"obs-{day}", calendarDay: day, volume: day, knownAt: Sample.Day(day + 1)),
                [Sample.Covering($"obs-{day}", observedDay: day)],
                token).ConfigureAwait(true);
        }

        var plan = await lake.Rows.ExplainAsync(
            FactSet.HistoryDaily, TimeRange.Between(Sample.Day(1), Sample.Day(5)), token).ConfigureAwait(true);

        // Отсекать нечего — движок читает все партиции, и строки об отсечении в плане нет.
        FilesRead.Match(Compact(plan)).Groups[1].Value.ShouldBe("4");
    }

    [Fact]
    public async Task PutRegionIntoThePartitionPathOfRegionPartitionedSets()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var domain = RegionId.From(10000043);

        foreach ((var observation, RegionId region) in
            new[] { ("obs-forge", Sample.TheForge), ("obs-domain", domain) })
        {
            var id = ObservationId.From(observation);

            _ = await lake.Writer.WriteAsync(
                FactBatch.Of(
                    FactSet.BookFeatures,
                    region,
                    id,
                    Sample.DayOnly(1),
                    [new FactEnvelope($"features/{region.Value}", EventTime.At(Sample.Day(1)), Sample.Day(1), id, StaticDataVersion.None)],
                    [FactColumn.OfDouble("spread", [1.5])]),
                [Sample.Covering(observation, observedDay: 1, region: region)],
                token).ConfigureAwait(true);
        }

        List<string?> partitions = [.. Directory
            .EnumerateDirectories(
                Path.Combine(lake.Layout.SetRoot(FactSet.BookFeatures), LakeLayoutSegment(Sample.DayOnly(1))))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)];

        partitions.ShouldBe(["region=10000002", "region=10000043"]);
    }

    [Fact]
    public async Task KeepRegionOutOfThePathForDailyHistory()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Источник публикует сутки одним глобальным файлом: разнесение по регионам дало бы
        // сотни файлов по паре сотен строк вместо одного целого.
        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-global", calendarDay: 1, volume: 1, knownAt: Sample.Day(2)),
            [Sample.Covering("obs-global", observedDay: 1)],
            token).ConfigureAwait(true);

        var partition = Path.Combine(
            lake.Layout.SetRoot(FactSet.HistoryDaily), LakeLayoutSegment(Sample.DayOnly(1)));

        Directory.EnumerateDirectories(partition).ShouldBeEmpty();
        Directory.EnumerateFiles(partition).Count().ShouldBe(1);
    }

    private static string LakeLayoutSegment(DateOnly date) =>
        Facts.Lake.LakeLayout.ObservedDateSegment(date);

    /// <summary>План приходит в рамке из псевдографики — склеиваем в одну строку.</summary>
    private static string Compact(string plan) =>
        string.Join(' ', plan.Split(['│', '┌', '┐', '└', '┘', '├', '┤', '─', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim()));
}
