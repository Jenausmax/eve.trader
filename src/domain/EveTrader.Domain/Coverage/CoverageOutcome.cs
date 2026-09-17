namespace EveTrader.Domain.Coverage;

/// <summary>Исход попытки наблюдения региона.</summary>
public enum CoverageOutcome
{
    /// <summary>Стакан получен полностью.</summary>
    Success = 0,

    /// <summary>Источник ответил, что данные не менялись. Наблюдение состоялось.</summary>
    NotModified = 1,

    /// <summary>Получена часть страниц. Отсутствие ордера в таком наблюдении ничего не значит.</summary>
    Partial = 2,

    /// <summary>Источник не ответил. Интервал не покрыт.</summary>
    Failure = 3,
}
