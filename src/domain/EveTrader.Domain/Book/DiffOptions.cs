namespace EveTrader.Domain.Book;

/// <summary>Настройки разметки событий.</summary>
/// <param name="Npc">Отпечаток ордеров NPC.</param>
/// <param name="MinEditInterval">
/// Минимальный интервал между правками одного ордера, установленный игрой. Наблюдение с
/// шагом не больше этого интервала полно по перестановкам; более редкое — нет.
/// </param>
/// <param name="DisappearanceWindow">
/// Сколько последовательных полных наблюдений без ордера подтверждают исчезновение.
/// Настройка, а не константа: точное значение выбирается по частоте пропусков источника,
/// замеренной на архиве. Стартовое — два.
/// </param>
public sealed record DiffOptions(
    NpcFingerprint Npc,
    TimeSpan MinEditInterval,
    int DisappearanceWindow)
{
    public static DiffOptions Default { get; } = new(
        NpcFingerprint.Default,
        TimeSpan.FromMinutes(5),
        DisappearanceWindow: 2);

    /// <summary>Полно ли наблюдение с таким шагом по перестановкам цены.</summary>
    public bool IsCompleteByReprice(TimeSpan step) => step <= MinEditInterval;
}
