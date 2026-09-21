using EveTrader.Application.Acceptance;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Приёмка конвейера: перестроить признаки за материализованный интервал из сырья и
/// сверить с записанными.
///
/// Печатает не только сошлось ли, но и что содержал интервал. Приёмка на интервале без
/// смены версии статических данных и без досинхронизации задним числом неполна, и
/// умолчать об этом значило бы выдать частичную проверку за полную.
/// </summary>
internal static class AcceptCommand
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

        var rebuild = new FeatureRebuild(
            context.Coverage, context.Rows, into.Intake, into.Rows, context.Loggers);

        AcceptanceReport report = await rebuild.RunAsync(
            TimeRange.Between(options.From, options.To),
            [.. options.Regions.Select(RegionId.From)],
            DiffOptions.Default,
            FeatureOptions.Default,
            StaticDataVersion.From(options.StaticData),
            cancellationToken).ConfigureAwait(false);

        Output.Line("интервал:", $"{report.Within.From:yyyy-MM-dd HH:mm} — {report.Within.To:yyyy-MM-dd HH:mm}");
        Output.Line("озеро перестройки:", $"{target}");
        Output.Line("записей покрытия:", $"{report.Observations}");
        Output.Line("полных наблюдений:", $"{report.Complete}");
        Output.Line("в сверке:", $"{report.Compared}");
        Output.Line("окно не закрыто:", $"{report.WindowOpen}");
        Output.Line("частичных:", $"{report.Partial}");
        Output.Line("«не изменилось»:", $"{report.NotModified}");
        Output.Line("отказов:", $"{report.Failed}");
        Output.Line("пропусков источника:", $"{report.SourceGaps}");
        Output.Line("узнано задним числом:", $"{report.Backdated}");
        Output.Line("версий статических:", $"{string.Join(", ", report.StaticDataVersions)}");
        Output.Line("признаков записано:", $"{report.FeaturesRecorded}");
        Output.Line("признаков перестроено:", $"{report.FeaturesRebuilt}");
        Output.Line("не воспроизведено:", $"{report.Missing}");
        Output.Line("лишних:", $"{report.Extra}");
        Output.Line("разрывов наблюдения:", $"{report.Gaps.Count}");

        foreach (ObservationGap gap in report.Gaps)
        {
            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"  разрыв: регион {gap.Region.Value}, {gap.Range.From:yyyy-MM-dd HH:mm:ss} — {gap.Range.To:yyyy-MM-dd HH:mm:ss}, тактов {gap.Steps}"));
        }

        Output.Line("наблюдений разошлось:", $"{report.Mismatches.Count}");

        foreach (FeatureMismatch mismatch in report.Mismatches)
        {
            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"  {mismatch.Moment:yyyy-MM-dd HH:mm:ss}: записано {mismatch.Recorded}, перестроено {mismatch.Rebuilt}, не воспроизведено {mismatch.Missing}, лишних {mismatch.Extra}"));
        }

        Output.Text(string.Empty);
        Output.Line("признаки перестроены:", $"{(report.Rebuilt ? "да" : "нет")}");
        Output.Line("смена версии SDE:", $"{(report.CarriesStaticDataChange ? "есть" : "нет")}");
        Output.Line("досинхронизация:", $"{(report.CarriesBackdatedResync ? "есть" : "нет")}");
        Output.Line("приёмка доказательна:", $"{(report.IsConclusive ? "да" : "нет")}");

        return report.Rebuilt ? 0 : 1;
    }
}
