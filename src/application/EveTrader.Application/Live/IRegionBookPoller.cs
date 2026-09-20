using EveTrader.Domain.Facts;

namespace EveTrader.Application.Live;

/// <summary>
/// Опрос стакана одного региона у живого источника.
///
/// Протокол добычи у живого сбора свой: снимков заранее нет, опрос идёт по истечении
/// срока годности. Общим с архивом остаётся то, что выходит наружу, — форма наблюдения,
/// и этого достаточно: спека требует неразличимости стадий **после приёма**, а не
/// одинаковой добычи.
/// </summary>
public interface IRegionBookPoller
{
    /// <summary>Имя источника; попадает в запись покрытия.</summary>
    string Name { get; }

    /// <summary>
    /// Опрашивает регион. <paramref name="validator" /> — валидатор кэша прошлого
    /// ответа; с ним источник вправе ответить «не изменилось» и не слать тело.
    /// </summary>
    Task<RegionPoll> PollAsync(RegionId region, string? validator, CancellationToken cancellationToken);
}
