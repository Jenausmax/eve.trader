using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Domain.Backtest;

/// <summary>
/// Отчёт о прогоне правила на истории.
///
/// Метрика без базовой линии запрещена: доля успеха без точки отсчёта не отличает
/// работающее правило от растущего рынка. Базовая линия — «не торговать» на том же
/// интервале: нулевой результат и нулевой риск. Правило обязано быть лучше неё, а не
/// лучше нуля сигналов.
///
/// Доля неизвестных исходов называется всегда. Прогон, где она велика, доказательным не
/// является, и умолчать об этом значило бы выдать неполную проверку за полную. Убыточный
/// результат называется как есть — бэктест существует ради него в той же мере, что ради
/// прибыльного.
/// </summary>
/// <param name="ParameterSet">Имя набора параметров.</param>
/// <param name="Label">Метка оператора.</param>
/// <param name="Canonical">Пороги и ставки набора в каноническом виде.</param>
/// <param name="Scope">Станция.</param>
/// <param name="Interval">Интервал моментов решения.</param>
/// <param name="Step">Шаг решений.</param>
/// <param name="Horizon">Горизонт оценки исхода.</param>
/// <param name="StaticData">Версия статических данных.</param>
/// <param name="RanAt">Момент прогона.</param>
/// <param name="Decisions">Моментов решения.</param>
/// <param name="Considered">Пар рассмотрено, по всем моментам.</param>
/// <param name="Signals">Сигналов.</param>
/// <param name="ConditionsNotMet">Исходов «условия не выполнены».</param>
/// <param name="InsufficientData">Исходов «данных не хватило».</param>
/// <param name="Successes">Сигналов с замкнувшимся кругом.</param>
/// <param name="Failures">Сигналов, где круг не замкнулся.</param>
/// <param name="Unknown">Сигналов с неизвестным исходом.</param>
/// <param name="TotalRealizedCents">Суммарный результат на единицу по определённым исходам, в сотых ISK.</param>
/// <param name="MeanRealizedCents">Средний результат на определённый исход, в сотых ISK.</param>
/// <param name="DeviationRealizedCents">Разброс результата — стандартное отклонение, в сотых ISK.</param>
/// <param name="MaxUnknownShare">Объявленный порог доли неизвестных исходов.</param>
public sealed record BacktestReport(
    ParameterSetName ParameterSet,
    string Label,
    string Canonical,
    StationTradingScope Scope,
    TimeRange Interval,
    TimeSpan Step,
    TimeSpan Horizon,
    StaticDataVersion StaticData,
    DateTimeOffset RanAt,
    int Decisions,
    int Considered,
    int Signals,
    int ConditionsNotMet,
    int InsufficientData,
    int Successes,
    int Failures,
    int Unknown,
    long TotalRealizedCents,
    decimal MeanRealizedCents,
    double DeviationRealizedCents,
    decimal MaxUnknownShare)
{
    /// <summary>Сигналов с определённым исходом.</summary>
    public int Determined => Successes + Failures;

    public decimal DeterminedShare => Signals == 0 ? 0m : Determined / (decimal)Signals;

    public decimal UnknownShare => Signals == 0 ? 0m : Unknown / (decimal)Signals;

    /// <summary>Доля успехов среди определённых исходов.</summary>
    public decimal HitRate => Determined == 0 ? 0m : Successes / (decimal)Determined;

    /// <summary>Результат базовой линии «не торговать» на том же интервале — ноль по построению.</summary>
    public long BaselineTotalCents => 0L;

    /// <summary>Правило лучше базовой линии: есть определённые исходы, и их сумма положительна.</summary>
    public bool BeatsBaseline => Determined > 0 && TotalRealizedCents > BaselineTotalCents;

    /// <summary>
    /// Прогон доказателен: сигналы были, исходы определены, и доля неизвестных не выше
    /// объявленного порога. Прогон без сигналов не доказывает ничего — ни в пользу правила,
    /// ни против.
    /// </summary>
    public bool IsConclusive => Signals > 0 && Determined > 0 && UnknownShare <= MaxUnknownShare;

    /// <summary>Отчёт из исходов. Суммы целые, порядок исходов на результат не влияет.</summary>
    public static BacktestReport Of(
        BacktestSetup setup,
        int decisions,
        IReadOnlyList<StationTradingVerdict> verdicts,
        IReadOnlyList<AssessedSignal> assessed)
    {
        long[] realized =
        [
            .. assessed
                .Select(static item => item.Realized)
                .Where(static amount => amount.HasValue)
                .Select(static amount => amount.GetValueOrDefault().Cents),
        ];
        var total = realized.Sum();
        var mean = realized.Length == 0 ? 0m : total / (decimal)realized.Length;
        var variance = realized.Length == 0
            ? 0m
            : realized.Sum(value => (value - mean) * (value - mean)) / realized.Length;

        return new BacktestReport(
            setup.Parameters.Name,
            setup.Parameters.Label,
            StationTradingParameters.Canonical(setup.Parameters),
            setup.Scope,
            setup.Interval,
            setup.Step,
            setup.Horizon,
            setup.StaticData,
            setup.RanAt,
            decisions,
            verdicts.Count,
            verdicts.Count(static verdict => verdict.Outcome is ConsiderationOutcome.Signal),
            verdicts.Count(static verdict => verdict.Outcome is ConsiderationOutcome.ConditionsNotMet),
            verdicts.Count(static verdict => verdict.Outcome is ConsiderationOutcome.InsufficientData),
            assessed.Count(static item => item.Outcome is SignalOutcome.Success),
            assessed.Count(static item => item.Outcome is SignalOutcome.Failure),
            assessed.Count(static item => item.Outcome is SignalOutcome.Unknown),
            total,
            mean,
            Math.Sqrt((double)variance),
            setup.MaxUnknownShare);
    }
}
