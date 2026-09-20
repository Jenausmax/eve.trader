using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Параллельная закачка файлов архива с объявленным пределом вежливости.
///
/// Два способа выдачи, и разница между ними — не оптимизация, а требование потребителя.
/// Дневная история независима по суткам, поэтому её сутки едут в канал в том порядке, в
/// каком доехали. Снимки стакана сворачиваются наблюдателем, который держит состояние:
/// снимок, пришедший раньше предыдущего, дал бы выдуманные события, а не более быструю
/// загрузку. Для них выдача строго по порядку, при том же пределе вежливости.
/// </summary>
internal static class ArchivePump
{
    /// <summary>
    /// Тянет файлы с ограниченным параллелизмом и складывает в канал в порядке готовности.
    ///
    /// Отказ канала завершается исключением, а не тишиной: читатель на том конце иначе
    /// ждал бы вечно, приняв обрыв за конец данных.
    /// </summary>
    public static async Task RunAsync<TKey, TItem>(
        ChannelWriter<TItem> writer,
        IReadOnlyList<TKey> keys,
        int parallelism,
        Func<TKey, CancellationToken, Task<TItem?>> fetch,
        CancellationToken cancellationToken)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fetch);

        try
        {
            await Parallel.ForEachAsync(
                keys,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken,
                },
                async (key, token) =>
                {
                    TItem? item = await fetch(key, token).ConfigureAwait(false);

                    if (item is not null)
                    {
                        await writer.WriteAsync(item, token).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            _ = writer.TryComplete();
        }
        catch (Exception exception)
        {
            _ = writer.TryComplete(exception);
        }
    }

    /// <summary>
    /// Тянет файлы тем же числом потоков, но отдаёт строго в порядке ключей: пока
    /// потребитель работает над очередным, следующие уже едут.
    ///
    /// Окно упреждения равно пределу вежливости намеренно — иначе оно пришлось бы
    /// объявлять отдельно, и память под разобранные снимки росла бы независимо от того,
    /// о чём договорились с источником.
    /// </summary>
    public static async IAsyncEnumerable<TItem> InOrderAsync<TKey, TItem>(
        IReadOnlyList<TKey> keys,
        int parallelism,
        Func<TKey, CancellationToken, Task<TItem?>> fetch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(fetch);

        var ahead = new Queue<Task<TItem?>>(Math.Max(1, parallelism));
        var next = 0;

        while (next < keys.Count || ahead.Count > 0)
        {
            while (ahead.Count < Math.Max(1, parallelism) && next < keys.Count)
            {
                ahead.Enqueue(fetch(keys[next++], cancellationToken));
            }

            TItem? item;

            try
            {
                item = await ahead.Dequeue().ConfigureAwait(false);
            }
            catch
            {
                // Упреждение уже запущено, и брошенный обрыв его не отменяет. Задачи
                // надо досмотреть: их исключения иначе всплыли бы в финализаторе — уже
                // без всякой связи с местом обрыва.
                while (ahead.Count > 0)
                {
                    _ = ahead.Dequeue().ContinueWith(
                        static observed => observed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default);
                }

                throw;
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }
}
