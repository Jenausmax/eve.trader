using EveTrader.Application.Facts;
using EveTrader.Application.Series;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Signals;

/// <summary>
/// Рассмотрение пар станции правилом в моменты решения — один путь на бой и на
/// бэктест.
///
/// Каждый момент решения видит ровно то, что система знала в этот момент: версия
/// каждого факта выбирается на момент решения (<see cref="Bitemporal.AsOf{T}" />), а не
/// на момент прогона. Факт, ставший известным позже, — даже если он о более раннем
/// времени — на решение не влияет. Бой отличается от бэктеста только тем, какие моменты
/// решения подставлены: в бою — текущий, в бэктесте — сетка по истории.
///
/// Читается посуточно, все версии разом: моментов решения в сутках десятки, и каждому
/// нужна своя версия каждого факта. Читать по запросу на момент значило бы перечитывать
/// одни и те же сутки на каждом шаге.
/// </summary>
public sealed class SignalGeneration(IFactRowReader rows)
{
    public async Task<SignalRun> EvaluateAsync(SignalRequest request, CancellationToken cancellationToken)
    {
        StationTradingParameters parameters = request.Parameters;
        StationTradingScope scope = request.Scope;
        IReadOnlyList<SeriesDefinition> definitions = StationTradingSeries.Definitions(parameters, request.Step);
        HashSet<string> wanted = [.. definitions.Select(static definition => definition.Key)];

        var verdicts = new List<StationTradingVerdict>();
        var summaries = new List<DecisionSummary>();

        foreach (IGrouping<DateOnly, DateTimeOffset> day in request.Decisions
            .Distinct()
            .Order()
            .GroupBy(static decision => DateOnly.FromDateTime(decision.UtcDateTime)))
        {
            DateTimeOffset first = day.First();
            DateTimeOffset last = day.Last();
            HashSet<DateTimeOffset> moments = [.. day];

            IReadOnlyList<FactRow> seriesRows = await rows
                .SelectAsync(FactSet.FeatureSeries, TimeRange.Between(first, last.AddTicks(1)), last, cancellationToken)
                .ConfigureAwait(false);

            var seriesAt = seriesRows
                .Where(row => row.Region == scope.Region
                    && moments.Contains(row.Envelope.EventTime.To)
                    && row.Values.TryGetValue(SeriesFacts.Definition, out var key)
                    && key is string definition
                    && wanted.Contains(definition))
                .GroupBy(static row => row.Envelope.EventTime.To)
                .ToDictionary(static group => group.Key, static group => group.ToList());

            IReadOnlyList<FactRow> featureRows = await rows
                .SelectAsync(
                    FactSet.BookFeatures,
                    TimeRange.Between(first - parameters.Window, last.AddTicks(1)),
                    last,
                    cancellationToken)
                .ConfigureAwait(false);

            FactRow[] quotes =
            [
                .. featureRows
                    .Where(row => row.Region == scope.Region
                        && scope.Includes(SeriesFactRows.Int64Of(row, BookFeatureFacts.LocationId)))
                    .OrderBy(static row => row.Envelope.EventTime.From),
            ];

            foreach (DateTimeOffset decision in day)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<FactRow> known = Bitemporal.AsOf(seriesAt.GetValueOrDefault(decision) ?? [], decision);

                SeriesWindowVerdict[] windows = [.. known.Where(SeriesFactRows.IsWindow).Select(SeriesFactRows.Window)];
                SeriesPoint[] points =
                [
                    .. known
                        .Where(static row => !SeriesFactRows.IsWindow(row))
                        .Select(SeriesFactRows.Point)
                        .Where(point => scope.Includes(point.LocationId)),
                ];

                Dictionary<int, BookFeatures> latest = GenerationSteps.Quotes(
                    quotes, decision, parameters.Window, request.Thresholds);

                List<StationTradingVerdict> considered =
                [
                    .. latest.Keys
                        .Union(points.Select(static point => point.TypeId))
                        .Order()
                        .Select(typeId => StationTradingRule.Evaluate(
                            parameters,
                            new StationTradingInput(
                                scope,
                                typeId,
                                decision,
                                request.Step,
                                latest.GetValueOrDefault(typeId),
                                new SeriesComputed(windows, [.. points.Where(point => point.TypeId == typeId)])))),
                ];

                verdicts.AddRange(considered);
                summaries.Add(GenerationSteps.Summary(decision, definitions, windows, considered));
            }
        }

        return new SignalRun(verdicts, summaries);
    }
}

file static class GenerationSteps
{
    /// <summary>
    /// Последний снимок каждой пары в окне, известный на момент решения. Снимок, узнанный
    /// позже момента решения, в выбор не входит.
    /// </summary>
    public static Dictionary<int, BookFeatures> Quotes(
        FactRow[] ordered,
        DateTimeOffset decision,
        TimeSpan window,
        IReadOnlyList<int> thresholds)
    {
        var from = Lower(ordered, decision - window, inclusive: false);
        var to = Lower(ordered, decision, inclusive: true);

        return Bitemporal.AsOf(new ArraySegment<FactRow>(ordered, from, to - from), decision)
            .Select(row => SeriesFactRows.Features(row, thresholds))
            .GroupBy(static snapshot => snapshot.TypeId)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair
                    .OrderByDescending(static snapshot => snapshot.ObservedAt)
                    .ThenBy(static snapshot => snapshot.Observation.Value, StringComparer.Ordinal)
                    .First());
    }

    /// <summary>
    /// Первая позиция после <paramref name="instant" /> (<paramref name="inclusive" /> —
    /// включая сам момент в левую часть) в строках, упорядоченных по времени события.
    /// </summary>
    public static int Lower(FactRow[] ordered, DateTimeOffset instant, bool inclusive)
    {
        var low = 0;
        var high = ordered.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            DateTimeOffset at = ordered[middle].Envelope.EventTime.From;

            if (at < instant || (inclusive && at == instant))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    public static DecisionSummary Summary(
        DateTimeOffset decision,
        IReadOnlyList<SeriesDefinition> definitions,
        IReadOnlyList<SeriesWindowVerdict> windows,
        IReadOnlyList<StationTradingVerdict> considered)
    {
        SeriesWindowVerdict[] read =
        [
            .. windows.Where(window => window.Definition.Kind is not SeriesKind.FilledDisappearanceShare
                && definitions.Contains(window.Definition)),
        ];

        return new DecisionSummary(
            decision,
            read.Length == 0 ? null : read.Max(static window => window.Window.Admission),
            read.Length == 0 ? null : read.Max(static window => window.Window.Coverage),
            considered.Count,
            considered.Count(static verdict => verdict.Outcome is ConsiderationOutcome.Signal),
            considered.Count(static verdict => verdict.Outcome is ConsiderationOutcome.ConditionsNotMet),
            considered.Count(static verdict => verdict.Outcome is ConsiderationOutcome.InsufficientData));
    }
}
