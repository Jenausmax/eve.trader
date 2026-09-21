using System.Globalization;

namespace EveTrader.Cli;

/// <summary>
/// Печать результата команды. Выравнивание по колонке — не украшение: оператор читает
/// эти числа глазами и сравнивает их между прогонами.
/// </summary>
internal static class Output
{
    public const int LabelWidth = 24;

    public static void Line(string label, FormattableString value) =>
        Console.WriteLine(
            label.PadRight(LabelWidth) + value.ToString(CultureInfo.InvariantCulture));

    public static void Text(string text) => Console.WriteLine(text);

    /// <summary>Байты в читаемом виде: оператор считает гигабайтами, а не разрядами.</summary>
    public static string Bytes(long bytes) =>
        bytes >= 1L << 30
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F2} ГиБ")
            : bytes >= 1L << 20
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):F2} МиБ")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes} Б");
}
