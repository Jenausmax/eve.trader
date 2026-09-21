using EveTrader.Application.Book;
using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>Озеро на диске и конвертация архива поверх подменного источника — под один тест.</summary>
internal sealed class OrderBookFixture : IDisposable
{
    public OrderBookFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "eve-trader-orderbook", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);

        Layout = new LakeLayout(new LakeOptions { Root = Root });
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Registry = new ParquetMaterializationRegistry(Layout);
        Rows = new DuckDbFactRowReader(Layout);
    }

    public static RegionId TheForge { get; } = RegionId.From(10000002);

    public static RegionId Domain { get; } = RegionId.From(10000043);

    public string Root { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public ParquetMaterializationRegistry Registry { get; }

    public DuckDbFactRowReader Rows { get; }

    public StubOrderArchive Stub { get; } = new();

    /// <summary>Источник поверх подменного HTTP.</summary>
    public EveRefOrderBookArchive Archive(int parallelism = 2, TimeSpan? step = null)
    {
        var client = new HttpClient(Stub, disposeHandler: false)
        {
            BaseAddress = new Uri("https://data.everef.net/market-orders/history/"),
        };

        return new EveRefOrderBookArchive(
            client,
            new EveRefOptions
            {
                MaxParallelDownloads = parallelism,
                RetryDelay = TimeSpan.FromMilliseconds(1),
                OrdersStep = step ?? TimeSpan.FromMinutes(30),
            },
            NullLogger<EveRefOrderBookArchive>.Instance);
    }

    /// <summary>
    /// Новый прогон конвертации. Свёртка и политика чекпойнтов заводятся заново каждый
    /// раз — именно так выглядит перезапуск после обрыва: ничего в памяти не осталось.
    /// </summary>
    public OrderBookImport Import() =>
        new(
            new ObservationIntake(
                new ObservationDerivation(Writer, new DailyCheckpointPolicy()),
                NullLogger<ObservationIntake>.Instance),
            Coverage,
            Registry,
            TimeProvider.System,
            NullLoggerFactory.Instance);

    public Task<OrderBookImportReport> RunAsync(
        IOrderBookSource source,
        TimeRange within,
        CancellationToken cancellationToken,
        IReadOnlyList<RegionId>? regions = null) =>
        Import().RunAsync(
            source,
            new OrderBookScope(within, regions ?? []),
            DiffOptions.Default,
            FeatureOptions.Default,
            StaticDataVersion.From("sde-test"),
            cancellationToken);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
