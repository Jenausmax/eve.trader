using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveTrader.Application.Integration.History;

/// <summary>Озеро на диске и импорт поверх подменного источника — под один тест.</summary>
internal sealed class ImportFixture : IDisposable
{
    public ImportFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "eve-trader-history", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);

        var options = new LakeOptions { Root = Root };
        Layout = new LakeLayout(options);
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Registry = new ParquetMaterializationRegistry(Layout);
        Rows = new DuckDbFactRowReader(Layout);

        Import = new DailyHistoryImport(
            Writer, Registry, TimeProvider.System, NullLogger<DailyHistoryImport>.Instance);
    }

    public string Root { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public ParquetMaterializationRegistry Registry { get; }

    public DuckDbFactRowReader Rows { get; }

    public DailyHistoryImport Import { get; }

    public StubArchive Stub { get; } = new();

    public EveRefMarketHistoryArchive Archive(int parallelism = 2)
    {
        var client = new HttpClient(Stub, disposeHandler: false)
        {
            BaseAddress = new Uri("https://data.everef.net/market-history/"),
        };

        return new EveRefMarketHistoryArchive(
            client,
            new EveRefOptions { MaxParallelDownloads = parallelism },
            NullLogger<EveRefMarketHistoryArchive>.Instance);
    }

    public Task<DailyHistoryImportReport> RunAsync(
        IMarketHistorySource source,
        TimeRange within,
        DateTimeOffset? knownSince,
        CancellationToken cancellationToken) =>
        Import.RunAsync(
            source,
            new MarketHistoryScope(within, [], [], knownSince),
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
