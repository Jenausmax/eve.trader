using EveTrader.Domain.Facts;

namespace EveTrader.Application.Acceptance;

/// <summary>
/// Разрыв в цепочке наблюдений региона: между двумя соседними наблюдениями прошло
/// заметно больше объявленного шага.
///
/// Пробел считается по шагу, а не по доле покрытого времени. Наблюдение занимает
/// секунды, шаг — минуты, поэтому доля покрытого времени у исправного сбора всё равно
/// мала, и по ней ничего не видно. Видно по тому, пропущен ли такт.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Range">Границы разрыва.</param>
/// <param name="Steps">Сколько тактов пропущено.</param>
public sealed record ObservationGap(RegionId Region, TimeRange Range, int Steps);
