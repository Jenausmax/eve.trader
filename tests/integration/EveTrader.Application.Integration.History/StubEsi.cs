using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text;

namespace EveTrader.Application.Integration.History;

/// <summary>Подменный ESI: отдаёт историю по паре «регион и тип» в формате эндпоинта.</summary>
internal sealed class StubEsi : HttpMessageHandler
{
    /// <summary>(регион, тип) -> строки истории.</summary>
    public Dictionary<(int Region, int Type), List<(DateOnly Date, long Volume)>> History { get; } = [];

    public DateTimeOffset LastModified { get; set; } = new(2026, 1, 4, 11, 0, 0, TimeSpan.Zero);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = request.RequestUri!.AbsolutePath;
        NameValueCollection query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);

        var region = int.Parse(path.Split('/')[^3], CultureInfo.InvariantCulture);
        var type = int.Parse(query["type_id"]!, CultureInfo.InvariantCulture);

        if (!History.TryGetValue((region, type), out List<(DateOnly Date, long Volume)>? rows))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var json = new StringBuilder("[");

        for (var index = 0; index < rows.Count; index++)
        {
            (DateOnly date, var volume) = rows[index];

            _ = html(json, index).Append(CultureInfo.InvariantCulture,
                $"{{\"average\":10.5,\"date\":\"{date:yyyy-MM-dd}\",\"highest\":12,\"lowest\":9,\"order_count\":7,\"volume\":{volume}}}");
        }

        _ = json.Append(']');

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json"),
        };
        response.Content.Headers.LastModified = LastModified;

        return Task.FromResult(response);

        static StringBuilder html(StringBuilder builder, int index)
        {
            return index == 0 ? builder : builder.Append(',');
        }
    }
}
