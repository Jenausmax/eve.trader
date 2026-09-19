using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Parquet.Serialization;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Журнал покрытия поверх Parquet. Запись покрытия — точка фиксации наблюдения: до неё
/// строк данных для читателя не существует, после неё они факты.
///
/// Имя файла записи — идентификатор наблюдения. Поэтому «подтверждено ли наблюдение» —
/// это один <see cref="File.Exists(string)" />, а не разбор содержимого: проверка идёт на
/// каждой записи, и разбор журнала на ней был бы расточительством.
/// </summary>
public sealed class ParquetCoverageLog(LakeLayout layout) : ICoverageLog
{
    public async Task<IReadOnlyList<CoverageEntry>> ReadAsync(
        TimeRange observed,
        IReadOnlyCollection<RegionId> regions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regions);

        HashSet<RegionId>? wanted = regions.Count == 0 ? null : [.. regions];
        var entries = new List<CoverageEntry>();

        foreach (var file in CoverageFiles(layout))
        {
            foreach (IReadOnlyDictionary<string, object?> row in await ReadFileAsync(file, cancellationToken).ConfigureAwait(false))
            {
                CoverageEntry entry = CoverageSchema.FromRow(row);

                if (!entry.Collected.Overlaps(observed))
                {
                    continue;
                }

                if (wanted is not null && !wanted.Contains(entry.Region))
                {
                    continue;
                }

                entries.Add(entry);
            }
        }

        return [.. entries
            .OrderBy(entry => entry.Collected.From)
            .ThenBy(entry => entry.Region.Value)
            .ThenBy(entry => entry.Observation.Value, StringComparer.Ordinal)];
    }

    public Task<IReadOnlySet<ObservationId>> ConfirmedObservationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlySet<ObservationId> confirmed = CoverageFiles(layout)
            .Select(LakeLayout.ObservationOf)
            .ToHashSet();

        return Task.FromResult(confirmed);
    }

    /// <summary>
    /// Дописывает записи покрытия одного наблюдения. Наблюдение считается состоявшимся
    /// ровно с этого момента, поэтому вызов идёт последним в протоколе записи.
    ///
    /// Записей может быть несколько: одно наблюдение архива накрывает все регионы,
    /// присутствующие в глобальном файле источника. Файл по-прежнему один и назван
    /// идентификатором наблюдения — иначе идемпотентность и уборка перестали бы
    /// опираться на имя.
    /// </summary>
    public async Task AppendAsync(IReadOnlyList<CoverageEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            throw new ArgumentException("Наблюдение несёт хотя бы одну запись покрытия", nameof(entries));
        }

        ObservationId observation = entries[0].Observation;

        if (entries.Any(entry => entry.Observation != observation))
        {
            throw new ArgumentException(
                "Все записи одного файла покрытия принадлежат одному наблюдению",
                nameof(entries));
        }

        var observedDate = DateOnly.FromDateTime(entries[0].Collected.From.UtcDateTime);
        var file = layout.CoverageFileFor(observedDate, observation);

        await AtomicParquet
            .WriteAsync(file, CoverageSchema.Schema, [.. entries.Select(CoverageSchema.ToRow)], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Подтверждено ли наблюдение — по наличию файла записи покрытия.</summary>
    public bool IsConfirmed(DateOnly observedDate, ObservationId observation) =>
        File.Exists(layout.CoverageFileFor(observedDate, observation));

    public static IEnumerable<string> CoverageFiles(LakeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var root = layout.SetRoot(FactSet.Coverage);

        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*" + LakeLayout.ParquetExtension, SearchOption.AllDirectories)
            : [];
    }

    public static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadFileAsync(
        string file,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(file);

        DeserializationResult<Dictionary<string, object>> result = await ParquetSerializer
            .DeserializeUntypedAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. result.Data.Select(static row => (IReadOnlyDictionary<string, object?>)row.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.Ordinal))];
    }
}
