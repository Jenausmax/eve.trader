using System.Globalization;
using System.Net;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Подменный архив снимков: отдаёт <c>index.json</c> по годам и суткам и сами файлы из
/// памяти, считает запросы и пиковый параллелизм.
///
/// Через <see cref="HttpMessageHandler" />, а не через настоящий сервер на порту:
/// поднимать слушающий сокет из-под агента запрещено, да и проверять здесь надо
/// поведение клиента, а не сети.
/// </summary>
internal sealed class StubOrderArchive : HttpMessageHandler
{
    private readonly Lock guard = new();

    private int inFlight;

    /// <summary>Содержимое снимков: момент -> CSV.</summary>
    public Dictionary<DateTimeOffset, string> Snapshots { get; } = [];

    /// <summary>Задержка ответа по снимку; позволяет проверить порядок выдачи при обгоне.</summary>
    public Dictionary<DateTimeOffset, TimeSpan> Delays { get; } = [];

    /// <summary>Сколько первых попыток по снимку оборвать на середине тела.</summary>
    public Dictionary<DateTimeOffset, int> TruncateAttempts { get; } = [];

    public List<string> Requests { get; } = [];

    public int BodiesServed { get; private set; }

    public int TruncatedServed { get; private set; }

    public int PeakParallelism { get; private set; }

    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(20);

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

    /// <summary>Время изменения файла у источника: обход заканчивается позже, чем начат.</summary>
    public static DateTimeOffset LastModifiedOf(DateTimeOffset at) => at.AddMinutes(5);

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

            return path.EndsWith("index.json", StringComparison.Ordinal) ? Index(path) : await FileAsync(path, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            lock (guard)
            {
                inFlight--;
            }
        }
    }

    /// <summary>Листинг года — каталоги суток; листинг суток — файлы снимков.</summary>
    public HttpResponseMessage Index(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directory = segments[^2];

        var json = new StringBuilder("{");

        if (DateOnly.TryParseExact(directory, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            _ = json.Append("\"files\":[");
            _ = json.AppendJoin(',', Snapshots.Keys
                .Where(at => DateOnly.FromDateTime(at.UtcDateTime) == day)
                .Order()
                .Select(static at => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{\"name\":\"market-orders-{at.UtcDateTime:yyyy-MM-dd_HH-mm-ss}.v3.csv.bz2\",\"size\":1}}")));
            _ = json.Append(']');
        }
        else
        {
            var year = int.Parse(directory, CultureInfo.InvariantCulture);

            _ = json.Append("\"directories\":[");
            _ = json.AppendJoin(',', Snapshots.Keys
                .Select(static at => DateOnly.FromDateTime(at.UtcDateTime))
                .Where(at => at.Year == year)
                .Distinct()
                .Order()
                .Select(static at => string.Create(CultureInfo.InvariantCulture, $"{{\"name\":\"{at:yyyy-MM-dd}\"}}")));
            _ = json.Append(']');
        }

        _ = json.Append('}');

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToString()) };
    }

    public async Task<HttpResponseMessage> FileAsync(string path, CancellationToken cancellationToken)
    {
        var name = path.Split('/')[^1];
        var stamp = name["market-orders-".Length..^".v3.csv.bz2".Length];

        var at = new DateTimeOffset(
            DateTime.ParseExact(stamp, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture), TimeSpan.Zero);

        if (!Snapshots.TryGetValue(at, out var csv))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (Delays.TryGetValue(at, out TimeSpan extra))
        {
            await Task.Delay(extra, cancellationToken).ConfigureAwait(true);
        }

        var body = Compress(csv);

        lock (guard)
        {
            if (TruncateAttempts.TryGetValue(at, out var left) && left > 0)
            {
                TruncateAttempts[at] = left - 1;
                TruncatedServed++;

                // Тело обрезано, но Content-Length объявлен полным — ровно так выглядит
                // разрыв соединения на середине загрузки.
                var truncated = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body[..(body.Length / 2)]),
                };
                truncated.Content.Headers.ContentLength = body.Length;
                truncated.Content.Headers.LastModified = LastModifiedOf(at);

                return truncated;
            }

            BodiesServed++;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.LastModified = LastModifiedOf(at);

        return response;
    }
}
