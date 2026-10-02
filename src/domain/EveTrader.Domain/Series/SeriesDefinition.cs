using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Определение ряда: что считается, на каком окне, с каким шагом и из чего выводится.
///
/// Определение входит в ключ факта целиком. Величина, посчитанная за час и за сутки, —
/// это два разных признака, отвечающих на разные вопросы; съехавшись в один ряд, они
/// неотличимы от шума, а в обучении дают модель, объясняющую собственную разметку.
/// </summary>
public sealed record SeriesDefinition
{
    private SeriesDefinition(SeriesKind kind, TimeSpan window, TimeSpan step, int bandBasisPoints)
    {
        Kind = kind;
        Window = window;
        Step = step;
        BandBasisPoints = bandBasisPoints;
        Sources = SeriesKinds.SourcesOf(kind);
        Key = KeyOf(kind, window, step, bandBasisPoints);
    }

    public SeriesKind Kind { get; }

    /// <summary>Окно, за которое считается величина.</summary>
    public TimeSpan Window { get; }

    /// <summary>Шаг, с которым окно двигается.</summary>
    public TimeSpan Step { get; }

    /// <summary>Полоса вокруг лучшей цены в сотых долях процента; ноль — виду не нужна.</summary>
    public int BandBasisPoints { get; }

    /// <summary>Наборы фактов, из которых ряд выводится.</summary>
    public IReadOnlyList<FactSet> Sources { get; }

    /// <summary>Устойчивое имя определения; входит в ключ факта.</summary>
    public string Key { get; }

    public static SeriesDefinition Of(
        SeriesKind kind,
        TimeSpan window,
        TimeSpan step,
        int bandBasisPoints = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(step, window);

        return SeriesKinds.UsesBand(kind) == (bandBasisPoints <= 0)
            ? throw new ArgumentOutOfRangeException(
                nameof(bandBasisPoints),
                bandBasisPoints,
                SeriesKinds.UsesBand(kind)
                    ? "Виду нужна полоса вокруг лучшей цены"
                    : "Виду полоса вокруг лучшей цены не нужна")
            : new SeriesDefinition(kind, window, step, bandBasisPoints);
    }

    public static string KeyOf(SeriesKind kind, TimeSpan window, TimeSpan step, int bandBasisPoints)
    {
        var head = string.Create(
            CultureInfo.InvariantCulture,
            $"{SeriesKinds.PathSegment(kind)}/w{(long)window.TotalSeconds}/s{(long)step.TotalSeconds}");

        return bandBasisPoints > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{head}/b{bandBasisPoints}")
            : head;
    }

    public override string ToString() => Key;
}
