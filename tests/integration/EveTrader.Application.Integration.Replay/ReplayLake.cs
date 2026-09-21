using EveTrader.Application.Book;
using EveTrader.Application.Intake;
using EveTrader.Application.Replay;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveTrader.Application.Integration.Replay;

/// <summary>
/// Озеро на диске со своим приёмом. Реплей пишет в отдельное озеро, а не в то же:
/// иначе идемпотентность отбросила бы всё по совпадению идентификаторов наблюдений, и
/// сверять было бы нечего.
/// </summary>
internal sealed class ReplayLake : IDisposable
{
    public ReplayLake(string tag)
    {
        Root = Path.Combine(Path.GetTempPath(), "eve-trader-replay", $"{tag}-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Root);

        Layout = new LakeLayout(new LakeOptions { Root = Root });
        Coverage = new ParquetCoverageLog(Layout);
        Writer = new ParquetFactWriter(Layout, Coverage);
        Rows = new DuckDbFactRowReader(Layout);

        Intake = new ObservationIntake(
            new ObservationDerivation(Writer, new DailyCheckpointPolicy()),
            NullLogger<ObservationIntake>.Instance);
    }

    public string Root { get; }

    public LakeLayout Layout { get; }

    public ParquetCoverageLog Coverage { get; }

    public ParquetFactWriter Writer { get; }

    public DuckDbFactRowReader Rows { get; }

    public ObservationIntake Intake { get; }

    public Task<IntakeReport> RunAsync(IObservationSource source, CancellationToken cancellationToken) =>
        Intake.RunAsync(
            source, DiffOptions.Default, FeatureOptions.Default,
            StaticDataVersion.From("sde-test"), cancellationToken);

    /// <summary>Источник реплея поверх этого озера.</summary>
    public LakeReplaySource Replay(TimeRange within) =>
        new(Coverage, Rows, within, [], NullLogger<LakeReplaySource>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
