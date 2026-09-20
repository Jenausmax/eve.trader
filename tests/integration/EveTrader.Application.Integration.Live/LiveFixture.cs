using System.Globalization;
using System.Text;
using EveTrader.Application.Live;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Esi;
using EveTrader.Infrastructure.Esi.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveTrader.Application.Integration.Live;

/// <summary>Часы под управлением теста: пауза бюджета иначе не проверяется.</summary>
internal sealed class Clock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    /// <summary>
    /// На сколько часы уходят вперёд при каждом обращении. Не украшение: наблюдение
    /// крупного региона занимает десятки секунд, и тест, в котором время стоит, проверял
    /// бы не то, что происходит на самом деле.
    /// </summary>
    public TimeSpan TickPerRead { get; set; } = TimeSpan.FromSeconds(1);

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset now = Now;
        Now += TickPerRead;

        return now;
    }
}

/// <summary>Опросчик ESI поверх подменного источника.</summary>
internal sealed class LiveFixture : IDisposable
{
    public LiveFixture()
    {
        RawRoot = Path.Combine(Path.GetTempPath(), "eve-trader-raw", Guid.NewGuid().ToString("N"));
        Clock = new Clock(Start);
        Budget = new ErrorBudget(Clock, pauseBelow: 20);
        RawPages = new RawPageArchive(RawRoot);

        var client = new HttpClient(Stub, disposeHandler: false)
        {
            BaseAddress = new Uri("https://esi.evetech.net/latest/"),
        };

        Poller = new EsiRegionBookPoller(
            client, new EsiOptions(), Budget, Clock, RawPages,
            NullLogger<EsiRegionBookPoller>.Instance);
    }

    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static RegionId Forge { get; } = RegionId.From(10000002);

    public string RawRoot { get; }

    public Clock Clock { get; }

    public ErrorBudget Budget { get; }

    public RawPageArchive RawPages { get; }

    public StubEsiOrders Stub { get; } = new();

    public EsiRegionBookPoller Poller { get; }

    /// <summary>Страница ордеров в формате ответа ESI.</summary>
    public static string Page(params (long Id, decimal Price, long Remain, bool IsBuy)[] orders)
    {
        var json = new StringBuilder("[");

        for (var index = 0; index < orders.Length; index++)
        {
            (var id, var price, var remain, var isBuy) = orders[index];

            if (index > 0)
            {
                _ = json.Append(',');
            }

            _ = json.Append(CultureInfo.InvariantCulture,
                $$"""
                  {"order_id":{{id}},"type_id":34,"location_id":60003760,"is_buy_order":{{(isBuy ? "true" : "false")}},
                  "price":{{price}},"volume_remain":{{remain}},"volume_total":100,"duration":90,
                  "issued":"2026-01-01T11:00:00Z","range":"region","min_volume":1,"system_id":30000142}
                  """);
        }

        return json.Append(']').ToString();
    }

    public void Dispose()
    {
        if (Directory.Exists(RawRoot))
        {
            Directory.Delete(RawRoot, recursive: true);
        }
    }
}
