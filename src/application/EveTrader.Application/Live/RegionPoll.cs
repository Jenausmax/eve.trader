using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Live;

/// <summary>
/// Результат опроса одного региона.
///
/// Форма наблюдения здесь та же, что у архива: стакан и границы интервала сбора.
/// Различие между источниками кончается на приёме — дальше по конвейеру его быть не должно.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Outcome">Исход.</param>
/// <param name="Orders">Полученные ордера; пусто при отказе и при «не изменилось».</param>
/// <param name="Collected">Границы интервала сбора: от первой страницы до последней.</param>
/// <param name="Pages">Страницы с временами получения.</param>
/// <param name="PagesExpected">Сколько страниц объявил источник.</param>
/// <param name="ExpiresAt">Срок годности ответа по объявлению источника.</param>
/// <param name="Validator">Валидатор кэша для следующего запроса.</param>
/// <param name="FailureReason">Причина отказа.</param>
public sealed record RegionPoll(
    RegionId Region,
    RegionPollOutcome Outcome,
    IReadOnlyList<OrderSnapshot> Orders,
    TimeRange Collected,
    IReadOnlyList<PageFetch> Pages,
    int PagesExpected,
    DateTimeOffset ExpiresAt,
    string? Validator,
    string? FailureReason)
{
    /// <summary>Доля полученных страниц; единица, если объявленного числа нет.</summary>
    public double PagesFraction =>
        PagesExpected <= 0 ? 1d : (double)Pages.Count / PagesExpected;

    /// <summary>
    /// Наблюдение состоялось и полно. «Не изменилось» тоже полно: источник прямо сказал,
    /// что стакан тот же.
    /// </summary>
    public bool IsComplete => Outcome is RegionPollOutcome.Complete or RegionPollOutcome.NotModified;
}
