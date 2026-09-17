using System.Data.Common;
using DuckDB.NET.Data;

namespace EveTrader.Infrastructure.Facts.Query;

/// <summary>Общее для обоих портов чтения: соединение и экранирование путей.</summary>
public static class DuckDb
{
    public static DuckDBConnection Open()
    {
        var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        return connection;
    }

    /// <summary>
    /// Целое из ячейки выдачи. Отдельный метод нужен из-за DuckDB: count и sum он
    /// возвращает как HUGEINT, а тот приезжает в .NET как <see cref="System.Numerics.BigInteger" />,
    /// который не реализует <see cref="IConvertible" /> — Convert.ToInt64 на нём падает.
    /// </summary>
    public static long ToInt64(object? value) => value switch
    {
        null or DBNull => 0L,
        long number => number,
        int number => number,
        System.Numerics.BigInteger number => (long)number,
        decimal number => (long)number,
        double number => (long)number,
        _ => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Путь как строковый литерал SQL.</summary>
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Выполняет запрос и склеивает выдачу в текст. Нужен для планов запроса: отсечение
    /// партиций проверяется планом, а не выдачей — выдача совпадёт и без отсечения,
    /// движок просто прочитает лишние файлы.
    /// </summary>
    public static async Task<string> TextAsync(string sql, CancellationToken cancellationToken)
    {
        await using DuckDBConnection connection = Open();
        await using DuckDBCommand command = connection.CreateCommand();

        // CA2100: запрос строится из путей конфигурации, пользовательского ввода в нём нет.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var text = new System.Text.StringBuilder();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                _ = text.AppendLine(reader.GetValue(column)?.ToString());
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Время как литерал SQL в UTC. Через литерал, а не параметр, потому что запросы
    /// строятся вокруг <c>read_parquet</c>, где параметризуется не всё.
    /// </summary>
    public static string Timestamp(DateTimeOffset value) =>
        "TIMESTAMP '" + value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture) + "'";
}
