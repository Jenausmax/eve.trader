using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Scope;

/// <summary>Когда регион можно наблюдать снова и почему именно тогда.</summary>
/// <param name="Region">Регион.</param>
/// <param name="DueAt">Момент, раньше которого спрашивать нельзя.</param>
/// <param name="HeldBySource">
/// Срок держит источник, а не политика: политика хотела бы чаще, но срок годности
/// предыдущего ответа ещё не истёк.
/// </param>
public readonly record struct RegionDue(RegionId Region, DateTimeOffset DueAt, bool HeldBySource);
