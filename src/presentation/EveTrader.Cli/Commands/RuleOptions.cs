using System.Globalization;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Параметры правила из командной строки.
///
/// У ставок умолчаний нет и быть не может: они зависят от навыков игрока и репутации на
/// станции, и умолчание работало бы как тихое завышение маржи. Без объявленных ставок
/// команда отклоняется. Пороги умолчания имеют — они попадают в имя набора, и смена
/// любого из них даёт новое имя.
/// </summary>
internal static class RuleOptions
{
    public static StationTradingParameters? Parameters(CommandLine options)
    {
        if (Rate(options, "broker") is not { } broker
            || Rate(options, "tax") is not { } tax
            || Rate(options, "relist") is not { } relist)
        {
            Output.Text("Ставки не объявлены: --broker, --tax и --relist обязательны.");
            Output.Text("Умолчаний нет — ставки зависят от навыков и репутации, а маржа без них систематически завышена.");

            return null;
        }

        return StationTradingParameters.Of(
            options.Values.GetValueOrDefault("label", "jita"),
            FeeSchedule.Of(broker, tax, relist),
            TimeSpan.FromHours(Number(options, "window", 2d)),
            Rate(options, "min-margin") ?? 0.02m,
            (long)Number(options, "min-turnover", 1d),
            (int)Number(options, "band", 100d),
            (int)Number(options, "max-competitors", 10d),
            (int)Number(options, "max-relists", 20d),
            (int)Number(options, "buy-relists", 1d),
            (int)Number(options, "sell-relists", 1d));
    }

    public static TimeSpan Step(CommandLine options) => TimeSpan.FromMinutes(Number(options, "step", 30d));

    public static TimeSpan Horizon(CommandLine options) => TimeSpan.FromHours(Number(options, "horizon", 2d));

    public static decimal MaxUnknownShare(CommandLine options) => Rate(options, "max-unknown") ?? 0.5m;

    /// <summary>
    /// Интервал объявлен явно. Умолчание командной строки — от 2003 до 2100 года, и сетка
    /// решений с получасовым шагом на таком интервале — полтора миллиона моментов.
    /// </summary>
    public static TimeRange? Interval(CommandLine options)
    {
        if (!options.Values.ContainsKey("from") || !options.Values.ContainsKey("to"))
        {
            Output.Text("Интервал обязателен: укажите --from и --to.");

            return null;
        }

        return TimeRange.Between(options.From, options.To);
    }

    public static decimal? Rate(CommandLine options, string name) =>
        options.Values.TryGetValue(name, out var text) ? decimal.Parse(text, CultureInfo.InvariantCulture) : null;

    public static double Number(CommandLine options, string name, double fallback) =>
        options.Values.TryGetValue(name, out var text) ? double.Parse(text, CultureInfo.InvariantCulture) : fallback;

    public static void Print(StationTradingParameters parameters, StationTradingScope scope, TimeSpan step)
    {
        Output.Line("станция:", $"{scope.Name} ({scope.StationId}, регион {scope.Region.Value})");
        Output.Line("набор параметров:", $"{parameters.Name}");
        Output.Line("ставки:", $"брокерская {parameters.Fees.BrokerFeeRate}, налог {parameters.Fees.SalesTaxRate}, перестановка {parameters.Fees.RelistFeeRate}");
        Output.Line("окно / шаг:", $"{parameters.Window.TotalHours} ч / {step.TotalMinutes} мин");
        Output.Line("пороги:", $"маржа ≥ {parameters.MinNetMarginRate}, оборот ≥ {parameters.MinObservedTurnover}, конкурентов ≤ {parameters.MaxCompetitors} в {parameters.CompetitorBandBasisPoints} б.п., перестановок ≤ {parameters.MaxRelistPressure}");
        Output.Line("перестановок в марже:", $"покупка {parameters.ExpectedBuyRelists}, продажа {parameters.ExpectedSellRelists}");
    }
}
