namespace EveTrader.Domain.Book;

/// <summary>Итог свёртки одного наблюдения.</summary>
/// <param name="Events">События жизни ордера.</param>
/// <param name="Features">Признаки стакана, посчитанные в той же проходке.</param>
/// <param name="SourceGaps">
/// Сколько ордеров отсутствовали и вернулись. Их частота и есть основание для выбора
/// длины окна подтверждения исчезновения.
/// </param>
/// <param name="OrdersSeen">Сколько ордеров увидено.</param>
/// <param name="PendingDisappearances">Сколько исчезновений ждут подтверждения.</param>
public sealed record ObservationOutcome(
    IReadOnlyList<OrderEvent> Events,
    IReadOnlyList<BookFeatures> Features,
    int SourceGaps,
    int OrdersSeen,
    int PendingDisappearances);
