namespace EveTrader.Domain.Scope;

/// <summary>
/// Смена политики охвата со временем вступления в силу.
///
/// Время здесь не бухгалтерия, а условие корректности: регион, добавленный в охват,
/// наблюдается впервые с этого момента, и его первое наблюдение обязано быть базовой
/// линией, а не массовым появлением ордеров.
/// </summary>
/// <param name="Policy">Политика.</param>
/// <param name="EffectiveFrom">С какого момента действует.</param>
public sealed record PolicyChange(ScopePolicy Policy, DateTimeOffset EffectiveFrom);
