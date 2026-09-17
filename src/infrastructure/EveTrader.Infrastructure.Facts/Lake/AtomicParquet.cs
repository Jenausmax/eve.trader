using Parquet.Schema;
using Parquet.Serialization;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Запись файла Parquet во временное имя с последующим атомарным переименованием.
///
/// Прямая запись в целевое имя оставила бы после аварии полуфайл, неотличимый от целого:
/// читатель увидел бы обрезанное наблюдение и принял бы его за факт. Переименование в
/// пределах тома атомарно, поэтому файл появляется под своим именем либо целым, либо
/// никаким.
/// </summary>
public static class AtomicParquet
{
    public static async Task WriteAsync(
        string path,
        ParquetSchema schema,
        IReadOnlyCollection<IDictionary<string, object?>> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _ = Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? throw new ArgumentException("Путь без каталога", nameof(path)));

        var temporary = path + ".tmp";

        try
        {
            await using (FileStream stream = File.Create(temporary))
            {
                await ParquetSerializer
                    .SerializeUntypedAsync(rows, schema, stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }
}
