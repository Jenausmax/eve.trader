using System.Security.Cryptography;
using System.Text;

namespace EveTrader.Domain.Series;

/// <summary>
/// Отпечаток набора определений рядов. SHA-256, а не встроенный хеш строки: тот
/// рандомизирован на процесс, и идентификатор порции менялся бы при каждом запуске —
/// повторная материализация перестала бы быть идемпотентной.
/// </summary>
public static class SeriesDigest
{
    public static string Of(IReadOnlyList<SeriesDefinition> definitions)
    {
        var canonical = string.Join(
            ';',
            definitions.Select(static definition => definition.Key).Distinct().Order(StringComparer.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];
    }
}
