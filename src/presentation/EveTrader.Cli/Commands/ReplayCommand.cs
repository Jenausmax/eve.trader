using EveTrader.Application.Intake;
using EveTrader.Application.Replay;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Реплей собственного озера: восстановленные наблюдения подаются на тот же вход, что и
/// живой сбор, и пишутся в отдельное озеро.
///
/// В цепочку восстановления входят только полные наблюдения: по частичному неизвестно,
/// чего в стакане не было, и подать его как полное значило бы соврать.
/// </summary>
internal static class ReplayCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var target = RebuildTarget.RootFor(options);

        using var into = new CliContext(target, context.Loggers);

        var source = new LakeReplaySource(
            context.Coverage,
            context.Rows,
            TimeRange.Between(options.From, options.To),
            [.. options.Regions.Select(RegionId.From)],
            context.Loggers.CreateLogger<LakeReplaySource>());

        IntakeReport report = await into.Intake.RunAsync(
            source,
            DiffOptions.Default,
            FeatureOptions.Default,
            StaticDataVersion.From(options.StaticData),
            cancellationToken).ConfigureAwait(false);

        Output.Line("источник:", $"{report.Source}");
        Output.Line("озеро назначения:", $"{target}");
        Output.Line("наблюдений записано:", $"{report.Written}");
        Output.Line("наблюдений уже было:", $"{report.AlreadyPresent}");
        Output.Line("частичных:", $"{report.Partial}");
        Output.Line("событий:", $"{report.Events}");
        Output.Line("пропусков источника:", $"{report.SourceGaps}");
        Output.Line("регионов:", $"{report.Regions}");

        MetricsBlock.Print(context.Metrics);

        return 0;
    }
}
