using System.Globalization;
using EveTrader.Domain.Book;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Охват признаков стакана, объявленный оператором.
///
/// Умолчания нет: команда, которая материализует признаки, без объявления отклоняется.
/// Полный охват пар стоит 770 ГиБ за окно против ~62 ГиБ сырья, и выбирать его молча —
/// ровно то, от чего норма отказалась.
/// </summary>
internal static class FeatureScopes
{
    public const string Option = "features";

    public static FeatureOptions Of(CommandLine options)
    {
        return !options.Values.TryGetValue(Option, out var scope)
            ? FeatureOptions.Undeclared
            : scope switch
            {
                "rule" => FeatureOptions.For(StationTradingScope.CoverageFor([StationTradingScope.Jita44])),
                "all" => FeatureOptions.AllPairs,
                "none" => FeatureOptions.None,
                _ when scope.StartsWith("station:", StringComparison.Ordinal) => FeatureOptions.For(
                    FeatureCoverage.Locations(
                        "станции, объявленные оператором: " + scope["station:".Length..],
                        [.. scope["station:".Length..]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(static part => long.Parse(part, CultureInfo.InvariantCulture))])),
                _ => throw new FormatException($"Неизвестный охват признаков '{scope}': rule, all, none или station:<id,...>"),
            };
    }

    /// <summary>Сообщает оператору, что охват обязателен, если он не объявлен.</summary>
    public static bool Rejected(FeatureOptions features)
    {
        if (features.IsDeclared)
        {
            return false;
        }

        Output.Text("Охват признаков обязателен: материализация признаков без объявленного охвата запрещена.");
        Output.Text("Укажите --features rule (пары правила станционной торговли), all, none или station:<id,...>.");

        return true;
    }
}
