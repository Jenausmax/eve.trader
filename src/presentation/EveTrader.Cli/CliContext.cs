using EveTrader.Application.Book;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Intake;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
using Microsoft.Extensions.Logging;

namespace EveTrader.Cli;

/// <summary>
/// Обвязка озера: всё, что нужно любой команде оператора.
///
/// Собирается на команду, а не на процесс: команда живёт один прогон, и держать
/// соединения с озером дольше незачем.
/// </summary>
internal sealed class CliContext : IDisposable
{
    public CliContext(string root, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        Root = root;
        Loggers = loggers;
        Layout = new LakeLayout(new LakeOptions { Root = root });
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Registry = new ParquetMaterializationRegistry(Layout);
        Rows = new DuckDbFactRowReader(Layout);
        Telemetry = new MeterDiagnosticSource();
        Diagnostics = new ObservationDiagnostics(Telemetry);

        // Подписка заводится сразу после инструментов: метрика, которую некому прочитать,
        // от объявления не появляется, а команда оператора обязана её показать.
        Metrics = new MetricExport(Telemetry.ModuleName);

        Intake = new ObservationIntake(
            new ObservationDerivation(Writer, new DailyCheckpointPolicy()),
            Diagnostics,
            loggers.CreateLogger<ObservationIntake>());

        // Каталог источника пуст: для отчёта о покрытии это значит, что пробел считается
        // безвозвратным, пока не доказано обратное. Ошибка в безопасную сторону —
        // «восполнимо» без подтверждения источником было бы обещанием, которое некому
        // выполнять.
        Reports = new DuckDbOperationalReportReader(Layout, Coverage, Registry, UpstreamCatalog.Empty);
    }

    public string Root { get; }

    public ILoggerFactory Loggers { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public ParquetMaterializationRegistry Registry { get; }

    public DuckDbFactRowReader Rows { get; }

    public MeterDiagnosticSource Telemetry { get; }

    public ObservationDiagnostics Diagnostics { get; }

    public MetricExport Metrics { get; }

    public ObservationIntake Intake { get; }

    public DuckDbOperationalReportReader Reports { get; }

    public void Dispose()
    {
        Metrics.Dispose();
        Telemetry.Dispose();
    }
}
