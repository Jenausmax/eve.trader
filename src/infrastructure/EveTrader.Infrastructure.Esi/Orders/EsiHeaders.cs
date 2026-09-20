using System.Globalization;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>Заголовки ESI, из которых строится расписание и бюджет ошибок.</summary>
public static class EsiHeaders
{
    public const string Pages = "X-Pages";
    public const string ErrorLimitRemain = "X-ESI-Error-Limit-Remain";
    public const string ErrorLimitReset = "X-ESI-Error-Limit-Reset";

    /// <summary>Сколько страниц объявил источник; одна, если не сказал.</summary>
    public static int PageCount(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Headers.TryGetValues(Pages, out IEnumerable<string>? values)
            && int.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, out var pages)
            && pages > 0
            ? pages
            : 1;
    }

    /// <summary>
    /// Срок годности ответа.
    ///
    /// Берётся из <c>Expires</c>, а не рассчитывается: граница поколения кэша смещается
    /// на секунды, и расчётная сетка либо опаздывает, либо стучится раньше срока — второе
    /// источник считает невежливостью.
    /// </summary>
    public static DateTimeOffset? ExpiresAt(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Content.Headers.Expires;
    }

    /// <summary>Остаток бюджета ошибок и момент сброса окна, если источник их назвал.</summary>
    public static (int Remaining, DateTimeOffset ResetsAt)? ErrorBudget(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues(ErrorLimitRemain, out IEnumerable<string>? remainValues)
            || !int.TryParse(remainValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var remaining))
        {
            return null;
        }

        var seconds = response.Headers.TryGetValues(ErrorLimitReset, out IEnumerable<string>? resetValues)
            && int.TryParse(resetValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 60;

        return (remaining, now.AddSeconds(seconds));
    }

    /// <summary>Валидатор кэша для следующего запроса.</summary>
    public static string? Validator(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Headers.ETag?.Tag;
    }
}
