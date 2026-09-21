using EveTrader.Application.Acceptance;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Unit.Acceptance;

/// <summary>
/// Приёмка сверяет не всё, что записано, а то, что перестройка в принципе может дать.
///
/// Граница проходит по окну подтверждения исчезновений. Пока окно не закрылось, ордер
/// для конвейера жив: записанные признаки взяты из снимка, где его уже нет, а
/// перестроенные — из состава, подтверждённого событиями, где он ещё есть. Требовать
/// тут совпадения значило бы требовать, чтобы вывод был сделан раньше, чем набраны
/// подтверждения.
///
/// Замерено на архиве EVE Ref за 15 сентября 2026: расхождение приёмки приходилось
/// ровно на эти наблюдения и больше ни на какие.
/// </summary>
public sealed class DisappearanceWindowInAcceptanceShould
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly RegionId Forge = RegionId.From(10000002);

    private static readonly HashSet<ObservationId> NoBaselines = [];

    [Fact]
    public void LeaveTheLastObservationOfTheIntervalOutOfComparison()
    {
        IReadOnlyList<CoverageEntry> entries = [.. Enumerable.Range(0, 4).Select(Observation)];

        HashSet<ObservationId> closed = FactShapes.WindowClosed(entries, NoBaselines, disappearanceWindow: 2);

        // Последнему подтверждать нечем: следующего наблюдения нет.
        closed.ShouldBe([Name(0), Name(1), Name(2)], ignoreOrder: true);
    }

    [Fact]
    public void LeaveTheObservationBeforeABaselineOutOfComparison()
    {
        IReadOnlyList<CoverageEntry> entries = [.. Enumerable.Range(0, 4).Select(Observation)];

        // Свёртку перезапустили на третьем наблюдении: она начала с чистого состояния,
        // и кандидаты на исчезновение, набранные до неё, подтверждения не получат.
        HashSet<ObservationId> closed = FactShapes.WindowClosed(
            entries, new HashSet<ObservationId> { Name(2) }, disappearanceWindow: 2);

        closed.ShouldBe([Name(0), Name(2)], ignoreOrder: true);
    }

    [Fact]
    public void NeedTheWholeWindowAheadNotJustOneObservation()
    {
        IReadOnlyList<CoverageEntry> entries = [.. Enumerable.Range(0, 4).Select(Observation)];

        HashSet<ObservationId> closed = FactShapes.WindowClosed(entries, NoBaselines, disappearanceWindow: 3);

        // Окно в три наблюдения съедает два последних, а не одно.
        closed.ShouldBe([Name(0), Name(1)], ignoreOrder: true);
    }

    [Fact]
    public void CountWindowsPerRegionAndNotAcrossThem()
    {
        IReadOnlyList<CoverageEntry> entries =
        [
            .. Enumerable.Range(0, 3).Select(Observation),
            .. Enumerable.Range(0, 3).Select(static step => Observation(step, RegionId.From(10000043))),
        ];

        HashSet<ObservationId> closed = FactShapes.WindowClosed(entries, NoBaselines, disappearanceWindow: 2);

        // По два наблюдения из трёх в каждом регионе: цепочки независимы.
        closed.Count.ShouldBe(4);
    }

    [Fact]
    public void IgnoreEverythingButCompleteObservations()
    {
        IReadOnlyList<CoverageEntry> entries =
        [
            Observation(0),
            CoverageEntries.Failed(
                Name(1), Forge, Collected(1), "everef", TimeSpan.FromMinutes(30),
                "источник ответил 503", Start.AddMinutes(30)),
            Observation(2),
        ];

        HashSet<ObservationId> closed = FactShapes.WindowClosed(entries, NoBaselines, disappearanceWindow: 2);

        // Отказ в цепочку не входит вовсе: сверять по нему нечего, и следующим для
        // первого наблюдения оказывается третье.
        closed.ShouldBe([Name(0)], ignoreOrder: true);
    }

    [Fact]
    public void KeepChainsOfDifferentSourcesApart()
    {
        IReadOnlyList<CoverageEntry> entries =
        [
            .. Enumerable.Range(0, 3).Select(Observation),

            // Суточная история наблюдает тот же регион, но своей цепочкой: соседом
            // снимку стакана она не приходится и его окно не закрывает.
            CoverageEntries.Success(
                ObservationId.From("everef-archive-2026-09-15"),
                Forge,
                TimeRange.Between(Start.AddHours(-12), Start.AddHours(12)),
                pages: 1,
                orderCount: 1,
                source: "everef-archive",
                observationStep: TimeSpan.FromDays(1),
                knownAt: Start.AddDays(6)),
        ];

        HashSet<ObservationId> closed = FactShapes.WindowClosed(entries, NoBaselines, disappearanceWindow: 2);

        closed.ShouldBe([Name(0), Name(1)], ignoreOrder: true);
    }

    [Fact]
    public void NotCallTwoSourcesOfOneRegionABreakInTheChain()
    {
        IReadOnlyList<CoverageEntry> entries =
        [
            .. Enumerable.Range(0, 3).Select(Observation),
            CoverageEntries.Success(
                ObservationId.From("everef-archive-2026-09-15"),
                Forge,
                TimeRange.Between(Start.AddHours(-12), Start.AddHours(12)),
                pages: 1,
                orderCount: 1,
                source: "everef-archive",
                observationStep: TimeSpan.FromDays(1),
                knownAt: Start.AddDays(6)),
        ];

        // Полсуток между суточным наблюдением и получасовым — не разрыв, а два разных
        // источника. Считать иначе значило бы объявлять пробел там, где данные полны.
        FactShapes.GapsIn(entries).ShouldBeEmpty();
    }

    private static ObservationId Name(int step) => ObservationId.From($"everef-{step}");

    private static TimeRange Collected(int step) =>
        TimeRange.Between(Start.AddMinutes(30 * step), Start.AddMinutes((30 * step) + 3));

    private static CoverageEntry Observation(int step) => Observation(step, Forge);

    private static CoverageEntry Observation(int step, RegionId region) =>
        CoverageEntries.Success(
            region == Forge ? Name(step) : ObservationId.From($"everef-{region.Value}-{step}"),
            region,
            Collected(step),
            pages: 1,
            orderCount: 1,
            source: "everef",
            observationStep: TimeSpan.FromMinutes(30),
            knownAt: Start.AddMinutes((30 * step) + 3));
}
