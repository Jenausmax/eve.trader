using EveTrader.Application.Live;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>Сборка исходов опроса региона.</summary>
internal static class RegionPolls
{
    /// <summary>
    /// Конец интервала сбора.
    ///
    /// Интервал всегда положительной ширины: наблюдение заняло время, даже если часы
    /// не успели это заметить. Вырожденный интервал означал бы атомарный срез — ровно
    /// то, на что мы перестали претендовать.
    /// </summary>
    public static DateTimeOffset EndOf(List<PageFetch> pages, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(pages);

        DateTimeOffset last = pages.Count == 0 ? startedAt : pages[^1].ReceivedAt;

        return last > startedAt ? last : startedAt.AddTicks(1);
    }

    public static RegionPoll Complete(
        RegionId region,
        IReadOnlyList<OrderSnapshot> orders,
        DateTimeOffset startedAt,
        List<PageFetch> pages,
        int expected,
        DateTimeOffset expiresAt,
        string? validator) =>
        new(
            region, RegionPollOutcome.Complete, orders,
            TimeRange.Between(startedAt, EndOf(pages, startedAt)),
            pages, expected, expiresAt, validator, null);

    /// <summary>
    /// Обрыв посреди пагинации. Ни одной страницы — наблюдения не было вовсе; часть
    /// страниц — наблюдение состоялось, но неполное, и об отсутствии ордера в нём
    /// судить нельзя.
    /// </summary>
    public static RegionPoll Torn(
        RegionId region,
        IReadOnlyList<OrderSnapshot> orders,
        DateTimeOffset startedAt,
        List<PageFetch> pages,
        int expected,
        DateTimeOffset expiresAt,
        string? validator,
        string reason) =>
        new(
            region,
            pages.Count == 0 ? RegionPollOutcome.Failed : RegionPollOutcome.Partial,
            pages.Count == 0 ? [] : orders,
            TimeRange.Between(startedAt, EndOf(pages, startedAt)),
            pages, expected, expiresAt, validator, reason);

    public static RegionPoll NotModified(
        RegionId region,
        DateTimeOffset startedAt,
        DateTimeOffset expiresAt,
        string? validator) =>
        new(
            region, RegionPollOutcome.NotModified, [],
            TimeRange.Between(startedAt, startedAt.AddTicks(1)),
            [], 0, expiresAt, validator, null);

    public static RegionPoll Skipped(RegionId region, string reason, DateTimeOffset resumeAt) =>
        new(
            region, RegionPollOutcome.BudgetExhausted, [],
            TimeRange.Between(resumeAt.AddTicks(-1), resumeAt),
            [], 0, resumeAt, null, reason);
}
