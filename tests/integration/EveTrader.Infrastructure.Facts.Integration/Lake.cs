using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Озеро на диске под один тест. Тесты идут против настоящих файлов Parquet и
/// настоящего DuckDB: проверяется протокол записи и отсечение партиций, а они на моках
/// не воспроизводятся.
/// </summary>
internal sealed class Lake : IDisposable
{
    public Lake()
    {
        Root = Path.Combine(Path.GetTempPath(), "eve-trader-lake", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);

        Options = new LakeOptions { Root = Root, OrderBookWindow = TimeSpan.FromDays(730) };
        Layout = new LakeLayout(Options);
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Maintenance = new ParquetFactMaintenance(Layout, Coverage);
        Registry = new ParquetMaterializationRegistry(Layout);
        Rows = new DuckDbFactRowReader(Layout);
    }

    public string Root { get; }

    public LakeOptions Options { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public ParquetFactMaintenance Maintenance { get; }

    public ParquetMaterializationRegistry Registry { get; }

    public DuckDbFactRowReader Rows { get; }

    public DuckDbOperationalReportReader Reports(UpstreamCatalog upstream) =>
        new(Layout, Coverage, Registry, upstream);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
