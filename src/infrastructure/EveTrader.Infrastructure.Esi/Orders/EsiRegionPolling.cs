using System.Net;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Live;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Проход по страницам стакана одного региона.
///
/// Каждый ответ источника отмечается в метриках здесь, а не у вызывающей стороны: число
/// запросов и принятый объём известны только тут, и посчитать их снаружи можно было бы
/// только повторив разбор ответа.
/// </summary>
internal static class EsiRegionPolling
{
    public static async Task<RegionPoll> PagesAsync(
        EsiPollContext context,
        IOperationScope operation,
        RegionId region,
        string? validator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);

        DateTimeOffset startedAt = context.Clock.GetUtcNow();
        var pages = new List<PageFetch>();
        var written = 0;
        var expected = 1;
        DateTimeOffset expiresAt = startedAt;
        var nextValidator = validator;

        for (var page = 1; page <= expected; page++)
        {
            HttpResponseMessage response;

            try
            {
                using HttpRequestMessage request = EsiRequests.OrdersPage(
                    region, page, page == 1 ? validator : null, context.Options.Datasource);

                context.Diagnostics.SourceRequests.Add(1);

                response = await context.Client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
            {
                context.Logger.LogWarning(
                    failure, "Регион {Region}: получено {Received} страниц из {Expected}",
                    region.Value, pages.Count, expected);

                return Torn(
                    context, operation, region, written, startedAt, pages, expected, expiresAt,
                    nextValidator, failure.Message);
            }

            using (response)
            {
                // Остаток бюджета из заголовков — общий на процесс, а не на регион:
                // источник считает ошибки по клиенту.
                if (EsiHeaders.ErrorBudget(response, context.Clock.GetUtcNow()) is { } reported)
                {
                    context.Budget.Reported(reported.Remaining, reported.ResetsAt);
                    context.Diagnostics.ErrorBudgetRemaining.Record(reported.Remaining);
                }

                if (page == 1 && response.StatusCode == HttpStatusCode.NotModified)
                {
                    // Источник прямо сказал, что стакан тот же. Наблюдение состоялось,
                    // событий нет — и это факт о рынке, а не пробел.
                    context.Diagnostics.NotModifiedResponses.Add(1);
                    _ = operation.WithTag("status", "not_modified");

                    return RegionPolls.NotModified(
                        region,
                        startedAt,
                        EsiHeaders.ExpiresAt(response) ?? startedAt,
                        EsiHeaders.Validator(response) ?? validator);
                }

                if (!response.IsSuccessStatusCode)
                {
                    context.Logger.LogWarning(
                        "Регион {Region}: источник ответил {Status} на странице {Page} из {Expected}",
                        region.Value, (int)response.StatusCode, page, expected);

                    return Torn(
                        context, operation, region, written, startedAt, pages, expected, expiresAt,
                        nextValidator, $"источник ответил {(int)response.StatusCode}");
                }

                if (page == 1)
                {
                    expected = EsiHeaders.PageCount(response);
                    nextValidator = EsiHeaders.Validator(response);
                    context.Buffer.EnsureFor(expected);
                }

                expiresAt = EsiHeaders.ExpiresAt(response) ?? expiresAt;

                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                context.Diagnostics.SourceBytes.Add(body.Length);

                await context.RawPages
                    .StoreAsync(region, page, startedAt, body, cancellationToken)
                    .ConfigureAwait(false);

                var read = EsiOrderReader.Read(body, context.Buffer.Items, written);
                written += read;

                pages.Add(new PageFetch(page, context.Clock.GetUtcNow(), read));
            }
        }

        _ = operation.WithTag("status", "ok");

        return RegionPolls.Complete(
            region, context.Buffer.Items[..written], startedAt, pages, expected, expiresAt, nextValidator);
    }

    /// <summary>
    /// Обрыв посреди пагинации: исход и тег операции ставятся по тому, доехала ли хоть
    /// одна страница. Отказ и неполное наблюдение — разные вещи, и красить их одинаково
    /// нельзя: по неполному нельзя судить об исчезновении, но оно состоялось.
    /// </summary>
    public static RegionPoll Torn(
        EsiPollContext context,
        IOperationScope operation,
        RegionId region,
        int written,
        DateTimeOffset startedAt,
        List<PageFetch> pages,
        int expected,
        DateTimeOffset expiresAt,
        string? validator,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(pages);

        _ = operation.WithTag("status", pages.Count == 0 ? "failed" : "partial");

        return RegionPolls.Torn(
            region, context.Buffer.Items[..written], startedAt, pages, expected, expiresAt, validator, reason);
    }
}
