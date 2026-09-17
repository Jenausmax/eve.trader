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
                Sample.Covering($"obs-{day}", observedDay: day),
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
                Sample.Covering($"obs-{day}", observedDay: day),
                token).ConfigureAwait(true);
        }

        var plan = await lake.Rows.ExplainAsync(
            FactSet.HistoryDaily, TimeRange.Between(Sample.Day(1), Sample.Day(5)), token).ConfigureAwait(true);

        // Отсекать нечего — движок читает все партиции, и строки об отсечении в плане нет.
        FilesRead.Match(Compact(plan)).Groups[1].Value.ShouldBe("4");
    }

    [Fact]
    public async Task PutRegionIntoThePartitionPath()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var domain = RegionId.From(10000043);

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-forge", calendarDay: 1, volume: 1, knownAt: Sample.Day(2)),
            Sample.Covering("obs-forge", observedDay: 1),
            token).ConfigureAwait(true);
        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-domain", calendarDay: 1, volume: 2, knownAt: Sample.Day(2), region: domain),
            Sample.Covering("obs-domain", observedDay: 1, region: domain),
            token).ConfigureAwait(true);

        var partitions = Directory
            .EnumerateDirectories(
                Path.Combine(lake.Layout.SetRoot(FactSet.HistoryDaily), LakeLayoutSegment(Sample.DayOnly(1))))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        partitions.ShouldBe(["region=10000002", "region=10000043"]);
    }

    private static string LakeLayoutSegment(DateOnly date) =>
        Facts.Lake.LakeLayout.ObservedDateSegment(date);

    /// <summary>План приходит в рамке из псевдографики — склеиваем в одну строку.</summary>
    private static string Compact(string plan) =>
        string.Join(' ', plan.Split(['│', '┌', '┐', '└', '┘', '├', '┤', '─', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim()));
}
