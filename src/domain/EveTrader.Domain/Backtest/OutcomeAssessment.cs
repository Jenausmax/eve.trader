using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Domain.Backtest;

/// <summary>
/// Исход сигнала по фактам, наблюдённым после момента решения.
///
/// Сигнал предлагает купить по лучшей цене покупки и продать по лучшей цене продажи.
/// Круг замкнулся, если на горизонте наблюдались исполнения по обе стороны: на стороне
/// покупки — по цене не ниже цены сигнала (продавцы били в покупку на нашем уровне или
/// выше), на стороне продажи — не выше (покупатели забирали продажу на нашем уровне или
/// ниже). Тогда результат — маржа сигнала после комиссий. Не замкнулся — ордера
/// снимаются, и потеряна брокерская комиссия за их размещение.
///
/// Исход, который нельзя подтвердить наблюдением, — неизвестный, и в метрики не входит.
/// Горизонт с ненаблюдавшимся интервалом не говорит, были исполнения или нет, а
/// засчитать его в неудачу значило бы выдать пробел сбора за свойство правила.
///
/// Горизонт называется длиной, а читается по снимкам. Наблюдение — снимки через шаг, и
/// исполнения за горизонт видны в первом снимке не раньше его конца: этот снимок и
/// закрывает горизонт, и ручается за отрезок до предыдущего. Если такого снимка нет в
/// пределах полутора шагов — горизонт закрыть нечем, исход неизвестен.
///
/// Мера грубая и названа так намеренно: наблюдённые исполнения — нижняя граница
/// настоящих (сделка, после которой ордер исчез между снимками, видна как исчезновение).
/// Неудачи здесь поэтому скорее завышены, чем занижены.
/// </summary>
public static class OutcomeAssessment
{
    /// <summary>
    /// Отрезок, по которому читается исход: от момента решения до первого снимка не
    /// раньше конца названного горизонта. <see langword="null" />, если такого снимка нет
    /// в пределах полутора шагов, — горизонт закрыть нечем.
    /// </summary>
    /// <param name="decision">Момент решения.</param>
    /// <param name="horizon">Названный горизонт.</param>
    /// <param name="chain">Цепочка снимков (<see cref="SeriesCoverage.Chain" />).</param>
    public static TimeRange? Observed(DateTimeOffset decision, TimeSpan horizon, IReadOnlyList<CoverageEntry> chain)
    {
        DateTimeOffset due = decision + horizon;

        return chain
            .Where(entry => entry.Covers && entry.Collected.To >= due)
            .MinBy(static entry => entry.Collected.To) is { } closing
            && closing.Collected.To - due <= closing.ObservationStep * SeriesCoverage.StepTolerance
            ? TimeRange.Between(decision, closing.Collected.To)
            : null;
    }

    /// <param name="signal">Сигнал с обоснованием.</param>
    /// <param name="horizon">Названный горизонт.</param>
    /// <param name="observed">
    /// Отрезок, по которому исход читается (<see cref="Observed" />); <see langword="null" />
    /// — горизонт закрыть нечем.
    /// </param>
    /// <param name="horizonWindow">Отрезок с приговором о его покрытии.</param>
    /// <param name="events">События, наблюдённые на отрезке или около него.</param>
    public static AssessedSignal Assess(
        StationTradingVerdict signal,
        TimeSpan horizon,
        TimeRange? observed,
        SeriesWindow? horizonWindow,
        IReadOnlyList<OrderEvent> events)
    {
        if (signal.Justification is not { } justification || !signal.IsSignal)
        {
            throw new ArgumentException("Исход оценивается только у сигнала", nameof(signal));
        }

        // В исход пишется названный горизонт, а не отрезок до закрывающего снимка: отчёт
        // называет горизонт правила, а снимок — лишь то, чем он прочитан.
        var named = TimeRange.Between(signal.Decision, signal.Decision + horizon);

        if (observed is not { } range
            || horizonWindow is not { IsAdmitted: true, IsIncomplete: false })
        {
            return new AssessedSignal(signal, named, SignalOutcome.Unknown, null);
        }

        var fills = events
            .Where(moment => moment.Kind is OrderEventKind.ObservedFill
                && moment.TypeId == signal.TypeId
                && moment.LocationId == signal.Scope.StationId
                && moment.ObservedAt > signal.Decision
                && moment.ObservedAt <= range.To)
            .ToList();

        var bought = fills.Any(moment => moment.IsBuy && moment.Price >= justification.BestBid);
        var sold = fills.Any(moment => !moment.IsBuy && moment.Price <= justification.BestAsk);

        if (bought && sold)
        {
            return new AssessedSignal(signal, named, SignalOutcome.Success, justification.NetMargin);
        }

        var broker = justification.Parameters.Fees.BrokerFeeRate;
        var lost = IskAmount.FromIsk(-((justification.BestBid.ToIsk() + justification.BestAsk.ToIsk()) * broker));

        return new AssessedSignal(signal, named, SignalOutcome.Failure, lost);
    }
}
