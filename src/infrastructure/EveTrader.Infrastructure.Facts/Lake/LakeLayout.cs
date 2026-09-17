using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Раскладка озера на диске. Партиционирование по дате наблюдения и региону — не вкус:
/// запрос за интервал отсекает лишнее по путям, а суточная партиция это естественная
/// единица компакции.
///
/// Имя файла — идентификатор наблюдения. Отсюда бесплатно получаются идемпотентность
/// (файл уже на месте) и уборка (файл без записи покрытия виден по имени).
/// </summary>
public sealed class LakeLayout(LakeOptions options)
{
    public const string ParquetExtension = ".parquet";

    public const string TempExtension = ".parquet.tmp";

    public string Root { get; } = options.Root;

    public string FactsRoot => Path.Combine(Root, "facts");

    public string MaterializationRoot => Path.Combine(Root, "materialization");

    public string SetRoot(FactSet set) => Path.Combine(FactsRoot, FactSets.PathSegment(set));

    /// <summary>Каталог партиции: набор, дата наблюдения, регион.</summary>
    public string PartitionDirectory(FactSet set, DateOnly observedDate, RegionId region) =>
        Path.Combine(SetRoot(set), ObservedDateSegment(observedDate), RegionSegment(region));

    /// <summary>Каталог партиции покрытия: регион в пути не участвует — записи лежат вперемешку.</summary>
    public string CoverageDirectory(DateOnly observedDate) =>
        Path.Combine(SetRoot(FactSet.Coverage), ObservedDateSegment(observedDate));

    public string RegistryFile(FactSet set) =>
        Path.Combine(MaterializationRoot, FactSets.PathSegment(set) + ParquetExtension);

    public static string ObservedDateSegment(DateOnly observedDate) =>
        "observed_date=" + observedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string RegionSegment(RegionId region) =>
        "region=" + region.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Glob для DuckDB: все партиции набора.</summary>
    public string SetGlob(FactSet set) =>
        Path.Combine(SetRoot(set), "**", "*" + ParquetExtension);

    public string FileFor(FactSet set, DateOnly observedDate, RegionId region, ObservationId observation) =>
        Path.Combine(PartitionDirectory(set, observedDate, region), observation.Value + ParquetExtension);

    public string CoverageFileFor(DateOnly observedDate, ObservationId observation) =>
        Path.Combine(CoverageDirectory(observedDate), observation.Value + ParquetExtension);

    /// <summary>
    /// Есть ли в наборе хоть один файл. Проверять каталог недостаточно: после удаления
    /// производного набора каталог остаётся пустым, а read_parquet на пустом шаблоне
    /// падает, а не возвращает ноль строк.
    /// </summary>
    public bool HasFiles(FactSet set)
    {
        var root = SetRoot(set);

        return Directory.Exists(root)
            && Directory.EnumerateFiles(root, "*" + ParquetExtension, SearchOption.AllDirectories).Any();
    }

    /// <summary>Все файлы данных озера, кроме покрытия и реестра.</summary>
    public IEnumerable<string> DataFiles() =>
        Enum.GetValues<FactSet>()
            .Where(static set => set != FactSet.Coverage)
            .Select(SetRoot)
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*" + ParquetExtension, SearchOption.AllDirectories));

    /// <summary>Недописанные файлы, оставшиеся от прерванной записи.</summary>
    public IEnumerable<string> TempFiles() =>
        Directory.Exists(FactsRoot)
            ? Directory.EnumerateFiles(FactsRoot, "*" + TempExtension, SearchOption.AllDirectories)
            : [];

    public static ObservationId ObservationOf(string file) =>
        ObservationId.From(Path.GetFileNameWithoutExtension(file));
}
