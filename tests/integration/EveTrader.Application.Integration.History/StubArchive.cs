using System.Net;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;

namespace EveTrader.Application.Integration.History;

/// <summary>
/// Подменный источник HTTP: отдаёт листинги и файлы из памяти, считает запросы и
/// пиковый параллелизм.
///
/// Через <see cref="HttpMessageHandler" />, а не через настоящий сервер на порту:
/// поднимать слушающий сокет из-под агента запрещено, да и проверять здесь надо
/// поведение клиента, а не сети.
/// </summary>
internal sealed class StubArchive : HttpMessageHandler
{
    private readonly Lock guard = new();

    private int inFlight;

    /// <summary>Содержимое суток: дата -> (CSV, время изменения файла).</summary>
    public Dictionary<DateOnly, (string Csv, DateTimeOffset LastModified)> Days { get; } = [];

    public List<string> Requests { get; } = [];

    public int BodiesServed { get; private set; }

    public int NotModifiedServed { get; private set; }

    public int PeakParallelism { get; private set; }

    /// <summary>Задержка ответа: без неё параллелизм не измерить — запросы не пересекутся.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(40);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        lock (guard)
        {
            Requests.Add(path);
            inFlight++;
            PeakParallelism = Math.Max(PeakParallelism, inFlight);
        }

        try
        {
            await Task.Delay(Latency, cancellationToken).ConfigureAwait(true);

            return path.EndsWith('/') ? Listing(path) : File(path, request);
        }
        finally
        {
            lock (guard)
            {
                inFlight--;
            }
        }
    }

    private HttpResponseMessage Listing(string path)
    {
        var year = int.Parse(path.TrimEnd('/').Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

        var html = new StringBuilder("<html><body>");

        foreach (DateOnly day in Days.Keys.Where(day => day.Year == year).Order())
        {
            _ = html.Append(System.Globalization.CultureInfo.InvariantCulture, $"<a href=\"market-history-{day:yyyy-MM-dd}.csv.bz2\">x</a>");
        }

        _ = html.Append("</body></html>");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html.ToString()) };
    }

    private HttpResponseMessage File(string path, HttpRequestMessage request)
    {
        var name = path.Split('/')[^1];
        var stamp = name["market-history-".Length..^".csv.bz2".Length];
        var day = DateOnly.ParseExact(stamp, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        if (!Days.TryGetValue(day, out (string Csv, DateTimeOffset LastModified) file))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // Условный запрос: неизменившийся файл отдаётся без тела — трафик не тратится.
        if (request.Headers.IfModifiedSince is { } since && file.LastModified <= since)
        {
            lock (guard)
            {
                NotModifiedServed++;
            }

            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }

        lock (guard)
        {
            BodiesServed++;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Compress(file.Csv)) };
        response.Content.Headers.LastModified = file.LastModified;

        return response;
    }

    public static byte[] Compress(string csv)
    {
        using var output = new MemoryStream();

        using (var bzip = new BZip2OutputStream(output))
        {
            bzip.IsStreamOwner = false;
            var bytes = Encoding.UTF8.GetBytes(csv);
            bzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
