namespace EveTrader.Domain.Signals;

/// <summary>Почему сигнала нет — по каждому основанию отдельно, чтобы оператор видел все сразу.</summary>
public enum VerdictReason
{
    /// <summary>Ряд, нужный правилу, за этот момент не материализован.</summary>
    SeriesNotMaterialized = 0,

    /// <summary>Окно ряда отклонено: внутри был ненаблюдавшийся интервал.</summary>
    WindowRefused = 1,

    /// <summary>Окно ряда содержало частичное наблюдение — признак помечен неполным.</summary>
    WindowIncomplete = 2,

    /// <summary>Снимка стакана по паре в окне нет.</summary>
    NoQuote = 3,

    /// <summary>Снимок стакана частичный: лучшие цены по нему не гарантированы.</summary>
    QuoteIncomplete = 4,

    /// <summary>Глубина конкуренции по паре не посчитана — счётчиков в признаках нет.</summary>
    CompetitorDepthUnavailable = 5,

    /// <summary>Стакан односторонний: нет цены, от которой считать маржу.</summary>
    OneSidedBook = 6,

    /// <summary>Комиссии съедают разницу цен: маржа после них неположительна.</summary>
    MarginNotPositive = 7,

    /// <summary>Маржа после комиссий ниже порога.</summary>
    MarginBelowThreshold = 8,

    /// <summary>Наблюдённый оборот хотя бы одной стороны ниже порога.</summary>
    TurnoverBelowThreshold = 9,

    /// <summary>Конкурентов в полосе больше, чем терпимо.</summary>
    TooManyCompetitors = 10,

    /// <summary>Перестановок цены за окно больше, чем терпимо.</summary>
    RelistPressureTooHigh = 11,
}
