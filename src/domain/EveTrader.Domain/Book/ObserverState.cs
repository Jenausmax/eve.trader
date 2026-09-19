namespace EveTrader.Domain.Book;

/// <summary>
/// Состояние свёртки региона между наблюдениями: база последнего полного наблюдения,
/// индексы и реестр кандидатов на исчезновение.
///
/// Вынесено отдельным типом, потому что логика свёртки живёт в
/// <see cref="OrderDiff" /> статическими функциями. Состояние и операции над ним
/// разделены намеренно — так видно, что именно наблюдатель помнит, и почему помнит
/// вообще: исчезновение выводится не из пары снимков, а из нескольких подряд.
///
/// Буферы переиспользуются: новый массив на миллион структур каждые пять минут
/// фрагментирует кучу больших объектов.
/// </summary>
internal sealed class ObserverState(DiffOptions options, FeatureOptions featureOptions)
{
    public DiffOptions Options { get; } = options;

    public BookFeatureBuilder Features { get; } = new(featureOptions);

    public Dictionary<long, Absence> Absences { get; } = [];

    public OrderIndex BaselineIndex { get; } = new();

    /// <summary>
    /// Индекс текущего наблюдения. Поле, а не локальная переменная: создавать его на
    /// каждое наблюдение значит аллоцировать таблицу на миллион записей каждые пять
    /// минут — ровно то, ради чего переиспользование и заведено.
    /// </summary>
    public OrderIndex CurrentIndex { get; } = new();

    /// <summary>Последнее полное наблюдение. Частичное базой не становится.</summary>
    public OrderSnapshot[] Baseline { get; set; } = [];

    public int BaselineCount { get; set; }

    public ObservationMeta? BaselineMeta { get; set; }

    public bool HasBaseline => BaselineMeta is not null;
}
