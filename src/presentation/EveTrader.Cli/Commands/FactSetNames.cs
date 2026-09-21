using EveTrader.Domain.Facts;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Имена наборов для оператора. Те же, что сегменты пути в озере: имя, набранное в
/// команде, обязано совпадать с именем каталога, иначе оператор ищет на диске не то.
/// </summary>
internal static class FactSetNames
{
    public static string All { get; } =
        string.Join(", ", Enum.GetValues<FactSet>().Select(FactSets.PathSegment));

    public static FactSet? Parse(string name) =>
        Enum.GetValues<FactSet>()
            .Cast<FactSet?>()
            .FirstOrDefault(set => string.Equals(FactSets.PathSegment(set!.Value), name, StringComparison.Ordinal));
}
