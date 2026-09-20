using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text;

namespace EveTrader.Application.Integration.Live;

/// <summary>
/// Подменный ESI: отдаёт страницы стакана из памяти, объявляет число страниц, срок
/// годности, бюджет ошибок и валидатор кэша.
///
/// Через <see cref="HttpMessageHandler" />, а не через сервер на порту: поднимать
/// слушающий сокет из-под агента запрещено, да и проверять здесь надо поведение клиента,
/// а не сети.
/// </summary>
internal sealed class StubEsiOrders : HttpMessageHandler
{
    private readonly Lock guard = new();

    /// <summary>Страницы по региону: список страниц, каждая — список ордеров в JSON.</summary>
    public Dictionary<int, List<string>> Pages { get; } = [];

    public Dictionary<int, string> ETags { get; } = [];

    /// <summary>Регионы, по которым источник обязан ответить «не изменилось».</summary>
    public HashSet<int> NotModified { get; } = [];

    /// <summary>Со скольких страниц источник перестаёт отвечать; ноль — отвечает всегда.</summary>
    public Dictionary<int, int> FailFromPage { get; } = [];

    public DateTimeOffset ExpiresAt { get; set; } = new(2026, 1, 1, 12, 5, 0, TimeSpan.Zero);

    public int? ErrorLimitRemain { get; set; }

    public int ErrorLimitReset { get; set; } = 60;

    public List<string> Requests { get; } = [];

    public int BodiesServed { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Uri uri = request.RequestUri!;
        var region = int.Parse(uri.AbsolutePath.Split('/')[^3], CultureInfo.InvariantCulture);
        NameValueCollection query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var page = int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture);

        lock (guard)
        {
            Requests.Add($"{region}/{page}");
        }

        if (page == 1 && NotModified.Contains(region) && request.Headers.IfNoneMatch.Count > 0)
        {
            return Task.FromResult(Decorate(new HttpResponseMessage(HttpStatusCode.NotModified), region));
        }

        if (FailFromPage.TryGetValue(region, out var failFrom) && failFrom > 0 && page >= failFrom)
        {
            return Task.FromResult(Decorate(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), region));
        }

        if (!Pages.TryGetValue(region, out List<string>? pages) || page > pages.Count)
        {
            return Task.FromResult(Decorate(new HttpResponseMessage(HttpStatusCode.NotFound), region));
        }

        lock (guard)
        {
            BodiesServed++;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(pages[page - 1], Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("X-Pages", pages.Count.ToString(CultureInfo.InvariantCulture));

        return Task.FromResult(Decorate(response, region));
    }

    private HttpResponseMessage Decorate(HttpResponseMessage response, int region)
    {
        response.Content.Headers.Expires = ExpiresAt;

        if (ETags.TryGetValue(region, out var tag))
        {
            _ = response.Headers.TryAddWithoutValidation("ETag", tag);
        }

        if (ErrorLimitRemain is { } remain)
        {
            response.Headers.Add("X-ESI-Error-Limit-Remain", remain.ToString(CultureInfo.InvariantCulture));
            response.Headers.Add("X-ESI-Error-Limit-Reset", ErrorLimitReset.ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }
}
