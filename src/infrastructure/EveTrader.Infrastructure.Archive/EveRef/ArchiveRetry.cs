using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Повтор попытки с удваивающейся паузой — один на все наборы архива.
///
/// Прогон идёт на тысячи файлов, и обрыв в нём не исключение, а норма. Отсюда два
/// разных исхода: исчерпав попытки, повтор не бросает, а отдаёт пустоту — один
/// несостоявшийся файл не повод ронять прогон. Пропущенный файл остаётся без записи
/// покрытия, а значит покрытие честно покажет его восполнимым, и следующий прогон его
/// заберёт.
/// </summary>
internal static class ArchiveRetry
{
    public static async Task<T?> RunAsync<T>(
        string what,
        int maxAttempts,
        TimeSpan retryDelay,
        ILogger logger,
        Func<CancellationToken, Task<T?>> attempt,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(attempt);

        TimeSpan delay = retryDelay;

        for (var number = 1; ; number++)
        {
            try
            {
                return await attempt(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (number < maxAttempts && ArchiveDownload.IsTransient(failure))
            {
                logger.LogWarning(
                    failure, "{What}: попытка {Attempt} не удалась, повтор через {Delay}", what, number, delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay *= 2;
            }
            catch (Exception failure) when (ArchiveDownload.IsTransient(failure))
            {
                logger.LogError(failure, "{What}: пропущено после {Attempts} попыток", what, maxAttempts);

                return null;
            }
            catch (ArchiveFormatException failure)
            {
                // Дефект данных источника повтором не лечится, но и ронять из-за него
                // прогон на тысячи файлов нельзя.
                logger.LogError(failure, "{What}: файл источника не разобран, пропущено", what);

                return null;
            }
        }
    }
}
