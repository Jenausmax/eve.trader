namespace EveTrader.Application.Unit.BackgroundServices;

/// <summary>
/// Scoped-зависимость, по которой видно, создан ли на цикл свежий DI-scope:
/// два цикла в одном scope вернули бы один и тот же экземпляр.
/// </summary>
internal sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}
