namespace EveTrader.Application.Live;

/// <summary>
/// Бюджет ошибок источника: сколько отказов подряд он ещё стерпит и когда простит.
///
/// Общий на процесс, и это требование, а не удобство: источник считает ошибки по
/// клиенту, а не по нашим компонентам. Бюджет на регион означал бы, что шестьдесят восемь
/// регионов независимо подводят общий счёт к нулю, каждый считая, что у него всё в порядке.
///
/// Времени «сейчас» здесь нет — часы приходят снаружи, иначе паузу не проверить тестом.
/// </summary>
public sealed class ErrorBudget(TimeProvider clock, int pauseBelow = 20)
{
    private readonly Lock guard = new();

    private int remaining = int.MaxValue;

    private DateTimeOffset resetsAt = DateTimeOffset.MinValue;

    /// <summary>Порог, ниже которого запросы приостанавливаются до сброса окна.</summary>
    public int PauseBelow { get; } = pauseBelow;

    public int Remaining
    {
        get
        {
            lock (guard)
            {
                return remaining;
            }
        }
    }

    /// <summary>Что источник сообщил об остатке бюджета и сроке сброса.</summary>
    public void Reported(int remainingErrors, DateTimeOffset windowResetsAt)
    {
        lock (guard)
        {
            remaining = remainingErrors;
            resetsAt = windowResetsAt;
        }
    }

    /// <summary>
    /// Можно ли слать запрос сейчас, и если нет — до какого момента ждать.
    ///
    /// Окно сбрасывается само по времени: дождавшись срока, бюджет снова считается
    /// полным, пока источник не скажет иначе.
    /// </summary>
    public BudgetVerdict Check()
    {
        DateTimeOffset now = clock.GetUtcNow();

        lock (guard)
        {
            if (now >= resetsAt)
            {
                remaining = int.MaxValue;
                resetsAt = DateTimeOffset.MinValue;

                return BudgetVerdict.Allowed;
            }

            return remaining < PauseBelow
                ? BudgetVerdict.PausedUntil(resetsAt)
                : BudgetVerdict.Allowed;
        }
    }
}
