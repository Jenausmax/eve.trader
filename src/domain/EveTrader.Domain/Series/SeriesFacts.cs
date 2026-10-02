using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Перевод рядов в порцию фактов.
///
/// Ряд — факт, а не запрос: он материализуется отдельным набором, потому что прогон
/// правила за год перечитывал бы события заново на каждом шаге окна. Определение ряда
/// входит в ключ факта целиком, поэтому смена окна или полосы даёт новые строки рядом
/// со старыми, а не уточнённую версию их.
///
/// В наборе строки двух видов — приговор окну и точка. Приговор пишется и для
/// отклонённого окна: причина отказа доступна как состояние покрытия, и отсутствие
/// точки в допущенном окне отличимо от отсутствия точки в ненаблюдавшемся.
///
/// Момент знания каждой строки — конец её окна: точка посчитана из того, что было
/// известно к этому моменту, и ничего сверх.
/// </summary>
public static class SeriesFacts
{
    public const string RowKind = "row_kind";
    public const string Definition = "definition";
    public const string Kind = "kind";
    public const string WindowSeconds = "window_seconds";
    public const string StepSeconds = "step_seconds";
    public const string BandBasisPoints = "band_bp";
    public const string Sources = "sources";
    public const string TypeId = "type_id";
    public const string LocationId = "location_id";
    public const string Side = "side";
    public const string Value = "value";
    public const string Incomplete = "incomplete";
    public const string Admission = "admission";
    public const string CoverageState = "coverage_state";
    public const string PartialObservations = "partial_observations";

    /// <summary>Строка — приговор окну.</summary>
    public const long WindowRow = 0;

    /// <summary>Строка — точка ряда.</summary>
    public const long PointRow = 1;

    /// <summary>Источник в записи покрытия производной порции.</summary>
    public const string Source = "series";

    public static FactBatch ToBatch(
        RegionId region,
        ObservationId observation,
        DateOnly partition,
        IReadOnlyList<SeriesWindowVerdict> windows,
        IReadOnlyList<SeriesPoint> points,
        StaticDataVersion staticData)
    {
        var rows = new List<SeriesRow>(windows.Count + points.Count);

        rows.AddRange(windows.Select(static window => new SeriesRow(
            window.FactKey, WindowRow, window.Definition, 0, 0, SeriesSide.Both, window.Window.Range, 0d,
            window.Window.IsIncomplete, window.Window)));

        rows.AddRange(points.Select(static point => new SeriesRow(
            point.FactKey, PointRow, point.Definition, point.TypeId, point.LocationId, point.Side, point.Window,
            point.Value, point.Incomplete, null)));

        var envelopes = rows
            .Select(row => new FactEnvelope(
                row.FactKey,
                EventTime.Between(row.Range.From, row.Range.To),
                row.Range.To,
                observation,
                staticData))
            .ToList();

        return FactBatch.Of(
            FactSet.FeatureSeries,
            region,
            observation,
            partition,
            envelopes,
            [
                FactColumn.OfInt64(RowKind, [.. rows.Select(static row => row.RowKind)]),
                FactColumn.OfString(Definition, [.. rows.Select(static row => row.Definition.Key)]),
                FactColumn.OfInt64(Kind, [.. rows.Select(static row => (long)row.Definition.Kind)]),
                FactColumn.OfInt64(WindowSeconds, [.. rows.Select(static row => (long)row.Definition.Window.TotalSeconds)]),
                FactColumn.OfInt64(StepSeconds, [.. rows.Select(static row => (long)row.Definition.Step.TotalSeconds)]),
                FactColumn.OfInt64(BandBasisPoints, [.. rows.Select(static row => (long)row.Definition.BandBasisPoints)]),
                FactColumn.OfString(Sources, [.. rows.Select(static row => SourcesOf(row.Definition))]),
                FactColumn.OfInt64(TypeId, [.. rows.Select(static row => (long)row.TypeId)]),
                FactColumn.OfInt64(LocationId, [.. rows.Select(static row => row.LocationId)]),
                FactColumn.OfInt64(Side, [.. rows.Select(static row => (long)row.Side)]),
                FactColumn.OfDouble(Value, [.. rows.Select(static row => row.Value)]),
                FactColumn.OfInt64(Incomplete, [.. rows.Select(static row => row.Incomplete ? 1L : 0L)]),
                FactColumn.OfInt64(Admission, [.. rows.Select(static row => (long)(row.Window?.Admission ?? SeriesRows.AdmissionOf(row)))]),
                FactColumn.OfInt64(CoverageState, [.. rows.Select(static row => (long)(row.Window?.Coverage ?? Coverage.CoverageState.Observed))]),
                FactColumn.OfInt64(PartialObservations, [.. rows.Select(static row => (long)(row.Window?.PartialObservations ?? 0))]),
            ]);
    }

    /// <summary>Наборы-источники определения через запятую: происхождение ряда читается из строки.</summary>
    public static string SourcesOf(SeriesDefinition definition) =>
        string.Join(',', definition.Sources.Select(FactSets.PathSegment));

    /// <summary>
    /// Наблюдение, подтверждающее порцию рядов региона за отрезок концов окон. Выводится
    /// из содержимого, а не из часов: повторная материализация того же отрезка тем же
    /// набором определений даёт тот же идентификатор и ничего не удваивает.
    /// </summary>
    public static ObservationId ObservationFor(
        RegionId region,
        IReadOnlyList<SeriesDefinition> definitions,
        DateTimeOffset firstEnd,
        DateTimeOffset lastEnd)
    {
        var digest = SeriesDigest.Of(definitions);

        return ObservationId.From(string.Create(
            CultureInfo.InvariantCulture,
            $"series-{region.Value}-{digest}-{firstEnd:yyyyMMddTHHmmss}-{lastEnd:yyyyMMddTHHmmss}"));
    }
}

/// <summary>Строка набора рядов до раскладки по колонкам: приговор окну либо точка.</summary>
file sealed record SeriesRow(
    string FactKey,
    long RowKind,
    SeriesDefinition Definition,
    int TypeId,
    long LocationId,
    SeriesSide Side,
    TimeRange Range,
    double Value,
    bool Incomplete,
    SeriesWindow? Window);

file static class SeriesRows
{
    /// <summary>Точка порождается только в допущенном окне, поэтому её допуск выводится из пометки.</summary>
    public static SeriesAdmission AdmissionOf(SeriesRow row) =>
        row.Incomplete ? SeriesAdmission.AdmittedIncomplete : SeriesAdmission.Admitted;
}
