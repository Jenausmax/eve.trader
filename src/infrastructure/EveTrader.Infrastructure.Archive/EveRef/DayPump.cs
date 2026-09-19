using System.Threading.Channels;
using EveTrader.Application.History;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>Параллельная закачка суток в канал с объявленным пределом вежливости.</summary>
internal static class DayPump
{
    /// <summary>
    /// Тянет сутки с ограниченным параллелизмом и складывает в канал.
    ///
    /// Отказ канала завершается исключением, а не тишиной: читатель на том конце иначе
    /// ждал бы вечно, приняв обрыв за конец данных.
    /// </summary>
    public static async Task RunAsync(
        ChannelWriter<MarketHistoryObservation> writer,
        IReadOnlyList<DateOnly> days,
        int parallelism,
        Func<DateOnly, CancellationToken, Task<MarketHistoryObservation?>> fetch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fetch);

        try
        {
            await Parallel.ForEachAsync(
                days,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken,
                },
                async (day, token) =>
                {
                    MarketHistoryObservation? observation = await fetch(day, token).ConfigureAwait(false);

                    if (observation is not null)
                    {
                        await writer.WriteAsync(observation, token).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            _ = writer.TryComplete();
        }
        catch (Exception exception)
        {
            _ = writer.TryComplete(exception);
        }
    }
}
