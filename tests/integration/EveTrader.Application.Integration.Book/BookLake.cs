using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;

namespace EveTrader.Application.Integration.Book;

/// <summary>Озеро и свёртка наблюдений поверх него — под один тест.</summary>
internal sealed class BookLake : IDisposable
{
    public BookLake(FeatureOptions? features = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "eve-trader-book", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);

        Layout = new LakeLayout(new LakeOptions { Root = Root });
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Rows = new DuckDbFactRowReader(Layout);

        FeatureOptions = features ?? FeatureOptions.Default;
        Observer = new RegionObserver(Region, DiffOptions.Default, FeatureOptions);
        Derivation = new ObservationDerivation(Writer, new DailyCheckpointPolicy());
    }

    public static RegionId Region { get; } = RegionId.From(10000002);

    public string Root { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public DuckDbFactRowReader Rows { get; }

    public RegionObserver Observer { get; }

    public ObservationDerivation Derivation { get; }

    public FeatureOptions FeatureOptions { get; }

    /// <summary>Свернуть наблюдение и записать всё, что из него вышло.</summary>
    public async Task<ObservationOutcome> ObserveAsync(
        OrderSnapshot[] orders,
        ObservationMeta meta,
        CancellationToken cancellationToken)
    {
        ObservationOutcome outcome = Observer.Observe(orders, meta);

        _ = await Derivation.WriteAsync(
            meta, outcome, orders, FeatureOptions, StaticDataVersion.From("sde-test"), cancellationToken)
            .ConfigureAwait(true);

        return outcome;
    }

    public Task<IReadOnlyList<Facts.FactRow>> ReadAsync(
        FactSet set,
        CancellationToken cancellationToken) =>
        Rows.SelectAsync(
            set,
            TimeRange.Between(Samples.At(-1440), Samples.At(10080)),
            null,
            cancellationToken);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
