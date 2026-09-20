using System.Net;
using EveTrader.Application.Live;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Опрос стакана региона у публичного ESI.
///
/// Буфер ордеров переиспользуется между наблюдениями, поэтому опросчик заводится один на
/// регион, а не на запрос: новый массив на миллион структур каждые пять минут
/// фрагментирует кучу больших объектов.
/// </summary>
public sealed class EsiRegionBookPoller(
    HttpClient client,
    EsiOptions options,
    ErrorBudget budget,
    TimeProvider clock,
    IRawPageArchive rawPages,
    ILogger<EsiRegionBookPoller> logger) : IRegionBookPoller
{
    private readonly OrderBuffer buffer = new();

    public string Name => "esi";

    public async Task<RegionPoll> PollAsync(
        RegionId region,
        string? validator,
        CancellationToken cancellationToken)
    {
        BudgetVerdict verdict = budget.Check();

        if (!verdict.IsAllowed)
        {
            logger.LogWarning("Регион {Region} не опрошен: {Reason}", region.Value, verdict.Reason);

            return RegionPolls.Skipped(region, verdict.Reason, verdict.ResumeAt);
        }

        DateTimeOffset startedAt = clock.GetUtcNow();
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
                    region, page, page == 1 ? validator : null, options.Datasource);

                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(
                    failure, "Регион {Region}: получено {Received} страниц из {Expected}",
                    region.Value, pages.Count, expected);

                return RegionPolls.Torn(
                    region, buffer.Items[..written], startedAt, pages, expected, expiresAt,
                    nextValidator, failure.Message);
            }

            using (response)
            {
                // Остаток бюджета из заголовков — общий на процесс, а не на регион:
                // источник считает ошибки по клиенту.
                if (EsiHeaders.ErrorBudget(response, clock.GetUtcNow()) is { } reported)
                {
                    budget.Reported(reported.Remaining, reported.ResetsAt);
                }

                if (page == 1 && response.StatusCode == HttpStatusCode.NotModified)
                {
                    // Источник прямо сказал, что стакан тот же. Наблюдение состоялось,
                    // событий нет — и это факт о рынке, а не пробел.
                    return RegionPolls.NotModified(
                        region,
                        startedAt,
                        EsiHeaders.ExpiresAt(response) ?? startedAt,
                        EsiHeaders.Validator(response) ?? validator);
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Регион {Region}: источник ответил {Status} на странице {Page} из {Expected}",
                        region.Value, (int)response.StatusCode, page, expected);

                    return RegionPolls.Torn(
                        region, buffer.Items[..written], startedAt, pages, expected, expiresAt,
                        nextValidator, $"источник ответил {(int)response.StatusCode}");
                }

                if (page == 1)
                {
                    expected = EsiHeaders.PageCount(response);
                    nextValidator = EsiHeaders.Validator(response);
                    buffer.EnsureFor(expected);
                }

                expiresAt = EsiHeaders.ExpiresAt(response) ?? expiresAt;

                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await rawPages.StoreAsync(region, page, startedAt, body, cancellationToken).ConfigureAwait(false);

                var read = EsiOrderReader.Read(body, buffer.Items, written);
                written += read;

                pages.Add(new PageFetch(page, clock.GetUtcNow(), read));
            }
        }

        return RegionPolls.Complete(
            region, buffer.Items[..written], startedAt, pages, expected, expiresAt, nextValidator);
    }
}
