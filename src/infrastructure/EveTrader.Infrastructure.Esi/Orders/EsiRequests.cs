using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>Построение запросов к ESI.</summary>
internal static class EsiRequests
{
    /// <summary>
    /// Запрос страницы стакана. Валидатор кэша ставится только на первую страницу:
    /// «не изменилось» относится к региону целиком, и спрашивать его у каждой страницы
    /// значило бы тратить запросы впустую.
    /// </summary>
    public static HttpRequestMessage OrdersPage(RegionId region, int page, string? validator, string datasource)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"markets/{region.Value}/orders/?order_type=all&page={page}&datasource={datasource}");

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (validator is not null)
        {
            _ = request.Headers.TryAddWithoutValidation("If-None-Match", validator);
        }

        return request;
    }
}
