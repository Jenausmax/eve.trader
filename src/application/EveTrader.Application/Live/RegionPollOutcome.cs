namespace EveTrader.Application.Live;

/// <summary>Чем закончился опрос региона.</summary>
public enum RegionPollOutcome
{
    /// <summary>Стакан получен полностью.</summary>
    Complete = 0,

    /// <summary>Источник ответил, что данные не менялись. Наблюдение состоялось.</summary>
    NotModified = 1,

    /// <summary>Получена часть страниц.</summary>
    Partial = 2,

    /// <summary>Источник не ответил.</summary>
    Failed = 3,

    /// <summary>Не спрашивали: бюджет ошибок исчерпан.</summary>
    BudgetExhausted = 4,
}
