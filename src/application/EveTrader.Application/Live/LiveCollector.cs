using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;

namespace EveTrader.Application.Live;

/// <summary>
/// Живой сбор: один цикл диспетчера — опросить созревшие регионы и сдать их приёму.
///
/// Свёртки здесь нет и быть не должно: она общая на все источники и живёт в
/// <see cref="ObservationIntake" />. Здесь остаётся то, чего нет у архива и реплея, —
/// расписание, бюджет ошибок и валидаторы кэша.
/// </summary>
public sealed class LiveCollector(
    IRegionBookPoller poller,
    ObservationIntake intake,
    ScopeHistory scope,
    RegionViability viability,
    ObservationSchedule schedule,
    IReadOnlyList<RegionId> hubs,
    TimeProvider clock)
{
    private CollectorState? state;

    public async Task<CollectionCycle> RunCycleAsync(
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        // Состояние переживает циклы: наблюдатели, валидаторы и отметки о входе в охват
        // заводятся один раз. Созданные заново каждый цикл, они не подтвердили бы ни
        // одного исчезновения и не отправили бы ни одного условного запроса.
        state ??= new CollectorState();

        var source = new LiveObservationSource(
            poller, scope, viability, schedule, state, hubs, clock);

        IntakeReport report = await intake
            .RunAsync(source, diffOptions, featureOptions, staticData, cancellationToken)
            .ConfigureAwait(false);

        return new CollectionCycle(
            report.Written - report.Partial,
            report.Unchanged,
            report.Partial,
            report.Failed,
            report.Events);
    }
}
