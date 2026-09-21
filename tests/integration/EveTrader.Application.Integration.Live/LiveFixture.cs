using System.Globalization;
using System.Text;
using EveTrader.Application.Book;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Intake;
using EveTrader.Application.Live;
using EveTrader.Application.Reporting;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using EveTrader.Infrastructure.Esi;
using EveTrader.Infrastructure.Esi.Orders;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
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

        // Своё имя метрики на фикстуру: инструменты процессные, и фикстуры,
        // работающие одновременно, складывали бы замеры друг другу.
        Telemetry = new MeterDiagnosticSource($"EveTrader.Test.{Guid.NewGuid():N}");
        Diagnostics = new ObservationDiagnostics(Telemetry);
        Metrics = new MetricExport(Telemetry.ModuleName);

        Poller = new EsiRegionBookPoller(
            client, new EsiOptions(), Budget, Clock, RawPages, Diagnostics,
            NullLogger<EsiRegionBookPoller>.Instance);

        LakeRoot = Path.Combine(Path.GetTempPath(), "eve-trader-live", Guid.NewGuid().ToString("N"));
        Layout = new LakeLayout(new LakeOptions { Root = LakeRoot });
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Registry = new ParquetMaterializationRegistry(Layout);

        Intake = new ObservationIntake(
            new ObservationDerivation(Writer, new DailyCheckpointPolicy()),
            Diagnostics,
            NullLogger<ObservationIntake>.Instance);

        Reports = new DuckDbOperationalReportReader(Layout, Coverage, Registry, UpstreamCatalog.Empty);
        Usage = new ResourceUsageReport(Reports, RawPages);
    }

    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static RegionId Forge { get; } = RegionId.From(10000002);

    public string RawRoot { get; }

    public Clock Clock { get; }

    public ErrorBudget Budget { get; }

    public RawPageArchive RawPages { get; }

    public StubEsiOrders Stub { get; } = new();

    public MeterDiagnosticSource Telemetry { get; }

    public ObservationDiagnostics Diagnostics { get; }

    public MetricExport Metrics { get; }

    public EsiRegionBookPoller Poller { get; }

    public string LakeRoot { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public ParquetMaterializationRegistry Registry { get; }

    public ObservationIntake Intake { get; }

    public DuckDbOperationalReportReader Reports { get; }

    public ResourceUsageReport Usage { get; }

    /// <summary>
    /// Сбор поверх этой фикстуры. Охват задаётся явно: пригодность определяется
    /// наблюдением, а наблюдаются только пригодные, и без затравки первый цикл не
    /// наблюдал бы ничего.
    /// </summary>
    public LiveCollector Collector(params RegionId[] regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var scope = new ScopeHistory();
        scope.Record(new PolicyChange(ScopePolicy.Of(regions, TimeSpan.FromMinutes(5)), Start.AddMinutes(-1)));

        var viability = new RegionViability();

        foreach (RegionId region in regions)
        {
            viability.Observed(region, 1);
        }

        return new LiveCollector(
            Poller, Intake, scope, viability, new ObservationSchedule(), regions, Clock);
    }

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
        Metrics.Dispose();
        Telemetry.Dispose();

        if (Directory.Exists(RawRoot))
        {
            Directory.Delete(RawRoot, recursive: true);
        }

        if (Directory.Exists(LakeRoot))
        {
            Directory.Delete(LakeRoot, recursive: true);
        }
    }
}
