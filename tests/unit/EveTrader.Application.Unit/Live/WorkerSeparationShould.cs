using System.Reflection;
using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Book;
using EveTrader.Application.Facts;
using EveTrader.Application.Live;
using EveTrader.Application.Workers;
using EveTrader.Domain.Scope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace EveTrader.Application.Unit.Live;

/// <summary>
/// Разные задачи — разные воркеры. Правило запрещает их склеивать, и причина
/// практическая: у склеенных счётчики общие, а значит по ним нельзя сказать, что именно
/// отказало.
/// </summary>
public sealed class WorkerSeparationShould
{
    private static readonly Type[] Workers =
        [typeof(MarketObservationWorker), typeof(DailyHistoryWorker), typeof(CompactionWorker)];

    [Fact]
    public void KeepObservationHistoryAndCompactionApart()
    {
        Workers.Length.ShouldBe(3);
        Workers.ShouldAllBe(static worker => worker.IsSubclassOf(typeof(ScheduledWorkerBase)));
        Workers.Select(static worker => worker.Name).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void GiveEachWorkerItsOwnCounters()
    {
        Type[] cycles = [typeof(CollectionCycle), typeof(DailyHistoryCycle), typeof(CompactionCycle)];

        var names = cycles
            .Select(static cycle => cycle.GetProperties()
                .Select(static property => property.GetCustomAttribute<CounterAttribute>()?.Name)
                .Where(static name => name is not null)
                .Order(StringComparer.Ordinal)
                .ToList())
            .ToList();

        names.ShouldAllBe(static set => set.Count > 0);

        // Наборы счётчиков не пересекаются: «сколько убрано» и «сколько наблюдений
        // записано» отвечают на разные вопросы.
        names.SelectMany(static set => set).Distinct().Count()
            .ShouldBe(names.Sum(static set => set.Count));
    }

    [Fact]
    public void PollAdaptivelyForObservationAndOnAnIntervalForTheRest()
    {
        // Наблюдение — AdaptivePollSchedule: пока есть созревшие регионы, цикл идёт без
        // пауз; сетки нет, потому что общего среза мира нет.
        _ = ScheduleOf(Observation()).ShouldBeOfType<AdaptivePollSchedule>();

        // История и компакция привязаны к своему темпу, а не к готовности регионов.
        _ = ScheduleOf(Compaction()).ShouldBeOfType<IntervalSchedule>();
    }

    [Fact]
    public void DescribeTheirOwnSchedules()
    {
        // Describe() попадает в стартовый лог — по нему оператор узнаёт, с каким темпом
        // воркер поднялся, не читая конфигурацию.
        ScheduleOf(Observation()).Describe().ShouldContain("adaptive poll");
        ScheduleOf(Compaction()).Describe().ShouldContain("every");
    }

    private static MarketObservationWorker Observation()
    {
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var derivation = new ObservationDerivation(
            Substitute.For<IFactWriter>(), new DailyCheckpointPolicy());

        var collector = new LiveCollector(
            Substitute.For<IRegionBookPoller>(),
            derivation,
            new ScopeHistory(),
            new RegionViability(),
            new ObservationSchedule(),
            [],
            TimeProvider.System,
            NullLogger<LiveCollector>.Instance);

        return new MarketObservationWorker(
            services, collector, new ObservationOptions(), TimeProvider.System, null,
            NullLogger<MarketObservationWorker>.Instance);
    }

    private static CompactionWorker Compaction() =>
        new(
            new ServiceCollection().BuildServiceProvider(),
            Substitute.For<IFactMaintenance>(),
            Substitute.For<IRawPageArchive>(),
            new CompactionOptions(),
            TimeProvider.System,
            null,
            NullLogger<CompactionWorker>.Instance);

    /// <summary>Расписание воркера. Свойство защищённое — читается рефлексией.</summary>
    private static WorkerSchedule ScheduleOf(ScheduledWorkerBase worker) =>
        (WorkerSchedule)worker.GetType()
            .GetProperty("Schedule", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker)!;
}
