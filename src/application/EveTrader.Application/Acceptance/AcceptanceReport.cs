using EveTrader.Domain.Facts;

namespace EveTrader.Application.Acceptance;

/// <summary>
/// Итог приёмочного прогона: перестроились ли признаки из сырья и что содержал
/// проверенный интервал.
///
/// Содержимое интервала — часть результата, а не справка. Приёмка на интервале без
/// смены версии статических данных и без досинхронизации задним числом неполна, и
/// умолчать об этом значило бы выдать частичную проверку за полную.
/// </summary>
/// <param name="Within">Проверенный интервал.</param>
/// <param name="Observations">Записей покрытия за интервал.</param>
/// <param name="Complete">Полных наблюдений за интервал, по всем источникам.</param>
/// <param name="Compared">Наблюдений, вошедших в сверку признаков.</param>
/// <param name="WindowOpen">
/// Наблюдений с признаками, оставленных вне сверки: их окно подтверждения исчезновений
/// не закрылось внутри интервала. Число печатается, а не прячется: сверка, из которой
/// тихо выпало наблюдение, выглядит как сверка, которая сошлась.
/// </param>
/// <param name="Partial">Частичных наблюдений; в сверку не входят по построению.</param>
/// <param name="NotModified">Ответов «не изменилось».</param>
/// <param name="Failed">Отказов.</param>
/// <param name="SourceGaps">Ордеров, пропавших и вернувшихся.</param>
/// <param name="Backdated">Наблюдений, узнанных позже, чем закончился их интервал сбора.</param>
/// <param name="StaticDataVersions">Версии статических данных, встреченные в фактах интервала.</param>
/// <param name="FeaturesRecorded">Признаков записано исходным прогоном.</param>
/// <param name="FeaturesRebuilt">Признаков получено перестройкой из сырья.</param>
/// <param name="Missing">Записанных признаков, которых перестройка не дала.</param>
/// <param name="Extra">Перестроенных признаков, которых нет среди записанных.</param>
/// <param name="Gaps">Разрывы в цепочке наблюдений.</param>
/// <param name="Mismatches">Наблюдения, на которых перестройка разошлась с записанным.</param>
public sealed record AcceptanceReport(
    TimeRange Within,
    int Observations,
    int Complete,
    int Compared,
    int WindowOpen,
    int Partial,
    int NotModified,
    int Failed,
    int SourceGaps,
    int Backdated,
    IReadOnlyList<string> StaticDataVersions,
    int FeaturesRecorded,
    int FeaturesRebuilt,
    int Missing,
    int Extra,
    IReadOnlyList<ObservationGap> Gaps,
    IReadOnlyList<FeatureMismatch> Mismatches)
{
    /// <summary>
    /// Признаки перестроились. Пустая сверка успехом не считается: сверять было нечего,
    /// и обещание «признаки выводимы из сырья» такой прогон не подтверждает.
    /// </summary>
    public bool Rebuilt => FeaturesRecorded > 0 && Missing == 0 && Extra == 0;

    /// <summary>Интервал содержит смену версии статических данных.</summary>
    public bool CarriesStaticDataChange => StaticDataVersions.Count > 1;

    /// <summary>Интервал содержит досинхронизацию задним числом.</summary>
    public bool CarriesBackdatedResync => Backdated > 0;

    /// <summary>
    /// Проверка доказательна: перестройка сошлась и интервал содержал оба редких случая.
    /// </summary>
    public bool IsConclusive => Rebuilt && CarriesStaticDataChange && CarriesBackdatedResync;
}
