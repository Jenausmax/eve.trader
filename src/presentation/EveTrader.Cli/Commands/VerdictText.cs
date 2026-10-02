using System.Globalization;
using EveTrader.Application.Signals;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>Исходы рассмотрения для оператора: сигнал с обоснованием и причины молчания.</summary>
internal static class VerdictText
{
    public static string Signal(StationTradingVerdict verdict)
    {
        if (verdict.Justification is not { } why)
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"{verdict.Decision:yyyy-MM-dd HH:mm} тип {verdict.TypeId}: {verdict.Outcome}");
        }

        var head = string.Create(
            CultureInfo.InvariantCulture,
            $"{verdict.Decision:yyyy-MM-dd HH:mm} тип {verdict.TypeId}: купить по {why.BestBid}, продать по {why.BestAsk}, маржа после комиссий {why.NetMargin} ISK ({why.NetMarginRate:P2})");
        var series = string.Create(
            CultureInfo.InvariantCulture,
            $"оборот {why.BuyTurnover}/{why.SellTurnover}, конкурентов {why.BuyCompetitors:0.#}/{why.SellCompetitors:0.#}, перестановок {why.BuyRelists}/{why.SellRelists}");
        var blind = why.FilledShare is { } share
            ? string.Create(CultureInfo.InvariantCulture, $", доля исчезновений с исполнением {share:P0}")
            : string.Empty;

        return head + "; " + series + blind;
    }

    public static string Decision(DecisionSummary summary) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{summary.Decision:yyyy-MM-dd HH:mm}  окно {Admission(summary)}  пар {summary.Considered,6}  сигналов {summary.Signals,4}  не прошли {summary.ConditionsNotMet,6}  нет данных {summary.InsufficientData,6}");

    public static string Admission(DecisionSummary summary) =>
        summary.Admission is { } admission
            ? (summary.Coverage is { } coverage && admission is SeriesAdmission.Refused
                ? $"{admission} ({coverage})"
                : admission.ToString())
            : "не материализовано";

    /// <summary>Причины молчания по всем рассмотренным парам — сколько раз каждая.</summary>
    public static void Silence(IReadOnlyList<StationTradingVerdict> verdicts)
    {
        foreach (IGrouping<VerdictReason, StationTradingVerdict> reason in verdicts
            .SelectMany(static verdict => verdict.Reasons.Select(reason => new { reason, verdict }))
            .GroupBy(static pair => pair.reason, static pair => pair.verdict)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key))
        {
            Output.Text(string.Create(CultureInfo.InvariantCulture, $"  {reason.Key,-28} {reason.Count(),8}"));
        }
    }
}
