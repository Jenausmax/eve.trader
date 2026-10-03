namespace EveTrader.Domain.Backtest;

/// <summary>
/// Сравнение двух наборов параметров — по записанным отчётам, а не повторным прогоном.
///
/// Сравнимы только отчёты об одном и том же: тот же интервал, станция, шаг и горизонт.
/// Иначе разница результатов говорит о разнице рынков, а не правил. И даже сравнимое
/// сравнение не доказательнее своих отчётов: если хоть один прогон недоказателен,
/// лидер называется, но с этой оговоркой.
/// </summary>
/// <param name="First">Первый отчёт.</param>
/// <param name="Second">Второй отчёт.</param>
public sealed record BacktestComparison(BacktestReport First, BacktestReport Second)
{
    public bool Comparable =>
        First.Interval == Second.Interval
        && First.Scope == Second.Scope
        && First.Step == Second.Step
        && First.Horizon == Second.Horizon;

    /// <summary>Оба прогона доказательны — только тогда лидер что-то значит.</summary>
    public bool Conclusive => First.IsConclusive && Second.IsConclusive;

    /// <summary>
    /// Набор с большим суммарным результатом; при равенстве — с большей долей успехов.
    /// <see langword="null" />, если отчёты несравнимы или неразличимы.
    /// </summary>
    public BacktestReport? Leader
    {
        get
        {
            if (!Comparable)
            {
                return null;
            }

            var byTotal = First.TotalRealizedCents.CompareTo(Second.TotalRealizedCents);
            var order = byTotal != 0 ? byTotal : First.HitRate.CompareTo(Second.HitRate);

            return order switch
            {
                > 0 => First,
                < 0 => Second,
                _ => null,
            };
        }
    }
}
