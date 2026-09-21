using System.Globalization;
using EveTrader.Application.Live;
using EveTrader.Application.Reporting;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Сырые страницы на диске: <c>raw-pages/YYYY-MM-DD/HH/region-page.json</c>.
///
/// Лежат вне <c>facts/</c> намеренно — это не факты, а материал для разбора дефектов
/// парсера, и ни один путь чтения фактов их не касается.
/// </summary>
public sealed class RawPageArchive(string root) : IRawPageArchive, IRawTrafficMeter
{
    public string Root { get; } = root;

    public async Task StoreAsync(
        RegionId region,
        int page,
        DateTimeOffset observedAt,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var directory = HourDirectory(observedAt);
        _ = Directory.CreateDirectory(directory);

        var file = Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"{region.Value}-{page:D4}.json"));

        await File.WriteAllBytesAsync(file, body, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> SweepAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(Root))
        {
            return Task.FromResult(0);
        }

        var removed = 0;

        foreach (var hour in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
            .Where(static path => Directory.EnumerateFiles(path).Any())
            .ToList())
        {
            if (HourOf(hour) is not { } at || at >= olderThan)
            {
                continue;
            }

            removed += Directory.EnumerateFiles(hour).Count();
            Directory.Delete(hour, recursive: true);
        }

        // Опустевшие сутки убираются следом: иначе каталог обрастает пустыми папками,
        // и «сколько окна осталось» перестаёт читаться глазами.
        foreach (var day in Directory.EnumerateDirectories(Root).ToList())
        {
            if (!Directory.EnumerateFileSystemEntries(day).Any())
            {
                Directory.Delete(day);
            }
        }

        return Task.FromResult(removed);
    }

    public Task<RawPageUsage> MeasureAsync(TimeRange within, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(Root))
        {
            return Task.FromResult(new RawPageUsage(0, 0, null));
        }

        var pages = 0;
        var bytes = 0L;
        DateTimeOffset? earliest = null;

        foreach (var hour in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
        {
            // Час целиком или никак: страницы внутри часа лежат под именем региона,
            // а не под временем получения, и точнее окно не нарезается.
            if (HourOf(hour) is not { } at || at < within.From.AddHours(-1) || at > within.To)
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(hour))
            {
                pages++;
                bytes += new FileInfo(file).Length;
            }

            if (earliest is null || at < earliest)
            {
                earliest = at;
            }
        }

        return Task.FromResult(new RawPageUsage(pages, bytes, earliest));
    }

    public string HourDirectory(DateTimeOffset at) =>
        Path.Combine(
            Root,
            at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            at.UtcDateTime.ToString("HH", CultureInfo.InvariantCulture));

    public static DateTimeOffset? HourOf(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var hour = Path.GetFileName(directory);
        var day = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);

        return DateTimeOffset.TryParseExact(
            $"{day}T{hour}:00:00Z",
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}
